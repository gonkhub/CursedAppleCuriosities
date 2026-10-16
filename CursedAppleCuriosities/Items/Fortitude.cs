using BepInEx.Configuration;
using R2API;
using RoR2;

namespace CursedAppleCuriosities.Items
{
    public class Fortitude : ItemBase
    {
        public override string ItemName => "Fortitude";
        public override string ItemLangTokenName => "FORTITUDE";

        // A dedicated max health item (user 3.0): its out-of-danger regeneration (1% max health/s after 10 s) is gone, and the
        // health per stack went 5% -> 15% to carry the item alone.
        public override string ItemPickupDesc => "Increase max health.";
        public override string ItemFullDescription =>
            $"Gain <style=cIsHealing>+{HealthBonusPerStack * 100:0}% max health</style> " +
            $"<style=cStack>(+{HealthBonusPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore => "Someone's using their noodle.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_fortitude.png";

        public static float HealthBonusPerStack;

        public static ItemDef? StaticItemDef;

        public override void Init(ConfigFile config)
        {
            HealthBonusPerStack = 0.15f;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            int count = sender.inventory.GetItemCountEffective(ItemDef);
            if (count > 0) args.healthMultAdd += HealthBonusPerStack * count;
        }
    }
}
