using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Fortitude : ItemBase
    {
        public override string ItemName => "Fortitude";
        public override string ItemLangTokenName => "FORTITUDE";

        public override string ItemPickupDesc => "Increase max health. Regenerate quickly when out of danger.";
        public override string ItemFullDescription =>
            $"Gain <style=cIsHealing>+{HealthBonusPerStack * 100:0}% max health</style> " +
            $"<style=cStack>(+{HealthBonusPerStack * 100:0}% per stack)</style>. " +
            $"After not taking damage for <style=cIsUtility>{RegenDelay:0}s</style>, " +
            $"regenerate <style=cIsHealing>{RegenFractionPerStack * 100:0}% max health per second</style> " +
            $"<style=cStack>(+{RegenFractionPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore => "Someone's using their noodle.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_fortitude.png";

        public static float HealthBonusPerStack;
        public const float RegenFractionPerStack = 0.01f;
        public const float RegenDelay            = 10f;

        public static ItemDef? StaticItemDef;

        public override void Init(ConfigFile config)
        {
            HealthBonusPerStack = config.Bind("Green", "Fortitude - Max Health Per Stack", 0.05f,
                "Bonus maximum health per stack.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            On.RoR2.HealthComponent.TakeDamage       += HealthComponent_TakeDamage;
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            int count = sender.inventory.GetItemCountEffective(ItemDef);
            if (count > 0) args.healthMultAdd += HealthBonusPerStack * count;
        }

        private void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;
            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<FortitudeTracker>();
            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<FortitudeTracker>();
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self, DamageInfo damageInfo)
        {
            orig(self, damageInfo);
            if (NetworkServer.active && self.body != null)
                self.body.GetComponent<FortitudeTracker>()?.NotifyDamage();
        }
    }

    public class FortitudeTracker : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timeSinceDamage;

        private void Awake()
        {
            _body            = GetComponent<CharacterBody>();
            _timeSinceDamage = 0f;
        }

        public void NotifyDamage() => _timeSinceDamage = 0f;

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body?.healthComponent == null) return;

            _timeSinceDamage += Time.fixedDeltaTime;
            if (_timeSinceDamage < Fortitude.RegenDelay) return;

            if (Fortitude.StaticItemDef == null || _body.inventory == null) return;
            int count = _body.inventory.GetItemCountEffective(Fortitude.StaticItemDef);
            if (count <= 0) return;

            float regenPerSec = _body.maxHealth * Fortitude.RegenFractionPerStack * count;
            _body.healthComponent.Heal(regenPerSec * Time.fixedDeltaTime, default, false);
        }
    }
}
