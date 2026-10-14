using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Shiv;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// Shiv from Deadlock as a RoR2 survivor (same structure as Vyper). The body is a clone of Commando's rig; the Deadlock
    /// model, animations and icons come from the player's own Deadlock install at runtime. Kit by the user (2026-10-14).
    /// </summary>
    public static class ShivSurvivor
    {
        public const string Token = "CURSEDAPPLE_SHIV_";
        public const string BodyName = "CursedAppleShivBody";
        public const string MuzzleName = "ShivMuzzle";
        public static readonly Color Color = new Color(0.62f, 0.14f, 0.14f); // character select / UI: blood red
        public const float SlashHue = 0.99f;                                 // his cuts are red

        public static GameObject? BodyPrefab, DisplayPrefab, KnifeProjectile;
        public static SurvivorDef? Survivor;
        public static BuffDef? RageBuff, KnifeSlow;
        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, SlashEffect, ExecuteEffect;

        private static SkillDef? _gun, _alt, _knife, _slice, _blow;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Shiv", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            ShivData.Bind(config);
            foreach (DeadlockSound s in ShivData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { ShivData.IconPortrait, ShivData.IconKnife, ShivData.IconDash, ShivData.IconBloodletting, ShivData.IconKillingBlow })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(ShivModel.Def);
            DeadlockInstall.WantModel(ShivModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            KnifeProjectile = Kit.CloneProjectile(
                TryLoad<GameObject>("RoR2/Base/Bandit2/Bandit2ShivProjectile.prefab"),
                "CursedAppleShivKnife", typeof(ShivKnife), ShivData.KnifeSpeed, 3f, gravity: false, triggers: false, steer: false);
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            ShivItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            ShivNet.Register();
            DeadlockHeroVoice.Track(BodyName, ShivData.Hero, "shiv");
            DeadlockPingVoice.Track(BodyName, ShivData.Hero, "shiv");

            host.AddComponent<ShivIcons>();
        }

        private static void LoadAssets()
        {
            TracerEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/TracerBandit2Shotgun.prefab");
            HitEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/HitsparkBandit.prefab");
            MuzzleEffect = TryLoad<GameObject>("RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            SlashEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/OmniImpactVFXSlashMerc.prefab"), "CursedAppleShivSlash", SlashHue, effect: true);
            ExecuteEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Merc/ImpactMercEvis.prefab"), "CursedAppleShivExecute", SlashHue, effect: true);
        }

        private static void CreateBuffs()
        {
            RageBuff = Buff("CursedAppleShivRage", new Color(0.85f, 0.15f, 0.1f), debuff: false, stack: true);
            KnifeSlow = Buff("CursedAppleShivKnifeSlow", Color.gray, debuff: true, stack: false, hidden: true);
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
            foreach (Type t in new[] { typeof(FireShotgun), typeof(FireAltShotgun), typeof(ThrowKnife), typeof(SliceNDice), typeof(KillingBlow) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleShiv", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireShotgun), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            _alt = Skill<SkillDef>("Alt", typeof(FireAltShotgun), "Weapon", 0f, InterruptPriority.Any);
            _alt.cancelSprintingOnActivation = true;
            _alt.mustKeyPress = false;

            _knife = Skill<SkillDef>("Knife", typeof(ThrowKnife), "Weapon", ShivData.KnifeRecharge, InterruptPriority.Skill);
            _knife.baseMaxStock = ShivData.KnifeCharges;
            _knife.rechargeStock = 1;
            _knife.mustKeyPress = true;
            _knife.cancelSprintingOnActivation = false;

            _slice = Skill<SkillDef>("Slice", typeof(SliceNDice), "Body", ShivData.DashCooldown, InterruptPriority.PrioritySkill);
            _slice.mustKeyPress = true;
            _slice.cancelSprintingOnActivation = false;

            _blow = Skill<SkillDef>("KillingBlow", typeof(KillingBlow), "Body", ShivData.KillingBlowCooldown, InterruptPriority.PrioritySkill);
            _blow.mustKeyPress = true;
            _blow.cancelSprintingOnActivation = false;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = ShivData.BaseHealth;
            cb.levelMaxHealth = ShivData.HealthPerLevel;
            cb.baseArmor = ShivData.BaseArmor;
            cb.baseDamage = ShivData.BaseDamage;
            cb.levelDamage = ShivData.DamagePerLevel;
            cb.baseMoveSpeed = ShivData.BaseSpeed;
            cb.baseRegen = ShivData.BaseRegen;
            cb.levelRegen = ShivData.BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!, _alt!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _knife!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _slice!);
            locator.special = Kit.Slot(body, BodyName, "Special", _blow!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, ShivData.Movement);
            body.AddComponent<ShivBloodletting>();
            body.AddComponent<ShivRage>();

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = ShivData.Hero;
            Kit.MuteStandIn(body, model.gameObject, ShivData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleShivDEFAULT", Token + "SKIN_DEFAULT", ShivSkins.DefaultIcon, ("skinCursedAppleShivOXBLOOD", Token + "SKIN_OXBLOOD", ShivSkins.OxbloodIcon));

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleShivDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = ShivData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleShivDISPLAY", Token + "SKIN_DEFAULT", ShivSkins.DefaultIcon, ("skinCursedAppleShivDISPLAYOXBLOOD", Token + "SKIN_OXBLOOD", ShivSkins.OxbloodIcon));
            }
            display.AddComponent<ShivDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleShiv";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 108f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(ShivData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Set(_knife, ShivData.IconKnife);
            Set(_slice, ShivData.IconDash);
            Sprite? blow = Set(_blow, ShivData.IconKillingBlow);
            if (blow != null && RageBuff != null) RageBuff.iconSprite = blow; // Rage is Killing Blow's passive in Deadlock
            Texture2D? blood = DeadlockInstall.Icon(ShivData.IconBloodletting);
            if (BodyPrefab != null && (blood != null || portrait != null))
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = Kit.Sprite(blood ?? portrait!);
                locator.passiveSkill = passive;
            }
            if (portrait != null) { _gun!.icon = Kit.Sprite(portrait); _alt!.icon = Kit.Sprite(portrait); }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Shiv] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Shiv");
            LanguageAPI.Add(Token + "SUBTITLE", "The Bloody Cleaner");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_OXBLOOD", "Oxblood");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Shiv cleans up messes for the Cursed Apple's worst. This planet is a mess.<color=#CCD3E0>\n\n" +
                "< ! > Half of every hit you take bleeds out over time instead: the yellow on your health bar is what's still coming.\n\n" +
                "< ! > Keep dealing damage to build Rage. Full Rage doubles your bleeds and Slice'n'Dice, and Killing Blow executes much higher.\n\n" +
                "< ! > Land a Killing Blow execute to wash away your bleeding, fill your Rage and get the ability straight back.\n\n" +
                "< ! > The alt fire kicks you back hard: blast the ground to hop, or behind you to stretch a slide.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, the job done and the mess behind him.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, one more stain on the floor.");
            LanguageAPI.Add(Token + "LORE", "Somebody has to clean up.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Bloodletting & Rage");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                $"<style=cIsHealing>{ShivData.DeferredShare * 100f:0}%</style> of damage taken is dealt over <style=cIsUtility>{ShivData.DeferredSeconds:0}s</style> instead. " +
                $"Dealing damage builds <style=cIsDamage>Rage</style>; at full Rage, bleeds and Slice'n'Dice deal <style=cIsDamage>double</style> and Killing Blow executes below <style=cIsHealth>{ShivData.ExecuteThresholdRage * 100f:0}%</style>.");

            LanguageAPI.Add(Token + "GUN_NAME", "Shotgun");
            LanguageAPI.Add(Token + "GUN_DESC",
                $"Fire {ShivData.Pellets} pellets for <style=cIsDamage>{ShivData.Pellets}x{D(ShivData.PelletDamage)} damage</style>. Falls off with distance.");
            LanguageAPI.Add(Token + "ALT_NAME", "Alt Fire");
            LanguageAPI.Add(Token + "ALT_DESC",
                $"Fire {ShivData.AltPellets} pellets for <style=cIsDamage>{ShivData.AltPellets}x{D(ShivData.AltPelletDamage)} damage</style> at half the rate. Falls off quickly. <style=cIsUtility>Knocks you back hard</style>.");

            LanguageAPI.Add(Token + "KNIFE_NAME", "Serrated Knife");
            LanguageAPI.Add(Token + "KNIFE_DESC",
                $"Throw a knife for <style=cIsDamage>{D(ShivData.KnifeDamage)} damage</style> that <style=cIsDamage>bleeds</style> <style=cIsDamage>{ShivData.KnifeBleeds}</style> times and <style=cIsUtility>slows</style>. <style=cIsUtility>{ShivData.KnifeCharges} charges</style>.");

            LanguageAPI.Add(Token + "SLICE_NAME", "Slice'n'Dice");
            LanguageAPI.Add(Token + "SLICE_DESC",
                $"Dash forward, dealing <style=cIsDamage>{D(ShivData.DashDamage)} damage</style> to enemies in your path. Each enemy struck reduces the cooldown by <style=cIsUtility>{ShivData.DashCooldownPerHit:0}s</style>.");

            LanguageAPI.Add(Token + "KILLINGBLOW_NAME", "Killing Blow");
            LanguageAPI.Add(Token + "KILLINGBLOW_DESC",
                $"Lunge a great distance, slashing through enemies for <style=cIsDamage>{D(ShivData.LungeDamage)} damage</style>. The first enemy below <style=cIsHealth>{ShivData.ExecuteThreshold * 100f:0}% health</style> is <style=cIsHealth>executed</style> (bosses: half): " +
                "it ends your bleeding, grants Rage and <style=cIsUtility>refunds the cooldown</style>.");
        }
    }

    public class ShivIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { ShivSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Shiv] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
