using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class SpiritBurn : ItemBase
    {
        public override string ItemName => "Spirit Burn";
        public override string ItemLangTokenName => "SPIRITBURN";

        public override string ItemPickupDesc => "Big hits cause an explosion that ignites enemies. <style=cIsVoid>Corrupts all Tankbusters</style>.";
        public override string ItemFullDescription =>
            $"<style=cIsVoid>Corrupts all Tankbusters</style>. " +
            $"Dealing <style=cIsDamage>{DamageThreshold * 100:0}%+ damage</style> in a single hit " +
            $"creates a <style=cIsDamage>large explosion</style> " +
            $"<style=cStack>(radius: {ExplosionRadius:0}m)</style> " +
            $"that ignites all enemies hit, dealing " +
            $"<style=cIsDamage>{BurnDpsPerStack * 100:0}% damage/s</style> " +
            $"<style=cStack>(+{BurnDpsPerStack * 100:0}% per stack)</style> " +
            $"for <style=cIsUtility>{BurnDuration:0}s</style>. " +
                $"<style=cIsUtility>{Cooldown:0}s cooldown</style>.";

        public override string ItemLore => "That's a very special type of hurt.";

        public override ItemTier Tier => ItemTier.VoidTier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_spiritburn.png";

        public const float DamageThreshold = 4f; // as Tankbuster and the elemental Bands (user 3.0; was 5)
        public const float ExplosionRadius = 15f;
        public static float BurnDpsPerStack;
        public const float BurnDuration    = 5f;
        // The game's Burn deals damageCoefficient 0.1 of the attacker's damage x damageMultiplier every 0.2 s:
        // 0.5 x damageMultiplier per second. (Before 1.8 this was taken as 1 tick per 0.5 s, so it dealt a quarter
        // of the described DPS.)
        private const float BurnDpsPerMultiplier = 0.5f;
        public const float Cooldown         = 3f;
        /// <summary>The cooldown, shown like the elemental Bands' and Tankbuster's (user 3.0): one stack per second left, counting down.</summary>
        public static BuffDef? CooldownBuff;

        public override void Init(ConfigFile config)
        {
            BurnDpsPerStack = 0.6f; // 0.35 before 1.8, which actually dealt ~0.09 (user: buff)
            CooldownBuff = ScriptableObject.CreateInstance<BuffDef>();
            CooldownBuff.name = "CursedApple_SpiritBurnCooldown";
            CooldownBuff.canStack = true;
            CooldownBuff.isDebuff = false;
            CooldownBuff.isCooldown = true; // Duration Extender leaves it alone, cleansing doesn't clear it
            CooldownBuff.buffColor = new Color(0.55f, 0.4f, 0.65f);
            CooldownBuff.iconSprite = BuffIcon(IconFileName, CooldownBuff.buffColor);
            ContentAddition.AddBuffDef(CooldownBuff);
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy      += OnHitEnemy;
            On.RoR2.ItemCatalog.SetItemRelationships   += InjectVoidPair;
        }

        private void InjectVoidPair(
            On.RoR2.ItemCatalog.orig_SetItemRelationships orig,
            ItemRelationshipProvider[] providers)
        {
            if (Tankbuster.StaticItemDef != null && ItemDef != null)
            {
                var provider = ScriptableObject.CreateInstance<ItemRelationshipProvider>();
                provider.name             = "CursedApple_SpiritBurnVoidPair";
                provider.relationshipType = DLC1Content.ItemRelationshipTypes.ContagiousItem;
                provider.relationships    = new[]
                {
                    new ItemDef.Pair { itemDef1 = Tankbuster.StaticItemDef, itemDef2 = ItemDef }
                };
                providers = (providers ?? System.Array.Empty<ItemRelationshipProvider>())
                            .Append(provider).ToArray();
            }
            orig(providers);
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
            if (attackerBody?.inventory == null || attackerBody.teamComponent == null) return;
            int itemCount = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            if (attackerBody.damage <= 0f) return;
            if (damageInfo.damage / attackerBody.damage < DamageThreshold) return;

            if (CooldownBuff != null)
            {
                if (attackerBody.HasBuff(CooldownBuff)) return; // still cooling down
                // As vanilla does the Bands' cooldown: a stack per second, each lasting one second longer, so the icon counts down.
                for (int i = 1; i <= Mathf.CeilToInt(Cooldown); i++) attackerBody.AddTimedBuff(CooldownBuff, i);
            }

            var victimHC = victim.GetComponent<HealthComponent>();
            Vector3 blastCenter = victimHC?.body?.transform.position ?? damageInfo.position;

            var hurtBoxes = new List<HurtBox>();
            new SphereSearch
            {
                origin = blastCenter,
                radius = ExplosionRadius * MysticExpansion.RadiusMultiplier(attackerBody),
                mask   = LayerIndex.entityPrecise.mask,
            }.RefreshCandidates()
             .FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(attackerBody.teamComponent.teamIndex))
             .FilterCandidatesByDistinctHurtBoxEntities()
             .GetHurtBoxes(hurtBoxes);

            // Target DPS = BurnDpsPerStack * itemCount * attackerBody.damage (Burn's own cap, 5% of the target's
            // max health per second per multiplier, only matters on weak enemies).
            float damageMultiplier = BurnDpsPerStack * itemCount / BurnDpsPerMultiplier;

            foreach (var hb in hurtBoxes)
            {
                if (hb?.healthComponent?.gameObject == null) continue;
                DotController.InflictDot(
                    hb.healthComponent.gameObject,
                    attackerBody.gameObject,
                    hb,
                    DotController.DotIndex.Burn,
                    BurnDuration,
                    damageMultiplier);
            }
        }
    }
}
