using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class BattleVest : ItemBase
    {
        public override string ItemName => "Battle Vest";
        public override string ItemLangTokenName => "BATTLEVEST";

        public override string ItemPickupDesc => "Deal more damage while at high health.";
        public override string ItemFullDescription =>
            $"While above <style=cIsHealing>{HealthThreshold * 100:0}% health</style>, " +
            $"deal <style=cIsDamage>+{DamageBonusPerStack * 100:0}% more damage</style> " +
            $"<style=cStack>(+{DamageBonusPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore => "Stay strong, buddy!";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_battlevest.png";

        public const float HealthThreshold     = 0.75f;
        public static float DamageBonusPerStack;

        public override void Init(ConfigFile config)
        {
            DamageBonusPerStack = config.Bind("White", "Battle Vest - Damage Per Stack", 0.20f,
                "Bonus damage per stack while above the health threshold.").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self, DamageInfo damageInfo)
        {
            if (FinalDamage(damageInfo)) { orig(self, damageInfo); return; } // already worked out (Bloodletting's ticks)
            if (NetworkServer.active
                && damageInfo?.attacker != null
                && damageInfo.damage > 0f)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (attackerBody?.inventory != null && attackerBody.healthComponent != null)
                {
                    int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
                    if (count > 0)
                    {
                        float hpFrac = attackerBody.healthComponent.health
                                     / attackerBody.healthComponent.fullHealth;
                        if (hpFrac >= HealthThreshold)
                            damageInfo.damage *= 1f + DamageBonusPerStack * count;
                    }
                }
            }
            orig(self, damageInfo);
        }
    }
}
