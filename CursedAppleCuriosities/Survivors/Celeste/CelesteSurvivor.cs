using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Celeste;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Celeste from Deadlock (hero_unicorn) as a RoR2 survivor, same structure as Vyper and Victor. The body is a clone of
    /// Commando's rig; the Deadlock model, animations, sounds and icons come from the player's own Deadlock install at runtime.
    /// Kit (picked 2026-10-12 while the user was away, for their review): M1 Carny Life, M2 Light Eater / alt Radiant Daggers,
    /// Utility Dazzling Trick, Special Shining Wonder.
    /// </summary>
    public static class CelesteSurvivor
    {
        public const string Token = "CURSEDAPPLE_CELESTE_";
        public const string BodyName = "CursedAppleCelesteBody";
        public const string MuzzleName = "CelesteMuzzle";
        public static readonly Color Color = new Color(0.95f, 0.55f, 0.82f); // character select / UI: her pink hair
        public static readonly Color LightColor = new Color(1f, 0.6f, 0.9f, 0.85f);
        /// <summary>Skin icon in the hero's own palette (pink hair, brown leather jacket, plum skirt, pale skin), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.93f, 0.6f, 0.85f), new Color(0.27f, 0.19f, 0.15f), new Color(0.58f, 0.24f, 0.4f), new Color(0.86f, 0.72f, 0.64f));
        private static Sprite? _defaultSkinIcon;
        private static Sprite MintSkinIcon => _mintSkinIcon ??= Kit.SkinIcon(new Color(0.6f, 0.93f, 0.82f), new Color(0.27f, 0.19f, 0.15f), new Color(0.24f, 0.55f, 0.5f), new Color(0.86f, 0.72f, 0.64f));
        private static Sprite? _mintSkinIcon;
        public const float ProjectileHue = 0.72f; // her blades and orb: light blue-violet (user)
        public const float Hue = 0.9f;       // her light is pink, as her hair

        public static GameObject? BodyPrefab, DisplayPrefab, DaggerProjectile, OrbProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? MarkBuff, RadianceBuff, PrismBuff, OrbSlow;
        public static BodyIndex BodyIndex = BodyIndex.None;

        public static GameObject? HitEffect, FlareEffect, BeamEffect, BeamWarning, ShatterEffect, BreakEffect, OrbHitEffect;
        public static Material? LineMaterial;

        private static SkillDef? _blade, _flare, _beam, _prism, _orb;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Celeste", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            CelesteData.Bind(config);
            foreach (DeadlockSound s in CelesteData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { CelesteData.IconPortrait, CelesteData.IconFlare, CelesteData.IconShield, CelesteData.IconBeam, CelesteData.IconOrb, CelesteData.IconGun })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(CelesteModel.Def);
            DeadlockInstall.WantModel(CelesteModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            CreateProjectiles();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            CelesteItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            CelesteNet.Register();
            DeadlockHeroVoice.Track(BodyName, CelesteData.Hero);
            EquipmentFx.RegisterBuffFx(PrismBuff, "RoR2/Base/LunarGolem/LunarDefense.prefab", "celeste_prism_loop"); // a crystal shell
            EquipmentFx.RegisterBuffFx(MarkBuff, "RoR2/Base/Merc/MercExposeEffect.prefab", "celeste_mark_loop"); // a mark over marked enemies
            RoR2Application.onLoad += () => BodyIndex = BodyCatalog.FindBodyIndex(BodyName);

            host.AddComponent<CelesteIcons>();
        }

        private static void LoadAssets()
        {
            HitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab"), "CursedAppleCelesteHit", Hue, effect: true);
            FlareEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/ArtifactShell/ExplosionSolarFlare.prefab", "RoR2/Base/Captain/CaptainTazerNova.prefab"), "CursedAppleCelesteFlare", Hue, effect: true);
            BeamEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Lightning/LightningStrikeImpact.prefab"), "CursedAppleCelesteBeam", Hue, effect: true);
            ShatterEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Captain/CaptainTazerNova.prefab"), "CursedAppleCelesteShatter", Hue, effect: true);
            BreakEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/ShieldBreakEffect.prefab");
            OrbHitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/ArtifactShell/ExplosionSolarFlare.prefab", "RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab"), "CursedAppleCelesteOrbHit", ProjectileHue, effect: true);
            Material? laser = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            LineMaterial = laser != null ? new Material(laser) : null;
            if (LineMaterial != null) // the laser material ignores line colours: tint it pink
                foreach (string prop in new[] { "_TintColor", "_Color" })
                    if (LineMaterial.HasProperty(prop)) LineMaterial.SetColor(prop, LightColor);
            BeamWarning = CreateBeamWarning();
        }

        /// <summary>Radiant Daggers' warning as an effect of our own (lines drawn by CelesteBeamWarning), so every client sees it.</summary>
        private static GameObject CreateBeamWarning()
        {
            var go = new GameObject("CursedAppleCelesteBeamWarningSource");
            go.AddComponent<EffectComponent>().applyScale = false;
            go.AddComponent<VFXAttributes>().DoNotPool = true;
            go.AddComponent<CelesteBeamWarning>();
            GameObject prefab = go.InstantiateClone("CursedAppleCelesteBeamWarning", false);
            UnityEngine.Object.Destroy(go);
            ContentAddition.AddEffect(prefab);
            return prefab;
        }

        private static void CreateBuffs()
        {
            MarkBuff = Buff("CursedAppleCelesteMarked", new Color(1f, 0.85f, 0.4f), debuff: true, stack: false);
            RadianceBuff = Buff("CursedAppleCelesteRadiance", new Color(1f, 0.92f, 0.6f), debuff: false, stack: true);
            PrismBuff = Buff("CursedAppleCelestePrism", new Color(0.8f, 0.7f, 1f), debuff: false, stack: false);
            OrbSlow = Buff("CursedAppleCelesteDazzled", new Color(1f, 0.9f, 0.55f), debuff: true, stack: false);
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

        private static void CreateProjectiles()
        {
            DaggerProjectile = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/Bandit2/Bandit2ShivProjectile.prefab", "RoR2/Base/Characters/Bandit2/Skills/Bandit2ShivProjectile.prefab"),
                "CursedAppleCelesteBlade", typeof(CelesteDagger), CelesteData.DaggerSpeed, CelesteData.DaggerLifetime + 0.5f, gravity: false, triggers: false, steer: false);
            RecolourGhost(DaggerProjectile, "CursedAppleCelesteBladeGhost", 0.45f); // much smaller (user)
            // Shining Wonder: the Artifact shell's seeking solar flare, steered by CelesteOrb (it passes through everything).
            OrbProjectile = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/ArtifactShell/ArtifactShellSeekingSolarFlare.prefab", "RoR2/Base/Mage/MageLightningBombProjectile.prefab"),
                "CursedAppleCelesteOrb", typeof(CelesteOrb), CelesteData.OrbSpeed, 40f, gravity: false, triggers: true, steer: false);
            var orbPc = OrbProjectile != null ? OrbProjectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (orbPc != null && orbPc.ghostPrefab == null) // the seeking flare draws itself: give it the flare's own ghost
                orbPc.ghostPrefab = TryLoad<GameObject>("RoR2/Base/ArtifactShell/SolarFlareGhost.prefab", "RoR2/Base/Mage/MageLightningBombGhost.prefab");
            RecolourGhost(OrbProjectile, "CursedAppleCelesteOrbGhost", 0.8f);    // smaller (user)
        }

        private static void RecolourGhost(GameObject? projectile, string name, float scale)
        {
            var pc = projectile != null ? projectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (pc == null || pc.ghostPrefab == null) return;
            GameObject? ghost = Kit.Recoloured(pc.ghostPrefab, name, ProjectileHue, effect: false);
            if (ghost == null) return;
            ghost.transform.localScale *= scale;
            pc.ghostPrefab = ghost;
        }

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(ThrowBlade), typeof(LightEater), typeof(RadiantDaggers), typeof(DazzlingTrick), typeof(ShiningWonder) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleCeleste", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _blade = Skill<SkillDef>("Blade", typeof(ThrowBlade), "Weapon", 0f, InterruptPriority.Any);
            _blade.cancelSprintingOnActivation = true;
            _blade.mustKeyPress = false;

            _flare = Skill<SkillDef>("Flare", typeof(LightEater), "Weapon", CelesteData.FlareCooldown, InterruptPriority.Skill);
            _flare.mustKeyPress = true;
            _flare.cancelSprintingOnActivation = true;

            _beam = Skill<SkillDef>("Beam", typeof(RadiantDaggers), "Weapon", CelesteData.BeamCooldown, InterruptPriority.Skill);
            _beam.mustKeyPress = true;
            _beam.cancelSprintingOnActivation = false;

            _prism = Skill<SkillDef>("Prism", typeof(DazzlingTrick), "Weapon", CelesteData.PrismCooldown, InterruptPriority.Skill);
            _prism.mustKeyPress = true;
            _prism.cancelSprintingOnActivation = false;

            _orb = Skill<SkillDef>("Orb", typeof(ShiningWonder), "Weapon", CelesteData.OrbCooldown, InterruptPriority.Skill);
            _orb.mustKeyPress = true;
            _orb.cancelSprintingOnActivation = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = CelesteData.BaseHealth;
            cb.levelMaxHealth = CelesteData.HealthPerLevel;
            cb.baseRegen = CelesteData.BaseRegen;
            cb.levelRegen = CelesteData.BaseRegen * 0.2f;
            cb.baseArmor = CelesteData.BaseArmor;
            cb.baseDamage = CelesteData.BaseDamage;
            cb.levelDamage = CelesteData.DamagePerLevel;
            cb.baseMoveSpeed = CelesteData.BaseSpeed;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _blade!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _flare!, _beam!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _prism!);
            locator.special = Kit.Slot(body, BodyName, "Special", _orb!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, CelesteData.Movement);
            body.AddComponent<CelesteGlow>(); // a faint purple light around her (user)

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = CelesteData.Hero;
            Kit.MuteStandIn(body, model.gameObject, CelesteData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleCelesteDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleCelesteMINT", Token + "SKIN_MINT", MintSkinIcon));

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleCelesteDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = CelesteData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleCelesteDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon, ("skinCursedAppleCelesteDISPLAYMINT", Token + "SKIN_MINT", MintSkinIcon));
            }
            display.AddComponent<CelesteDisplayIntro>();
            display.AddComponent<CelesteGlow>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleCeleste";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 107f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(CelesteData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? flare = Set(_flare, CelesteData.IconFlare);
            if (flare != null && MarkBuff != null) MarkBuff.iconSprite = flare;
            Sprite? beam = Set(_beam, CelesteData.IconBeam);
            if (beam != null && RadianceBuff != null) RadianceBuff.iconSprite = beam;
            Sprite? prism = Set(_prism, CelesteData.IconShield);
            if (prism != null && PrismBuff != null) PrismBuff.iconSprite = prism;
            Sprite? orb = Set(_orb, CelesteData.IconOrb);
            if (orb != null && OrbSlow != null) OrbSlow.iconSprite = orb;
            Sprite? gun = Set(_blade, CelesteData.IconGun);
            if (gun == null && portrait != null) _blade!.icon = Kit.Sprite(portrait);
            if (BodyPrefab != null && portrait != null)
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = Kit.Sprite(portrait);
                locator.passiveSkill = passive;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Celeste");
            LanguageAPI.Add(Token + "SUBTITLE", "The Carny");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_MINT", "Mint");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Celeste ran away with the carnival and never stopped performing. The show goes on, wherever the tent lands.<color=#CCD3E0>\n\n" +
                "< ! > Carny Life's blades arc, curve onto enemies near your aim and bounce once off walls. Aim close, not exact.\n\n" +
                "< ! > Light Eater first: marked enemies take extra damage from every blade and heal you for part of all you deal them.\n\n" +
                "< ! > Dazzling Trick rewards being hit. Raise it in the thick of a fight and let them break it.\n\n" +
                "< ! > Shining Wonder bounces off your own prism: raise Dazzling Trick in a crowd and let the orb ping-pong.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so she left, to a standing ovation.");
            LanguageAPI.Add(Token + "FAILURE", "..and so she vanished, her final trick.");
            LanguageAPI.Add(Token + "LORE", "Every show needs an encore.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Showstopper");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                "Celeste falls <style=cIsUtility>25% slower</style> with <style=cIsUtility>stronger air control</style>. She hangs in the air while she casts Light Eater and floats down while aiming Shining Wonder. " +
                $"She has <style=cIsUtility>{CelesteData.Movement.StaminaPips} stamina</style>, <style=cIsUtility>sprints automatically</style> out of combat, and can <style=cIsUtility>air jump</style> and <style=cIsUtility>air dash</style> twice.");

            LanguageAPI.Add(Token + "BLADE_NAME", "Carny Life");
            LanguageAPI.Add(Token + "BLADE_DESC",
                $"Throw a blade of light for <style=cIsDamage>{D(CelesteData.DaggerDamage)} damage</style>. It <style=cIsUtility>homes</style> onto enemies near its path " +
                "and <style=cIsUtility>bounces</style> once off walls.");

            LanguageAPI.Add(Token + "FLARE_NAME", "Light Eater");
            LanguageAPI.Add(Token + "FLARE_DESC",
                $"Flare in a cone for <style=cIsDamage>{D(CelesteData.FlareDamage)} damage</style>, <style=cIsUtility>marking</style> enemies for {CelesteData.MarkSeconds:0}s. " +
                $"Your blades deal <style=cIsDamage>+{D(CelesteData.MarkDaggerBonus)} damage</style> to marked enemies, and you <style=cIsHealing>heal {D(CelesteData.MarkLifesteal)}</style> of all damage you deal them.");

            LanguageAPI.Add(Token + "BEAM_NAME", "Radiant Daggers");
            LanguageAPI.Add(Token + "BEAM_DESC",
                $"Call a beam of light onto a spot up to {CelesteData.BeamRange:0}m away. After <style=cIsUtility>{CelesteData.BeamDelay:0.#}s</style> it explodes for " +
                $"<style=cIsDamage>{D(CelesteData.BeamDamage)} damage</style> in {CelesteData.BeamRadius:0}m. Each enemy hit gives <style=cIsDamage>+{D(CelesteData.RadianceDamage)} damage</style> " +
                $"for {CelesteData.RadianceSeconds:0}s, up to {CelesteData.RadianceMaxStacks} times.");

            LanguageAPI.Add(Token + "PRISM_NAME", "Dazzling Trick");
            LanguageAPI.Add(Token + "PRISM_DESC",
                $"Surround yourself with a prism: <style=cIsHealing>{D(CelesteData.PrismBarrier)} barrier</style> for {CelesteData.PrismSeconds:0}s. " +
                $"If it's <style=cIsDamage>broken</style>, it bursts for <style=cIsDamage>{D(CelesteData.PrismDamage)} damage</style> in {CelesteData.PrismRadius:0}m and " +
                $"<style=cIsUtility>stuns</style> enemies for {CelesteData.PrismStun:0.##}s.");

            LanguageAPI.Add(Token + "ORB_NAME", "Shining Wonder");
            LanguageAPI.Add(Token + "ORB_DESC",
                $"Hold to aim, release to launch an orb of light for <style=cIsDamage>{D(CelesteData.OrbDamage)} damage</style> that <style=cIsUtility>slows</style> by {D(CelesteData.OrbSlow)}. " +
                $"It <style=cIsDamage>bounces</style> up to {CelesteData.OrbBounces} times to the nearest enemy within {CelesteData.OrbBounceRange:0.#}m, and waits a moment for a target when none is near. " +
                "It also bounces off your <style=cIsUtility>Dazzling Trick</style> prism, restarting it and restoring its barrier.");
        }
    }

    /// <summary>A faint purple light at her chest (user: "glow purple a little"), on the body and the lobby display.</summary>
    public class CelesteGlow : MonoBehaviour
    {
        public static readonly Color GlowColor = new Color(0.7f, 0.45f, 1f);
        private Light? _light;

        private void Start()
        {
            var go = new GameObject("CelesteGlow");
            go.transform.SetParent(transform, false);
            // The body's origin is its capsule's centre; the lobby display's is at its feet.
            go.transform.localPosition = Vector3.up * (GetComponent<CharacterBody>() != null ? 0.4f : 1.3f);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = GlowColor;
            _light.intensity = 1.1f;
            _light.range = 3.5f * Mathf.Max(0.1f, transform.lossyScale.y);
            _light.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (_light == null) return;
            _light.intensity = 1.1f + 0.15f * Mathf.Sin(Time.time * 2.2f); // a slow shimmer
            var hc = GetComponent<HealthComponent>();
            _light.enabled = hc == null || hc.alive;
        }
    }

    public class CelesteIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { CelesteSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Celeste] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
