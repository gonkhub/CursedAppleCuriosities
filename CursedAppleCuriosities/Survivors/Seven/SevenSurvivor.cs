using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Seven;
using R2API;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Seven from Deadlock as a RoR2 survivor (same structure as Abrams). The body is a clone of Commando's rig; the
    /// Deadlock model, animations and icons come from the player's own Deadlock install at runtime. Without Deadlock,
    /// Seven plays the same with the stand-in model.
    /// </summary>
    public static class SevenSurvivor
    {
        public const string Token = "CURSEDAPPLE_SEVEN_";
        public const string BodyName = "CursedAppleSevenBody";
        public const string MuzzleName = "SevenMuzzle";
        public static readonly Color Color = new Color(1f, 0.55f, 0.15f); // character select / UI: orange (user, 2026-10-12)
        /// <summary>Skin icon in the hero's own palette (prison orange, dark grey, canvas, his electric blue), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.85f, 0.45f, 0.15f), new Color(0.24f, 0.24f, 0.24f), new Color(0.75f, 0.67f, 0.5f), new Color(0.2f, 0.3f, 0.85f));
        private static Sprite? _defaultSkinIcon;
        /// <summary>Seven's lightning is orange (user): his effects are recoloured to this hue.</summary>
        public const float Hue = 0.07f;
        public static readonly Color Orange = Color.HSVToRGB(Hue, 0.85f, 1f);
        /// <summary>See-through range sphere (Helfire Tincture's): Lightning Ball's true damage radius.</summary>
        public static Material? RangeMaterial;

        public static GameObject? BodyPrefab, DisplayPrefab, BallProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? SurgeBuff, StormBuff;

        // Vanilla effects reused by the states (loaded once).
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, StrikeEffect, BoltEffect;
        public static Material IndicatorMaterial = null!;

        private static SkillDef? _gun, _ball, _static, _surge, _storm;
        internal static SkillDef? StaticSkill => _static;

        public static void Init(ConfigFile config, GameObject host)
        {
            SevenData.Bind(config);
            foreach (DeadlockSound s in SevenData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { SevenData.IconPortrait, SevenData.IconBall, SevenData.IconStatic, SevenData.IconSurge, SevenData.IconStorm })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(SevenModel.Def);
            DeadlockInstall.WantModel(SevenModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            BallProjectile = CreateBall();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            SevenItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            SevenNet.Register();
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (SurgeBuff != null && body.HasBuff(SurgeBuff) && body.baseMoveSpeed > 0f)
                    args.moveSpeedMultAdd += SevenData.SurgeMoveSpeed / body.baseMoveSpeed;
            };
            DeadlockHeroVoice.Track(BodyName, SevenData.Hero);
            EquipmentFx.RegisterBuffFx(SurgeBuff, null, "seven_surge_loop");
            EquipmentFx.RegisterBuffFx(StormBuff, null, "seven_storm_loop");
            EquipmentFx.RegisterBuffFx(StormBuff, null, "seven_storm_aoe_loop");
            // Ping lines wait for the user's sound review (DeadlockPingVoice registers Deadlock VO itself).

            host.AddComponent<SevenIcons>();
        }

        // ---------------- assets ----------------

        /// <summary>Addressables keys moved under RoR2/Base/Characters/... in places; the loader tries both spellings.</summary>
        internal static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Seven", keys);

        private static void LoadAssets()
        {
            // Electric bullets (user): Artificer's unused lightning-laser tracer and her lightning sparks, in Seven's hue;
            // Commando's tracer and hitspark if those are missing
            TracerEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Junk/Characters/Mage/TracerMageLightningLaser.prefab", "RoR2/Junk/Mage/TracerMageLightningLaser.prefab",
                "RoR2/Base/Commando/TracerCommandoDefault.prefab", "RoR2/Base/Characters/Commando/TracerCommandoDefault.prefab"), "CursedAppleSevenTracer", Hue, effect: true);
            Thin(TracerEffect, 0.15f); // Artificer's laser is a thick beam: a hairline of it (user)
            HitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/Stylized(Omni)/OmniImpactVFXLightning.prefab",
                "RoR2/Base/Commando/HitsparkCommando.prefab", "RoR2/Base/Characters/Commando/HitsparkCommando.prefab"), "CursedAppleSevenHit", Hue, effect: true);
            MuzzleEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Mage/MuzzleflashMageLightning.prefab", "RoR2/Base/Characters/Mage/Skills/MuzzleflashMageLightning.prefab"), "CursedAppleSevenMuzzle", Hue, effect: true);
            StrikeEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Lightning/LightningStrikeImpact.prefab", "RoR2/Base/Equipment/Lightning/LightningStrikeImpact.prefab"), "CursedAppleSevenStrike", Hue, effect: true);
            BoltEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/LightningStrikeOnHit/SimpleLightningStrikeImpact.prefab", "RoR2/Base/Items/LightningStrikeOnHit/SimpleLightningStrikeImpact.prefab"), "CursedAppleSevenBolt", Hue, effect: true);
            RangeMaterial = TryLoad<Material>("RoR2/Base/BurnNearby/matHelfireRangeIndicator.mat", "RoR2/Base/Equipment/BurnNearby/matHelfireRangeIndicator.mat",
                "RoR2/Base/WardOnLevel/matWarbannerSphereIndicator.mat", "RoR2/Base/Items/WardOnLevel/matWarbannerSphereIndicator.mat");
            if (RangeMaterial != null)
            {
                RangeMaterial = new Material(RangeMaterial) { name = "matCursedAppleSevenBallRange" };
                if (RangeMaterial.HasProperty("_TintColor")) RangeMaterial.SetColor("_TintColor", new Color(Orange.r, Orange.g, Orange.b, RangeMaterial.GetColor("_TintColor").a));
            }
            Material? laser = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            IndicatorMaterial = laser != null ? new Material(laser) : new Material(Addressables.LoadAssetAsync<Material>("RoR2/Base/Loader/matLoaderHookRope.mat").WaitForCompletion());
        }

        /// <summary>Narrows a beam effect: its line renderers and trails to a fraction of their width, its particles to a fraction of their size.</summary>
        private static void Thin(GameObject? effect, float scale)
        {
            if (effect == null) return;
            foreach (LineRenderer lr in effect.GetComponentsInChildren<LineRenderer>(true)) lr.widthMultiplier *= scale;
            foreach (TrailRenderer tr in effect.GetComponentsInChildren<TrailRenderer>(true)) tr.widthMultiplier *= scale;
            foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                if (main.startSize3D) { main.startSizeXMultiplier *= scale; main.startSizeYMultiplier *= scale; main.startSizeZMultiplier *= scale; }
                else main.startSizeMultiplier *= scale;
            }
        }

        private static void CreateBuffs()
        {
            SurgeBuff = Buff("CursedAppleSevenSurge", new Color(0.45f, 0.8f, 1f));
            StormBuff = Buff("CursedAppleSevenStorm", new Color(0.3f, 0.45f, 1f));
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

        /// <summary>Lightning Ball: Artificer's Nano-Bomb orb with its own behaviour stripped; SevenBall shocks, slows and sticks it.</summary>
        private static GameObject? CreateBall()
        {
            GameObject? p = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/Mage/MageLightningBombProjectile.prefab", "RoR2/Base/Characters/Mage/Skills/MageLightningBombProjectile.prefab"),
                "CursedAppleSevenLightningBall", typeof(SevenBall), SevenData.BallSpeed, SevenData.BallLifetime + 0.5f, gravity: false, triggers: true);
            var pc = p != null ? p.GetComponent<ProjectileController>() : null;
            if (pc != null && pc.ghostPrefab != null)
            {
                // The orb itself: orange, and bigger (the range sphere around it shows the real reach).
                GameObject ghost = Kit.Recoloured(pc.ghostPrefab, "CursedAppleSevenLightningBallGhost", Hue, effect: false)!;
                ghost.transform.localScale *= 2.7f; // 1.8x, then +50% with the radius (1.9)
                pc.ghostPrefab = ghost;
            }
            return p;
        }

        // ---------------- states and skills ----------------

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireBurst), typeof(LightningBall), typeof(StaticCharge), typeof(PowerSurge), typeof(StormCloud) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleSeven", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireBurst), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _ball = Skill<SkillDef>("Ball", typeof(LightningBall), "Weapon", SevenData.BallCooldown, InterruptPriority.Skill);
            _ball.mustKeyPress = true;
            _ball.cancelSprintingOnActivation = true;

            _static = Skill<SkillDef>("Static", typeof(StaticCharge), "Weapon", SevenData.StaticCooldown, InterruptPriority.Skill);
            _static.mustKeyPress = true;
            _static.cancelSprintingOnActivation = true;

            _surge = Skill<SkillDef>("Surge", typeof(PowerSurge), "Surge", SevenData.SurgeCooldown, InterruptPriority.Any);
            _surge.mustKeyPress = true;
            _surge.cancelSprintingOnActivation = false;

            _storm = Skill<SkillDef>("Storm", typeof(StormCloud), "Body", SevenData.StormCooldown, InterruptPriority.PrioritySkill);
            _storm.mustKeyPress = true;
            _storm.cancelSprintingOnActivation = true;
            _storm.beginSkillCooldownOnSkillEnd = true;
        }

        // ---------------- body ----------------

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = SevenData.BaseHealth;
            cb.levelMaxHealth = SevenData.HealthPerLevel;
            cb.baseArmor = SevenData.BaseArmor;
            cb.baseDamage = SevenData.BaseDamage;
            cb.levelDamage = SevenData.DamagePerLevel;
            cb.baseMoveSpeed = SevenData.BaseSpeed;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            Kit.AddMachine(body, "Surge"); // Power Surge runs alongside everything
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _ball!, _static!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _surge!);
            locator.special = Kit.Slot(body, BodyName, "Special", _storm!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            body.AddComponent<SevenStorm>();
            body.AddComponent<SevenSurgeEnd>();
            body.AddComponent<SevenStormFx>();
            body.AddComponent<SevenStaticReticle>();
            body.AddComponent<SevenStormLook>();
            DeadlockMovement.Apply(body, SevenData.Movement);

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = SevenData.Hero;
            Kit.MuteStandIn(body, model.gameObject, SevenData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleSevenDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon);

            Kit.BiggerCamera(body, "ccpCursedAppleSeven", 0.6f, 1.25f); // pivot up (m), distance (x Commando's) for the bigger model
            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleSevenDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = SevenData.Hero;
                swap.IsDisplay = true;
                cm.gameObject.AddComponent<SevenStormLook>(); // keeps the Storm Cloud skeleton hidden in the lobby
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleSevenDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon);
            }
            display.AddComponent<SevenDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleSeven";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 102f; // after Lash and Abrams
            ContentAddition.AddSurvivorDef(Survivor);
        }

        // ---------------- icons (from Deadlock, once extracted) ----------------

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(SevenData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Set(_ball, SevenData.IconBall);
            Set(_static, SevenData.IconStatic);
            Sprite? surge = Set(_surge, SevenData.IconSurge);
            if (surge != null && SurgeBuff != null) SurgeBuff.iconSprite = surge;
            Sprite? storm = Set(_storm, SevenData.IconStorm);
            if (storm != null && StormBuff != null) StormBuff.iconSprite = storm;
            if (portrait != null && BodyPrefab != null)
            {
                _gun!.icon = Kit.Sprite(portrait);
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = _gun.icon;
                locator.passiveSkill = passive;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Seven] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Seven");
            LanguageAPI.Add(Token + "SUBTITLE", "The Storm That Would Not Die");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Seven is a condemned occultist who laughed his way out of his own execution.<color=#CCD3E0>\n\n" +
                "< ! > Throw Lightning Ball into a crowd: it slows down to keep shocking them.\n\n" +
                "< ! > During Power Surge, keep your bursts on target to chain lightning through the pack.\n\n" +
                "< ! > Storm Cloud only hurts what Seven can see. Float somewhere with a view.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, laughing.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, the storm finally spent.");
            LanguageAPI.Add(Token + "LORE", "No ward could hold him, and no planet will either.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Electric Current");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                "Sprints automatically out of combat; dash, air jump and slide. During <style=cIsUtility>Power Surge</style> he keeps sprinting until hit.");

            LanguageAPI.Add(Token + "GUN_NAME", "Cold Calculus");
            LanguageAPI.Add(Token + "GUN_DESC",
                $"Fire a <style=cIsDamage>3-round burst</style> for <style=cIsDamage>3x{D(SevenData.GunDamage)} damage</style>.");

            LanguageAPI.Add(Token + "BALL_NAME", "Lightning Ball");
            LanguageAPI.Add(Token + "BALL_DESC",
                $"Throw a slow ball of lightning that <style=cIsUtility>shocks</style> nearby enemies for <style=cIsDamage>{D(SevenData.BallDps)} damage per second</style>. It sticks to walls.");

            LanguageAPI.Add(Token + "STATIC_NAME", "Static Charge");
            LanguageAPI.Add(Token + "STATIC_DESC",
                $"Charge an enemy you target. After {SevenData.StaticDelay:0.#}s, deal <style=cIsDamage>{D(SevenData.StaticDamage)} damage</style> and <style=cIsDamage>stun</style> everything near them for {SevenData.StaticStun:0.#}s.");

            LanguageAPI.Add(Token + "SURGE_NAME", "Power Surge");
            LanguageAPI.Add(Token + "SURGE_DESC",
                $"For <style=cIsUtility>{SevenData.SurgeDuration:0}s</style>, gain <style=cIsUtility>+{SevenData.SurgeMoveSpeed:0.#} m/s</style> and your bursts <style=cIsDamage>chain lightning</style> through up to {SevenData.SurgeChainCount} enemies for <style=cIsDamage>{D(SevenData.SurgeChainDamage)}</style>, growing each jump.");

            LanguageAPI.Add(Token + "STORM_NAME", "Storm Cloud");
            LanguageAPI.Add(Token + "STORM_DESC",
                $"<style=cIsUtility>Float</style> and channel a storm for <style=cIsUtility>{SevenData.StormChannel:0}s</style> that grows from {SevenData.StormInitialRadius:0} m to cover <style=cIsDamage>the whole map</style>, striking every enemy you can see with lightning for <style=cIsDamage>{D(SevenData.StormDpsStart)}-{D(SevenData.StormDpsEnd)} damage per second</style>. " +
                $"Primary calls a <style=cIsDamage>Lightning Strike</style> for {D(SevenData.StrikeDamage)}.");
        }
    }

    /// <summary>Waits for the Deadlock reader, then swaps placeholder icons for Seven's own.</summary>
    public class SevenIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { SevenSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Seven] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
