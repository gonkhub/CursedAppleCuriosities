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
            MaxBonusPerStack = config.Bind("White", "Long Range - Max Bonus Damage", 0.30f,
                "Bonus damage per stack when hitting an enemy at maximum range.").Value;
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
            if (damageInfo.procCoefficient <= 0f) return;
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            float distance = Vector3.Distance(damageInfo.attacker.transform.position, victim.transform.position);
            if (distance <= MinRange) return;

            float rangeScalar = Mathf.Clamp01((distance - MinRange) / (MaxRange - MinRange));
            float bonusMult   = count * (MinBonusPerStack + (MaxBonusPerStack - MinBonusPerStack) * rangeScalar);
            if (bonusMult <= 0f) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            var proc = new DamageInfo();
            proc.attacker         = damageInfo.attacker;
            proc.inflictor        = damageInfo.attacker;
            proc.damage           = damageInfo.damage * bonusMult;
            proc.crit             = damageInfo.crit;
            proc.position         = damageInfo.position;
            proc.procCoefficient  = 0f;
            proc.damageColorIndex = DamageColorIndex.Item;

            victimHC.TakeDamage(proc);
        }
    }
}
