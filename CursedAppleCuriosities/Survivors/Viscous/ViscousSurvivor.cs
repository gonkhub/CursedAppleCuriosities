using System;
using System.Collections;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Viscous;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// Viscous from Deadlock (hero_viscous). Same structure as Victor: Commando's rig as the stand-in, the Deadlock model,
    /// animations, sounds and icons read from the player's own Deadlock install at runtime. Goo Ball first (user
    /// 2026-10-12), then the rest of his kit (user): M1 his gun / alt his alt-fire glob, M2 Splatter / alt Puddle Punch,
    /// Utility The Cube (tap: ally, hold: himself), Special Goo Ball.
    /// </summary>
    public static class ViscousSurvivor
    {
        public const string Token = "CURSEDAPPLE_VISCOUS_";
        public const string BodyName = "CursedAppleViscousBody";

        /// <summary>Is this body Viscous (the survivor, not an ally he cubed)?</summary>
        public static bool Is(CharacterBody? body) => body != null && body.name.StartsWith(BodyName, System.StringComparison.Ordinal);
        public const string MuzzleName = "ViscousMuzzle";
        public static readonly Color Color = new Color(0.38f, 0.75f, 0.4f); // his goo green (Deadlock viscous_body)
        /// <summary>Skin icon in the hero's own palette (goo greens and his dark outline), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.38f, 0.75f, 0.4f), new Color(0.43f, 0.94f, 0.43f), new Color(0.14f, 0.16f, 0.15f), new Color(0.17f, 0.55f, 0.2f));
        private static Sprite? _defaultSkinIcon;
        private static Sprite BlueSkinIcon => _blueSkinIcon ??= Kit.SkinIcon(new Color(0.3f, 0.45f, 0.75f), new Color(0.35f, 0.55f, 0.95f), new Color(0.14f, 0.16f, 0.16f), new Color(0.3f, 0.45f, 0.75f));
        private static Sprite? _blueSkinIcon;
        public const float Hue = 0.33f;

        public static GameObject? BodyPrefab, DisplayPrefab;
        public static SurvivorDef? Survivor;
        public static BuffDef? BallBuff, CubeBuff, PostCubeBuff;
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, SplashEffect, WallEffect, HitBigEffect, JumpEffect;
        public static GameObject? AltProjectile, SplatterProjectile, AltBurstEffect, SplatterEffect, PunchPortalEffect, PunchEffect;

        private static SkillDef? _gun, _alt, _splatter, _punch, _cube, _ball;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Viscous", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            ViscousData.Bind(config);
            foreach (DeadlockSound s in ViscousData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { ViscousData.IconPortrait, ViscousData.IconBall, ViscousData.IconSplatter, ViscousData.IconPunch, ViscousData.IconCube, ViscousData.IconAlt })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(ViscousModel.Def);
            DeadlockInstall.WantModel(ViscousModel.Def.ModelRequest());
            DeadlockInstall.WantModel(new DeadlockInstall.ModelRequest { Hero = ViscousData.CubeKey, DlModel = ViscousData.DlCubeModel });

            LoadAssets();
            CreateBuffs();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            ViscousItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            ViscousNet.Register();
            DeadlockHeroVoice.Track(BodyName, ViscousData.Hero, "viscous");
            DeadlockPingVoice.Track(BodyName, ViscousData.Hero, "viscous");

            host.AddComponent<ViscousIcons>();
            host.AddComponent<ViscousCubeHost>();
        }

        private static void LoadAssets()
        {
            TracerEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab"), "CursedAppleViscousTracer", Hue, effect: true);
            HitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Croco/CrocoDiseaseImpactEffect.prefab", "RoR2/Base/Commando/HitsparkCommando.prefab"), "CursedAppleViscousHit", Hue, effect: true);
            MuzzleEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/Muzzleflash1.prefab"), "CursedAppleViscousMuzzle", Hue, effect: true);
            SplashEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/BeetleGuard/BeetleGuardGroundSlam.prefab"), "CursedAppleViscousSplash", Hue, effect: true);
            WallEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Loader/OmniImpactVFXLoader.prefab"), "CursedAppleViscousWall", Hue, effect: true);
            HitBigEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Croco/CrocoDiseaseImpactEffect.prefab", "RoR2/Base/Loader/OmniImpactVFXLoader.prefab"), "CursedAppleViscousBallHit", Hue, effect: true);
            JumpEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab"), "CursedAppleViscousJump", Hue, effect: true);
            SplatterEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/DLC1/AcidLarva/AcidLarvaLeapExplosion.prefab", "RoR2/DLC1/Characters/AcidLarva/Skills/AcidLarvaLeapExplosion.prefab",
                "RoR2/Base/Common/VFX/OmniExplosionVFX.prefab"), "CursedAppleViscousSplatterBurst", Hue, effect: true);
            // The glob splats like Splatter does: Beetle Queen's spit burst drew a second, wrong-sized ring (playtest).
            AltBurstEffect = SplatterEffect;
            PunchPortalEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/BeetleGuard/BeetleGuardGroundSlam.prefab"), "CursedAppleViscousPunchPortal", Hue, effect: true);
            PunchEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Loader/LoaderGroundSlam.prefab"), "CursedAppleViscousPunch", Hue, effect: true);

            // Acrid's spit is already a green glob; Commando's grenade if it's missing. Both keep their flight and networking.
            GameObject? spit = TryLoad<GameObject>("RoR2/Base/Croco/CrocoSpit.prefab", "RoR2/Base/Characters/Croco/Skills/CrocoSpit.prefab", "RoR2/Base/Commando/CommandoGrenadeProjectile.prefab");
            AltProjectile = Kit.CloneProjectile(spit, "CursedAppleViscousAltGlob", typeof(ViscousGlob), ViscousData.AltSpeed, 4.5f, gravity: false, triggers: false, steer: false);
            SplatterProjectile = Kit.CloneProjectile(spit, "CursedAppleViscousSplatter", typeof(ViscousGlob), ViscousData.SplatterSpeed, ViscousData.SplatterLifetime + 0.5f, gravity: false, triggers: false, steer: false);
            if (SplatterProjectile != null)
            {
                SplatterProjectile.GetComponent<ViscousGlob>().Splatter = true;
                var pc = SplatterProjectile.GetComponent<RoR2.Projectile.ProjectileController>();
                if (pc != null && pc.ghostPrefab != null)
                {
                    GameObject ghost = pc.ghostPrefab.InstantiateClone("CursedAppleViscousSplatterGhost", false);
                    ghost.transform.localScale *= 1.8f; // a big glob of goo
                    pc.ghostPrefab = ghost;
                }
            }
        }

        private static BuffDef Buff(string name, Color color)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name;
            b.buffColor = color;
            b.canStack = false;
            b.isDebuff = false;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        private static void CreateBuffs()
        {
            BallBuff = Buff("CursedAppleViscousGooBall", Color);
            CubeBuff = Buff("CursedAppleViscousCube", new Color(0.55f, 0.9f, 0.55f));
            PostCubeBuff = Buff("CursedAppleViscousPostCube", new Color(0.7f, 1f, 0.6f));
        }

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireGooGun), typeof(FireAltGlob), typeof(ThrowSplatter), typeof(PuddlePunch), typeof(CubeCast), typeof(GooBallCast), typeof(GooBall) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleViscous", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            // Primary is the ball's "Go Fast" while rolling, so the guns hold their fire then.
            _gun = Skill<ViscousGunSkillDef>("Gun", typeof(FireGooGun), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;
            _alt = Skill<ViscousGunSkillDef>("Alt", typeof(FireAltGlob), "Weapon", 0f, InterruptPriority.Any);
            _alt.cancelSprintingOnActivation = true;
            _alt.mustKeyPress = false;

            _splatter = Skill<SkillDef>("Splatter", typeof(ThrowSplatter), "Weapon", ViscousData.SplatterCooldown, InterruptPriority.Skill);
            _splatter.mustKeyPress = true;
            _splatter.cancelSprintingOnActivation = false;
            _punch = Skill<SkillDef>("Punch", typeof(PuddlePunch), "Weapon", ViscousData.PunchCooldown, InterruptPriority.Skill);
            _punch.mustKeyPress = true;
            _punch.cancelSprintingOnActivation = false;

            _cube = Skill<SkillDef>("Cube", typeof(CubeCast), "Cube", ViscousData.CubeCooldown, InterruptPriority.Skill);
            _cube.mustKeyPress = true;
            _cube.cancelSprintingOnActivation = false;

            _ball = Skill<ViscousBallSkillDef>("Ball", typeof(GooBallCast), "Body", ViscousData.BallCooldown, InterruptPriority.PrioritySkill);
            _ball.keywordTokens = new[] { "KEYWORD_HEAVY" }; // RoR2's own Heavy keyword (Loader)
            _ball.mustKeyPress = true;
            _ball.cancelSprintingOnActivation = false;
            _ball.beginSkillCooldownOnSkillEnd = true; // Deadlock: the cooldown starts when he pops out
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = ViscousData.BaseHealth;
            cb.levelMaxHealth = ViscousData.HealthPerLevel;
            cb.baseRegen = ViscousData.BaseRegen;
            cb.levelRegen = ViscousData.BaseRegen * 0.2f;
            cb.baseArmor = ViscousData.BaseArmor;
            cb.baseDamage = ViscousData.BaseDamage;
            cb.levelDamage = ViscousData.DamagePerLevel;
            cb.baseMoveSpeed = ViscousData.BaseSpeed;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!, _alt!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _splatter!, _punch!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _cube!);
            locator.special = Kit.Slot(body, BodyName, "Special", _ball!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, ViscousData.Movement);
            Kit.AddMachine(body, "Cube"); // The Cube's tap-or-hold runs alongside everything
            body.AddComponent<ViscousCubeReticle>();

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = ViscousData.Hero;
            model.gameObject.AddComponent<ViscousBallLook>();
            body.AddComponent<ViscousBallHurtbox>(); // the hurtbox becomes the ball while he rolls
            Kit.MuteStandIn(body, model.gameObject, ViscousData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleViscousDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleViscousBLUE", Token + "SKIN_BLUE", BlueSkinIcon));

            Kit.BiggerCamera(body, "ccpCursedAppleViscous", 0.5f, 1.2f); // pivot up (m), distance (x Commando's) for the bigger model
            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleViscousDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = ViscousData.Hero;
                swap.IsDisplay = true;
                cm.gameObject.AddComponent<ViscousBallLook>(); // keeps the ball hidden on the pedestal
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleViscousDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleViscousDISPLAYBLUE", Token + "SKIN_BLUE", BlueSkinIcon));
            }
            display.AddComponent<ViscousDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleViscous";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 106f; // after Victor
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(ViscousData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            void Set(SkillDef? s, string path, params BuffDef?[] buffs)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                foreach (BuffDef? b in buffs) if (b != null) b.iconSprite = sprite;
            }
            Set(_ball, ViscousData.IconBall, BallBuff);
            Set(_splatter, ViscousData.IconSplatter);
            Set(_punch, ViscousData.IconPunch);
            Set(_cube, ViscousData.IconCube, CubeBuff, PostCubeBuff);
            Set(_alt, ViscousData.IconAlt);
            if (portrait != null)
            {
                _gun!.icon = Kit.Sprite(portrait);
                if (BodyPrefab != null)
                {
                    var locator = BodyPrefab.GetComponent<SkillLocator>();
                    var passive = locator.passiveSkill;
                    passive.icon = Kit.Sprite(portrait);
                    locator.passiveSkill = passive;
                }
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Viscous] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Viscous");
            LanguageAPI.Add(Token + "SUBTITLE", "The Goo");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_BLUE", "Bluescous"); // user 3.0
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Viscous is a living blob of goo in a diving suit, here to help, mostly.<color=#CCD3E0>\n\n" +
                "< ! > Goo Ball steers where you look. The faster it rolls, the harder it hits.\n\n" +
                "< ! > Puddle Punch launches everything above it, you included.\n\n" +
                "< ! > Tap The Cube to protect an ally, hold it to protect yourself.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, still dripping.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he died, a puddle on a strange world.");
            LanguageAPI.Add(Token + "LORE", "Goo.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Goo");
            LanguageAPI.Add(Token + "PASSIVE_DESC", Kit.MovementLine(ViscousData.Movement, alone: true));

            LanguageAPI.Add(Token + "GUN_NAME", "Goo Gun");
            LanguageAPI.Add(Token + "GUN_DESC", $"Fire for <style=cIsDamage>{D(ViscousData.GunDamage)} damage</style>.");

            LanguageAPI.Add(Token + "ALT_NAME", "Goo Glob");
            LanguageAPI.Add(Token + "ALT_DESC",
                $"Lob a glob of goo that bursts for <style=cIsDamage>{D(ViscousData.AltDamage)} damage</style>.");

            LanguageAPI.Add(Token + "SPLATTER_NAME", "Splatter");
            LanguageAPI.Add(Token + "SPLATTER_DESC",
                $"Throw a bouncing ball of goo that bursts for <style=cIsDamage>{D(ViscousData.SplatterDamage)} damage</style> each bounce, <style=cIsUtility>{ViscousData.SplatterBounces} times</style>. It passes through enemies, hitting each for <style=cIsDamage>{D(ViscousData.SplatterPassDamage)} damage</style>.");
            LanguageAPI.Add(Token + "PUNCH_NAME", "Puddle Punch");
            LanguageAPI.Add(Token + "PUNCH_DESC",
                $"Punch up from the ground where you aim for <style=cIsDamage>{D(ViscousData.PunchDamage)} damage</style>, <style=cIsUtility>launching</style> everything above it, you included.");

            LanguageAPI.Add(Token + "CUBE_NAME", "The Cube");
            LanguageAPI.Add(Token + "CUBE_DESC",
                $"<style=cIsUtility>Tap</style> to encase an ally, <style=cIsUtility>hold</style> to encase yourself, for {ViscousData.CubeDuration:0}s: <style=cIsUtility>cleansed</style>, <style=cIsUtility>immune to damage</style> and <style=cIsHealing>healing {ViscousData.CubeHeal * 100f:0}% per second</style>. " +
                $"Afterwards, gain <style=cIsUtility>+{ViscousData.PostCubeSpeed:0.#} m/s</style> and faster stamina.");

            LanguageAPI.Add(Token + "BALL_NAME", "Goo Ball");
            LanguageAPI.Add(Token + "BALL_DESC",
                $"<style=cIsDamage>Heavy</style>. Roll as a goo ball for <style=cIsUtility>{ViscousData.BallDuration:0}s</style>, dealing <style=cIsDamage>{D(ViscousData.BallDamage)} damage</style> (+{D(ViscousData.BallHeavyDamage)} per m/s, more the bigger the ball) and <style=cIsUtility>stunning</style> enemies in your way. " +
                $"Hold Primary to roll faster. Take <style=cIsUtility>{ViscousData.BallResist * 100f:0}% less damage</style> while rolling.");
        }
    }

    /// <summary>
    /// Goo Ball with charges to spare: Special while rolling ends the ball, and that same press (still "just pressed" for
    /// the next physics ticks) used to roll the next ball at once (user). It can't start while he is a ball, nor in the
    /// moment after one ends, so the stocked charge waits for a new press.
    /// </summary>
    public class ViscousBallSkillDef : SkillDef
    {
        private const float Grace = 0.25f;
        private class EndTime { public float At = -10f; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, EndTime> Ended =
            new System.Runtime.CompilerServices.ConditionalWeakTable<GameObject, EndTime>();

        public static void MarkEnded(GameObject body) => Ended.GetOrCreateValue(body).At = Time.time;

        public override bool CanExecute(GenericSkill skillSlot)
        {
            GameObject? body = skillSlot.characterBody != null ? skillSlot.characterBody.gameObject : null;
            if (body != null && (ViscousNet.IsBall(body) || Ended.TryGetValue(body, out EndTime e) && Time.time - e.At < Grace))
                return false;
            return base.CanExecute(skillSlot);
        }
    }

    /// <summary>The guns hold their fire while he is a ball: Primary is the ball's "Go Fast" then.</summary>
    public class ViscousGunSkillDef : SkillDef
    {
        private static bool Ball(GenericSkill slot) => ViscousNet.IsBall(slot.characterBody != null ? slot.characterBody.gameObject : null);

        public override bool CanExecute(GenericSkill skillSlot) => !Ball(skillSlot) && base.CanExecute(skillSlot);
        public override bool IsReady(GenericSkill skillSlot) => base.IsReady(skillSlot) && !Ball(skillSlot);
    }

    /// <summary>
    /// Viscous' character-select entrance (user): he is on the pedestal inside The Cube for its usual 3 s, then it pops
    /// and he says one of his select lines (the base class plays it when this ends).
    /// </summary>
    public class ViscousDisplayIntro : DeadlockDisplayIntro
    {
        // The cube is a scene object of its own: picking another survivor mid-intro destroys the display (and stops this
        // coroutine), so the cube and its sound are cleaned up from OnDisable too (playtest: it stayed forever).
        private GameObject? _cube;
        private DeadlockVoice? _loop;

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            yield return null;
            _cube = ViscousCubes.MakeVisual(ViscousCubes.FitSize(gameObject, GetComponentInChildren<CharacterModel>()) ?? 2.4f * Scale);
            _cube.transform.SetPositionAndRotation(Home, HomeRotation);
            DeadlockSounds.Play("viscous_cube_cast", Home);
            _loop = DeadlockSounds.Loop("viscous_cube_loop", _cube, ViscousData.CubeDuration + 0.5f);
            yield return new WaitForSeconds(ViscousData.CubeDuration);
            CleanUp();
            DeadlockSounds.Play("viscous_cube_end", Home);
            if (ViscousSurvivor.SplashEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.SplashEffect, new EffectData { origin = Home, scale = 3f * Scale }, false);
        }

        private void CleanUp()
        {
            DeadlockSounds.Stop(_loop);
            _loop = null;
            if (_cube != null) Destroy(_cube);
            _cube = null;
        }

        private void OnDisable() => CleanUp();
    }

    public class ViscousIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { ViscousSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Viscous] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
