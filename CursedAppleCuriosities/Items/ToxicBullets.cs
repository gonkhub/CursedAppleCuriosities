using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class ToxicBullets : ItemBase
    {
        public override string ItemName => "Toxic Bullets";
        public override string ItemLangTokenName => "TOXICBULLETS";

        public override string ItemPickupDesc => "Repeated hits poison enemies and reduce their healing.";
        public override string ItemFullDescription =>
            $"Hits build up <style=cIsHealing>Toxin</style> on enemies " +
            $"(max <style=cIsUtility>{ChargesRequired} charges</style>). " +
            $"At full charge, inflict a poison dealing " +
            $"<style=cIsHealing>{DotDpsPercent * 100:0}% max HP/s</style> " +
            $"<style=cStack>(+{DotDpsPercent * 100:0}% per stack)</style> " +
            $"for <style=cIsUtility>{DotDuration:0}s</style> " +
            $"and reduce their <style=cIsHealing>healing by {HealReduction * 100:0}%</style>.";

        public override string ItemLore => "Hurts going in and coming out.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_toxicbullets.png";

        public static int   ChargesRequired;
        public static float DotDpsPercent;
        public const float DotDuration     = 5f;
        public const float HealReduction   = 0.50f;
        public const float ChargeDuration  = 8f;

        public static ItemDef? StaticItemDef;
        public static BuffDef? ToxicChargeBuffDef;
        public static BuffDef? ToxicDoTBuffDef;
        public static BuffDef? ToxicWeakenedBuffDef;

        public override void Init(ConfigFile config)
        {
            var chargeColor = new Color(0.3f, 0.8f, 0.2f);
            ToxicChargeBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            ToxicChargeBuffDef.name      = "CursedApple_ToxinCharge";
            ToxicChargeBuffDef.canStack  = true;
            ToxicChargeBuffDef.isDebuff  = true;
            ToxicChargeBuffDef.buffColor = chargeColor;
            ToxicChargeBuffDef.isHidden  = true;  // UI: only the poison itself shows
            ContentAddition.AddBuffDef(ToxicChargeBuffDef);

            var dotColor = new Color(0.15f, 0.7f, 0.1f);
            ToxicDoTBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            ToxicDoTBuffDef.name      = "CursedApple_ToxinPoison";
            ToxicDoTBuffDef.canStack  = false;
            ToxicDoTBuffDef.isDebuff  = true;
            ToxicDoTBuffDef.buffColor = dotColor;
            ContentAddition.AddBuffDef(ToxicDoTBuffDef);

            var weakColor = new Color(0.55f, 0.55f, 0.55f);
            ToxicWeakenedBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            ToxicWeakenedBuffDef.name      = "CursedApple_ToxinWeakened";
            ToxicWeakenedBuffDef.canStack  = false;
            ToxicWeakenedBuffDef.isDebuff  = true;
            ToxicWeakenedBuffDef.buffColor = weakColor;
            ToxicWeakenedBuffDef.isHidden  = true;  // UI: folded into the poison icon
            ContentAddition.AddBuffDef(ToxicWeakenedBuffDef);

            ChargesRequired = config.Bind("Green", "Toxic Bullets - Charges To Trigger", 10,
                "Number of hits to build full Toxin before the poison triggers.").Value;
            DotDpsPercent = config.Bind("Green", "Toxic Bullets - Poison DPS Percent", 0.01f,
                "Max HP per second dealt by the poison at 1 stack.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            var icon = ItemDef?.pickupIconSprite;
            ToxicChargeBuffDef.iconSprite   = icon ?? BuffIcon(null, chargeColor);
            ToxicDoTBuffDef.iconSprite      = icon ?? BuffIcon(null, dotColor);
            ToxicWeakenedBuffDef.iconSprite = icon ?? BuffIcon(null, weakColor);
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
            On.RoR2.HealthComponent.Heal           += HealthComponent_Heal;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            int itemCount = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody == null || ToxicChargeBuffDef == null) return;

            victimBody.AddTimedBuff(ToxicChargeBuffDef, ChargeDuration);

            int charges = victimBody.GetBuffCount(ToxicChargeBuffDef.buffIndex);
            if (charges < ChargesRequired) return;

            for (int i = 0; i < charges; i++)
                victimBody.RemoveBuff(ToxicChargeBuffDef.buffIndex);

            float dps    = DotDpsPercent * itemCount;
            var tracker  = victimBody.GetComponent<ToxicPoisonTracker>();
            if (tracker != null)
                tracker.Refresh(dps, attackerBody.gameObject);
            else
            {
                tracker = victimBody.gameObject.AddComponent<ToxicPoisonTracker>();
                tracker.Initialize(dps, attackerBody.gameObject);
            }

            if (ToxicWeakenedBuffDef != null)
                victimBody.AddTimedBuff(ToxicWeakenedBuffDef, DotDuration);
        }

        private float HealthComponent_Heal(
            On.RoR2.HealthComponent.orig_Heal orig,
            HealthComponent self, float amount, ProcChainMask procChainMask, bool nonRegen)
        {
            if (ToxicWeakenedBuffDef != null
                && self.body != null
                && self.body.GetBuffCount(ToxicWeakenedBuffDef.buffIndex) > 0)
                amount *= 1f - HealReduction;
            return orig(self, amount, procChainMask, nonRegen);
        }
    }

    public class ToxicPoisonTracker : MonoBehaviour
    {
        private const float TickInterval = 0.25f;

        private CharacterBody _body;
        private GameObject? _attacker; // credited with the damage: kills pay gold and XP and count as theirs
        private float _dpsPercent;
        private float _remaining;
        private float _tick;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Initialize(float dpsPercent, GameObject attacker)
        {
            _attacker   = attacker;
            _dpsPercent = dpsPercent;
            _remaining  = ToxicBullets.DotDuration * DurationExtender.Current; // started inside the attacker's on-hit scope
            if (ToxicBullets.ToxicDoTBuffDef != null && _body != null)
                _body.AddBuff(ToxicBullets.ToxicDoTBuffDef);
        }

        public void Refresh(float dpsPercent, GameObject attacker)
        {
            if (dpsPercent >= _dpsPercent) _attacker = attacker; // the strongest poison's owner gets the credit
            _dpsPercent = Mathf.Max(_dpsPercent, dpsPercent);
            _remaining  = Mathf.Max(_remaining, ToxicBullets.DotDuration * DurationExtender.Current);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body?.healthComponent == null) return;
            float step = Mathf.Min(Time.fixedDeltaTime, Mathf.Max(0f, _remaining));
            _remaining -= Time.fixedDeltaTime;
            _tick      += step;
            if (_tick >= TickInterval || _remaining <= 0f)
            {
                float dmg = _body.maxHealth * _dpsPercent * _tick; // exactly dps% of max health per second, armor ignored
                _tick = 0f;
                if (dmg > 0f) Damage(dmg);
            }
            if (_remaining <= 0f) Destroy(this);
        }

        private void Damage(float dmg)
        {
            var di = new DamageInfo();
            di.attacker         = _attacker;
            di.inflictor        = _attacker;
            di.damage           = dmg;
            di.damageType       = DamageType.BypassArmor | DamageType.DoT;
            di.procCoefficient  = 0f;
            di.position         = _body.transform.position;
            di.damageColorIndex = DamageColorIndex.Poison;
            _body.healthComponent.TakeDamage(di);
        }

        private void OnDestroy()
        {
            if (_body != null && ToxicBullets.ToxicDoTBuffDef != null
                && _body.HasBuff(ToxicBullets.ToxicDoTBuffDef.buffIndex))
                _body.RemoveBuff(ToxicBullets.ToxicDoTBuffDef.buffIndex);
        }
    }
}
