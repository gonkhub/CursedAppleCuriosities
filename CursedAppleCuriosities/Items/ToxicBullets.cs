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
            $"Hits build up <style=cIsHealing>Toxin</style> on enemies by the damage they deal: " +
            $"<style=cIsDamage>{DamageToTrigger * 100:0}% damage</style> fills it. " +
            $"At full Toxin, inflict a poison dealing " +
            $"<style=cIsHealing>{DotDpsPercent * 100:0.#}% max HP/s</style> " +
            $"<style=cStack>(+{DotDpsPercent * 100:0.#}% per stack)</style> " +
            $"for <style=cIsUtility>{DotDuration:0}s</style> " +
            $"and reduce their <style=cIsHealing>healing by {HealReduction * 100:0}%</style>.";

        public override string ItemLore => "Hurts going in and coming out.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_toxicbullets.png";

        // 3.0 (user): Toxin is a 0-100 meter per attacker, filled by damage and drained like Mina's Love Bites
        // (was one charge per hit, 10 to trigger, 8 s each); shown on the enemy as 0-100 stacks.
        public static float DamageToTrigger;  // damage coefficient that fills the meter (10 = 1000% of your damage)
        public static float DotDpsPercent;
        public const float DotDuration     = 5f;
        public const float HealReduction   = 0.50f;
        public const float BuildUpMax         = 100f;
        public const float BuildUpDecayDelay  = 3f; // Love Bites'
        public const float BuildUpDecaySeconds = 2f; // full to nothing once draining

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
            ToxicChargeBuffDef.isHidden  = false; // the buildup shows as 0-100 stacks (user, 3.0)
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

            DamageToTrigger = 10f;
            DotDpsPercent = 0.005f; // 0.01 before 3.0 (user)
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
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            int itemCount = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody == null || ToxicChargeBuffDef == null) return;
            if (victimBody.healthComponent == null || !victimBody.healthComponent.alive) return;
            if (attackerBody.damage <= 0f || DamageToTrigger <= 0f) return;

            // Like any proc item, the hit must proc it: chance = its proc coefficient (luck applies) (user, 3.0).
            if (!Util.CheckRoll(100f * Mathf.Min(damageInfo.procCoefficient, 1f), attackerBody.master)) return;

            // The hit's damage coefficient (before armor and crits) fills the attacker's meter on this enemy.
            float add = damageInfo.damage / attackerBody.damage / DamageToTrigger * BuildUpMax;
            var buildup = victimBody.GetComponent<ToxicBuildup>() ?? victimBody.gameObject.AddComponent<ToxicBuildup>();
            if (!buildup.Build(attackerBody, add)) return;

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

    /// <summary>
    /// Server, on the enemy: each attacker's Toxin meter (0-100), drained like Mina's Love Bites: it starts emptying
    /// <see cref="ToxicBullets.BuildUpDecayDelay"/> s after that attacker's last hit. The fullest meter shows as stacks.
    /// </summary>
    public class ToxicBuildup : MonoBehaviour
    {
        private class Mark { public float BuildUp, LastHit; }
        private readonly System.Collections.Generic.Dictionary<CharacterBody, Mark> _marks =
            new System.Collections.Generic.Dictionary<CharacterBody, Mark>();
        private CharacterBody _body = null!;

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>Adds to the attacker's meter; true (and the meter empties) when it fills.</summary>
        public bool Build(CharacterBody attacker, float add)
        {
            if (add <= 0f) return false;
            if (!_marks.TryGetValue(attacker, out Mark m)) _marks[attacker] = m = new Mark();
            m.BuildUp += add;
            m.LastHit = Time.time;
            bool full = m.BuildUp >= ToxicBullets.BuildUpMax;
            if (full) _marks.Remove(attacker);
            Show();
            return full;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _marks.Count == 0) return;
            bool changed = false;
            foreach (var kv in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<CharacterBody, Mark>>(_marks))
            {
                if (kv.Key == null) { _marks.Remove(kv.Key!); changed = true; continue; }
                if (Time.time - kv.Value.LastHit < ToxicBullets.BuildUpDecayDelay) continue;
                kv.Value.BuildUp -= ToxicBullets.BuildUpMax / ToxicBullets.BuildUpDecaySeconds * Time.fixedDeltaTime;
                if (kv.Value.BuildUp <= 0f) _marks.Remove(kv.Key);
                changed = true;
            }
            if (changed) Show();
        }

        private void Show()
        {
            if (ToxicBullets.ToxicChargeBuffDef == null) return;
            float most = 0f;
            foreach (Mark m in _marks.Values) most = Mathf.Max(most, m.BuildUp);
            int want = Mathf.Clamp(Mathf.CeilToInt(most), 0, 99);
            if (_body.GetBuffCount(ToxicBullets.ToxicChargeBuffDef) != want)
                _body.SetBuffCount(ToxicBullets.ToxicChargeBuffDef.buffIndex, want);
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
