using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class RestorativeShot : ItemBase
    {
        public override string ItemName => "Restorative Shot";
        public override string ItemLangTokenName => "RESTORATIVESHOT";

        public override string ItemPickupDesc => "Periodically, your next hit heals you.";
        public override string ItemFullDescription =>
            $"Every <style=cIsUtility>{CooldownDuration}s</style>, " +
            $"your next hit heals you for <style=cIsHealing>{BaseHealPercent * 100:0}% max HP</style> " +
            $"<style=cStack>(+{HealPercentPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore =>
            "That's gonna help keep your blood on the inside.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing };
        protected override string IconFileName => "icon_restorativeshot.png";

        public const float CooldownDuration    = 3f;
        public static float BaseHealPercent;
        public const float HealPercentPerStack = 0.03f;

        public override void Init(ConfigFile config)
        {
            BaseHealPercent = config.Bind("White", "Restorative Shot - Heal Percent", 0.03f,
                "Max HP healed by the periodic charged hit at 1 stack.").Value;
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
            if (damageInfo == null || damageInfo.attacker == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var tracker = attackerBody.GetComponent<RestorativeShotTracker>()
                ?? attackerBody.gameObject.AddComponent<RestorativeShotTracker>();

            if (!tracker.IsReady) return;

            float percent = BaseHealPercent + HealPercentPerStack * (count - 1);
            float amount  = attackerBody.maxHealth * percent;
            attackerBody.healthComponent?.Heal(amount, default(ProcChainMask));
            tracker.StartCooldown(CooldownDuration);
        }
    }

    public class RestorativeShotTracker : MonoBehaviour
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
