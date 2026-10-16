using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Sharpshooter : ItemBase
    {
        public override string ItemName => "Long Range";
        public override string ItemLangTokenName => "LONGRANGE";

        public override string ItemPickupDesc => "Deal bonus damage to distant enemies.";
        public override string ItemFullDescription =>
            $"Hitting an enemy beyond <style=cIsUtility>{MinRange}m</style> deals " +
            $"<style=cIsDamage>+{MinBonusPerStack * 100:0}%</style> to " +
            $"<style=cIsDamage>+{MaxBonusPerStack * 100:0}% bonus damage</style> " +
            $"<style=cStack>per stack</style>, " +
            $"scaling with distance up to <style=cIsUtility>{MaxRange}m</style>.";

        public override string ItemLore =>
            "Hope you win whatever fight you're about to start.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_longrange.png";

        public const float MinRange          = 30f;
        public const float MaxRange          = 80f;
        public const float MinBonusPerStack  = 0.10f; // bonus at minimum range per stack
        public static float MaxBonusPerStack;          // bonus at maximum range per stack (config)

        public override void Init(ConfigFile config)
        {
            MaxBonusPerStack = 0.30f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // Boosts the hit itself (1.8, user; it used to be a separate 0-proc hit): one damage number, and the
        // bonus is part of the hit everything else sees.
        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (FinalDamage(damageInfo)) { orig(self, damageInfo); return; } // already worked out (Bloodletting's ticks)
            if (NetworkServer.active
                && damageInfo != null
                && damageInfo.attacker != null
                && damageInfo.procCoefficient > 0f
                && damageInfo.dotIndex == DotController.DotIndex.None
                && damageInfo.damage > 0f)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                int count = attackerBody != null && attackerBody.inventory != null ? attackerBody.inventory.GetItemCountEffective(ItemDef) : 0;
                if (count > 0)
                {
                    float distance = Vector3.Distance(damageInfo.attacker.transform.position, self.transform.position);
                    float reach = MysticExpansion.RadiusMultiplier(attackerBody); // Mystic Expansion slides the window closer
                    float min = MinRange / reach, max = MaxRange / reach;
                    if (distance > min)
                    {
                        float rangeScalar = Mathf.Clamp01((distance - min) / (max - min));
                        damageInfo.damage *= 1f + count * (MinBonusPerStack + (MaxBonusPerStack - MinBonusPerStack) * rangeScalar);
                    }
                }
            }
            orig(self, damageInfo);
        }
    }
}
