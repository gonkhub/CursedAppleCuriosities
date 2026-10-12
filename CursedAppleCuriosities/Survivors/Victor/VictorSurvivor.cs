using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Victor;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor from Deadlock (hero_frank) as a RoR2 survivor, same structure as Vyper. The body is a clone of Commando's
    /// rig; the Deadlock model, animations, sounds and icons come from the player's own Deadlock install at runtime.
    /// Kit (user): M1 Memento Mori, M2 Pain Battery, Utility Jumpstart, Special Aura of Suffering, passive Shocking Reanimation.
    /// </summary>
    public static class VictorSurvivor
    {
        public const string Token = "CURSEDAPPLE_VICTOR_";
        public const string BodyName = "CursedAppleVictorBody";
        public const string MuzzleName = "VictorMuzzle";
        public static readonly Color Color = new Color(0.55f, 0.66f, 0.55f); // character select / UI: greyish green (user, 2026-10-12)
        public const float Hue = 0.33f;      // his VFX are green, the aura range grey (user)

        public static GameObject? BodyPrefab, DisplayPrefab;
        public static SurvivorDef? Survivor;
        public static BuffDef? BatteryBuff, JumpstartBuff, AuraBuff, ReanimatingBuff, ReviveCooldownBuff, ReviveSlow;

        public static GameObject? TracerEffect, HitEffect, MuzzleEffect, BoltTracer, ZapEffect, WaveEffect, BlastEffect;
        public static Material? IndicatorMaterial; // the aura ring (Abrams' Siphon Life ring material)

        private static SkillDef? _gun, _battery, _jumpstart, _aura;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Victor", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            VictorData.Bind(config);
            foreach (DeadlockSound s in VictorData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { VictorData.IconPortrait, VictorData.IconBattery, VictorData.IconJumpstart, VictorData.IconAura, VictorData.IconRevive })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(VictorModel.Def);
            DeadlockInstall.WantModel(VictorModel.Def.ModelRequest());
            DeadlockInstall.WantDump(VictorWireGlow.Snapshot);
            DeadlockInstall.WantModel(new DeadlockInstall.ModelRequest
            { Hero = VictorData.FxKey, DlModel = VictorData.DlFxModel, Clips = new[] { VictorData.FxTogether, VictorData.FxHover } });

            LoadAssets();
            CreateBuffs();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            VictorItemDisplays.Register();
            CreateSurvivor();
            AddLanguage();
            VictorNet.Register();
            DeadlockHeroVoice.Track(BodyName, VictorData.Hero);
            EquipmentFx.RegisterBuffFx(AuraBuff, null, "victor_aura_loop");
            EquipmentFx.RegisterBuffFx(JumpstartBuff, null, "victor_jumpstart_loop");

            host.AddComponent<VictorIcons>();
        }

        private static void LoadAssets()
        {
            TracerEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab", "RoR2/Base/Characters/Commando/TracerCommandoDefault.prefab"), "CursedAppleVictorTracer", Hue, effect: true);
            HitEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab", "RoR2/Base/Characters/Commando/HitsparkCommando.prefab"), "CursedAppleVictorHit", Hue, effect: true);
            MuzzleEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Mage/MuzzleflashMageLightning.prefab", "RoR2/Base/Characters/Mage/Skills/MuzzleflashMageLightning.prefab"), "CursedAppleVictorMuzzle", Hue, effect: true);
            BoltTracer = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Commando/TracerCommandoDefault.prefab", "RoR2/Base/Characters/Commando/TracerCommandoDefault.prefab"), "CursedAppleVictorBolt", Hue, effect: true);
            ZapEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/LightningStrikeOnHit/SimpleLightningStrikeImpact.prefab", "RoR2/Base/Items/LightningStrikeOnHit/SimpleLightningStrikeImpact.prefab"), "CursedAppleVictorZap", Hue, effect: true);
            WaveEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Captain/CaptainTazerNova.prefab", "RoR2/Base/Characters/Captain/CaptainTazerNova.prefab",
                "RoR2/Base/Lightning/LightningStrikeImpact.prefab"), "CursedAppleVictorWave", Hue, effect: true);
            BlastEffect = Kit.Recoloured(TryLoad<GameObject>("RoR2/Base/Lightning/LightningStrikeImpact.prefab", "RoR2/Base/Equipment/Lightning/LightningStrikeImpact.prefab"), "CursedAppleVictorBlast", Hue, effect: true);
            Material? laser = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            IndicatorMaterial = laser != null ? new Material(laser) : TryLoad<Material>("RoR2/Base/Loader/matLoaderHookRope.mat");
        }

        private static void CreateBuffs()
        {
            BatteryBuff = Buff("CursedAppleVictorBattery", Color, debuff: false, stack: true);
            JumpstartBuff = Buff("CursedAppleVictorJumpstart", new Color(0.5f, 0.85f, 0.5f), debuff: false, stack: true);
            AuraBuff = Buff("CursedAppleVictorAura", new Color(0.7f, 0.7f, 0.7f), debuff: false, stack: false);
            ReanimatingBuff = Buff("CursedAppleVictorReanimating", Color, debuff: false, stack: false);
            ReviveCooldownBuff = Buff("CursedAppleVictorReviveCooldown", Color.gray, debuff: false, stack: false);
            ReviveCooldownBuff.isCooldown = true;
            ReviveSlow = Buff("CursedAppleVictorReviveSlow", Color.gray, debuff: true, stack: true, hidden: true);
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
            foreach (Type t in new[] { typeof(FireMementoMori), typeof(FirePainBattery), typeof(Jumpstart), typeof(AuraOfSuffering), typeof(Reanimate) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleVictor", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _gun = Skill<SkillDef>("Gun", typeof(FireMementoMori), "Weapon", 0f, InterruptPriority.Any);
            _gun.cancelSprintingOnActivation = true;
            _gun.mustKeyPress = false;

            // Pain Battery: ready only when full; the charge comes from damage taken alone (user).
            _battery = Skill<VictorBatterySkillDef>("Battery", typeof(FirePainBattery), "Weapon", VictorData.BatteryCooldown, InterruptPriority.Skill);
            _battery.mustKeyPress = true;
            _battery.cancelSprintingOnActivation = true;

            _jumpstart = Skill<SkillDef>("Jumpstart", typeof(Jumpstart), "Weapon", VictorData.JumpstartCooldown, InterruptPriority.Skill);
            _jumpstart.mustKeyPress = true;
            _jumpstart.baseMaxStock = VictorData.JumpstartCharges;
            _jumpstart.rechargeStock = 1;
            _jumpstart.cancelSprintingOnActivation = false;

            _aura = Skill<SkillDef>("Aura", typeof(AuraOfSuffering), "Aura", VictorData.AuraCooldown, InterruptPriority.Skill);
            _aura.mustKeyPress = true;
            _aura.cancelSprintingOnActivation = false;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);

            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = VictorData.BaseHealth;
            cb.levelMaxHealth = VictorData.HealthPerLevel;
            cb.baseRegen = VictorData.BaseRegen;
            cb.levelRegen = VictorData.BaseRegen * 0.2f;
            cb.baseArmor = VictorData.BaseArmor;
            cb.baseDamage = VictorData.BaseDamage;
            cb.levelDamage = VictorData.DamagePerLevel;
            cb.baseMoveSpeed = VictorData.BaseSpeed;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _battery!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _jumpstart!);
            locator.special = Kit.Slot(body, BodyName, "Special", _aura!);
            locator.passiveSkill = new SkillLocator.PassiveSkill
            {
                enabled = true,
                skillNameToken = Token + "PASSIVE_NAME",
                skillDescriptionToken = Token + "PASSIVE_DESC",
                icon = ItemBase.CreatePlaceholderSprite(Color),
            };
            DeadlockMovement.Apply(body, VictorData.Movement);
            Kit.AddMachine(body, "Aura"); // Aura of Suffering runs alongside everything
            body.AddComponent<VictorGun>();
            body.AddComponent<VictorBattery>();
            body.AddComponent<VictorAura>();
            body.AddComponent<VictorAuraFx>();
            body.AddComponent<VictorRevive>();
            body.AddComponent<VictorWireGlow>(); // left-arm barbed wire glows while Pain Battery is full

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = VictorData.Hero;
            Kit.MuteStandIn(body, model.gameObject, VictorData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleVictorDEFAULT", Token + "SKIN_DEFAULT");

            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleVictorDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = VictorData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleVictorDISPLAY", Token + "SKIN_DEFAULT");
            }
            display.AddComponent<VictorDisplayIntro>();
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleVictor";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 105f; // after Grey Talon
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(VictorData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path, BuffDef? buff = null)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                if (buff != null) buff.iconSprite = sprite;
                return sprite;
            }
            Set(_battery, VictorData.IconBattery, BatteryBuff);
            Set(_jumpstart, VictorData.IconJumpstart, JumpstartBuff);
            Set(_aura, VictorData.IconAura, AuraBuff);
            Sprite? revive = Set(null, VictorData.IconRevive, ReviveCooldownBuff);
            if (revive != null && ReanimatingBuff != null) ReanimatingBuff.iconSprite = revive;
            if (BodyPrefab != null && (revive != null || portrait != null))
            {
                var locator = BodyPrefab.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                passive.icon = revive ?? Kit.Sprite(portrait!);
                locator.passiveSkill = passive;
            }
            if (portrait != null) _gun!.icon = Kit.Sprite(portrait);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Victor] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        // ---------------- language (drafts: the user reviews the writing) ----------------

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            string P(float share) => $"{share * 100f:0.#}%";
            LanguageAPI.Add(Token + "NAME", "Victor");
            LanguageAPI.Add(Token + "SUBTITLE", "The Reanimated");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Victor woke up on a mortuary table, stitched together from strangers. Pain is the one thing he understands.<color=#CCD3E0>\n\n" +
                "< ! > Pain Battery fills only when you get hurt, from any source, your own Aura and Jumpstart included.\n\n" +
                "< ! > Jumpstart costs health up front but heals back more than it took. Use it to fill the battery.\n\n" +
                "< ! > Aura of Suffering hurts you too, and can kill you. Shocking Reanimation catches you when it's ready.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, still looking for the people he's made of.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he died, and this time stayed dead.");
            LanguageAPI.Add(Token + "LORE", "You can't kill me.");

            LanguageAPI.Add(Token + "PASSIVE_NAME", "Shocking Reanimation");
            LanguageAPI.Add(Token + "PASSIVE_DESC",
                "When you would die, you go down instead: nearby enemies are <style=cIsUtility>slowed</style> and you are <style=cIsUtility>invulnerable</style> for " +
                $"{VictorData.ReviveChannel:0}s, then get back up with <style=cIsHealing>{P(VictorData.ReviveHealth)} health</style>, dealing <style=cIsDamage>{D(VictorData.ReviveDamage)} damage</style> " +
                $"and <style=cIsUtility>stunning</style> enemies within {VictorData.ReviveRadius:0}m. Recharges in <style=cIsUtility>{VictorData.ReviveCooldown:0}s</style> or on a new stage. " +
                $"Victor has <style=cIsUtility>{VictorData.Movement.StaminaPips} stamina</style>, <style=cIsUtility>sprints automatically</style> out of combat, and can <style=cIsUtility>dash</style> and <style=cIsUtility>air jump</style>.");

            LanguageAPI.Add(Token + "GUN_NAME", "Memento Mori");
            LanguageAPI.Add(Token + "GUN_DESC",
                $"Fire for <style=cIsDamage>{D(VictorData.GunDamage)} damage</style>. Fires faster the longer you hold it.");

            LanguageAPI.Add(Token + "BATTERY_NAME", "Pain Battery");
            LanguageAPI.Add(Token + "BATTERY_DESC",
                $"<style=cIsHealth>Charges only from damage you take</style> ({P(VictorData.BatteryFillFraction)} of your max health fills it). " +
                $"When full, fire {VictorData.BatteryBolts} shocking bolts in a fan, dealing <style=cIsDamage>{D(VictorData.BatteryDamage)} damage</style> to each enemy hit. " +
                $"Each enemy hit <style=cIsHealing>heals {P(VictorData.BatteryHealMissing)} of your missing health</style>.");

            LanguageAPI.Add(Token + "JUMPSTART_NAME", "Jumpstart");
            LanguageAPI.Add(Token + "JUMPSTART_DESC",
                $"Shock yourself for <style=cIsHealth>{P(VictorData.JumpstartCost)} of your current health</style>, then <style=cIsHealing>heal {VictorData.JumpstartHealRatio:0.#}x that</style> " +
                $"(at least {P(VictorData.JumpstartMinHeal)} of max health) and gain <style=cIsUtility>+{VictorData.JumpstartSpeed:0} m/s move speed</style>, both fading over {VictorData.JumpstartSeconds:0.#}s. " +
                $"Holds <style=cIsUtility>{VictorData.JumpstartCharges} charges</style>.");

            LanguageAPI.Add(Token + "AURA_NAME", "Aura of Suffering");
            LanguageAPI.Add(Token + "AURA_DESC",
                $"Toggle an aura that deals <style=cIsDamage>{D(VictorData.AuraMinDamage)}</style> rising to <style=cIsDamage>{D(VictorData.AuraMaxDamage)} damage per second</style> " +
                $"over {VictorData.AuraRampSeconds:0}s to enemies within {VictorData.AuraRadius:0}m, and <style=cIsHealth>{P(VictorData.AuraSelfMin)} rising to {P(VictorData.AuraSelfMax)} of your max health per second to yourself</style>. " +
                "Lasts until you press it again or die.");
        }
    }

    public class VictorIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { VictorSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Victor] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
