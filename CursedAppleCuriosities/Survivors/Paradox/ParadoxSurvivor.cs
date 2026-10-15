using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Paradox;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Paradox from Deadlock (hero_chrono) as a RoR2 survivor (same structure as Vyper). The body is a clone of Commando's rig;
    /// the Deadlock model, animations and icons come from the player's own Deadlock install at runtime. Kit by the user (2026-10-14).
    /// </summary>
    public static class ParadoxSurvivor
    {
        public const string Token = "CURSEDAPPLE_PARADOX_";
        public const string BodyName = "CursedAppleParadoxBody";
        public const string MuzzleName = "ParadoxMuzzle";
        public static readonly Color Color = new Color(0.55f, 0.16f, 0.2f);   // character select / UI: her maroon coat
        public static readonly Color Light = new Color(1f, 0.72f, 0.3f, 0.35f); // her time effects: amber
        public const float Hue = 0.1f;

        public static GameObject? BodyPrefab, DisplayPrefab, PulseProjectile, SwapProjectile, ScopeOverlay;
        public static SurvivorDef? Survivor;
        public static BuffDef? AmpBuff, SlowBuff, CarbineSpeedBuff;
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, PulseEffect, BlockEffect, CarbineTracer, CarbineHit, CarbineMuzzle, SwapHitEffect;
        public static Material? WallMaterial;

        private static SkillDef? _gun, _pulse, _wall, _carbine, _swap;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Paradox", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            ParadoxData.Bind(config);
            foreach (DeadlockSound s in ParadoxData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { ParadoxData.IconPortrait, ParadoxData.IconPulse, ParadoxData.IconWall, ParadoxData.IconCarbine, ParadoxData.IconSwap })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(ParadoxModel.Def);
            DeadlockInstall.WantModel(ParadoxModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            PulseProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Commando/CommandoGrenadeProjectile.prefab"),
                "CursedAppleParadoxPulseBomb", typeof(ParadoxPulseBomb), ParadoxData.PulseThrowSpeed, 20f, gravity: true, triggers: false, steer: false);
            RecolourGhost(PulseProjectile, "CursedAppleParadoxPulseBombGhost", 1f);
            SwapProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Mage/MageLightningBombProjectile.prefab"),
                "CursedAppleParadoxSwapShot", typeof(ParadoxSwapShot), ParadoxData.SwapSpeed, 2f, gravity: false, triggers: true);
            RecolourGhost(SwapProjectile, "CursedAppleParadoxSwapShotGhost", 1.3f);
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            ParadoxItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            ParadoxNet.Register();
            ParadoxDisplayIntro.Hook();
            DeadlockHeroVoice.Track(BodyName, ParadoxData.Hero, "chrono");
            DeadlockPingVoice.Track(BodyName, ParadoxData.Hero, "chrono");

            host.AddComponent<ParadoxIcons>();
        }

        private static void LoadAssets()
        {
            TracerEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab"), "CursedAppleParadoxTracer", Hue, effect: true);
            HitEffect = TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            PulseEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab"), "CursedAppleParadoxPulse", Hue, effect: true);
            BlockEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab"), "CursedAppleParadoxBlock", Hue, effect: true);
            CarbineTracer = Kit.Recoloured(TryLoad<GameObject>("RoR2/DLC1/Railgunner/TracerRailgunSuper.prefab"), "CursedAppleParadoxCarbineTracer", Hue, effect: true);
            CarbineHit = Kit.Recoloured(TryLoad<GameObject>("RoR2/DLC1/Railgunner/ImpactRailgun.prefab"), "CursedAppleParadoxCarbineHit", Hue, effect: true);
            CarbineMuzzle = Kit.Recoloured(TryLoad<GameObject>("RoR2/DLC1/Railgunner/MuzzleflashRailgun.prefab"), "CursedAppleParadoxCarbineMuzzle", Hue, effect: true);
            SwapHitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Imp/ImpBlinkEffect.prefab"), "CursedAppleParadoxSwapFlash", Hue, effect: true);
            ScopeOverlay = TryLoad<GameObject>("RoR2/DLC1/Railgunner/RailgunnerScopeLightOverlay.prefab");
            Material? fill = TryLoad<Material>("RoR2/Base/BurnNearby/matHelfireRangeIndicator.mat");
            if (fill != null)
            {
                WallMaterial = new Material(fill) { name = "matCursedAppleParadoxTimeWall" };
                if (WallMaterial.HasProperty("_TintColor")) WallMaterial.SetColor("_TintColor", Light);
            }
        }

        private static void RecolourGhost(GameObject? projectile, string name, float scale)
        {
            var pc = projectile != null ? projectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (pc == null || pc.ghostPrefab == null) return;
            GameObject? ghost = Kit.Recoloured(pc.ghostPrefab, name, Hue, effect: false);
            if (ghost == null) return;
            ghost.transform.localScale *= scale;
            pc.ghostPrefab = ghost;
        }

        private static void CreateBuffs()
        {
            AmpBuff = Buff("CursedAppleParadoxPulseAmp", new Color(1f, 0.7f, 0.3f), debuff: true, stack: true);
            SlowBuff = Buff("CursedAppleParadoxTimeSlow", new Color(0.9f, 0.75f, 0.45f), debuff: true, stack: true, hidden: true);
            CarbineSpeedBuff = Buff("CursedAppleParadoxCarbineSpeed", new Color(1f, 0.8f, 0.4f), debuff: false, stack: true, hidden: true);
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
            foreach (Type t in new[] { typeof(FireBurst), typeof(ThrowPulseBomb), typeof(PlaceTimeWall), typeof(KineticCarbine), typeof(SwapCast), typeof(ParadoxSwapping) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleParadox", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireBurst), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _pulse = Skill<SkillDef>("Pulse", typeof(ThrowPulseBomb), "Weapon", ParadoxData.PulseCooldown, InterruptPriority.Skill);
            _pulse.mustKeyPress = true;
            _pulse.cancelSprintingOnActivation = true;

            _wall = Skill<SkillDef>("Wall", typeof(PlaceTimeWall), "Weapon", ParadoxData.WallCooldown, InterruptPriority.Skill);
            _wall.mustKeyPress = true;
            _wall.cancelSprintingOnActivation = false;

            _carbine = Skill<SkillDef>("Carbine", typeof(KineticCarbine), "Weapon", ParadoxData.CarbineCooldown, InterruptPriority.Skill);
            _carbine.mustKeyPress = true;
            _carbine.cancelSprintingOnActivation = true;
            _carbine.beginSkillCooldownOnSkillEnd = true;

            _swap = Skill<SkillDef>("Swap", typeof(SwapCast), "Weapon", ParadoxData.SwapCooldown, InterruptPriority.Skill);
            _swap.mustKeyPress = true;
            _swap.cancelSprintingOnActivation = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = ParadoxData.BaseHealth;
            cb.levelMaxHealth = ParadoxData.HealthPerLevel;
            cb.baseArmor = ParadoxData.BaseArmor;
            cb.baseDamage = ParadoxData.BaseDamage;
            cb.levelDamage = ParadoxData.DamagePerLevel;
            cb.baseMoveSpeed = ParadoxData.BaseSpeed;
            cb.baseRegen = ParadoxData.BaseRegen;
            cb.levelRegen = ParadoxData.BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _pulse!, _wall!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _carbine!);
            locator.special = Kit.Slot(body, BodyName, "Special", _swap!);
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = false };
            DeadlockMovement.Apply(body, ParadoxData.Movement);

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = ParadoxData.Hero;
            Kit.MuteStandIn(body, model.gameObject, ParadoxData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleParadoxDEFAULT", Token + "SKIN_DEFAULT", ParadoxModel.DefaultIcon, ("skinCursedAppleParadoxTEAL", Token + "SKIN_TEAL", ParadoxModel.TealIcon));

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleParadoxDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = ParadoxData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleParadoxDISPLAY", Token + "SKIN_DEFAULT", ParadoxModel.DefaultIcon, ("skinCursedAppleParadoxDISPLAYTEAL", Token + "SKIN_TEAL", ParadoxModel.TealIcon));
            }
            display.AddComponent<ParadoxDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleParadox";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 110f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(ParadoxData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? pulse = Set(_pulse, ParadoxData.IconPulse);
            if (pulse != null && AmpBuff != null) AmpBuff.iconSprite = pulse;
            Set(_wall, ParadoxData.IconWall);
            Set(_carbine, ParadoxData.IconCarbine);
            Set(_swap, ParadoxData.IconSwap);
            if (portrait != null) _gun!.icon = Kit.Sprite(portrait);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Paradox] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Paradox");
            LanguageAPI.Add(Token + "SUBTITLE", "The Wrinkle in Time");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_TEAL", "Teal");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Paradox has been everywhere and everywhen. This planet is just the next appointment.<color=#CCD3E0>\n\n" +
                "< ! > Every Pulse Bomb hit makes your damage to that enemy stronger. Throw it first, then shoot.\n\n" +
                "< ! > The Time Wall deletes enemy projectiles: stand behind it, and shoot through it for extra damage.\n\n" +
                "< ! > A fully charged Kinetic Carbine hits weak points. Charging also makes you faster.\n\n" +
                "< ! > Paradoxical Swap trades places with what it hits and heals you for all the damage you deal meanwhile.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so she left, right on schedule.");
            LanguageAPI.Add(Token + "FAILURE", "..and so she vanished, out of time.");
            LanguageAPI.Add(Token + "LORE", "It's not a paradox if you were always going to do it.");

            LanguageAPI.Add(Token + "GUN_NAME", "Burst Rifle");
            LanguageAPI.Add(Token + "GUN_DESC", $"Fire a {ParadoxData.BurstCount}-round burst for <style=cIsDamage>{ParadoxData.BurstCount}x{D(ParadoxData.GunDamage)} damage</style>.");

            LanguageAPI.Add(Token + "PULSE_NAME", "Pulse Bomb");
            LanguageAPI.Add(Token + "PULSE_DESC",
                $"Throw a bomb that pulses for <style=cIsDamage>{D(ParadoxData.PulseDamage)} damage</style> every {ParadoxData.PulseInterval:0.#}s (faster with attack speed) for {ParadoxData.PulseDuration:0.#}s, each pulse wider. " +
                $"Each hit makes the enemy take <style=cIsDamage>+{ParadoxData.PulseAmpPerStack * 100f:0}%</style> damage from you.");

            LanguageAPI.Add(Token + "WALL_NAME", "Time Wall");
            LanguageAPI.Add(Token + "WALL_DESC",
                $"Place a wall that <style=cIsUtility>deletes enemy projectiles</style>. Enemies passing through take <style=cIsDamage>{D(ParadoxData.WallDamage)} damage</style> and are slowed. " +
                $"Allied attacks through it deal <style=cIsDamage>+{ParadoxData.WallFriendlyBonus * 100f:0}%</style>.");

            LanguageAPI.Add(Token + "CARBINE_NAME", "Kinetic Carbine");
            LanguageAPI.Add(Token + "CARBINE_DESC",
                $"Tap to charge, <style=cIsUtility>moving faster</style>. Your next <style=cIsDamage>primary</style> click fires a shot for <style=cIsDamage>{D(ParadoxData.CarbineMinDamage)}-{D(ParadoxData.CarbineMaxDamage)} damage</style> that <style=cIsUtility>slows heavily</style>. Fully charged, it hits <style=cIsDamage>weak points</style> and waits <style=cIsUtility>5s</style> for the shot before it fizzles.");

            LanguageAPI.Add(Token + "SWAP_NAME", "Paradoxical Swap");
            LanguageAPI.Add(Token + "SWAP_DESC",
                $"Fire a shot that deals <style=cIsDamage>{D(ParadoxData.SwapDamage)} damage</style> and <style=cIsUtility>swaps places</style> with the enemy it hits, slowing it. <style=cIsHealing>100% lifesteal</style> during the swap.");
        }
    }

    public class ParadoxIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { ParadoxSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Paradox] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
