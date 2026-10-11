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

        public const float DamageThreshold = 5f;
        public const float ExplosionRadius = 15f;
        public static float BurnDpsPerStack;
        public const float BurnDuration    = 5f;
        // Burn ticks ~2x/s; damageMultiplier = DPS_fraction * tickInterval
        private const float BurnTickInterval = 0.5f;
        public const float Cooldown         = 3f;

        public override void Init(ConfigFile config)
        {
            BurnDpsPerStack = config.Bind("Void", "Spirit Burn - Burn DPS Per Stack", 0.35f,
                "Ignite damage per second as a fraction of your damage, per stack.").Value;
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
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null || attackerBody.teamComponent == null) return;
            int itemCount = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            if (attackerBody.damage <= 0f) return;
            if (damageInfo.damage / attackerBody.damage < DamageThreshold) return;

            var tracker = attackerBody.GetComponent<SpiritBurnTracker>()
                ?? attackerBody.gameObject.AddComponent<SpiritBurnTracker>();
            if (!tracker.IsReady) return;
            tracker.StartCooldown(Cooldown);

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

            // Target DPS = BurnDpsPerStack * itemCount * attackerBody.damage
            // InflictDot damage per tick = attackerBody.damage * damageMultiplier
            // DPS = damage per tick / tickInterval  →  damageMultiplier = DPS_fraction * tickInterval
            float damageMultiplier = BurnDpsPerStack * itemCount * BurnTickInterval;

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

    public class SpiritBurnTracker : MonoBehaviour
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
