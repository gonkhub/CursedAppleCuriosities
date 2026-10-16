using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Apollo;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>Apollo on the shared Deadlock model layer.</summary>
    public static class ApolloModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = ApolloData.Hero,
            DisplayName = "Apollo",
            DlModel = ApolloData.DlModel,
            ModelHeightScale = ApolloData.ModelHeightScale,
            BaseSpeed = ApolloData.BaseSpeed,
            MuzzleBone = ApolloData.MuzzleBone,
            MuzzleName = ApolloSurvivor.MuzzleName,
            Anims = ApolloData.Anims,
            RootLocked = ApolloData.Locked,
            ItemAnchors = ApolloItemDisplays.Anchors,
            // Blackmore Colors (skin 1, his mastery, user 3.0): his fencing whites become a Blackmore Academy uniform, after Graves'
            // jacket (her burgundy blazer with dark forest-green panels, read from her Deadlock textures): his white jacket goes
            // burgundy, his crimson panels forest green, his white trousers charcoal with burgundy piping. Skin and hair untouched.
            Skins = new[]
            {
                new DeadlockSkin { Id = "blackmore1", Only = new[] { "fencer_upper", "fencer_lower" }, RecolorFile = ApolloSkins.Blackmore },
                // Sapphire (skin 2, user 3.0): his reds swapped for Abrams' blue, his red skin included, and his gem.
                new DeadlockSkin { Id = "sapphire1", Only = new[] { "fencer_upper", "fencer_lower", "fencer_head", "fencer_gem" }, RecolorFile = ApolloSkins.Sapphire },
            },
            HideMeshes = new[] { "head_ui_smug_reduced", "head_ui_cocky", "head_ui_smug" }, // menu-portrait heads over his real one
            // His sword hangs off root_motion (weaponPivot/weapon); every clip puts its root exactly on weapon_target_hand_R (his right
            // hand's sword socket): snapped there, it stays in his hand whatever plays (user 2.5).
            HandIK = ("weapon", "weapon_target_hand_R"),
            HandSnap = true,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }

    /// <summary>
    /// Apollo from Deadlock (hero_fencer) as a RoR2 survivor. The user asked me to design him (2026-10-14): Family Heirloom's sword waves (Deadlock's Pride of Ixia),
    /// Disengaging Sigil, Flawless Advance (alt: Riposte), Itani Lo Sahn.
    /// </summary>
    public static class ApolloSurvivor
    {
        public const string Token = "CURSEDAPPLE_APOLLO_";
        public const string BodyName = "CursedAppleApolloBody";
        public const string MuzzleName = "ApolloMuzzle";
        public static readonly Color Color = new Color(0.9f, 0.8f, 0.55f); // his white and gold
        public const float Hue = 0.12f;

        public static GameObject? BodyPrefab, DisplayPrefab;
        public static SurvivorDef? Survivor;
        public static BuffDef? SigilSlow, RiposteSlow, ParryBuff, RiposteLifestealBuff, UltMaskBuff, SigilHasteBuff, UltMarkedBuff;
        public static GameObject? WaveTracer, SlashEffect, SigilEffect, UltEffect, WaveProjectile, UltStreak;

        private static SkillDef? _wave, _sigil, _advance, _riposte, _ult;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Apollo", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            ApolloData.Bind(config);
            foreach (DeadlockSound s in ApolloData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { ApolloData.IconPortrait, ApolloData.IconGun, ApolloData.IconSigil, ApolloData.IconRiposte, ApolloData.IconAdvance, ApolloData.IconUlt })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(ApolloModel.Def);
            DeadlockInstall.WantModel(ApolloModel.Def.ModelRequest());
            DeadlockInstall.WantModel(new DeadlockInstall.ModelRequest { Hero = ApolloData.MaskKey, DlModel = ApolloData.DlMaskModel }); // Itani Lo Sahn's mask

            WaveTracer = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBandit2Rifle.prefab"), "CursedAppleApolloWave", Hue, effect: true);
            // The sword wave: the little spinning blade of Celeste's primary (Bandit's shiv), red (user 2.5), on its own flight.
            WaveProjectile = Kit.CloneProjectile(TryLoad<GameObject>("RoR2/Base/Bandit2/Bandit2ShivProjectile.prefab", "RoR2/Base/Characters/Bandit2/Skills/Bandit2ShivProjectile.prefab"),
                "CursedAppleApolloWaveShot", typeof(ApolloWaveShot), ApolloData.WaveSpeed, ApolloData.WaveRange / ApolloData.WaveSpeed, gravity: false, triggers: true, steer: false);
            if (WaveProjectile != null && WaveProjectile.GetComponent<RoR2.Projectile.ProjectileController>() is RoR2.Projectile.ProjectileController wpc && wpc.ghostPrefab != null)
            {
                GameObject? blade = Kit.Recoloured(wpc.ghostPrefab, "CursedAppleApolloWaveGhost", 0f, effect: false);
                if (blade != null) { blade.transform.localScale *= 0.45f; wpc.ghostPrefab = blade; }
                // Its swing rings ride the projectile, not the ghost: shrunk with it and held at that size (as Celeste's).
                foreach (ParticleSystem ps in WaveProjectile.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    if (main.startSize3D) { main.startSizeXMultiplier *= 0.45f; main.startSizeYMultiplier *= 0.45f; main.startSizeZMultiplier *= 0.45f; }
                    else main.startSizeMultiplier *= 0.45f;
                    main.startSpeedMultiplier = 0f;
                    ParticleSystem.SizeOverLifetimeModule growth = ps.sizeOverLifetime;
                    growth.enabled = false;
                }
            }
            SlashEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/OmniImpactVFXSlashMerc.prefab"), "CursedAppleApolloSlash", Hue, effect: true);
            SigilEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab"), "CursedAppleApolloSigil", Hue, effect: true);
            UltEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/ImpactMercEvis.prefab"), "CursedAppleApolloUlt", Hue, effect: true);
            SigilSlow = Buff("CursedAppleApolloSigilSlow", new Color(0.9f, 0.8f, 0.5f), debuff: true);
            RiposteSlow = Buff("CursedAppleApolloRiposteSlow", new Color(0.8f, 0.7f, 0.4f), debuff: true);
            ParryBuff = Buff("CursedAppleApolloParry", Color, debuff: false);
            UltMarkedBuff = Buff("CursedAppleApolloUltMarked", new Color(1f, 0.1f, 0.1f), debuff: true);
            SigilHasteBuff = Buff("CursedAppleApolloSigilHaste", new Color(0.95f, 0.8f, 0.4f), debuff: false);
            UltMaskBuff = Buff("CursedAppleApolloUltMask", new Color(0.9f, 0.1f, 0.1f), debuff: false);
            // Itani Lo Sahn's streak from where he stood to where he lands: the Huntress's snipe tracer, made red.
            UltStreak = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Huntress/TracerHuntressSnipe.prefab", "RoR2/Base/Characters/Huntress/TracerHuntressSnipe.prefab"), "CursedAppleApolloUltStreak", 0f, effect: true);
            RiposteLifestealBuff = Buff("CursedAppleApolloRiposteLifesteal", new Color(0.9f, 0.25f, 0.3f), debuff: false);

            foreach (Type t in new[] { typeof(FireWave), typeof(DrawSigil), typeof(AdvanceReady), typeof(AdvanceStab), typeof(Riposte), typeof(RiposteDash), typeof(ItaniLoSahn) })
                ContentAddition.AddEntityState(t, out _);
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            ApolloItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            ApolloNet.Register();
            DeadlockHeroVoice.Track(BodyName, ApolloData.Hero, "fencer");
            DeadlockPingVoice.Track(BodyName, ApolloData.Hero, "fencer");
            host.AddComponent<ApolloIcons>();
        }

        private static BuffDef Buff(string name, Color color, bool debuff)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name; b.buffColor = color; b.canStack = false; b.isDebuff = debuff;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleApollo", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _wave = Skill<SkillDef>("Wave", typeof(FireWave), "Weapon", 0f, InterruptPriority.Any);
            _wave.cancelSprintingOnActivation = true;
            _wave.mustKeyPress = false;
            _sigil = Skill<SkillDef>("Sigil", typeof(DrawSigil), "Body", ApolloData.SigilCooldown, InterruptPriority.PrioritySkill);
            _sigil.mustKeyPress = true;
            _advance = Skill<SkillDef>("Advance", typeof(AdvanceReady), "Body", ApolloData.AdvanceCooldown, InterruptPriority.PrioritySkill);
            _advance.mustKeyPress = true;
            _advance.cancelSprintingOnActivation = false;
            _riposte = Skill<SkillDef>("Riposte", typeof(Riposte), "Body", ApolloData.RiposteCooldown, InterruptPriority.PrioritySkill);
            _riposte.mustKeyPress = true;
            _riposte.cancelSprintingOnActivation = false;
            _ult = Skill<SkillDef>("Ult", typeof(ItaniLoSahn), "Body", ApolloData.UltCooldown, InterruptPriority.PrioritySkill);
            _ult.mustKeyPress = true;
            _ult.beginSkillCooldownOnSkillEnd = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = ApolloData.BaseHealth;
            cb.levelMaxHealth = ApolloData.HealthPerLevel;
            cb.baseDamage = 12f;
            cb.levelDamage = 2.4f;
            cb.baseMoveSpeed = ApolloData.BaseSpeed;
            cb.baseRegen = ApolloData.BaseRegen;
            cb.levelRegen = ApolloData.BaseRegen * 0.2f;
            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _wave!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _sigil!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _advance!, _riposte!);
            locator.special = Kit.Slot(body, BodyName, "Special", _ult!);
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = false, skillNameToken = Token + "PASSIVE_NAME", skillDescriptionToken = Token + "PASSIVE_DESC", icon = ItemBase.CreatePlaceholderSprite(Color) }; // hidden: it only described his Deadlock movement, and the user cut those blurbs (3.0)
            DeadlockMovement.Apply(body, ApolloData.Movement);
            body.AddComponent<ApolloAdvanceRecast>();
            body.AddComponent<ApolloUltLook>();
            body.AddComponent<ApolloSwing>();
            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = ApolloData.Hero;
            Kit.MuteStandIn(body, model.gameObject, ApolloData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleApolloDEFAULT", Token + "SKIN_DEFAULT", ApolloSkins.DefaultIcon, ("skinCursedAppleApolloBLACKMORE", Token + "SKIN_BLACKMORE", ApolloSkins.BlackmoreIcon),
                ("skinCursedAppleApolloSAPPHIRE", Token + "SKIN_SAPPHIRE", ApolloSkins.SapphireIcon));
            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleApolloDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = ApolloData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleApolloDISPLAY", Token + "SKIN_DEFAULT", ApolloSkins.DefaultIcon, ("skinCursedAppleApolloDISPLAYBLACKMORE", Token + "SKIN_BLACKMORE", ApolloSkins.BlackmoreIcon),
                    ("skinCursedAppleApolloDISPLAYSAPPHIRE", Token + "SKIN_SAPPHIRE", ApolloSkins.SapphireIcon));
            }
            display.AddComponent<ApolloDisplayIntro>(); // lobby entrance (user 2.5)
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleApollo";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 101f; // RosterOrder re-sorts the whole roster A-Z at load (user 3.0)
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(ApolloData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? sigil = Set(_sigil, ApolloData.IconSigil);
            if (sigil != null && SigilSlow != null) SigilSlow.iconSprite = sigil;
            Sprite? riposte = Set(_riposte, ApolloData.IconRiposte);
            if (riposte != null) { if (ParryBuff != null) ParryBuff.iconSprite = riposte; if (RiposteLifestealBuff != null) RiposteLifestealBuff.iconSprite = riposte; if (RiposteSlow != null) RiposteSlow.iconSprite = riposte; }
            Set(_advance, ApolloData.IconAdvance);
            Set(_ult, ApolloData.IconUlt);
            if (portrait != null) _wave!.icon = Kit.Sprite(portrait);
            Set(_wave, ApolloData.IconGun); // the weapon itself (user 3.0); the portrait stays if it isn't cached
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Apollo] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Apollo");
            LanguageAPI.Add(Token + "PASSIVE_NAME", "Deadlock Movement");
            LanguageAPI.Add(Token + "PASSIVE_DESC", "");
            LanguageAPI.Add(Token + "SUBTITLE", "The Duelist");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_BLACKMORE", "Blackmore Colors");
            LanguageAPI.Add(Token + "SKIN_SAPPHIRE", "Sapphire");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Apollo is the prince of North Ixia, sent away for his own safety and waiting to prove he's worthy of his family's ancestral blade.<color=#CCD3E0>\n\n" +
                "< ! > Flawless Advance hits hardest when released on the glint. You get three stabs in a row.\n\n" +
                "< ! > Riposte turns the next hit you take into a counterattack.\n\n" +
                "< ! > Itani Lo Sahn freezes everything it cuts, then finishes them; it hits harder on wounded enemies.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, undefeated.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, outmatched at last.");
            LanguageAPI.Add(Token + "WAVE_NAME", "Family Heirloom"); // Deadlock's "Pride of Ixia" (user 3.0: that name went to Riposte's challenge)
            LanguageAPI.Add(Token + "WAVE_DESC", $"Slash a sword wave for <style=cIsDamage>{D(ApolloData.WaveDamage)} damage</style>, up to {ApolloData.WaveRange:0} m.");
            LanguageAPI.Add(Token + "SIGIL_NAME", "Disengaging Sigil");
            LanguageAPI.Add(Token + "SIGIL_DESC", $"Draw a sigil ahead that bursts for <style=cIsDamage>{D(ApolloData.SigilDamage)} damage</style> and <style=cIsUtility>slows</style>, as you <style=cIsUtility>flip away</style> from it (look down to cast it below you). Gain <style=cIsDamage>+{D(ApolloData.SigilHaste)} fire rate</style>; a hit gives back <style=cIsUtility>1 stamina</style> and your air jumps.");
            LanguageAPI.Add(Token + "ADVANCE_NAME", "Flawless Advance");
            LanguageAPI.Add(Token + "ADVANCE_DESC",
                $"Hold to ready a stab, release to lunge through enemies for <style=cIsDamage>{D(ApolloData.StabMin)}-{D(ApolloData.StabMax)} damage</style>; on the glint, <style=cIsDamage>{D(ApolloData.StabPerfect)}</style> and <style=cIsHealing>heal {D(ApolloData.PerfectHeal)} max health</style>. Up to <style=cIsUtility>{ApolloData.Stabs} stabs</style> in a row. Take <style=cIsHealing>{D(ApolloData.AdvanceDamageResist)} less damage</style> while advancing.");
            LanguageAPI.Add(Token + "RIPOSTE_NAME", "Riposte");
            LanguageAPI.Add(Token + "RIPOSTE_DESC",
                $"Ready a parry. The next hit is <style=cIsUtility>deflected</style>. Press again within {ApolloData.RiposteCounterWindow:0}s to dash at the enemy you aim at (up to {ApolloData.RiposteCounterRange:0} m) and slash for <style=cIsDamage>{D(ApolloData.RiposteDamage)} damage</style>, <style=cIsUtility>stunning</style> and slowing, then <style=cIsHealing>steal {D(ApolloData.RiposteLifesteal)} of damage dealt</style> for {ApolloData.RiposteLifestealSeconds:0}s.");
            LanguageAPI.Add(Token + "ULT_NAME", "Itani Lo Sahn");
            LanguageAPI.Add(Token + "ULT_DESC",
                $"Don your mask (<style=cIsUtility>70% less damage taken</style>) and hold; release to <style=cIsUtility>teleport</style> through a long line, slashing for <style=cIsDamage>{D(ApolloData.UltImpact)} damage</style>. Struck enemies freeze in slow motion, then take <style=cIsDamage>{D(ApolloData.UltDelayed)} damage</style> " +
                $"(<style=cIsDamage>+{D(ApolloData.UltLowHealthBonus)}</style> under half health). You are <style=cIsUtility>invulnerable</style> meanwhile.");
        }
    }

    public class ApolloIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { ApolloSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Apollo] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
