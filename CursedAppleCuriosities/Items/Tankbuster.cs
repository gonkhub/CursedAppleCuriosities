using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Tankbuster : ItemBase
    {
        public override string ItemName => "Tankbuster";
        public override string ItemLangTokenName => "TANKBUSTER";

        public override string ItemFullDescription =>
            $"Dealing <style=cIsDamage>{DamageThreshold * 100:0}%+ damage</style> in a single hit " +
            $"deals a bonus burst of <style=cIsDamage>{BurstPercent * 100:0}% of the enemy's HP</style>. " +
            $"<style=cIsUtility>{BaseCooldown:0}s cooldown</style> " +
            $"<style=cStack>(-{CooldownReduction * 100:0}% per stack)</style>.";

        public override string ItemLore => "Somebody's been hitting the gym.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_tankbuster.png";

        public const float DamageThreshold  = 5f;
        public static float BurstPercent;
        public const float BaseCooldown     = 20f;
        public const float CooldownReduction = 0.50f;
        public const float MinCooldown      = 2f;

        public static ItemDef? StaticItemDef;

        public override void Init(ConfigFile config)
        {
            BurstPercent = config.Bind("Green", "Tankbuster - Burst Percent Of Enemy HP", 0.10f,
                "Bonus burst damage as a fraction of the enemy's max HP on a heavy hit.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
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

            if (attackerBody.damage <= 0f) return;
            if (damageInfo.damage / attackerBody.damage < DamageThreshold) return;

            var tracker = attackerBody.GetComponent<TankbusterTracker>()
                ?? attackerBody.gameObject.AddComponent<TankbusterTracker>();
            if (!tracker.IsReady) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            var di = new DamageInfo();
            di.attacker         = attackerBody.gameObject;
            di.inflictor        = attackerBody.gameObject;
            di.damage           = victimHC.fullHealth * BurstPercent;
            di.procCoefficient  = 0f;
            di.damageType       = DamageType.BypassArmor;
            di.damageColorIndex = DamageColorIndex.WeakPoint;
            di.position         = damageInfo.position;
            victimHC.TakeDamage(di);

            float cooldown = Mathf.Max(MinCooldown,
                BaseCooldown * Mathf.Pow(1f - CooldownReduction, itemCount - 1));
            tracker.StartCooldown(cooldown);
        }
    }

    public class TankbusterTracker : MonoBehaviour
    {
        private float _cooldownRemaining;

        public bool IsReady => _cooldownRemaining <= 0f;

        private void Update()
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= Time.deltaTime;
        }

        public void StartCooldown(float duration) => _cooldownRemaining = duration;
    }
}
