using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class IntensifyingMagazine : ItemBase
    {
        public override string ItemName => "Intensifying Magazine";
        public override string ItemLangTokenName => "INTENSIFYINGMAGAZINE";

        public override string ItemPickupDesc => "Repeatedly hitting enemies ramps up your damage.";
        public override string ItemFullDescription =>
            $"Repeatedly hitting enemies builds a damage bonus up to " +
            $"<style=cIsDamage>+{BaseMaxBonus * 100:0}% damage</style> over " +
            $"<style=cIsUtility>{BaseMaxRampTime:0}s</style> " +
            $"<style=cStack>(+{BonusMaxPerStack * 100:0}% max bonus and +{RampTimePerStack:0}s per stack)</style>. " +
            $"The bonus fades between hits.";

        public override string ItemLore =>
            "Tell you what, I'll throw in a free key chain.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_intensifyingmagazine.png";

        public const float BaseMaxRampTime  = 2f;
        public const float RampTimePerStack = 1f;
        public static float BaseMaxBonus;
        public const float BonusMaxPerStack = 0.30f;
        public const float HitGapDuration  = 0.3f;

        public static BuffDef? RampBuffDef;
        public static ItemDef? StaticItemDef;

        public static float GetMaxRampTime(int count) =>
            BaseMaxRampTime + RampTimePerStack * (count - 1);

        private static bool _isProcessing = false;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(1f, 0.45f, 0.1f); // fiery orange
            RampBuffDef             = ScriptableObject.CreateInstance<BuffDef>();
            RampBuffDef.name        = "CursedApple_IntensifyingMag";
            RampBuffDef.canStack    = false;
            RampBuffDef.isDebuff    = false;
            RampBuffDef.buffColor   = color;
            RampBuffDef.iconSprite  = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(RampBuffDef);

            BaseMaxBonus = config.Bind("White", "Intensifying Magazine - Max Damage Bonus", 0.45f,
                "Maximum sustained-fire damage bonus at 1 stack.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
            On.RoR2.HealthComponent.TakeDamage    += HealthComponent_TakeDamage;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self,
            DamageInfo damageInfo,
            GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active) return;
            if (damageInfo == null || damageInfo.procCoefficient <= 0f) return;
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            if (attackerBody.inventory.GetItemCountEffective(ItemDef) <= 0) return;

            var tracker = attackerBody.GetComponent<IntensifyingMagTracker>()
                ?? attackerBody.gameObject.AddComponent<IntensifyingMagTracker>();
            tracker.OnHit();
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            bool applied = false;
            if (!_isProcessing
                && NetworkServer.active
                && damageInfo != null
                && damageInfo.procCoefficient > 0f
                && damageInfo.attacker != null)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (attackerBody?.inventory != null)
                {
                    int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
                    if (count > 0)
                    {
                        var tracker = attackerBody.GetComponent<IntensifyingMagTracker>();
                        if (tracker != null && tracker.RampRatio > 0f)
                        {
                            float maxBonus = BaseMaxBonus + BonusMaxPerStack * (count - 1);
                            damageInfo.damage *= 1f + tracker.RampRatio * maxBonus;
                            _isProcessing = true;
                            applied       = true;
                        }
                    }
                }
            }

            orig(self, damageInfo);

            // Only the call that SET the guard clears it (a nested TakeDamage must not clear it early).
            if (applied) _isProcessing = false;
        }
    }

    public class IntensifyingMagTracker : MonoBehaviour
    {
        private float         _rampTimer;
        private float         _hitTimer;
        private CharacterBody _body;
        private bool          _buffActive;

        public float RampRatio => Mathf.Clamp01(_rampTimer / IntensifyingMagazine.GetMaxRampTime(GetItemCount()));

        private int GetItemCount()
        {
            if (_body?.inventory == null || IntensifyingMagazine.StaticItemDef == null) return 1;
            return Mathf.Max(1, _body.inventory.GetItemCountEffective(IntensifyingMagazine.StaticItemDef));
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void OnHit() => _hitTimer = IntensifyingMagazine.HitGapDuration;

        private void Update()
        {
            if (_hitTimer > 0f)
            {
                _hitTimer  -= Time.deltaTime;
                _rampTimer  = Mathf.Min(IntensifyingMagazine.GetMaxRampTime(GetItemCount()), _rampTimer + Time.deltaTime);
            }
            else
            {
                _rampTimer = Mathf.Max(0f, _rampTimer - Time.deltaTime);
            }

            // Buff is visible while any ramp bonus is active (it lingers as the ramp fades out).
            SyncBuff(_rampTimer > 0f);
        }

        private void OnDestroy() => SyncBuff(false);

        private void SyncBuff(bool active)
        {
            if (!NetworkServer.active || _body == null) return;
            if (IntensifyingMagazine.RampBuffDef == null
                || IntensifyingMagazine.RampBuffDef.buffIndex == BuffIndex.None) return;
            if (active == _buffActive) return;

            _buffActive = active;
            var idx = IntensifyingMagazine.RampBuffDef.buffIndex;
            if (active && !_body.HasBuff(idx)) _body.AddBuff(idx);
            else if (!active && _body.HasBuff(idx)) _body.RemoveBuff(idx);
        }
    }
}
