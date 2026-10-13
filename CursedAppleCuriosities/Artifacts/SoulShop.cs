using System.Collections;
using System.Collections.Generic;
using CursedAppleCuriosities.Items;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>
    /// Artifact of the Catalog's shop, in the Bazaar Between Time: a command-cube picker selling every Cursed Apple item and
    /// equipment at its tier's soul price (white / green / red = 800 / 3200 / 6400). What a shopper can't afford is
    /// greyed out, and it stays open so they can keep buying. (The Curiosity Catalog item's shop, repurposed.)
    /// </summary>
    public static class SoulShop
    {
        // Items whose own tier doesn't say what they're worth.
        private static readonly HashSet<string> RedPriced = new HashSet<string>(System.StringComparer.Ordinal) { "ITEM_CURSEDAPPLE_SPIRITBURN" };

        private static GameObject? _cube;
        private static GameObject? Cube => _cube != null ? _cube : _cube = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Command/CommandCube.prefab").WaitForCompletion();

        public static void Hook()
        {
            Stage.onServerStageBegin += stage =>
            {
                if (CatalogArtifact.Active && stage != null && stage.sceneDef != null && stage.sceneDef.cachedName == "bazaar")
                    stage.StartCoroutine(SpawnInBazaar());
            };
            On.RoR2.PickupPickerController.GetContextString += (orig, self, activator) =>
                self.GetComponent<SoulShopMarker>() == null ? orig(self, activator)
                : "Browse Curiosities: " +
                  $"<color=#ffffff>{Souls.PerChest * CatalogArtifact.WhiteMult}</color> / " +
                  $"<color=#77ff17>{Souls.PerChest * CatalogArtifact.GreenMult}</color> / " +
                  $"<color=#e7543a>{Souls.PerChest * CatalogArtifact.RedMult}</color> souls";
            On.RoR2.PickupPickerController.OnInteractionBegin += (orig, self, activator) =>
            {
                var marker = self.GetComponent<SoulShopMarker>();
                if (marker != null && NetworkServer.active)
                {
                    marker.Shopper = activator != null ? activator.GetComponent<CharacterBody>() : null;
                    self.SetOptionsServer(Options(marker, marker.Shopper != null && marker.Shopper.master != null ? marker.Shopper.master.money : 0));
                }
                orig(self, activator);
            };
            On.RoR2.PickupPickerController.SubmitChoice += (orig, self, index) =>
            {
                var marker = self.GetComponent<SoulShopMarker>();
                if (marker == null || !NetworkServer.active) { orig(self, index); return; }
                Buy(self, marker, index); // never orig: that hands the item over free and closes the shop
            };
        }

        /// <summary>The Bazaar has no fixed spot for it: a few metres in front of the first player to arrive, on the ground.</summary>
        private static IEnumerator SpawnInBazaar()
        {
            CharacterBody? body = null;
            for (float waited = 0f; body == null && waited < 30f; waited += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                foreach (PlayerCharacterMasterController p in PlayerCharacterMasterController.instances)
                    if (p.master != null && p.master.GetBody() is CharacterBody b) { body = b; break; }
            }
            if (body == null) yield break;
            yield return new WaitForSeconds(1.5f); // out of the arrival animation
            Vector3 ahead = body.footPosition + Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized * 6f;
            Vector3 at = Physics.Raycast(ahead + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : ahead;
            Spawn(at);
        }

        internal static void Spawn(Vector3 at)
        {
            GameObject? prefab = Cube;
            if (prefab == null) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Catalog] CommandCube prefab not found."); return; }
            Stock(out PickupIndex[] pickups, out ItemTier[] tiers);
            if (pickups.Length == 0) return;
            GameObject obj = Object.Instantiate(prefab, at + Vector3.up * 1.5f, Quaternion.identity);
            var marker = obj.AddComponent<SoulShopMarker>();
            marker.Pickups = pickups;
            marker.Tiers = tiers;
            // Vanilla cubes inherit their droplet's pickup; other mods (WolfoFixes) assume one is set.
            if (obj.TryGetComponent(out PickupIndexNetworker networker)) networker.NetworkpickupState = new UniquePickup(pickups[0]);
            if (obj.TryGetComponent(out PickupPickerController ppc)) ppc.SetOptionsServer(Options(marker, uint.MaxValue));
            NetworkServer.Spawn(obj);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Catalog] Bazaar shop at {at} with {pickups.Length} curiosities");
        }

        private static int Price(ItemTier tier) => Souls.GoldFor(CatalogArtifact.Mult(tier));

        private static void Buy(PickupPickerController picker, SoulShopMarker marker, int index)
        {
            if (marker.Pickups == null || index < 0 || index >= marker.Pickups.Length) return;
            CharacterBody? body = marker.Shopper;
            if (body == null || body.master == null) return;
            int cost = Price(marker.Tiers![index]);
            if (body.master.money >= (uint)cost)
            {
                body.master.money -= (uint)cost;
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo { pickupIndex = marker.Pickups[index], rotation = Quaternion.identity },
                    body.transform.position + Vector3.up * 1.5f, Vector3.up * 15f + Random.insideUnitSphere * 3f);
            }
            else Chat.AddMessage($"<color=#ff6644>Not enough souls: it costs {Souls.FromGold(cost)}.</color>");
            picker.SetOptionsServer(Options(marker, body.master.money)); // still open: re-grey what's now out of reach
        }

        private static PickupPickerController.Option[] Options(SoulShopMarker marker, uint money)
        {
            PickupIndex[] pickups = marker.Pickups ?? System.Array.Empty<PickupIndex>();
            var options = new PickupPickerController.Option[pickups.Length];
            for (int i = 0; i < pickups.Length; i++)
#pragma warning disable CS0618
                options[i] = new PickupPickerController.Option { pickupIndex = pickups[i], available = money >= (uint)Price(marker.Tiers![i]) };
#pragma warning restore CS0618
            return options;
        }

        /// <summary>Every Cursed Apple item and equipment, white then green then red (registration order within a tier).</summary>
        private static void Stock(out PickupIndex[] pickups, out ItemTier[] tiers)
        {
            var stock = new List<(PickupIndex pickup, ItemTier tier)>();
            foreach (ItemDef def in CursedAppleCuriositiesPlugin.ModItemDefs)
                if (def != null && PickupCatalog.FindPickupIndex(def.itemIndex) is PickupIndex p && p != PickupIndex.none) stock.Add((p, ItemTab(def)));
            foreach (EquipmentDef def in CursedAppleCuriositiesPlugin.ModEquipmentDefs)
                if (def != null && PickupCatalog.FindPickupIndex(def.equipmentIndex) is PickupIndex p && p != PickupIndex.none) stock.Add((p, EquipmentTab(def)));
            var sorted = new List<(PickupIndex pickup, ItemTier tier)>();
            foreach (ItemTier tab in new[] { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3 })
                foreach (var entry in stock) if (entry.tier == tab) sorted.Add(entry);
            pickups = sorted.ConvertAll(e => e.pickup).ToArray();
            tiers = sorted.ConvertAll(e => e.tier).ToArray();
        }

        private static ItemTier ItemTab(ItemDef def)
        {
            if (RedPriced.Contains(def.name)) return ItemTier.Tier3;
            return def.tier switch
            {
                ItemTier.Tier1 or ItemTier.VoidTier1 => ItemTier.Tier1,
                ItemTier.Tier2 or ItemTier.VoidTier2 => ItemTier.Tier2,
                _ => ItemTier.Tier3, // red, void red, lunar, boss
            };
        }

        private static ItemTier EquipmentTab(EquipmentDef def)
        {
            if (GooseEgg.IsEgg(def)) return ItemTier.Tier1; // every egg at white prices
            if (def == BloodTribute.StaticEquipmentDef || def == Counterspell.StaticEquipmentDef || def == EtherealShift.StaticEquipmentDef
                || def == ArcticBlast.StaticEquipmentDef || def == CultistSacrifice.StaticEquipmentDef || def == MajesticLeap.StaticEquipmentDef)
                return ItemTier.Tier2;
            return ItemTier.Tier3;
        }
    }

    public class SoulShopMarker : MonoBehaviour
    {
        public PickupIndex[]? Pickups;
        public ItemTier[]? Tiers; // parallel to Pickups: each one's price tier
        public CharacterBody? Shopper;
    }
}
