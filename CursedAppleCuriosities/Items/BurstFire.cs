using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class BurstFire : ItemBase
    {
        public override string ItemName => "Burst Fire";
        public override string ItemLangTokenName => "BURSTFIRE";

        public override string ItemPickupDesc => "Hitting with your primary skill briefly increases movement and attack speed.";
        public override string ItemFullDescription =>
            $"Hitting an enemy with your <style=cIsDamage>primary skill</style> grants " +
            $"<style=cIsUtility>+{MoveSpeedBonusPerStack * 100:0}% movement speed</style> and " +
            $"<style=cIsDamage>+{AttackSpeedBonusPerStack * 100:0}% attack speed</style> " +
            $"<style=cStack>(+{MoveSpeedBonusPerStack * 100:0}% and +{AttackSpeedBonusPerStack * 100:0}% per stack)</style> " +
            $"for <style=cIsUtility>{BuffDuration}s</style>.";

        public override string ItemLore =>
            "I-It's brutal out there, eh?";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_burstfire.png";

        public const float BuffDuration            = 3f;
        public static float MoveSpeedBonusPerStack;
        public static float AttackSpeedBonusPerStack;

        public static BuffDef? BurstBuffDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(1f, 0.6f, 0.1f);
            BurstBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            BurstBuffDef.name       = "CursedApple_BurstFire";
            BurstBuffDef.canStack   = false;
            BurstBuffDef.isDebuff   = false;
            BurstBuffDef.buffColor  = color;
            BurstBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(BurstBuffDef);

            MoveSpeedBonusPerStack = 0.10f;
            AttackSpeedBonusPerStack = 0.15f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy   += OnHitEnemy;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self,
            DamageInfo damageInfo,
            GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)

            if (!NetworkServer.active) return;
            if (damageInfo == null || damageInfo.procCoefficient <= 0f) return;
            if (((DamageSource)damageInfo.damageType & DamageSource.Primary) == 0) return; // primary fire only (user, 1.8)
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            if (attackerBody.inventory.GetItemCountEffective(ItemDef) <= 0) return;

            var tracker = attackerBody.GetComponent<BurstFireTracker>()
                ?? attackerBody.gameObject.AddComponent<BurstFireTracker>();
            tracker.OnHit();
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            // Synced buff, not the server-only tracker, so the owning client gets the speed too.
            if (BurstBuffDef == null || BurstBuffDef.buffIndex == BuffIndex.None) return;
            if (!sender.HasBuff(BurstBuffDef)) return;

            int count = sender.inventory?.GetItemCountEffective(ItemDef) ?? 0;
            if (count <= 0) return;

            args.moveSpeedMultAdd   += MoveSpeedBonusPerStack  * count;
            args.attackSpeedMultAdd += AttackSpeedBonusPerStack * count;
        }
    }

    public class BurstFireTracker : MonoBehaviour
    {
        private float         _timer;
        private CharacterBody _body;

        public bool IsActive => _timer > 0f;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void OnHit()
        {
            bool wasInactive = _timer <= 0f;
            _timer = BurstFire.BuffDuration * DurationExtender.Multiplier(_body);
            if (wasInactive) AddBuff();
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) RemoveBuff();
        }

        private void OnDestroy() => RemoveBuff();

        private void AddBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (BurstFire.BurstBuffDef == null || BurstFire.BurstBuffDef.buffIndex == BuffIndex.None) return;
            if (!_body.HasBuff(BurstFire.BurstBuffDef.buffIndex))
                _body.AddBuff(BurstFire.BurstBuffDef.buffIndex);
        }

        private void RemoveBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (BurstFire.BurstBuffDef == null || BurstFire.BurstBuffDef.buffIndex == BuffIndex.None) return;
            if (_body.HasBuff(BurstFire.BurstBuffDef.buffIndex))
                _body.RemoveBuff(BurstFire.BurstBuffDef.buffIndex);
        }
    }
}
