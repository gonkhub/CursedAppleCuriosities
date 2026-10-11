using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Abrams;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Abrams
{
    /// <summary>
    /// Abrams from Deadlock as a RoR2 survivor (same structure as Lash). The
    /// body is a clone of Commando's rig; the Deadlock model, animations, sounds and icons come from the player's own
    /// Deadlock install at runtime. Without Deadlock, Abrams plays the same with the stand-in model.
    /// </summary>
    public static class AbramsSurvivor
    {
        public const string Token = "CURSEDAPPLE_ABRAMS_";
        public const string BodyName = "CursedAppleAbramsBody";
        public const string MuzzleName = "AbramsMuzzle";
        public static readonly Color Color = new Color(1f, 0.35f, 0.24f); // ember #FF5A3C

        public static GameObject? BodyPrefab, DisplayPrefab;
        public static SurvivorDef? Survivor;
        public static BuffDef? SiphonBuff;

        // Vanilla effects reused by the states (loaded once).
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, SlamEffect, WallEffect, PunchEffect, DrainEffect;
        public static Material IndicatorMaterial = null!;

        private static SkillDef? _shotgun, _siphon, _melee, _charge, _slam;

        private static T Load<T>(string key) where T : UnityEngine.Object => Addressables.LoadAssetAsync<T>(key).WaitForCompletion();

        public static void Init(ConfigFile config, GameObject host)
        {
            AbramsData.Bind(config);
            if (!config.Bind("Abrams - Debug", "Fitted Item Anchors", true,
                    "Item attach points fitted to Abrams' body. Off: the older automatic attach points (items will sit as before the item pass).").Value)
                AbramsModel.Def.ItemAnchors = null;

            foreach (DeadlockSound s in AbramsData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { AbramsData.IconPortrait, AbramsData.IconSiphon, AbramsData.IconCharge, AbramsData.IconResilience, AbramsData.IconSlam })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(AbramsModel.Def);
            DeadlockInstall.WantModel(AbramsModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            AbramsItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            AbramsNet.Register();
            AbramsResilience.Hook();
            AbramsResilienceBar.Hook();
            DeadlockHeroVoice.Track(BodyName, AbramsData.Hero);
            DeadlockPingVoice.Track(BodyName, AbramsData.Hero, "atlas");

            host.AddComponent<AbramsIcons>();
        }

        // ---------------- assets ----------------

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBandit2Shotgun.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/HitsparkBandit.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            SlamEffect = TryLoad<GameObject>("RoR2/Base/BeetleGuard/BeetleGuardGroundSlam.prefab");
            WallEffect = TryLoad<GameObject>("RoR2/Base/Loader/LoaderGroundSlam.prefab");
            PunchEffect = TryLoad<GameObject>("RoR2/Base/Loader/OmniImpactVFXLoader.prefab");
            DrainEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab");
            // Shader.Find is off limits in RoR2 (RoR2BepInExPack throws for unknown names): borrow the laser-pointer beam.
            Material? laser = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            IndicatorMaterial = laser != null ? new Material(laser) : new Material(Load<Material>("RoR2/Base/Loader/matLoaderHookRope.mat"));
        }

        /// <summary>One missing vanilla asset costs its effect, never the whole survivor.</summary>
        private static T? TryLoad<T>(string key) where T : UnityEngine.Object
        {
            try { return Load<T>(key); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Abrams] asset {key}: {e.Message}"); return null; }
        }

        private static void CreateBuffs()
        {
            SiphonBuff = ScriptableObject.CreateInstance<BuffDef>();
            SiphonBuff.name = "CursedAppleAbramsSiphon";
            SiphonBuff.buffColor = new Color(0.85f, 0.1f, 0.12f);
            SiphonBuff.canStack = false;
            SiphonBuff.isDebuff = false;
            SiphonBuff.iconSprite = ItemBase.CreatePlaceholderSprite(SiphonBuff.buffColor);
            ContentAddition.AddBuffDef(SiphonBuff);
        }

        // ---------------- states and skills ----------------

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireShotgun),typeof(SiphonLife), typeof(HeavyMelee), typeof(HeavyMeleeHit),
                         typeof(ShoulderCharge), typeof(ShoulderChargeEnd), typeof(SeismicLeap), typeof(SeismicCrash), typeof(SeismicLand) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleAbrams", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _shotgun = Skill<SkillDef>("Shotgun", typeof(FireShotgun), "Weapon", 0f, InterruptPriority.Any);
            _shotgun.cancelSprintingOnActivation = true;
            _shotgun.mustKeyPress = false;

            _siphon = Skill<SkillDef>("Siphon", typeof(SiphonLife), "Siphon", AbramsData.SiphonCooldown, InterruptPriority.Any);
            _siphon.mustKeyPress = true;
            _siphon.cancelSprintingOnActivation = false;

            _melee = Skill<SkillDef>("HeavyMelee", typeof(HeavyMelee), "Body", AbramsData.MeleeCooldown, InterruptPriority.PrioritySkill);
            _melee.mustKeyPress = true;
            _melee.cancelSprintingOnActivation = true;

            _charge = Skill<SkillDef>("Charge", typeof(ShoulderCharge), "Body", AbramsData.ChargeCooldown, InterruptPriority.PrioritySkill);
            _charge.mustKeyPress = true;
            _charge.cancelSprintingOnActivation = false;

            _slam = Skill<SkillDef>("Seismic", typeof(SeismicLeap), "Body", AbramsData.SlamCooldown, InterruptPriority.PrioritySkill); // cuts into Shoulder Charge
            _slam.mustKeyPress = true;
            _slam.cancelSprintingOnActivation = false;
            _slam.beginSkillCooldownOnSkillEnd = true;
        }

        // ---------------- body ----------------

        private static GameObject CreateBody()
        {
            GameObject body = Load<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = AbramsData.BaseHealth;
            cb.levelMaxHealth = AbramsData.HealthPerLevel;
            cb.baseArmor = AbramsData.BaseArmor;
            cb.baseDamage = AbramsData.BaseDamage;
            cb.levelDamage = AbramsData.DamagePerLevel;
            cb.baseMoveSpeed = AbramsData.BaseSpeed;

            // Skills: Commando's slots out, Abrams' in. Siphon Life gets its own machine, so it runs alongside everything.
            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            Kit.AddMachine(body, "Siphon");
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _shotgun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _siphon!, _melee!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _charge!);
            locator.special = Kit.Slot(body, BodyName, "Special", _slam!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            body.AddComponent<AbramsSiphon>();
            body.AddComponent<AbramsResilience>();
            DeadlockMovement.Apply(body, AbramsData.Movement); // stamina, dashes, jumps, slide

            // Model: Abrams goes on top of the stand-in; one skin (a copy of Commando's default) so the lobby has one.
            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = AbramsData.Hero;
            Kit.MuteStandIn(body, model.gameObject, AbramsData.Hero);
            // Reserve the muzzle slot now: ChildLocators can't grow after load. It points at the gun once Abrams is on.
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleAbramsDEFAULT", Token + "SKIN_DEFAULT");

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Load<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").InstantiateClone("CursedAppleAbramsDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = AbramsData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleAbramsDISPLAY", Token + "SKIN_DEFAULT");
            }
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleAbrams";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 101f; // right after Lash
            ContentAddition.AddSurvivorDef(Survivor);
        }

        // ---------------- icons (from Deadlock, once extracted) ----------------

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(AbramsData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? drain = Set(_siphon, AbramsData.IconSiphon);
            if (drain != null && SiphonBuff != null) SiphonBuff.iconSprite = drain;
            Set(_charge, AbramsData.IconCharge);
            Set(_slam, AbramsData.IconSlam);
            Texture2D? beef = DeadlockInstall.Icon(AbramsData.IconResilience);
            if (BodyPrefab != null && (beef != null || portrait != null))
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = Kit.Sprite(beef ?? portrait!);
                locator.passiveSkill = passive;
            }
            if (portrait != null) { _shotgun!.icon = Kit.Sprite(portrait); _melee!.icon = _shotgun.icon; }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Abrams] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Abrams");
            LanguageAPI.Add(Token + "SUBTITLE", "The Bull of the Cursed Apple");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Abrams is a hard-boiled detective who settles his cases up close.<color=#CCD3E0>\n\n" +
                "< ! > Get close: your shotgun's pellets fall off fast with range.\n\n" +
                "< ! > Siphon Life runs alongside everything else: start it, then charge or leap into the crowd.\n\n" +
                "< ! > Shoulder Charge carries everything it hits. Steer them into a wall to stun them all.\n\n" +
                "< ! > Seismic Impact hovers until you press special or primary again: pick your spot with the ring, then crash down.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, the case closed the hard way.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, one case he couldn't crack.");
            LanguageAPI.Add(Token + "LORE", "A detective from the Cursed Apple who took one case too far from home.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Infernal Resilience");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                $"<style=cIsHealing>Regenerate {D(AbramsData.ResilienceFraction)}</style> of all damage taken over <style=cIsUtility>{AbramsData.ResilienceSeconds:0}s</style>. " +
                $"Melee hits <style=cIsHealing>heal for {D(AbramsData.MeleeLifesteal)}</style> of the damage dealt. " +
                "Abrams <style=cIsUtility>sprints automatically</style> out of combat, and can <style=cIsUtility>slide</style>, <style=cIsUtility>dash</style> and <style=cIsUtility>air jump</style>.");

            LanguageAPI.Add(Token + "SHOTGUN_NAME", "Abrams' Shotgun");
            LanguageAPI.Add(Token + "SHOTGUN_DESC",
                $"Fire a blast of <style=cIsDamage>{AbramsData.Pellets}x{D(AbramsData.PelletDamage)} damage</style> pellets.");

            LanguageAPI.Add(Token + "SIPHON_NAME", "Siphon Life");
            LanguageAPI.Add(Token + "SIPHON_DESC",
                $"For <style=cIsUtility>{AbramsData.SiphonDuration:0}s</style>, drain every enemy within {AbramsData.SiphonRadius:0}m for <style=cIsDamage>{D(AbramsData.SiphonDps)} damage per second</style> " +
                $"that <style=cIsDamage>ignores armor</style>. <style=cIsHealing>Heal for {D(AbramsData.SiphonHeal)} of the damage dealt</style>. You can keep moving and fighting.");

            LanguageAPI.Add(Token + "HEAVYMELEE_NAME", "Heavy Melee");
            LanguageAPI.Add(Token + "HEAVYMELEE_DESC",
                $"Wind up and lunge, then punch everything in front of you for <style=cIsDamage>{D(AbramsData.MeleeDamage)} damage</style>.");

            LanguageAPI.Add(Token + "CHARGE_NAME", "Shoulder Charge");
            LanguageAPI.Add(Token + "CHARGE_DESC",
                $"<style=cIsUtility>Charge</style> where you aim, speeding up as you go. Enemies hit take <style=cIsDamage>{D(AbramsData.ChargeDamage)} damage</style> and are <style=cIsUtility>carried</style> along. " +
                $"Slam them into a wall to <style=cIsDamage>stun</style> them for {AbramsData.ChargeWallStun:0.#}s.");

            LanguageAPI.Add(Token + "SEISMIC_NAME", "Seismic Impact");
            LanguageAPI.Add(Token + "SEISMIC_DESC",
                "<style=cIsUtility>Leap</style> high into the air and hover. Press again to <style=cIsUtility>crash down</style> where you aim, " +
                $"dealing <style=cIsDamage>{D(AbramsData.SlamDamage)} damage</style> within {AbramsData.SlamRadius:0}m and <style=cIsDamage>stunning</style> for {AbramsData.SlamStun:0.#}s. " +
                "Enemies in your way are dragged down with you.");
        }
    }

    /// <summary>Waits for the Deadlock reader, then swaps placeholder icons for Abrams' own.</summary>
    public class AbramsIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { AbramsSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Abrams] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
