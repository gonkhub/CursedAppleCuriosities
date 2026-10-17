using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Inhibitor : ItemBase
    {
        public override string ItemName => "Inhibitor";
        public override string ItemLangTokenName => "INHIBITOR";

        public override string ItemPickupDesc => "Chance on hit to make enemies deal less damage and slow them.";
        public override string ItemFullDescription =>
            $"<style=cIsUtility>{ProcChance:0}%</style> chance on hit to <style=cIsUtility>inhibit</style> an enemy for <style=cIsUtility>{Duration}s</style>: it deals " +
            $"<style=cIsHealing>{ReductionFor(1) * 100:0}%</style> <style=cStack>(+{ReductionFor(1) * 100:0}% per stack, hyperbolically)</style> " +
            $"<style=cIsHealing>less damage</style> and is <style=cIsUtility>slowed by {SlowAmount * 100:0}%</style>.";

        public override string ItemLore =>
            "It doesn't make them any less angry. Just less effective about it.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_inhibitor.png";

        public const float Duration = 4f;
        public const float ProcChance = 25f; // percent, x the hit's proc coefficient, luck applies (user 3.1; was every hit)
        public static float ReductionCoefficient; // hyperbolic: 1 - 1/(1 + c*stacks); 1/3 → 25%, 40%, 50%...
        public static float SlowAmount;

        public static BuffDef? InhibitedBuffDef;

        public static float ReductionFor(int stacks) => 1f - 1f / (1f + ReductionCoefficient * stacks);

        public override void Init(ConfigFile config)
        {
            ReductionCoefficient = 1f / 3f;
            SlowAmount = 0.25f;

            var color                   = new Color(0.85f, 0.3f, 0.3f);
            InhibitedBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            InhibitedBuffDef.name       = "CursedApple_Inhibited";
            InhibitedBuffDef.canStack   = true; // count = strongest attacker's Inhibitor stacks
            InhibitedBuffDef.isDebuff   = true;
            InhibitedBuffDef.buffColor  = color;
            InhibitedBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(InhibitedBuffDef);

            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy   += OnHitEnemy;
            On.RoR2.HealthComponent.TakeDamage      += HealthComponent_TakeDamage;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
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
            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody == null) return;
            if (!Util.CheckRoll(ProcChance * damageInfo.procCoefficient, attackerBody.master)) return;

            var debuff = victimBody.GetComponent<InhibitorDebuff>()
                ?? victimBody.gameObject.AddComponent<InhibitorDebuff>();
            debuff.Apply(count, Duration * DurationExtender.Current); // the attacker's, from the on-hit scope
        }

        // The inhibited enemy is the ATTACKER here: shrink what it deals.
        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (FinalDamage(damageInfo)) { orig(self, damageInfo); return; } // already worked out (Bloodletting's ticks)
            if (NetworkServer.active
                && damageInfo?.attacker != null
                && InhibitedBuffDef != null
                && InhibitedBuffDef.buffIndex != BuffIndex.None)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                int stacks = attackerBody != null ? attackerBody.GetBuffCount(InhibitedBuffDef.buffIndex) : 0;
                if (stacks > 0)
                    damageInfo.damage *= 1f - ReductionFor(stacks);
            }
            orig(self, damageInfo);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (InhibitedBuffDef != null && sender.HasBuff(InhibitedBuffDef))
                args.moveSpeedReductionMultAdd += SlowAmount;
        }
    }

    // Server-side timer for the Inhibited debuff. Keeps the strongest stack count seen while active,
    // refreshes on every hit, and clears the buff when it runs out.
    public class InhibitorDebuff : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timer;
        private int           _stacks;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Apply(int stacks, float duration)
        {
            _timer  = duration;
            _stacks = Mathf.Max(_stacks, stacks);
            Sync(_stacks);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _timer <= 0f) return;
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f)
            {
                _stacks = 0;
                Sync(0);
            }
        }

        private void OnDestroy() => Sync(0);

        private void Sync(int count)
        {
            if (!NetworkServer.active || _body == null) return;
            var def = Inhibitor.InhibitedBuffDef;
            if (def == null || def.buffIndex == BuffIndex.None) return;
            if (_body.GetBuffCount(def.buffIndex) != count)
                _body.SetBuffCount(def.buffIndex, count);
        }
    }
}
