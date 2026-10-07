using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class BloodTribute : EquipmentBase
    {
        public override string EquipmentName          => "Blood Tribute";
        public override string EquipmentLangTokenName => "BLOODTRIBUTE";

        public override string FullDescription =>
            $"<style=cIsUtility>Toggle</style>. While active, drain " +
            $"<style=cIsHealth>{HpDrainPercent * 100:0}% max HP/s</style> and reduce " +
            $"<style=cIsHealth>healing received by {HealReductionPercent * 100:0}%</style>, " +
            $"but gain <style=cIsDamage>+{AttackSpeedBonus * 100:0}% attack speed</style> " +
            $"and <style=cIsUtility>+{MoveSpeedBonus * 100:0}% move speed</style>.";

        public override string Lore => "Pain is just weakness leaving the body.";

        public override float Cooldown => 1f;
        protected override string? IconFileName => "icon_bloodtribute.png";

        public static float HpDrainPercent;
        public static float AttackSpeedBonus;
        public static float MoveSpeedBonus;
        public static float HealReductionPercent; // healing reduced while active

        public static EquipmentDef? StaticEquipmentDef;
        public static BuffDef?      ActiveBuffDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(0.85f, 0.05f, 0.05f);
            ActiveBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            ActiveBuffDef.name      = "CursedApple_BloodTributeActive";
            ActiveBuffDef.canStack  = false;
            ActiveBuffDef.isDebuff  = false;
            ActiveBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(ActiveBuffDef);

            HpDrainPercent = config.Bind("Equipment", "Blood Tribute - HP Drain Per Second", 0.05f,
                "Max HP drained per second while active.").Value;
            AttackSpeedBonus = config.Bind("Equipment", "Blood Tribute - Attack Speed Bonus", 1.0f,
                "Bonus attack speed while active (1.0 = +100%).").Value;
            MoveSpeedBonus = config.Bind("Equipment", "Blood Tribute - Move Speed Bonus", 0.5f,
                "Bonus move speed while active (0.5 = +50%).").Value;
            HealReductionPercent = config.Bind("Equipment", "Blood Tribute - Healing Reduction", 0.50f,
                "Healing received is reduced by this fraction while active.").Value;

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            ActiveBuffDef.iconSprite = EquipmentDef?.pickupIconSprite
                                    ?? ItemBase.CreatePlaceholderSprite(color);

            if (EquipmentDef != null)
            {
                EquipmentDef.enigmaCompatible       = false;
                EquipmentDef.canBeRandomlyTriggered = false;
            }

            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
            On.RoR2.HealthComponent.Heal             += HealthComponent_Heal;
        }

        // While the tribute is active, healing the body receives is reduced.
        private float HealthComponent_Heal(
            On.RoR2.HealthComponent.orig_Heal orig,
            HealthComponent self, float amount, ProcChainMask procChainMask, bool nonRegen)
        {
            if (ActiveBuffDef != null
                && self.body != null
                && self.body.HasBuff(ActiveBuffDef.buffIndex))
                amount *= 1f - HealReductionPercent;
            return orig(self, amount, procChainMask, nonRegen);
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot?.characterBody;
            if (body == null || ActiveBuffDef == null) return false;

            if (body.HasBuff(ActiveBuffDef.buffIndex))
                body.RemoveBuff(ActiveBuffDef.buffIndex);
            else
                body.AddBuff(ActiveBuffDef);

            return true;
        }

        private void OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (!NetworkServer.active || self.inventory == null) return;

            var  eqIdx    = StaticEquipmentDef?.equipmentIndex ?? EquipmentIndex.None;
            bool holdsIt  = eqIdx != EquipmentIndex.None
                         && self.inventory.currentEquipmentIndex == eqIdx;

            var tracker = self.GetComponent<BloodTributeTracker>();
            if (holdsIt && tracker == null)
                self.gameObject.AddComponent<BloodTributeTracker>();
            else if (!holdsIt && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (ActiveBuffDef == null || !sender.HasBuff(ActiveBuffDef.buffIndex)) return;
            args.attackSpeedMultAdd += AttackSpeedBonus;
            args.moveSpeedMultAdd   += MoveSpeedBonus;
        }
    }

    public class BloodTributeTracker : MonoBehaviour
    {
        private CharacterBody _body;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy()
        {
            if (_body != null && BloodTribute.ActiveBuffDef != null
                && _body.HasBuff(BloodTribute.ActiveBuffDef.buffIndex))
                _body.RemoveBuff(BloodTribute.ActiveBuffDef.buffIndex);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body?.healthComponent == null) return;
            if (BloodTribute.ActiveBuffDef == null) return;
            if (!_body.HasBuff(BloodTribute.ActiveBuffDef.buffIndex)) return;

            float dmg = _body.maxHealth * BloodTribute.HpDrainPercent * Time.fixedDeltaTime;
            if (dmg <= 0f) return;

            var di = new DamageInfo();
            di.damage           = dmg;
            di.procCoefficient  = 0f;
            di.position         = _body.transform.position;
            di.damageType       = DamageType.BypassArmor;
            di.damageColorIndex = DamageColorIndex.Bleed;
            _body.healthComponent.TakeDamage(di);
        }
    }
}
