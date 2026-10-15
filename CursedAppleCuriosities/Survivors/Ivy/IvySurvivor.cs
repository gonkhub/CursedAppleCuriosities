using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Ivy;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>Ivy on the shared Deadlock model layer.</summary>
    public static class IvyModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = IvyData.Hero,
            CenterIdle = true, // her idle stood ~0.2 m to the side of her body (user 2.5)
            DisplayName = "Ivy",
            DlModel = IvyData.DlModel,
            ModelHeightScale = IvyData.ModelHeightScale,
            BaseSpeed = IvyData.BaseSpeed,
            MuzzleBone = IvyData.MuzzleBone,
            MuzzleName = IvySurvivor.MuzzleName,
            Anims = IvyData.Anims,
            RootLocked = IvyData.Locked,
            ItemAnchors = IvyItemDisplays.Anchors,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }

    /// <summary>
    /// Ivy from Deadlock (hero_tengu) as a RoR2 survivor. The user asked me to design her (2026-10-14): Protector's Hand, Kudzu Bomb,
    /// Stone Form, Air Drop; passive Kudzu Connection, always bound to the nearest ally (players, drones, minions: user 2.5).
    /// </summary>
    public static class IvySurvivor
    {
        public const string Token = "CURSEDAPPLE_IVY_";
        public const string BodyName = "CursedAppleIvyBody";
        public const string MuzzleName = "IvyMuzzle";
        public static readonly Color Color = new Color(0.45f, 0.62f, 0.4f); // her moss and stone
        public const float Hue = 0.3f;

        public static GameObject? BodyPrefab, DisplayPrefab, SeedProjectile, BombProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? TetherBuff, ThornsSlow, StoneBuff, AirDropBuff;
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, SlamEffect, BombEffect, KudzuBurstEffect, StoneSlamEffect;
        public static Material? VineMaterial;

        private static SkillDef? _gun, _thorns, _stone, _airdrop;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Ivy", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            IvyData.Bind(config);
            foreach (DeadlockSound s in IvyData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { IvyData.IconPortrait, IvyData.IconThorns, IvyData.IconTether, IvyData.IconStone, IvyData.IconAirDrop })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(IvyModel.Def);
            DeadlockInstall.WantModel(IvyModel.Def.ModelRequest());
            DeadlockInstall.WantModel(new DeadlockInstall.ModelRequest { Hero = IvyData.StatueKey, DlModel = IvyData.DlStatueModel, Clips = new[] { IvyData.StatueClip } });

            TracerEffect = TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            SlamEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab");
            // Kudzu Bomb's burst: REX's plant shockwave (green growth). Stone Form's landing: the Parent's ground slam (dust and debris).
            KudzuBurstEffect = TryLoad<GameObject>("RoR2/Base/Treebot/TreebotPlantShockwaveEffect.prefab", "RoR2/Base/Characters/Treebot/Skills/TreebotPlantShockwaveEffect.prefab");
            StoneSlamEffect = TryLoad<GameObject>("RoR2/Base/Parent/ParentSlamEffect.prefab", "RoR2/Base/Characters/Parent/ParentSlamEffect.prefab");
            BombEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/OmniExplosionVFXCommandoGrenade.prefab"), "CursedAppleIvyBombBlast", Hue, effect: true);
            VineMaterial = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            CreateBuffs();
            SeedProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Commando/CommandoGrenadeProjectile.prefab"),
                "CursedAppleIvySeed", typeof(IvySeed), IvyData.ThornsThrowSpeed, 6f, gravity: true, triggers: false, steer: false);
            RecolourGhost(SeedProjectile, "CursedAppleIvySeedGhost", 0.7f);
            BombProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Commando/CommandoGrenadeProjectile.prefab"),
                "CursedAppleIvyBomb", typeof(IvyBomb), IvyData.BombSpeed, 10f, gravity: true, triggers: false, steer: false);
            RecolourGhost(BombProjectile, "CursedAppleIvyBombGhost", 2f);

            foreach (Type t in new[] { typeof(FireIvyGun), typeof(ThrowThorns), typeof(StoneForm), typeof(AirDrop) })
                ContentAddition.AddEntityState(t, out _);
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            IvyItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            IvyNet.Register();
            DeadlockHeroVoice.Track(BodyName, IvyData.Hero, "tengu");
            DeadlockPingVoice.Track(BodyName, IvyData.Hero, "tengu");
            host.AddComponent<IvyIcons>();
        }

        private static void RecolourGhost(GameObject? projectile, string name, float scale)
        {
            var pc = projectile != null ? projectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (pc == null) return;
            if (pc.ghostPrefab == null) pc.ghostPrefab = TryLoad<GameObject>("RoR2/Base/Commando/CommandoGrenadeGhost.prefab"); // the clone came without one
            if (pc.ghostPrefab == null) return;
            GameObject? ghost = Kit.Recoloured(pc.ghostPrefab, name, Hue, effect: false);
            if (ghost == null) return;
            ghost.transform.localScale *= scale;
            pc.ghostPrefab = ghost;
        }

        private static void CreateBuffs()
        {
            TetherBuff = Buff("CursedAppleIvyKudzu", new Color(0.4f, 0.85f, 0.3f), debuff: false, stack: true);
            ThornsSlow = Buff("CursedAppleIvyThorns", new Color(0.3f, 0.6f, 0.2f), debuff: true, stack: false);
            StoneBuff = Buff("CursedAppleIvyStone", new Color(0.6f, 0.6f, 0.55f), debuff: false, stack: false);
            AirDropBuff = Buff("CursedAppleIvyAirDrop", new Color(0.9f, 0.85f, 0.4f), debuff: false, stack: false);
        }

        private static BuffDef Buff(string name, Color color, bool debuff, bool stack, bool hidden = false)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name; b.buffColor = color; b.canStack = stack; b.isDebuff = debuff; b.isHidden = hidden;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleIvy", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireIvyGun), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;
            _thorns = Skill<SkillDef>("Thorns", typeof(ThrowThorns), "Weapon", IvyData.ThornsCooldown, InterruptPriority.Skill);
            _thorns.mustKeyPress = true;
            _stone = Skill<SkillDef>("Stone", typeof(StoneForm), "Body", IvyData.StoneCooldown, InterruptPriority.PrioritySkill);
            _stone.mustKeyPress = true;
            _stone.cancelSprintingOnActivation = false;
            _airdrop = Skill<SkillDef>("AirDrop", typeof(AirDrop), "Body", IvyData.AirDropCooldown, InterruptPriority.PrioritySkill);
            _airdrop.mustKeyPress = true;
            _airdrop.cancelSprintingOnActivation = false;
            _airdrop.beginSkillCooldownOnSkillEnd = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = IvyData.BaseHealth;
            cb.levelMaxHealth = IvyData.HealthPerLevel;
            cb.baseDamage = 12f;
            cb.levelDamage = 2.4f;
            cb.baseMoveSpeed = IvyData.BaseSpeed;
            cb.baseRegen = IvyData.BaseRegen;
            cb.levelRegen = IvyData.BaseRegen * 0.2f;
            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _thorns!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _stone!);
            locator.special = Kit.Slot(body, BodyName, "Special", _airdrop!);
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = true, skillNameToken = Token + "KUDZU_NAME", skillDescriptionToken = Token + "KUDZU_DESC" }; // Kudzu Connection (user 2.5)
            DeadlockMovement.Apply(body, IvyData.Movement);
            body.AddComponent<IvyGunAudio>();
            body.AddComponent<IvyStoneHeal>();
            body.AddComponent<IvyStoneLook>();
            body.AddComponent<IvyKudzuPassive>();
            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = IvyData.Hero;
            Kit.MuteStandIn(body, model.gameObject, IvyData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleIvyDEFAULT", Token + "SKIN_DEFAULT");
            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleIvyDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = IvyData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleIvyDISPLAY", Token + "SKIN_DEFAULT");
            }
            display.AddComponent<IvyDisplayIntro>(); // lobby entrance (user 2.5)
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleIvy";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 112f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(IvyData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? thorns = Set(_thorns, IvyData.IconThorns);
            if (thorns != null && ThornsSlow != null) ThornsSlow.iconSprite = thorns;
            Sprite? tether = Set(null, IvyData.IconTether);
            if (tether != null && TetherBuff != null) TetherBuff.iconSprite = tether;
            if (tether != null && BodyPrefab != null && BodyPrefab.TryGetComponent(out SkillLocator sl)) sl.passiveSkill.icon = tether;
            Sprite? stone = Set(_stone, IvyData.IconStone);
            if (stone != null && StoneBuff != null) StoneBuff.iconSprite = stone;
            Sprite? drop = Set(_airdrop, IvyData.IconAirDrop);
            if (drop != null && AirDropBuff != null) AirDropBuff.iconSprite = drop;
            if (portrait != null) _gun!.icon = Kit.Sprite(portrait);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Ivy] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Ivy");
            LanguageAPI.Add(Token + "SUBTITLE", "The Gargoyle");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Ivy is a gargoyle who woke from centuries of stone to watch over her friends.<color=#CCD3E0>\n\n" +
                "< ! > Kudzu Bomb slows and grinds down crowds: hold to see where it lands, throw it where enemies are heading.\n\n" +
                "< ! > Kudzu Connection binds you to whichever ally is nearest: drones, turrets and minions count too.\n\n" +
                "< ! > Stone Form makes you invulnerable while you fall: use it to escape and to slam.\n\n" +
                "< ! > Air Drop flies you over the fight; drop the bomb on the biggest crowd.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so she left, her watch over.");
            LanguageAPI.Add(Token + "FAILURE", "..and so she vanished, turned to stone for good.");
            LanguageAPI.Add(Token + "GUN_NAME", "Protector's Hand");
            LanguageAPI.Add(Token + "GUN_DESC", $"Rapidly shoot for <style=cIsDamage>{D(IvyData.GunDamage)} damage</style>.");
            LanguageAPI.Add(Token + "THORNS_NAME", "Kudzu Bomb");
            LanguageAPI.Add(Token + "THORNS_DESC",
                $"Hold to aim, release to throw a bomb that grows a {IvyData.ThornsRadius:0} m kudzu patch for {IvyData.ThornsDuration:0}s, dealing <style=cIsDamage>{D(IvyData.ThornsDps)} damage per second</style> and <style=cIsUtility>slowing</style> enemies in it.");
            LanguageAPI.Add(Token + "KUDZU_NAME", "Kudzu Connection");
            LanguageAPI.Add(Token + "KUDZU_DESC",
                $"Always bound to the nearest ally within {IvyData.TetherRange:0} m: both gain <style=cIsDamage>+{D(IvyData.TetherAttackSpeed)} attack speed</style> and <style=cIsHealing>{D(IvyData.TetherLifesteal)} lifesteal</style>, and share <style=cIsHealing>{D(IvyData.TetherSharedHeal)}</style> of healing. Alone, half the bonuses." + Kit.MovementLine(IvyData.Movement));
            LanguageAPI.Add(Token + "STONE_NAME", "Stone Form");
            LanguageAPI.Add(Token + "STONE_DESC",
                $"Turn to stone: <style=cIsUtility>invulnerable</style> and <style=cIsHealing>healing {D(IvyData.StoneHeal)} max health per second</style>. Slam down for <style=cIsDamage>{D(IvyData.StoneDamage)} damage</style>, <style=cIsUtility>stunning</style>.");
            LanguageAPI.Add(Token + "AIRDROP_NAME", "Air Drop");
            LanguageAPI.Add(Token + "AIRDROP_DESC",
                $"Fly for up to {IvyData.AirDropSeconds:0}s carrying a bomb. Press again to toss it: <style=cIsDamage>{D(IvyData.AirDropDamage)} damage</style> in {IvyData.AirDropRadius:0} m. Afterwards deal <style=cIsDamage>+{D(IvyData.AirDropBuff)} damage</style> for {IvyData.AirDropBuffSeconds:0}s.");
        }
    }

    public class IvyIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { IvySurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Ivy] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
