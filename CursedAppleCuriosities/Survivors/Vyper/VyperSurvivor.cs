using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Vyper;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Vyper from Deadlock as a RoR2 survivor (same structure as Abrams and Seven). The body is a clone of Commando's
    /// rig; the Deadlock model, animations and icons come from the player's own Deadlock install at runtime.
    /// </summary>
    public static class VyperSurvivor
    {
        public const string Token = "CURSEDAPPLE_VYPER_";
        public const string BodyName = "CursedAppleVyperBody";
        public const string MuzzleName = "VyperMuzzle";
        public static readonly Color Color = new Color(0.72f, 0.9f, 0.22f); // character select / UI: yellowish green (user, 2026-10-12)
        /// <summary>Skin icon in the hero's own palette (lime scales, black, gold, white), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.55f, 0.75f, 0.2f), new Color(0.1f, 0.1f, 0.1f), new Color(0.86f, 0.78f, 0.24f), new Color(0.88f, 0.88f, 0.84f));
        private static Sprite? _defaultSkinIcon;
        private static Sprite CoralSkinIcon => _coralSkinIcon ??= Kit.SkinIcon(new Color(0.85f, 0.38f, 0.28f), new Color(0.1f, 0.1f, 0.1f), new Color(0.86f, 0.78f, 0.24f), new Color(0.88f, 0.88f, 0.84f));
        private static Sprite? _coralSkinIcon;

        public static GameObject? BodyPrefab, DisplayPrefab, DaggerProjectile, BolaProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? PetrifyBuff, VenomBuff, ScrewjabStacks, ScrewjabSlow, BolaSlow;

        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, VenomEffect, BolaEffect, BlockEffect;

        private static SkillDef? _gun, _dagger, _venom, _bola;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Vyper", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            VyperData.Bind(config);
            foreach (DeadlockSound s in VyperData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { VyperData.IconPortrait, VyperData.IconGun, VyperData.IconDagger, VyperData.IconVenom, VyperData.IconSlither, VyperData.IconBola })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(VyperModel.Def);
            DeadlockInstall.WantModel(VyperModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            DaggerProjectile = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/Bandit2/Bandit2ShivProjectile.prefab", "RoR2/Base/Characters/Bandit2/Skills/Bandit2ShivProjectile.prefab"),
                "CursedAppleVyperDagger", typeof(VyperDagger), VyperData.DaggerSpeed, 2f, gravity: false, triggers: false);
            BolaProjectile = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/Commando/CommandoGrenadeProjectile.prefab", "RoR2/Base/Characters/Commando/Skills/CommandoGrenadeProjectile.prefab"),
                "CursedAppleVyperBola", typeof(VyperBola), VyperData.BolaSpeed, VyperData.BolaLifetime + 0.5f, gravity: false, triggers: false, steer: false);
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            VyperItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            VyperNet.Register();
            DeadlockHeroVoice.Track(BodyName, VyperData.Hero, "viper");
            DeadlockPingVoice.Track(BodyName, VyperData.Hero, "viper");
            EquipmentFx.RegisterBuffFx(VenomBuff, null, "vyper_venom_loop");
            EquipmentFx.RegisterBuffFx(PetrifyBuff, "RoR2/Base/GainArmor/ElephantDefense.prefab", "vyper_petrify_loop"); // a stone shell while petrified
            VyperPetrifyLook.Register(); // and the body itself turned to stone, frozen in place (user 3.0)

            host.AddComponent<VyperIcons>();
        }

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBanditPistol.prefab", "RoR2/Base/Characters/Bandit2/VFX/TracerBanditPistol.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab", "RoR2/Base/Characters/Commando/HitsparkCommando.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab", "RoR2/Base/Characters/Bandit2/VFX/MuzzleflashBandit2.prefab");
            VenomEffect = TryLoad<GameObject>("RoR2/Base/Croco/CrocoDiseaseImpactEffect.prefab", "RoR2/Base/Characters/Croco/CrocoDiseaseImpactEffect.prefab");
            BolaEffect = TryLoad<GameObject>("RoR2/Base/Commando/OmniExplosionVFXCommandoGrenade.prefab", "RoR2/Base/Characters/Commando/OmniExplosionVFXCommandoGrenade.prefab");
            BlockEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab");
        }

        private static void CreateBuffs()
        {
            PetrifyBuff = Buff("CursedAppleVyperPetrified", new Color(0.6f, 0.6f, 0.55f), debuff: true, stack: false);
            VenomBuff = Buff("CursedAppleVyperVenom", new Color(0.45f, 0.9f, 0.2f), debuff: true, stack: false);
            ScrewjabStacks = Buff("CursedAppleVyperScrewjab", new Color(0.8f, 0.75f, 0.3f), debuff: true, stack: true);
            ScrewjabSlow = Buff("CursedAppleVyperScrewjabSlow", Color.gray, debuff: true, stack: false, hidden: true);
            BolaSlow = Buff("CursedAppleVyperBolaSlow", Color.gray, debuff: true, stack: false, hidden: true);
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
            foreach (Type t in new[] { typeof(FireRattler), typeof(ThrowDagger), typeof(LethalVenom), typeof(ThrowBola) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleVyper", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireRattler), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _dagger = Skill<SkillDef>("Dagger", typeof(ThrowDagger), "Weapon", VyperData.DaggerRecharge, InterruptPriority.Skill);
            _dagger.baseMaxStock = VyperData.DaggerCharges;
            _dagger.rechargeStock = 1;
            _dagger.mustKeyPress = true;
            _dagger.cancelSprintingOnActivation = true;

            _venom = Skill<SkillDef>("Venom", typeof(LethalVenom), "Weapon", VyperData.VenomCooldown, InterruptPriority.Skill);
            _venom.mustKeyPress = true;
            _venom.cancelSprintingOnActivation = false;

            _bola = Skill<SkillDef>("Bola", typeof(ThrowBola), "Weapon", VyperData.BolaCooldown, InterruptPriority.Skill);
            _bola.mustKeyPress = true;
            _bola.cancelSprintingOnActivation = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = VyperData.BaseHealth;
            cb.levelMaxHealth = VyperData.HealthPerLevel;
            cb.baseArmor = VyperData.BaseArmor;
            cb.baseDamage = VyperData.BaseDamage;
            cb.levelDamage = VyperData.DamagePerLevel;
            cb.baseMoveSpeed = VyperData.BaseSpeed;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _dagger!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _venom!);
            locator.special = Kit.Slot(body, BodyName, "Special", _bola!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, VyperData.Movement);
            body.AddComponent<VyperGunAudio>();

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = VyperData.Hero;
            Kit.MuteStandIn(body, model.gameObject, VyperData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleVyperDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleVyperCORAL", Token + "SKIN_CORAL", CoralSkinIcon));

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleVyperDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = VyperData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleVyperDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleVyperDISPLAYCORAL", Token + "SKIN_CORAL", CoralSkinIcon));
            }
            display.AddComponent<VyperDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleVyper";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 113f; // RosterOrder re-sorts the whole roster A-Z at load (user 3.0)
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(VyperData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? dagger = Set(_dagger, VyperData.IconDagger);
            if (dagger != null && ScrewjabStacks != null) ScrewjabStacks.iconSprite = dagger;
            Sprite? venom = Set(_venom, VyperData.IconVenom);
            if (venom != null && VenomBuff != null) VenomBuff.iconSprite = venom;
            Sprite? bola = Set(_bola, VyperData.IconBola);
            if (bola != null && PetrifyBuff != null) PetrifyBuff.iconSprite = bola;
            Texture2D? slither = DeadlockInstall.Icon(VyperData.IconSlither);
            if (BodyPrefab != null && (slither != null || portrait != null))
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = Kit.Sprite(slither ?? portrait!);
                locator.passiveSkill = passive;
            }
            if (portrait != null) _gun!.icon = Kit.Sprite(portrait);
            Set(_gun, VyperData.IconGun); // the weapon itself (user 3.0); the portrait stays if it isn't cached
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Vyper] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Vyper");
            LanguageAPI.Add(Token + "SUBTITLE", "The Escape Artist");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_CORAL", "Coral");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Vyper has broken out of every prison in the tri-state area. This one's just bigger.<color=#CCD3E0>\n\n" +
                "< ! > Stack Screwjab Daggers on one target: each dagger hits and slows harder than the last.\n\n" +
                "< ! > Lethal Venom hurts more the less health its target has left. Save it to finish the big ones.\n\n" +
                "< ! > Petrified enemies can't act, but your team can't hurt them either. Only Lethal Venom gets through.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so she left, one more jailbreak for the stories.");
            LanguageAPI.Add(Token + "FAILURE", "..and so she vanished, finally caught.");
            LanguageAPI.Add(Token + "LORE", "Getting arrested was never the hard part.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Slither");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                $"Slides further and turns faster, and The Rattler fires <style=cIsDamage>{VyperData.GunSlideFireRate * 100f:0}% faster</style> while sliding.");

            LanguageAPI.Add(Token + "GUN_NAME", "The Rattler");
            LanguageAPI.Add(Token + "GUN_DESC",
                $"Spray bullets for <style=cIsDamage>{D(VyperData.GunDamage)} damage</style> each.");

            LanguageAPI.Add(Token + "DAGGER_NAME", "Screwjab Dagger");
            LanguageAPI.Add(Token + "DAGGER_DESC",
                $"Throw a dagger for <style=cIsDamage>{D(VyperData.DaggerDamage)} damage</style> that <style=cIsUtility>slows</style>. Each dagger already in the target adds <style=cIsDamage>+{D(VyperData.DaggerStackDamage)}</style> and more slow. <style=cIsUtility>{VyperData.DaggerCharges} charges</style>.");

            LanguageAPI.Add(Token + "VENOM_NAME", "Lethal Venom");
            LanguageAPI.Add(Token + "VENOM_DESC",
                $"Poison a nearby enemy. After {VyperData.VenomDelay:0}s, it takes <style=cIsDamage>{D(VyperData.VenomMinDamage)}-{D(VyperData.VenomMaxDamage)} damage</style>, more the less health it has.");

            LanguageAPI.Add(Token + "BOLA_NAME", "Petrifying Bola");
            LanguageAPI.Add(Token + "BOLA_DESC",
                $"Throw a bola that explodes for <style=cIsDamage>{D(VyperData.BolaDamage)} damage</style> and <style=cIsUtility>petrifies</style> enemies for {VyperData.PetrifySeconds:0.#}s: they can't act or take damage, except from Lethal Venom.");
        }
    }

    public class VyperIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { VyperSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Vyper] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
