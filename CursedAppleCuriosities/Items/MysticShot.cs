using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class MysticShot : ItemBase
    {
        public override string ItemName => "Mystic Shot";
        public override string ItemLangTokenName => "MYSTICSHOT";

        public override string ItemPickupDesc => "Periodically, your next hit deals bonus damage.";
        public override string ItemFullDescription =>
            $"Every <style=cIsUtility>{CooldownDuration}s</style>, " +
            $"your next hit deals a bonus burst of " +
            $"<style=cIsDamage>{BaseDamagePercent * 100:0}% damage</style> " +
            $"<style=cStack>(+{DamagePercentPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore =>
            "The mystical part is that it still hurts even after all these years.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_mysticshot.png";

        public const float CooldownDuration     = 3f;
        public static float BaseDamagePercent;
        public static float DamagePercentPerStack;

        public override void Init(ConfigFile config)
        {
            BaseDamagePercent = config.Bind("White", "Mystic Shot - Burst Damage", 1.0f,
                "Bonus burst damage (fraction of base damage) on the periodic charged hit at 1 stack.").Value;
            DamagePercentPerStack = config.Bind("White", "Mystic Shot - Burst Damage Per Stack", 1.0f,
                "Additional burst damage (fraction of base damage) per extra copy of the item.").Value;
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
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var tracker = attackerBody.GetComponent<MysticShotTracker>()
                ?? attackerBody.gameObject.AddComponent<MysticShotTracker>();
            if (!tracker.IsReady) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            float percent = BaseDamagePercent + DamagePercentPerStack * (count - 1);
            float burst   = attackerBody.damage * percent;

            var di = new DamageInfo();
            di.attacker         = attackerBody.gameObject;
            di.inflictor        = attackerBody.gameObject;
            di.damage           = burst;
            di.procCoefficient  = 0f;
            di.position         = damageInfo.position;
            di.damageColorIndex = DamageColorIndex.WeakPoint;
            victimHC.TakeDamage(di);

            tracker.StartCooldown(CooldownDuration);
        }
    }

    public class MysticShotTracker : MonoBehaviour
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
