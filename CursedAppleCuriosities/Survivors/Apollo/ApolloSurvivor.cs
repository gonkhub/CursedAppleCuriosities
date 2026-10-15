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
            HideMeshes = new[] { "head_ui_smug_reduced", "head_ui_cocky", "head_ui_smug" }, // menu-portrait heads over his real one
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }

    /// <summary>
    /// Apollo from Deadlock (hero_fencer) as a RoR2 survivor. The user asked me to design him (2026-10-14): Pride of Ixia's sword waves,
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
        public static BuffDef? SigilSlow, RiposteSlow, ParryBuff, RiposteLifestealBuff;
        public static GameObject? WaveTracer, SlashEffect, SigilEffect, UltEffect;

        private static SkillDef? _wave, _sigil, _advance, _riposte, _ult;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Apollo", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            ApolloData.Bind(config);
            foreach (DeadlockSound s in ApolloData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { ApolloData.IconPortrait, ApolloData.IconSigil, ApolloData.IconRiposte, ApolloData.IconAdvance, ApolloData.IconUlt })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(ApolloModel.Def);
            DeadlockInstall.WantModel(ApolloModel.Def.ModelRequest());

            WaveTracer = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBandit2Rifle.prefab"), "CursedAppleApolloWave", Hue, effect: true);
            SlashEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/OmniImpactVFXSlashMerc.prefab"), "CursedAppleApolloSlash", Hue, effect: true);
            SigilEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab"), "CursedAppleApolloSigil", Hue, effect: true);
            UltEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/ImpactMercEvis.prefab"), "CursedAppleApolloUlt", Hue, effect: true);
            SigilSlow = Buff("CursedAppleApolloSigilSlow", new Color(0.9f, 0.8f, 0.5f), debuff: true);
            RiposteSlow = Buff("CursedAppleApolloRiposteSlow", new Color(0.8f, 0.7f, 0.4f), debuff: true);
            ParryBuff = Buff("CursedAppleApolloParry", Color, debuff: false);
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
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = false };
            DeadlockMovement.Apply(body, ApolloData.Movement);
            body.AddComponent<ApolloAdvanceRecast>();
            body.AddComponent<ApolloSwing>();
            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = ApolloData.Hero;
            Kit.MuteStandIn(body, model.gameObject, ApolloData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleApolloDEFAULT", Token + "SKIN_DEFAULT");
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
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleApolloDISPLAY", Token + "SKIN_DEFAULT");
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
            Survivor.desiredSortPosition = 113f;
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
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Apollo] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Apollo");
            LanguageAPI.Add(Token + "SUBTITLE", "The Duelist");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Apollo has never lost a duel. He has never fought a planet before.<color=#CCD3E0>\n\n" +
                "< ! > Your sword waves cut through every enemy in a line: line them up.\n\n" +
                "< ! > Flawless Advance hits hardest when released on the glint. You get three stabs in a row.\n\n" +
                "< ! > Riposte turns the next hit you take into a counterattack.\n\n" +
                "< ! > Itani Lo Sahn freezes everything it cuts, then finishes them; it hits harder on wounded enemies.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, undefeated.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, outmatched at last.");
            LanguageAPI.Add(Token + "WAVE_NAME", "Pride of Ixia");
            LanguageAPI.Add(Token + "WAVE_DESC", $"Slash a sword wave that <style=cIsDamage>pierces</style> enemies for <style=cIsDamage>{D(ApolloData.WaveDamage)} damage</style>, up to {ApolloData.WaveRange:0} m.");
            LanguageAPI.Add(Token + "SIGIL_NAME", "Disengaging Sigil");
            LanguageAPI.Add(Token + "SIGIL_DESC", $"Draw a sigil ahead that bursts for <style=cIsDamage>{D(ApolloData.SigilDamage)} damage</style> and <style=cIsUtility>slows</style>, as you <style=cIsUtility>leap back</style>.");
            LanguageAPI.Add(Token + "ADVANCE_NAME", "Flawless Advance");
            LanguageAPI.Add(Token + "ADVANCE_DESC",
                $"Hold to ready a stab, release to lunge through enemies for <style=cIsDamage>{D(ApolloData.StabMin)}-{D(ApolloData.StabMax)} damage</style>; on the glint, <style=cIsDamage>{D(ApolloData.StabPerfect)}</style> and <style=cIsHealing>heal {D(ApolloData.PerfectHeal)} max health</style>. Up to <style=cIsUtility>{ApolloData.Stabs} stabs</style> in a row.");
            LanguageAPI.Add(Token + "RIPOSTE_NAME", "Riposte");
            LanguageAPI.Add(Token + "RIPOSTE_DESC",
                $"Ready a parry. The next hit is <style=cIsUtility>deflected</style>; you dash at the attacker and slash for <style=cIsDamage>{D(ApolloData.RiposteDamage)} damage</style>, <style=cIsUtility>stunning</style> and slowing, then <style=cIsHealing>steal {D(ApolloData.RiposteLifesteal)} of damage dealt</style> for {ApolloData.RiposteLifestealSeconds:0}s.");
            LanguageAPI.Add(Token + "ULT_NAME", "Itani Lo Sahn");
            LanguageAPI.Add(Token + "ULT_DESC",
                $"Charge, then slash a long line for <style=cIsDamage>{D(ApolloData.UltImpact)} damage</style>. Struck enemies freeze in slow motion, then take <style=cIsDamage>{D(ApolloData.UltDelayed)} damage</style> " +
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
