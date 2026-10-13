using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.GreyTalon;
using R2API;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Grey Talon from Deadlock as a RoR2 survivor (same structure as Seven and Vyper). The body is a clone of Commando's rig;
    /// the Deadlock model, animations and icons come from the player's own Deadlock install at runtime. Without Deadlock,
    /// he plays the same with the stand-in model.
    /// </summary>
    public static class GreyTalonSurvivor
    {
        public const string Token = "CURSEDAPPLE_GREYTALON_";
        public const string BodyName = "CursedAppleGreyTalonBody";
        public const string MuzzleName = "GreyTalonMuzzle";
        public static readonly Color Color = new Color(0.13f, 0.42f, 0.2f); // character select / UI: dark green (user, 2026-10-12)
        /// <summary>Skin icon in the hero's own palette (green coat, orange scarf, teal, leather), not the stand-in's.</summary>
        private static Sprite DefaultSkinIcon => _defaultSkinIcon ??= Kit.SkinIcon(new Color(0.2f, 0.42f, 0.25f), new Color(0.85f, 0.36f, 0.15f), new Color(0.3f, 0.7f, 0.55f), new Color(0.58f, 0.38f, 0.22f));
        private static Sprite? _defaultSkinIcon;
        /// <summary>The owl's blast and glow are green (user).</summary>
        public const float OwlHue = 0.33f;

        public static GameObject? BodyPrefab, DisplayPrefab, ChargedProjectile;
        public static SurvivorDef? Survivor;
        /// <summary>Charging: Charged Shot drawn (slow walk, drawn-bow locomotion). OwlChannel: casting/steering the owl (no skills).
        /// OwlFlying: the owl is out. OwlTrophy: bosses the owl has killed this run.</summary>
        public static BuffDef? ChargingBuff, OwlChannelBuff, OwlFlyingBuff, OwlTrophyBuff;

        // Vanilla effects reused by the states (loaded once).
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, ChargedMuzzleEffect, ChargedHitEffect, OwlBlastEffect, OwlBlastWave, OwlGlow;

        private static SkillDef? _bow, _charged, _rain, _owl;

        public static void Init(ConfigFile config, GameObject host)
        {
            GreyTalonData.Bind(config);
            // His own Deadlock sounds wait for the user's review (their rule); the shared movement ones are already approved.
            foreach (DeadlockSound s in GreyTalonData.Sounds)
                if (GreyTalonData.SoundsApproved || GreyTalonData.PreApproved.Contains(s.Id)) DeadlockSounds.Register(s);
            foreach (string icon in new[] { GreyTalonData.IconPortrait, GreyTalonData.IconCharged, GreyTalonData.IconRain, GreyTalonData.IconOwl })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(GreyTalonModel.Def);
            DeadlockInstall.WantModel(GreyTalonModel.Def.ModelRequest());
            DeadlockInstall.WantModel(new DeadlockInstall.ModelRequest { Hero = GreyTalonData.OwlKey, DlModel = GreyTalonData.DlOwlModel });

            LoadAssets();
            CreateBuffs();
            ChargedProjectile = CreateChargedArrow();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            GreyTalonItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            GreyTalonNet.Register();
            // Charged Shot drawn: Deadlock's slow channel walk (ChannelMoveSpeed 1.5 m/s), scaled by his speed bonuses.
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ChargingBuff != null && body.HasBuff(ChargingBuff))
                    args.moveSpeedReductionMultAdd += GreyTalonData.BaseSpeed / GreyTalonData.ChargedMoveSpeed - 1f;
            };
            DeadlockHeroVoice.Track(BodyName, GreyTalonData.Hero);
            // Ping lines wait for the user's sound review (DeadlockPingVoice registers Deadlock VO itself).

            host.AddComponent<GreyTalonIcons>();
        }

        // ---------------- assets ----------------

        internal static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("GreyTalon", keys);

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Huntress/TracerHuntressSnipe.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/Muzzleflash1.prefab");
            ChargedMuzzleEffect = TryLoad<GameObject>("RoR2/DLC1/Railgunner/MuzzleflashRailgun.prefab");
            ChargedHitEffect = TryLoad<GameObject>("RoR2/DLC1/Railgunner/ImpactRailgun.prefab");
            // The owl's blast (user: more explosive, green): Captain's airstrike impact plus a big omni burst, both made green.
            OwlBlastEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Captain/CaptainAirstrikeImpact1.prefab", "RoR2/Base/Engi/EngiHarpoonExplosion.prefab"), "CursedAppleGreyTalonOwlBlast", OwlHue, effect: true);
            OwlBlastWave = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFX.prefab"), "CursedAppleGreyTalonOwlBlastWave", OwlHue, effect: true);
            GameObject? glow = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Engi/EngiHarpoonGhost.prefab"), "CursedAppleGreyTalonOwlGlowSource", OwlHue, effect: false);
            if (glow != null)
            {
                // A ghost without a projectile to follow: its follow script removed, kept inactive as a template.
                OwlGlow = glow;
                foreach (ProjectileGhostController g in OwlGlow.GetComponents<ProjectileGhostController>()) UnityEngine.Object.DestroyImmediate(g);
                OwlGlow.SetActive(false);
            }
        }

        private static void CreateBuffs()
        {
            ChargingBuff = Buff("CursedAppleGreyTalonCharging", new Color(0.6f, 0.8f, 1f), hidden: true, stack: false);
            OwlChannelBuff = Buff("CursedAppleGreyTalonOwlChannel", new Color(0.6f, 0.8f, 1f), hidden: true, stack: false);
            OwlFlyingBuff = Buff("CursedAppleGreyTalonOwlFlying", new Color(0.6f, 0.8f, 1f), hidden: true, stack: false);
            OwlTrophyBuff = Buff("CursedAppleGreyTalonOwlTrophy", new Color(0.85f, 0.75f, 0.45f), hidden: false, stack: true);
            SkillSilence.Register(OwlChannelBuff);
        }

        private static BuffDef Buff(string name, Color color, bool hidden, bool stack)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name;
            b.buffColor = color;
            b.canStack = stack;
            b.isDebuff = false;
            b.isHidden = hidden;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        /// <summary>Charged Shot: the Lynx Archer's arrow, made large (user: a large, fast projectile); GreyTalonChargedArrow pierces with it.</summary>
        private static GameObject? CreateChargedArrow()
        {
            GameObject? p = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/DLC4/LynxArcher/LynxArcherArrow.prefab", "RoR2/DLC1/Railgunner/RailgunnerPistolProjectile.prefab"),
                "CursedAppleGreyTalonChargedArrow", typeof(GreyTalonChargedArrow), GreyTalonData.ChargedSpeed, GreyTalonData.ChargedRange / GreyTalonData.ChargedSpeed,
                gravity: false, triggers: true, steer: true);
            var pc = p != null ? p.GetComponent<ProjectileController>() : null;
            if (pc == null) return p;
            // The Lynx Archer's arrow keeps its look in a separate ghost prefab (the projectile came without one: invisible).
            GameObject? source = pc.ghostPrefab != null ? pc.ghostPrefab : TryLoad<GameObject>("RoR2/DLC4/LynxArcher/LynxArcherArrowGhost.prefab", "RoR2/DLC1/Railgunner/RailgunnerPistolProjectileGhost.prefab");
            if (source == null) return p;
            GameObject ghost = source.InstantiateClone("CursedAppleGreyTalonChargedArrowGhost", false);
            ghost.transform.localScale *= 2.5f;
            // A long bright streak behind it (user: a tracer for the arrow), in the Huntress snipe tracer's material.
            Material? streak = TracerEffect != null && TracerEffect.GetComponentInChildren<LineRenderer>(true) is LineRenderer line ? line.sharedMaterial : null;
            if (streak != null)
            {
                var trail = ghost.AddComponent<TrailRenderer>();
                trail.sharedMaterial = streak;
                trail.time = 0.25f;
                trail.startWidth = 0.5f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.5f;
                trail.numCapVertices = 2;
                trail.startColor = new Color(0.7f, 0.85f, 1f, 1f);
                trail.endColor = new Color(0.7f, 0.85f, 1f, 0f);
            }
            pc.ghostPrefab = ghost;
            return p;
        }

        // ---------------- states and skills ----------------

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireBow), typeof(ChargedShot), typeof(RainOfArrows), typeof(GuidedOwl) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleGreyTalon", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _bow = Skill<SkillDef>("Bow", typeof(FireBow), "Weapon", 0f, InterruptPriority.Any);
            _bow.cancelSprintingOnActivation = true;
            _bow.mustKeyPress = false;

            _charged = Skill<SkillDef>("Charged", typeof(ChargedShot), "Weapon", GreyTalonData.ChargedCooldown, InterruptPriority.Skill);
            _charged.mustKeyPress = true;
            _charged.cancelSprintingOnActivation = true;
            _charged.beginSkillCooldownOnSkillEnd = true;

            _rain = Skill<SkillDef>("Rain", typeof(RainOfArrows), "Body", GreyTalonData.RainCooldown, InterruptPriority.Skill);
            _rain.mustKeyPress = true;
            _rain.cancelSprintingOnActivation = false;

            _owl = Skill<SkillDef>("Owl", typeof(GuidedOwl), "Body", GreyTalonData.OwlCooldown, InterruptPriority.PrioritySkill);
            _owl.mustKeyPress = true;
            _owl.cancelSprintingOnActivation = true;
            _owl.beginSkillCooldownOnSkillEnd = true;
        }

        // ---------------- body ----------------

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = GreyTalonData.BaseHealth;
            cb.levelMaxHealth = GreyTalonData.HealthPerLevel;
            cb.baseArmor = GreyTalonData.BaseArmor;
            cb.baseDamage = GreyTalonData.BaseDamage;
            cb.levelDamage = GreyTalonData.DamagePerLevel;
            cb.baseMoveSpeed = GreyTalonData.BaseSpeed;
            cb.baseRegen = GreyTalonData.BaseRegen;
            cb.levelRegen = GreyTalonData.BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _bow!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _charged!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _rain!);
            locator.special = Kit.Slot(body, BodyName, "Special", _owl!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            body.AddComponent<GreyTalonTrophySync>();
            DeadlockMovement.Apply(body, GreyTalonData.Movement);

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = GreyTalonData.Hero;
            Kit.MuteStandIn(body, model.gameObject, GreyTalonData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleGreyTalonDEFAULT", Token + "SKIN_DEFAULT", DefaultSkinIcon);

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleGreyTalonDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = GreyTalonData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleGreyTalonDISPLAY", Token + "SKIN_DEFAULT", DefaultSkinIcon);
            }
            display.AddComponent<GreyTalonDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleGreyTalon";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 104f; // after Lash, Abrams, Seven and Vyper
            ContentAddition.AddSurvivorDef(Survivor);
        }

        // ---------------- icons (from Deadlock, once extracted) ----------------

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(GreyTalonData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Set(_charged, GreyTalonData.IconCharged);
            Set(_rain, GreyTalonData.IconRain);
            Sprite? owl = Set(_owl, GreyTalonData.IconOwl);
            if (owl != null && OwlTrophyBuff != null) OwlTrophyBuff.iconSprite = owl;
            if (portrait != null && BodyPrefab != null)
            {
                _bow!.icon = Kit.Sprite(portrait);
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = _bow.icon;
                locator.passiveSkill = passive;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[GreyTalon] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Grey Talon");
            LanguageAPI.Add(Token + "SUBTITLE", "The Last Hunt");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Grey Talon spent forty years hunting monsters. This planet has more than he can count.<color=#CCD3E0>\n\n" +
                "< ! > His bow keeps full damage across long distances: keep your distance.\n\n" +
                "< ! > Line enemies up for Charged Shot: it pierces them all.\n\n" +
                "< ! > Rain of Arrows turns every shot into five. Leap above the crowd and shoot down into it.\n\n" +
                "< ! > His owl hits very hard, but takes a long time to return.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, one last trophy for the wall.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, the hunter finally hunted.");
            LanguageAPI.Add(Token + "LORE", "Every monster he ever hunted had a weakness. He meant to find this one's.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Seasoned Hunter");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                $"<style=cIsUtility>{GreyTalonData.Movement.StaminaPips} stamina</style>. Dash, air jump and slide; sprints automatically out of combat.");

            LanguageAPI.Add(Token + "BOW_NAME", "Bow");
            LanguageAPI.Add(Token + "BOW_DESC",
                $"Loose an arrow for <style=cIsDamage>{D(GreyTalonData.BowDamage)} damage</style>. Damage falls off beyond {GreyTalonData.BowFalloffStart:0}m.");

            LanguageAPI.Add(Token + "CHARGED_NAME", "Charged Shot");
            LanguageAPI.Add(Token + "CHARGED_DESC",
                $"Draw and <style=cIsUtility>hold</style>, then release a <style=cIsDamage>piercing</style> arrow for <style=cIsDamage>{D(GreyTalonData.ChargedDamage)} damage</style>.");

            LanguageAPI.Add(Token + "RAIN_NAME", "Rain of Arrows");
            LanguageAPI.Add(Token + "RAIN_DESC",
                $"<style=cIsUtility>Leap</style> and <style=cIsUtility>glide</style> for {GreyTalonData.RainDuration:0}s. While airborne, every shot fires <style=cIsDamage>{GreyTalonData.RainArrows} arrows</style> for <style=cIsDamage>+{D(GreyTalonData.RainDamageBonus)}</style> damage each.");

            LanguageAPI.Add(Token + "OWL_NAME", "Guided Owl");
            LanguageAPI.Add(Token + "OWL_DESC",
                $"Steer a spirit owl that explodes for <style=cIsDamage>{D(GreyTalonData.OwlDamage)} damage</style> and <style=cIsDamage>stuns</style>. You are <style=cIsUtility>invulnerable</style> while steering. " +
                $"Each boss it kills permanently grants <style=cIsDamage>+{D(GreyTalonData.OwlBossBonus)} damage</style>.");
        }
    }

    /// <summary>Waits for the Deadlock reader, then swaps placeholder icons for Grey Talon's own.</summary>
    public class GreyTalonIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { GreyTalonSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[GreyTalon] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
