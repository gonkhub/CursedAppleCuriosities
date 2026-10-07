using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Counterspell : EquipmentBase
    {
        public override string EquipmentName => "Counterspell";
        public override string EquipmentLangTokenName => "COUNTERSPELL";

        public override string FullDescription =>
            $"Become <style=cIsHealing>warded</style> for <style=cIsUtility>{WardDuration}s</style>. " +
            $"The next instance of damage you take is <style=cIsHealing>completely negated</style>" +
            (HealFraction > 0f
                ? $", healing you for <style=cIsHealing>{HealFraction * 100:0}% of your max health</style>."
                : ".");

        public override string Lore =>
            "Don't mind the blood on that, it's gently used.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_counterspell.png";

        public const float WardDuration = 2f;
        public static float CooldownSeconds;
        public static float HealFraction; // reward on a successful block; set 0 for pure block

        public static BuffDef? WardBuffDef;
        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Counterspell - Cooldown", 25f,
                "Equipment cooldown in seconds.").Value;
            HealFraction = config.Bind("Equipment", "Counterspell - Heal On Block", 0.08f,
                "Max HP healed on a successful block (0 = pure block, no heal).").Value;

            var color               = new Color(0.4f, 0.8f, 1f);
            WardBuffDef             = ScriptableObject.CreateInstance<BuffDef>();
            WardBuffDef.name        = "CursedApple_CounterspellWard";
            WardBuffDef.canStack    = false;
            WardBuffDef.isDebuff    = false;
            WardBuffDef.buffColor   = color;
            WardBuffDef.iconSprite  = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(WardBuffDef);

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null) return false;

            var ward = body.GetComponent<CounterspellWard>()
                ?? body.gameObject.AddComponent<CounterspellWard>();
            ward.Activate(WardDuration);
            return true; // start the cooldown
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active
                && self.body != null
                && damageInfo != null
                && damageInfo.damage > 0f
                // DoT ticks shouldn't waste the ward — it should save a real hit.
                && damageInfo.dotIndex == DotController.DotIndex.None)
            {
                var ward = self.body.GetComponent<CounterspellWard>();
                if (ward != null && ward.IsActive)
                {
                    ward.Consume();

                    if (HealFraction > 0f)
                        self.body.healthComponent?.Heal(self.body.maxHealth * HealFraction, default(ProcChainMask));

                    // Fully negate this instance.
                    damageInfo.damage = 0f;
                    damageInfo.force  = Vector3.zero;
                }
            }

            orig(self, damageInfo);
        }
    }

    // Lives on a body while Counterspell's ward is up; the TakeDamage hook consumes it on the
    // first real hit. Manages the ward buff icon so the player can see the window is active.
    public class CounterspellWard : MonoBehaviour
    {
        private float         _timer;
        private CharacterBody _body;

        public bool IsActive => _timer > 0f;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Activate(float duration)
        {
            _timer = duration;
            ShowBuff(true);
        }

        public void Consume()
        {
            _timer = 0f;
            ShowBuff(false);
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) ShowBuff(false);
        }

        private void OnDestroy() => ShowBuff(false);

        private void ShowBuff(bool on)
        {
            if (!NetworkServer.active || _body == null) return;
            if (Counterspell.WardBuffDef == null || Counterspell.WardBuffDef.buffIndex == BuffIndex.None) return;
            var idx = Counterspell.WardBuffDef.buffIndex;
            if (on && !_body.HasBuff(idx)) _body.AddBuff(idx);
            else if (!on && _body.HasBuff(idx)) _body.RemoveBuff(idx);
        }
    }
}
