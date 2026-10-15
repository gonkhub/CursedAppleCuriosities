using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Lash;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash from Deadlock as a RoR2 survivor. The body is a clone of
    /// Commando's rig (hurtboxes, camera, motor, item display bones); the Deadlock model, animations, sounds and
    /// icons come from the player's own Deadlock install at runtime. Without Deadlock, Lash plays the same with the
    /// stand-in model.
    /// </summary>
    public static class LashSurvivor
    {
        public const string Token = "CURSEDAPPLE_LASH_";
        public const string BodyName = "CursedAppleLashBody";
        public const string MuzzleName = "LashMuzzle";
        public static readonly Color Color = new Color(0.55f, 0.8f, 1f); // character select / UI: light blue (user, 2026-10-12)
        /// <summary>Skin icon in the hero's own palette (skin, dark leather, brown, the blue accent), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.8f, 0.6f, 0.46f), new Color(0.28f, 0.2f, 0.17f), new Color(0.5f, 0.33f, 0.24f), new Color(0.15f, 0.45f, 0.8f));
        private static Sprite? _defaultSkinIcon;
        private static Sprite GildedSkinIcon => _gildedSkinIcon ??= Kit.SkinIcon(new Color(0.8f, 0.6f, 0.46f), new Color(0.85f, 0.68f, 0.2f), new Color(0.5f, 0.33f, 0.24f), new Color(0.95f, 0.78f, 0.25f));
        private static Sprite? _gildedSkinIcon;

        public static GameObject? BodyPrefab, DisplayPrefab;
        public static SurvivorDef? Survivor;

        // Vanilla effects reused by the states (loaded once).
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, GroundSlamEffect, SlamEffect, SlashEffect, TrackingIndicator;
        public static Material LineMaterial = null!;
        public static Material IndicatorMaterial = null!;
        private static Material? _baseMaterial;

        private static SkillDef? _gun, _groundStrike, _grapple, _deathSlam;

        private static T Load<T>(string key) where T : UnityEngine.Object => Addressables.LoadAssetAsync<T>(key).WaitForCompletion();

        public static void Init(ConfigFile config, GameObject host)
        {
            LashData.Bind(config);

            foreach (DeadlockSound s in LashData.Sounds) DeadlockSounds.Register(s);
            DeadlockHeroVoice.Track(BodyName, LashData.Hero, "lash");
            DeadlockPingVoice.Track(BodyName, LashData.Hero, "lash");
            foreach (string icon in new[] { LashData.IconPortrait, LashData.IconGroundStrike, LashData.IconGrapple, LashData.IconDeathSlam })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(LashModelSwap.Def);
            DeadlockInstall.WantModel(LashModelSwap.Def.ModelRequest());

            LoadAssets();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            LashItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            LashNet.Register();

            host.AddComponent<LashIcons>();
        }

        // ---------------- assets ----------------

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/Muzzleflash1.prefab");
            GroundSlamEffect = TryLoad<GameObject>("RoR2/Base/Loader/LoaderGroundSlam.prefab");
            SlamEffect = TryLoad<GameObject>("RoR2/Base/BeetleGuard/BeetleGuardGroundSlam.prefab");
            SlashEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab");
            TrackingIndicator = TryLoad<GameObject>("RoR2/Base/Huntress/HuntressTrackingIndicator.prefab");
            _baseMaterial = TryLoad<Material>("RoR2/Base/Commando/matCommandoDualies.mat");
            // Shader.Find is off limits in RoR2 (RoR2BepInExPack throws for unknown names): borrow Loader's hook rope.
            Material? rope = TryLoad<Material>("RoR2/Base/Loader/matLoaderHookRope.mat");
            LineMaterial = rope != null ? new Material(rope) : _baseMaterial!;
            Material? laser = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            IndicatorMaterial = laser != null ? new Material(laser) : LineMaterial;
        }

        /// <summary>One missing vanilla asset costs its effect, never the whole survivor.</summary>
        private static T? TryLoad<T>(string key) where T : UnityEngine.Object
        {
            try { return Load<T>(key); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Lash] asset {key}: {e.Message}"); return null; }
        }

        // ---------------- states and skills ----------------

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireBurst), typeof(GroundStrike), typeof(GroundStrikeDive), typeof(GroundStrikeLand),
                         typeof(Grapple), typeof(DeathSlamChannel), typeof(DeathSlamHold), typeof(DeathSlamThrow) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef
        {
            var s = ScriptableObject.CreateInstance<T>();
            s.skillName = "CursedAppleLash" + id;
            ((ScriptableObject)s).name = s.skillName;
            s.skillNameToken = Token + id.ToUpperInvariant() + "_NAME";
            s.skillDescriptionToken = Token + id.ToUpperInvariant() + "_DESC";
            s.activationState = new SerializableEntityStateType(state);
            s.activationStateMachineName = machine;
            s.baseRechargeInterval = cooldown;
            s.interruptPriority = priority;
            s.icon = ItemBase.CreatePlaceholderSprite(Color);
            ContentAddition.AddSkillDef(s);
            return s;
        }

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireBurst), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _groundStrike = Skill<SkillDef>("GroundStrike", typeof(GroundStrike), "Body", LashData.GroundStrikeCooldown, InterruptPriority.PrioritySkill); // cuts into Grapple / Death Slam
            _groundStrike.cancelSprintingOnActivation = false;
            _groundStrike.mustKeyPress = true;

            _grapple = Skill<LashGrappleSkillDef>("Grapple", typeof(Grapple), "Body", LashData.GrappleCooldown, InterruptPriority.PrioritySkill);
            _grapple.cancelSprintingOnActivation = false;
            _grapple.mustKeyPress = true;
            _grapple.isCombatSkill = false;

            _deathSlam = Skill<SkillDef>("DeathSlam", typeof(DeathSlamChannel), "Body", LashData.DeathSlamCooldown, InterruptPriority.PrioritySkill);
            _deathSlam.cancelSprintingOnActivation = true;
            _deathSlam.mustKeyPress = true;
            _deathSlam.beginSkillCooldownOnSkillEnd = true;
        }

        private static readonly FieldInfo SkillFamilyField = typeof(GenericSkill).GetField("_skillFamily", BindingFlags.NonPublic | BindingFlags.Instance);

        private static GenericSkill Slot(GameObject body, string name, params SkillDef[] defs)
        {
            var family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = BodyName + name + "Family";
            var variants = new SkillFamily.Variant[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                variants[i] = new SkillFamily.Variant { skillDef = defs[i], viewableNode = new ViewablesCatalog.Node(defs[i].skillNameToken, false, null) };
            family.variants = variants;
            ContentAddition.AddSkillFamily(family);
            var slot = body.AddComponent<GenericSkill>();
            slot.skillName = name;
            SkillFamilyField.SetValue(slot, family);
            return slot;
        }

        // ---------------- body ----------------

        private static GameObject CreateBody()
        {
            GameObject body = Load<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = LashData.BaseHealth;
            cb.levelMaxHealth = LashData.HealthPerLevel;
            cb.baseDamage = LashData.BaseDamage;
            cb.levelDamage = LashData.DamagePerLevel;
            cb.baseMoveSpeed = LashData.BaseSpeed;

            // Skills: Commando's slots out, Lash's in.
            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Slot(body, "Primary", _gun!);
            locator.secondary = Slot(body, "Secondary", _grapple!);
            locator.utility = Slot(body, "Utility", _groundStrike!);
            locator.special = Slot(body, "Special", _deathSlam!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            body.AddComponent<LashGrappleTracker>();
            DeadlockMovement.Apply(body, LashData.Movement); // stamina, dashes, jumps, slide

            // Model: Lash goes on top of the stand-in; one skin (a copy of Commando's default) so the lobby has one.
            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = LashData.Hero;
            MuteStandIn(body, model.gameObject);
            // Reserve the muzzle slot now: ChildLocators can't grow after load. It points at the glove once Lash is on.
            var locatorChildren = model.GetComponent<ChildLocator>();
            if (locatorChildren != null && locatorChildren.FindChild(MuzzleName) == null) locatorChildren.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleLashDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleLashGILDED", Token + "SKIN_GILDED", GildedSkinIcon));

            BiggerCamera(body);
            ContentAddition.AddBody(body);
            return body;
        }

        /// <summary>Lash's own copy of the stand-in's camera, lifted and pulled back for his bigger model.</summary>
        private static void BiggerCamera(GameObject body)
        {
            var target = body.GetComponent<CameraTargetParams>();
            if (target == null || target.cameraParams == null) return;
            CharacterCameraParams cam = ScriptableObject.Instantiate(target.cameraParams);
            cam.name = "ccpCursedAppleLash";
            cam.data.pivotVerticalOffset.value += LashData.CameraHeight;
            Vector3 pos = cam.data.idealLocalCameraPos.value;
            cam.data.idealLocalCameraPos.value = new Vector3(pos.x, pos.y, pos.z * LashData.CameraDistance);
            target.cameraParams = cam;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Load<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").InstantiateClone("CursedAppleLashDisplay", false);
            DeadlockSurvivorKit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = LashData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleLashDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleLashDISPLAYGILDED", Token + "SKIN_GILDED", GildedSkinIcon));
            }
            display.AddComponent<LashDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleLash";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 100f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        // ---------------- icons (from Deadlock, once extracted) ----------------

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(LashData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            void Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (s != null && t != null) s.icon = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
            }
            Set(_groundStrike, LashData.IconGroundStrike);
            Set(_grapple, LashData.IconGrapple);
            Set(_deathSlam, LashData.IconDeathSlam);
            if (portrait != null && BodyPrefab != null)
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = Sprite.Create(portrait, new Rect(0, 0, portrait.width, portrait.height), new Vector2(0.5f, 0.5f));
                locator.passiveSkill = passive;
                _gun!.icon = passive.icon;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Lash] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- sounds ----------------

        /// <summary>
        /// Commando's own Wwise jump, landing, death and footstep sounds would play under Lash's: blank them. The
        /// stand-in's footstep animation events stay (with their dust); Lash's own feet drive the footsteps.
        /// </summary>
        private static void MuteStandIn(GameObject body, GameObject model)
        {
            var sfx = body.GetComponent<SfxLocator>();
            if (sfx != null) { sfx.jumpSound = ""; sfx.landingSound = ""; sfx.deathSound = ""; sfx.barkSound = ""; }
            var steps = model.GetComponent<FootstepHandler>();
            if (steps != null)
            {
                steps.baseFootstepString = "";
                steps.sprintFootstepOverrideString = "";
                steps.baseFootliftString = "";
                steps.sprintFootliftOverrideString = "";
            }
            model.AddComponent<DeadlockFootsteps>().HeroKey = LashData.Hero;
        }

        // ---------------- language ----------------

        /// <summary>Readable key names for tooltips (Unity counts mouse buttons from 0).</summary>
        private static string KeyName(KeyCode k) =>
            k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6 ? "Mouse " + (k - KeyCode.Mouse0 + 1) : k.ToString();

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Lash");
            LanguageAPI.Add(Token + "SUBTITLE", "Showman of the Cursed Apple");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_GILDED", "Gilded");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Jacob Lash is an Asshole<color=#CCD3E0>\n\n" +
                "< ! > Ground Strike hits harder the further you fall. Grapple up, then strike back down.\n\n" +
                "< ! > Grapple only needs a target: it locks onto enemies and allies, and gives your air jumps and dashes back.\n\n" +
                "< ! > Death Slam reaches far above and below you: keep your crosshair on enemies to lock on. Once one is locked, press primary (or special again) to release early.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, still waiting for an audience worth the show.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, the crowd finally silent.");
            LanguageAPI.Add(Token + "LORE", "A showman from the Cursed Apple, lost to the stars and looking for a bigger stage.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Perfect Cardio, Perfect Form, Perfect Hair");
            LanguageAPI.Add(Token + "PASSIVE_DESC", Kit.MovementLine(LashData.Movement, alone: true));

            LanguageAPI.Add(Token + "GUN_NAME", "Lash's Glove");
            LanguageAPI.Add(Token + "GUN_DESC", $"Fire a three-round burst for <style=cIsDamage>3x{D(LashData.GunDamage)} damage</style>.");

            LanguageAPI.Add(Token + "GROUNDSTRIKE_NAME", "Ground Strike");
            LanguageAPI.Add(Token + "GROUNDSTRIKE_DESC",
                $"<style=cIsUtility>Dive</style> where you aim. Land for <style=cIsDamage>{D(LashData.GroundStrikeDamage)} damage</style> " +
                $"<style=cIsDamage>+{D(LashData.GroundStrikeDamagePerMetre)} per metre fallen</style> in a wave in front of you. " +
                "Airborne enemies you dive into are <style=cIsUtility>dragged down</style> with you.");

            LanguageAPI.Add(Token + "GRAPPLE_NAME", "Grapple");
            LanguageAPI.Add(Token + "GRAPPLE_DESC",
                "Lash onto an enemy or ally near your crosshair and <style=cIsUtility>launch up toward them</style>. " +
                "<style=cIsUtility>Restores your air jumps and air dashes</style> and one stamina. Only usable with a target.");

            LanguageAPI.Add(Token + "DEATHSLAM_NAME", "Death Slam");
            LanguageAPI.Add(Token + "DEATHSLAM_DESC",
                $"Hover and channel, <style=cIsUtility>locking onto up to {LashData.DeathSlamMaxTargets} enemies</style> you keep your crosshair on, far above or below you. " +
                $"Lift them up, then <style=cIsDamage>slam them</style> where you aim for <style=cIsDamage>{D(LashData.DeathSlamDamage)} damage</style> around each landing, " +
                "<style=cIsUtility>slowing</style> everyone hit. Works on bosses too.");
        }
    }

    /// <summary>Waits for the Deadlock reader, then swaps placeholder icons for Lash's own.</summary>
    public class LashIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { LashSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Lash] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
