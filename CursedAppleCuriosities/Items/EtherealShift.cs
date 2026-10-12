using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class EtherealShift : EquipmentBase
    {
        public override string EquipmentName          => "Ethereal Shift";
        public override string EquipmentLangTokenName => "ETHEREALSHIFT";

        public override string PickupDesc => "Slip into the void: invulnerable but frozen in place. Your Special hits harder afterward.";
        public override string FullDescription =>
            $"Shift into the void for <style=cIsUtility>{ShiftDuration}s</style>, becoming " +
            $"<style=cIsHealing>invulnerable</style> and <style=cIsUtility>invisible</style>, but you " +
            $"<style=cIsHealth>can't move or use skills</style>. When the shift ends, your <style=cIsDamage>Special</style> deals " +
            $"<style=cIsDamage>+{SpecialBonus * 100:0}% damage</style> for <style=cIsUtility>{EmpowerDuration}s</style>.";

        public override string Lore =>
            "Step out for a moment. Come back with a vengeance.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_etherealshift.png";

        public static float CooldownSeconds;
        public static float ShiftDuration;
        public static float SpecialBonus;
        public static float EmpowerDuration;

        public static BuffDef? ShiftBuffDef;
        public static BuffDef? EmpowerBuffDef;
        public static EquipmentDef? StaticEquipmentDef;

        private static GameObject? _shiftEffect;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Ethereal Shift - Cooldown", 45f,
                "Equipment cooldown in seconds.").Value;
            ShiftDuration = config.Bind("Equipment", "Ethereal Shift - Duration", 3f,
                "Seconds of invulnerability (no skills while shifted).").Value;
            SpecialBonus = config.Bind("Equipment", "Ethereal Shift - Special Damage Bonus", 1f,
                "Bonus damage on Special-skill hits after the shift ends (1 = +100%).").Value;
            EmpowerDuration = config.Bind("Equipment", "Ethereal Shift - Special Bonus Duration", 8f,
                "Seconds the Special damage bonus lasts after the shift ends.").Value;

            var shiftColor             = new Color(0.55f, 0.95f, 0.75f);
            ShiftBuffDef               = ScriptableObject.CreateInstance<BuffDef>();
            ShiftBuffDef.name          = "CursedApple_EtherealShift";
            ShiftBuffDef.canStack      = false;
            ShiftBuffDef.isDebuff      = false;
            ShiftBuffDef.buffColor     = shiftColor;
            ShiftBuffDef.iconSprite    = ItemBase.BuffIcon(IconFileName, shiftColor);
            ContentAddition.AddBuffDef(ShiftBuffDef);

            var empowerColor           = new Color(1f, 0.55f, 0.85f);
            EmpowerBuffDef             = ScriptableObject.CreateInstance<BuffDef>();
            EmpowerBuffDef.name        = "CursedApple_EtherealEmpower";
            EmpowerBuffDef.canStack    = false;
            EmpowerBuffDef.isDebuff    = false;
            EmpowerBuffDef.buffColor   = empowerColor;
            EmpowerBuffDef.iconSprite  = ItemBase.BuffIcon(IconFileName, empowerColor);
            ContentAddition.AddBuffDef(EmpowerBuffDef);

            SkillSilence.Register(ShiftBuffDef);
            EquipmentFx.RegisterBuffFx(ShiftBuffDef, "RoR2/Base/Common/VoidFogMildEffect.prefab", "etherealshift_loop");
            EquipmentFx.RegisterBuffFx(EmpowerBuffDef, "RoR2/Base/EnergizedOnEquipmentUse/EnergizedEffect.prefab");

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            Hooks();
        }

        public override void RegisterSounds()
        {
            EquipmentFx.RegisterSound("etherealshift_cast", "sounds/mods/tech/stasis/ethereal_shift_cast", fallback: "Play_item_proc_teleportOnLowHealth");
            EquipmentFx.RegisterSound("etherealshift_end", "sounds/mods/tech/stasis/ethereal_shift_end");
            EquipmentFx.RegisterSound("etherealshift_loop", "sounds/mods/tech/stasis/ethereal_shift_active", volume: 0.5f, range: 25f);
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.CharacterMotor.UpdateVelocity += CharacterMotor_UpdateVelocity;
        }

        // Hold the body still (no walking, jumping, dashing or gravity) while shifted. This is the motor's last
        // velocity step, so it also stops the Deadlock heroes' dash states; it runs wherever the motor is
        // simulated (the owning client for players), and the shift buff is networked.
        private static void CharacterMotor_UpdateVelocity(On.RoR2.CharacterMotor.orig_UpdateVelocity orig, CharacterMotor self, ref Vector3 currentVelocity, float deltaTime)
        {
            orig(self, ref currentVelocity, deltaTime);
            if (ShiftBuffDef == null || ShiftBuffDef.buffIndex == BuffIndex.None) return;
            if (self.TryGetComponent(out CharacterBody body) && body.HasBuff(ShiftBuffDef.buffIndex))
                currentVelocity = Vector3.zero;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null) return false;

            var shift = body.GetComponent<EtherealShiftState>() ?? body.gameObject.AddComponent<EtherealShiftState>();
            shift.Begin(ShiftDuration * DurationExtender.Current); // the user's, from the equipment scope
            SpawnEffect(body);
            EquipmentFx.Sound("etherealshift_cast", body.corePosition, body.gameObject);
            return true;
        }

        internal static void SpawnEffect(CharacterBody body)
        {
            _shiftEffect ??= Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidSurvivor/VoidBlinkMuzzleflash.prefab").WaitForCompletion();
            if (_shiftEffect != null)
                EffectManager.SpawnEffect(_shiftEffect, new EffectData { origin = body.corePosition, scale = body.radius * 2f }, true);
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active
                && damageInfo?.attacker != null
                && EmpowerBuffDef != null
                && ((DamageSource)damageInfo.damageType & DamageSource.Special) != 0)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (attackerBody != null && attackerBody.HasBuff(EmpowerBuffDef))
                    damageInfo.damage *= 1f + SpecialBonus;
            }
            orig(self, damageInfo);
        }
    }

    // Server-side: the shift itself is three timed buffs (ours, which locks skills via SkillSilence and
    // movement via the UpdateVelocity hook; HiddenInvincibility; Cloak), so they expire on their own and never strip the same buff granted by
    // something else (e.g. Old War Stealthkit's Cloak). This only waits out the shift to grant the bonus.
    public class EtherealShiftState : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timer;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin(float duration)
        {
            if (!NetworkServer.active || _body == null) return;
            _timer = duration;
            using (DurationExtender.Exact()) // already extended: buffs match the timer
            {
                if (EtherealShift.ShiftBuffDef != null) _body.AddTimedBuff(EtherealShift.ShiftBuffDef, duration);
                _body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, duration);
                _body.AddTimedBuff(RoR2Content.Buffs.Cloak, duration);
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _timer <= 0f) return;
            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;

            if (EtherealShift.EmpowerBuffDef != null)
                _body.AddTimedBuff(EtherealShift.EmpowerBuffDef, EtherealShift.EmpowerDuration);
            EtherealShift.SpawnEffect(_body);
            EquipmentFx.Sound("etherealshift_end", _body.corePosition, _body.gameObject);
        }
    }
}
