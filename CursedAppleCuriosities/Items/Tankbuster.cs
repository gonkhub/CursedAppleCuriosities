using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Tankbuster : ItemBase
    {
        public override string ItemName => "Tankbuster";
        public override string ItemLangTokenName => "TANKBUSTER";

        public override string ItemPickupDesc => "Big hits deal bonus damage based on the enemy's health.";
        public override string ItemFullDescription =>
            $"Dealing <style=cIsDamage>{DamageThreshold * 100:0}%+ damage</style> in a single hit " +
            $"deals a bonus burst of <style=cIsDamage>{BurstPercent * 100:0.#}% of the enemy's max HP</style> " +
            $"<style=cStack>(+{StackPercent * 100:0.#}% per stack)</style>. " +
            $"<style=cIsUtility>{Cooldown:0}s cooldown</style>.";

        public override string ItemLore => "Somebody's been hitting the gym.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_tankbuster.png";

        public const float DamageThreshold  = 4f; // Runald's and Kjaro's Bands' own threshold (user 3.0; was 5)
        public static float BurstPercent;
        public static float StackPercent;
        public const float Cooldown         = 10f;

        public static ItemDef? StaticItemDef;
        /// <summary>The cooldown, shown like the elemental Bands' (user 3.0): one stack per second left, counting down.</summary>
        public static BuffDef? CooldownBuff;

        public override void Init(ConfigFile config)
        {
            BurstPercent = 0.05f; // 0.10 before 1.8 (user)
            StackPercent = 0.025f; // 0.05 before 1.8 (user)
            CooldownBuff = ScriptableObject.CreateInstance<BuffDef>();
            CooldownBuff.name = "CursedApple_TankbusterCooldown";
            CooldownBuff.canStack = true;
            CooldownBuff.isDebuff = false;
            CooldownBuff.isCooldown = true; // Duration Extender leaves it alone, cleansing doesn't clear it
            CooldownBuff.buffColor = new Color(0.55f, 0.55f, 0.6f);
            CooldownBuff.iconSprite = BuffIcon(IconFileName, CooldownBuff.buffColor);
            ContentAddition.AddBuffDef(CooldownBuff);
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
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
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            int itemCount = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            if (attackerBody.damage <= 0f) return;
            if (damageInfo.damage / attackerBody.damage < DamageThreshold) return;

            if (CooldownBuff != null && attackerBody.HasBuff(CooldownBuff)) return; // still cooling down

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            var di = new DamageInfo();
            di.attacker         = attackerBody.gameObject;
            di.inflictor        = attackerBody.gameObject;
            di.damage           = victimHC.fullHealth * (BurstPercent + StackPercent * (itemCount - 1));
            di.procCoefficient  = 0f;
            di.damageType       = DamageType.BypassArmor;
            di.damageColorIndex = DamageColorIndex.WeakPoint;
            di.position         = damageInfo.position;
            victimHC.TakeDamage(di);

            // As vanilla does the Bands' cooldown: a stack per second, each lasting one second longer, so the icon counts down.
            if (CooldownBuff != null)
                for (int i = 1; i <= Mathf.CeilToInt(Cooldown); i++) attackerBody.AddTimedBuff(CooldownBuff, i);
        }
    }
}
