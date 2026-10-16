using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class GlassCannon : ItemBase
    {
        public override string ItemName => "Glass Cannon";
        public override string ItemLangTokenName => "GLASSCANNON";

        public override string ItemPickupDesc => "Deal more damage and gain attack speed from kills... but take more damage.";
        public override string ItemFullDescription =>
            $"Gain <style=cIsDamage>+{FlatDamageBonus * 100:0}% damage</style> <style=cStack>per stack</style>. " +
            $"Each <style=cIsDamage>kill</style> grants a <style=cIsDamage>Glass Charge</style>, adding " +
            $"<style=cIsDamage>+{AttackSpeedPerCharge * 100:0.0}% attack speed</style> " +
            $"(up to <style=cIsUtility>{MaxChargesPerStack}</style> <style=cStack>per stack</style>). " +
            $"<style=cIsHealth>You take +{DamageTakenPenalty * 100:0}% increased damage</style> <style=cStack>per stack</style>, " +
            $"and lose all charges on death or <style=cIsUtility>stage transition</style>.";

        public override string ItemLore =>
            "High risk, high reward.";

        public override ItemTier Tier => ItemTier.Lunar;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_glasscannon.png";

        public static float FlatDamageBonus;             // per stack, always on
        public const float AttackSpeedPerCharge = 0.01f; // per Glass Charge
        public const int   MaxChargesPerStack  = 50;
        public static float DamageTakenPenalty;          // per stack, the "glass"

        public static BuffDef? GlassChargeBuffDef;

        public override void Init(ConfigFile config)
        {
            var color                  = new Color(1f, 0.5f, 0.25f);
            GlassChargeBuffDef         = ScriptableObject.CreateInstance<BuffDef>();
            GlassChargeBuffDef.name    = "CursedApple_GlassCharge";
            GlassChargeBuffDef.canStack = true;
            GlassChargeBuffDef.isDebuff = false;
            GlassChargeBuffDef.buffColor = color;
            GlassChargeBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(GlassChargeBuffDef);

            FlatDamageBonus = 0.25f;
            DamageTakenPenalty = 0.25f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnCharacterDeath += OnCharacterDeath;
            On.RoR2.HealthComponent.TakeDamage          += HealthComponent_TakeDamage;
            RecalculateStatsAPI.GetStatCoefficients     += GetStatCoefficients;
            Stage.onServerStageBegin                    += OnStageBegin;
        }

        // Charges are lost when you advance to the next stage.
        private void OnStageBegin(Stage stage)
        {
            if (!NetworkServer.active) return;
            if (GlassChargeBuffDef == null || GlassChargeBuffDef.buffIndex == BuffIndex.None) return;
            var idx = GlassChargeBuffDef.buffIndex;

            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body == null || body.GetBuffCount(idx) <= 0) continue;
                body.SetBuffCount(idx, 0);
                body.RecalculateStats();
            }
        }

        private void OnCharacterDeath(
            On.RoR2.GlobalEventManager.orig_OnCharacterDeath orig,
            GlobalEventManager self,
            DamageReport damageReport)
        {
            orig(self, damageReport);

            if (!NetworkServer.active || damageReport == null) return;
            if (GlassChargeBuffDef == null || GlassChargeBuffDef.buffIndex == BuffIndex.None) return;

            var idx = GlassChargeBuffDef.buffIndex;

            // The holder who died loses all charges (backs up the natural reset when the body is
            // destroyed, and covers revive-onto-same-body cases).
            var victim = damageReport.victimBody;
            if (victim != null && victim.GetBuffCount(idx) > 0)
                victim.SetBuffCount(idx, 0);

            // The killer gains a charge (capped).
            var killer = damageReport.attackerBody;
            if (killer?.inventory == null) return;

            int count = killer.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            if (killer.GetBuffCount(idx) >= MaxChargesPerStack * count) return;

            killer.AddBuff(idx);
            killer.RecalculateStats(); // refresh the attack-speed bonus
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (FinalDamage(damageInfo)) { orig(self, damageInfo); return; } // already worked out (Bloodletting's ticks)
            if (NetworkServer.active
                && damageInfo != null
                && self.body?.inventory != null)
            {
                int count = self.body.inventory.GetItemCountEffective(ItemDef);
                if (count > 0)
                    damageInfo.damage *= 1f + DamageTakenPenalty * count; // the glass
            }

            orig(self, damageInfo);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            int count = sender.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            args.damageMultAdd += FlatDamageBonus * count;

            if (GlassChargeBuffDef != null && GlassChargeBuffDef.buffIndex != BuffIndex.None)
            {
                int charges = sender.GetBuffCount(GlassChargeBuffDef.buffIndex);
                if (charges > 0)
                    args.attackSpeedMultAdd += AttackSpeedPerCharge * charges;
            }
        }
    }
}
