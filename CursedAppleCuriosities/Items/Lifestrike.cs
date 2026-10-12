using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Lifestrike : ItemBase
    {
        public override string ItemName => "Lifestrike";
        public override string ItemLangTokenName => "LIFESTRIKE";

        public override string ItemPickupDesc => "Hitting nearby enemies heals you and slows them.";
        public override string ItemFullDescription =>
            $"Hitting an enemy within <style=cIsUtility>{Range}m</style> heals you for " +
            $"<style=cIsHealing>{BaseHealFraction * 100:0}% of the damage dealt</style> " +
            $"<style=cStack>(+{HealFractionPerStack * 100:0}% per stack)</style>, up to " +
            $"<style=cIsHealing>{BaseHealCapFraction * 100:0}% of your max health</style> per hit, and " +
            $"<style=cIsUtility>slows</style> them for <style=cIsUtility>{SlowDuration}s</style>.";

        public override string ItemLore =>
            "Someone's gonna be bleedin' on the concrete, and it ain't gonna be you.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_lifestrike.png";

        public const float Range               = 13f;
        public static float BaseHealFraction;            // of damage dealt
        public const float HealFractionPerStack = 0.04f;
        public const float BaseHealCapFraction = 0.04f; // per-hit cap as a fraction of max HP
        public const float HealCapPerStack     = 0.02f;
        public const float SlowDuration        = 2f;

        public override void Init(ConfigFile config)
        {
            BaseHealFraction = config.Bind("Green", "Lifestrike - Heal Fraction Of Damage", 0.08f,
                "Healing as a fraction of damage dealt on a close hit, at 1 stack.").Value;
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
            if (damageInfo.procCoefficient <= 0f || damageInfo.damage <= 0f) return;
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            float distance = Vector3.Distance(damageInfo.attacker.transform.position, victim.transform.position);
            if (distance > Range * MysticExpansion.RadiusMultiplier(attackerBody)) return;

            // Slow the victim.
            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody != null && RoR2Content.Buffs.Slow50 != null)
                victimBody.AddTimedBuff(RoR2Content.Buffs.Slow50, SlowDuration);

            // Heal the attacker for a fraction of the damage dealt, capped per hit by max HP so it
            // can't run away on huge hits (the failure mode we hit with Siphon Bullets).
            float fraction = BaseHealFraction + HealFractionPerStack * (count - 1);
            float capFrac  = BaseHealCapFraction + HealCapPerStack * (count - 1);
            float heal     = Mathf.Min(damageInfo.damage * fraction, attackerBody.maxHealth * capFrac);
            if (heal > 0f)
                attackerBody.healthComponent?.Heal(heal, default(ProcChainMask));
        }
    }
}
