using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Mina;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Mina from Deadlock (hero_vampirebat) as a RoR2 survivor (same structure as Vyper). The body is a clone of Commando's rig;
    /// the Deadlock model, animations and icons come from the player's own Deadlock install at runtime. Kit by the user (2026-10-14).
    /// </summary>
    public static class MinaSurvivor
    {
        public const string Token = "CURSEDAPPLE_MINA_";
        public const string BodyName = "CursedAppleMinaBody";
        public const string MuzzleName = "MinaMuzzle";
        public static readonly Color Color = new Color(0.72f, 0.1f, 0.22f); // character select / UI: her umbrella's crimson
        public const float Hue = 0.97f;                                     // her effects: crimson

        public static GameObject? BodyPrefab, DisplayPrefab, BatProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? LoveMark, LoveWait, LoveStacksBuff;
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, SwipeEffect, SlashEffect, BlinkEffect, LoveBitesEffect;

        private static SkillDef? _gun, _rake, _retreat, _nox;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Mina", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            MinaData.Bind(config);
            foreach (DeadlockSound s in MinaData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { MinaData.IconPortrait, MinaData.IconRake, MinaData.IconRetreat, MinaData.IconLoveBites, MinaData.IconNox })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(MinaModel.Def);
            DeadlockInstall.WantModel(MinaModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            BatProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Mage/MageFireboltBasic.prefab"),
                "CursedAppleMinaBat", typeof(MinaBat), MinaData.BatSpeed, MinaData.BatLifetime + 0.5f, gravity: false, triggers: false, steer: false);
            BatGhost(BatProjectile);
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            CreateSurvivor();
            AddLanguage();
            MinaNet.Register();
            DeadlockHeroVoice.Track(BodyName, MinaData.Hero, "vampirebat");
            DeadlockPingVoice.Track(BodyName, MinaData.Hero, "vampirebat");

            host.AddComponent<MinaIcons>();
        }

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBanditPistol.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/HitsparkBandit2Pistol.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            SwipeEffect = TryLoad<GameObject>("RoR2/Base/Imp/ImpSwipeEffect.prefab");          // Imp's claws: already her crimson
            SlashEffect = TryLoad<GameObject>("RoR2/Base/Imp/ImpactImpSwipe.prefab");
            BlinkEffect = TryLoad<GameObject>("RoR2/Base/Imp/ImpBlinkEffect.prefab");
            LoveBitesEffect = TryLoad<GameObject>("RoR2/Base/BleedOnHitAndExplode/BleedOnHitAndExplode_Explosion.prefab");
        }

        /// <summary>A bat in flight: the firebolt's glow made dark crimson and small, with Deadlock's bat inside once converted (MinaBatGhost).</summary>
        private static void BatGhost(GameObject? projectile)
        {
            var pc = projectile != null ? projectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (pc == null) return;
            GameObject? ghost = Kit.Recoloured(pc.ghostPrefab ?? TryLoad<GameObject>("RoR2/Base/Mage/MageFireboltGhost.prefab"), "CursedAppleMinaBatGhost", Hue, effect: false);
            if (ghost == null) return;
            ghost.transform.localScale *= 0.4f;
            ghost.AddComponent<MinaBatGhost>();
            pc.ghostPrefab = ghost;
        }

        private static void CreateBuffs()
        {
            LoveMark = Buff("CursedAppleMinaLoveBitesMark", new Color(0.9f, 0.2f, 0.35f), debuff: true, stack: true);
            LoveWait = Buff("CursedAppleMinaLoveBitesWait", new Color(0.45f, 0.2f, 0.25f), debuff: true, stack: false);
            LoveStacksBuff = Buff("CursedAppleMinaLoveBites", Color, debuff: false, stack: true);
        }

        private static BuffDef Buff(string name, Color color, bool debuff, bool stack, bool hidden = false)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name;
            b.buffColor = color;
            b.canStack = stack;
            b.isDebuff = debuff;
            b.isHidden = hidden;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FirePistol), typeof(Rake), typeof(SanguineRetreat), typeof(NoxNostra) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleMina", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FirePistol), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _rake = Skill<SkillDef>("Rake", typeof(Rake), "Weapon", MinaData.RakeCooldown, InterruptPriority.Skill);
            _rake.mustKeyPress = true;
            _rake.cancelSprintingOnActivation = true;

            _retreat = Skill<SkillDef>("Retreat", typeof(SanguineRetreat), "Body", MinaData.RetreatCooldown, InterruptPriority.PrioritySkill);
            _retreat.mustKeyPress = true;
            _retreat.cancelSprintingOnActivation = false;

            _nox = Skill<SkillDef>("Nox", typeof(NoxNostra), "Body", MinaData.NoxCooldown, InterruptPriority.PrioritySkill);
            _nox.mustKeyPress = true;
            _nox.cancelSprintingOnActivation = true;
            _nox.beginSkillCooldownOnSkillEnd = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = MinaData.BaseHealth;
            cb.levelMaxHealth = MinaData.HealthPerLevel;
            cb.baseArmor = MinaData.BaseArmor;
            cb.baseDamage = MinaData.BaseDamage;
            cb.levelDamage = MinaData.DamagePerLevel;
            cb.baseMoveSpeed = MinaData.BaseSpeed;
            cb.baseRegen = MinaData.BaseRegen;
            cb.levelRegen = MinaData.BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _rake!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _retreat!);
            locator.special = Kit.Slot(body, BodyName, "Special", _nox!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, MinaData.Movement);
            body.AddComponent<MinaGunAudio>();
            body.AddComponent<MinaRetreatRecast>();

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = MinaData.Hero;
            Kit.MuteStandIn(body, model.gameObject, MinaData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleMinaDEFAULT", Token + "SKIN_DEFAULT", MinaModel.DefaultIcon, ("skinCursedAppleMinaNIGHTSHADE", Token + "SKIN_NIGHTSHADE", MinaModel.NightshadeIcon));

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleMinaDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = MinaData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleMinaDISPLAY", Token + "SKIN_DEFAULT", MinaModel.DefaultIcon, ("skinCursedAppleMinaDISPLAYNIGHTSHADE", Token + "SKIN_NIGHTSHADE", MinaModel.NightshadeIcon));
            }
            display.AddComponent<MinaDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleMina";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 109f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(MinaData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Set(_rake, MinaData.IconRake);
            Set(_retreat, MinaData.IconRetreat);
            Sprite? nox = Set(_nox, MinaData.IconNox);
            Sprite? love = Set(null, MinaData.IconLoveBites);
            if (love != null)
            {
                if (LoveMark != null) LoveMark.iconSprite = love;
                if (LoveWait != null) LoveWait.iconSprite = love;
                if (LoveStacksBuff != null) LoveStacksBuff.iconSprite = nox ?? love; // stacks are extra Nox Nostra bats
            }
            if (BodyPrefab != null && (love != null || portrait != null))
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = love ?? Kit.Sprite(portrait!);
                locator.passiveSkill = passive;
            }
            if (portrait != null) _gun!.icon = Kit.Sprite(portrait);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Mina] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Mina");
            LanguageAPI.Add(Token + "SUBTITLE", "The Vampire Socialite");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_NIGHTSHADE", "Nightshade");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Mina has outlived every party she was ever invited to. This one has a lot of guests.<color=#CCD3E0>\n\n" +
                "< ! > Your pistol is weak on its own: keep it on a target to fill Love Bites for a big burst.\n\n" +
                "< ! > Every Love Bites burst adds a bat to Nox Nostra until the stage ends. Save it for the boss.\n\n" +
                "< ! > Rake hits hardest on wounded enemies, and kills heal you. Finish things with it.\n\n" +
                "< ! > Sanguine Retreat can be cast twice in a row: get in, then get out.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so she left, the night still young.");
            LanguageAPI.Add(Token + "FAILURE", "..and so she vanished, before the sun came up.");
            LanguageAPI.Add(Token + "LORE", "The party never ends. It just moves.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Love Bites");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                $"Your damage builds Love Bites on enemies. When full, it bursts for <style=cIsDamage>{D(MinaData.LoveBitesDamage)} damage</style> and you gain a stack (resets each stage). " +
                $"The same enemy can't be Love Bitten again for <style=cIsUtility>{MinaData.LoveBitesTargetCooldown:0}s</style>.");

            LanguageAPI.Add(Token + "GUN_NAME", "Pistol");
            LanguageAPI.Add(Token + "GUN_DESC", $"Shoot for <style=cIsDamage>{D(MinaData.GunDamage)} damage</style>. Falls off with distance.");

            LanguageAPI.Add(Token + "RAKE_NAME", "Rake");
            LanguageAPI.Add(Token + "RAKE_DESC",
                $"Swipe your umbrella in a cone for <style=cIsDamage>{D(MinaData.RakeDamage)} damage</style>, plus <style=cIsDamage>{MinaData.RakeMissingHealth * 100f:0}%</style> of the enemy's missing health. " +
                $"Kills <style=cIsHealing>heal {MinaData.RakeKillHeal * 100f:0}%</style> of your max health.");

            LanguageAPI.Add(Token + "RETREAT_NAME", "Sanguine Retreat");
            LanguageAPI.Add(Token + "RETREAT_DESC",
                $"Become a swarm of bats and fly a short distance, <style=cIsUtility>invulnerable and untargetable</style>. Can be <style=cIsUtility>recast once</style> within {MinaData.RetreatRecastWindow:0}s.");

            LanguageAPI.Add(Token + "NOX_NAME", "Nox Nostra");
            LanguageAPI.Add(Token + "NOX_DESC",
                $"Leap into the air and hover, unleashing <style=cIsDamage>{MinaData.Bats} bats</style> that hunt enemies for <style=cIsDamage>{MinaData.BatMaxHealth * 100f:0.#}% of their max health</style> each (bosses: half). " +
                "<style=cIsUtility>+1 bat</style> per Love Bites stack.");
        }
    }

    public class MinaIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { MinaSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Mina] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
