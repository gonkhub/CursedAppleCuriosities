using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class LuckyShot : ItemBase
    {
        public override string ItemName => "Lucky Shot";
        public override string ItemLangTokenName => "LUCKYSHOT";

        public override string ItemPickupDesc => "Critical strikes have a chance to strike twice.";
        public override string ItemFullDescription =>
            $"Your <style=cIsDamage>critical strikes</style> have a " +
            $"<style=cIsDamage>{OneStackChance:0}% chance</style> to strike a second time " +
            $"<style=cStack>(chance increases hyperbolically with additional copies)</style>.";

        public override string ItemLore =>
            "They say a little luck never hurt anyone, but in this case it's gonna do some serious damage amirite?";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_luckyshot.png";

        // Hyperbolic stacking: chance = 1 - 1/(1 + ChancePerStack * count). Approaches but never
        // reaches 100%, so extra copies always add value (RoR2's convention for chance items).
        public static float ChancePerStack;

        // Chance shown in the description for a single copy.
        private static float OneStackChance => 100f * (1f - 1f / (1f + ChancePerStack));

        public override void Init(ConfigFile config)
        {
            ChancePerStack = config.Bind("Red", "Lucky Shot - Hyperbolic Chance Per Stack", 0.4f,
                "Hyperbolic second-crit chance weight per stack (chance = 1 - 1/(1 + weight*count)).").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self,
            DamageInfo damageInfo,
            GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active) return;
            if (damageInfo == null || victim == null) return;
            if (!damageInfo.crit) return;                  // only critical strikes
            if (damageInfo.procCoefficient <= 0f) return;  // ignore our own 0-proc second hit
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            float chance = 100f * (1f - 1f / (1f + ChancePerStack * count));
            if (!Util.CheckRoll(chance, attackerBody.master)) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            // Replicate the original critical strike: same base damage + crit flag => an identical
            // crit lands again ("strikes twice"). procCoefficient 0 so it can't chain/loop.
            var proc = new DamageInfo
            {
                attacker         = damageInfo.attacker,
                inflictor        = damageInfo.attacker,
                damage           = damageInfo.damage,
                crit             = true,
                procCoefficient  = 0f,
                position         = damageInfo.position,
                damageType       = DamageType.Generic,
                damageColorIndex = DamageColorIndex.Item,
            };
            victimHC.TakeDamage(proc);
        }
    }
}
