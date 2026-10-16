using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using CursedAppleCuriosities.Items;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// A Curiosity shop at a Deadlock shop counter: a floating Command cube that opens a picker of Deadlock's items, buys
    /// repeatedly, open to everyone, base price per tier scaled live by difficulty. Moved out of the Curiosity Catalog item
    /// when 2.0 removed the catalog.
    /// </summary>
    internal static class MapShop
    {
        /// <summary>The mod's items and equipment that aren't Deadlock's (checked against Deadlock's own item names).</summary>
        private static readonly HashSet<string> NotDeadlock = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "ITEM_CURSEDAPPLE_RADIANTREGEN",
            "EQUIP_CURSEDAPPLE_GREYGOOSEEGG",
            "EQUIP_CURSEDAPPLE_GREENGOOSEEGG",
            "EQUIP_CURSEDAPPLE_REDGOOSEEGG",
            "EQUIP_CURSEDAPPLE_VOIDGOOSEEGG",
        };

        // Items that ignore their natural tier and always price as red
        private static readonly HashSet<string> _redPriceOverrides = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "ITEM_CURSEDAPPLE_SPIRITBURN",  // VoidTier2 — manually priced as red
        };

        private static GameObject? _commandCubePrefab;
        private static GameObject? CommandCubePrefab =>
            _commandCubePrefab != null
                ? _commandCubePrefab
                : _commandCubePrefab = Resources.Load<GameObject>("Prefabs/NetworkedObjects/CommandCube");

        private static bool _hooked;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.PickupPickerController.GetContextString   += GetContextString;
            On.RoR2.PickupPickerController.OnInteractionBegin += OnInteractionBegin;
            On.RoR2.PickupPickerController.SubmitChoice       += SubmitChoice;
        }

        // Live difficulty-scaled price for a base cost (cost = base × coeff^1.25).
        private static int ScaledCost(int baseCost) =>
            Run.instance != null ? Run.instance.GetDifficultyScaledCost(baseCost) : baseCost;

        // What this shop charges now: the scaled price, raised by every item already bought here this stage (user 3.0: buying out
        // the shop by farming gold was too strong).
        private static int Price(MapShopMarker marker, int baseCost) =>
            Mathf.Max(1, Mathf.RoundToInt(ScaledCost(baseCost) * (1f + Mathf.Max(0f, DeadlockStages.ShopTaxPerBuy) * marker.Buys)));

        internal static GameObject? Spawn(Vector3 pos, int white, int green, int red)
        {
            var prefab = CommandCubePrefab;
            if (prefab == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning("[MapShop] CommandCube prefab not found.");
                return null;
            }

            BuildStock(white, green, red, out var pickups, out var baseCosts);
            if (pickups.Length == 0) return null;

            var obj = Object.Instantiate(prefab, pos + Vector3.up * 1.5f, Quaternion.identity);
            var marker       = obj.AddComponent<MapShopMarker>();
            marker.Pickups   = pickups;
            marker.BaseCosts = baseCosts;
            marker.TierCosts = new[] { white, green, red };

            // Vanilla cubes inherit the pickup of the droplet that made them; other mods
            // (e.g. WolfoFixes' SetOptionsInternal hook) assume it's set and NRE otherwise.
            var ppc = obj.GetComponent<PickupPickerController>();
            var pickupIndexNetworker = obj.GetComponent<PickupIndexNetworker>();
            if (pickupIndexNetworker != null)
                pickupIndexNetworker.NetworkpickupState = new UniquePickup(pickups[0]);
            if (ppc != null) ppc.SetOptionsServer(BuildOptions(marker, money: uint.MaxValue));

            NetworkServer.Spawn(obj);
            return obj;
        }

        // ── The stock, ordered white → green → red ───────────────────────────
        private static void BuildStock(int white, int green, int red, out PickupIndex[] pickups, out int[] baseCosts)
        {
            var stock = new List<(PickupIndex pickup, ItemTier tab)>();

            foreach (var itemDef in CursedAppleCuriositiesPlugin.ModItemDefs)
            {
                if (itemDef == null || NotDeadlock.Contains(itemDef.name)) continue;
                if (Run.instance != null && !Run.instance.IsItemAvailable(itemDef.itemIndex)) continue; // e.g. the expansion is off
                var pidx = PickupCatalog.FindPickupIndex(itemDef.itemIndex);
                if (pidx != PickupIndex.none) stock.Add((pidx, GetItemTab(itemDef)));
            }

            foreach (var eqDef in CursedAppleCuriositiesPlugin.ModEquipmentDefs)
            {
                if (eqDef == null || NotDeadlock.Contains(eqDef.name)) continue;
                if (Run.instance != null && !Run.instance.IsEquipmentAvailable(eqDef.equipmentIndex)) continue;
                var pidx = PickupCatalog.FindPickupIndex(eqDef.equipmentIndex);
                if (pidx != PickupIndex.none) stock.Add((pidx, GetEquipmentTab(eqDef)));
            }

            var sorted = new List<(PickupIndex pickup, ItemTier tab)>();
            foreach (var tab in new[] { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3 })
                foreach (var e in stock)
                    if (e.tab == tab) sorted.Add(e);

            pickups   = new PickupIndex[sorted.Count];
            baseCosts = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                pickups[i]   = sorted[i].pickup;
                baseCosts[i] = sorted[i].tab switch { ItemTier.Tier1 => white, ItemTier.Tier2 => green, _ => red };
            }
        }

        private static PickupPickerController.Option[] BuildOptions(MapShopMarker marker, uint money)
        {
            var pickups = marker.Pickups ?? System.Array.Empty<PickupIndex>();
            var options = new PickupPickerController.Option[pickups.Length];
            for (int i = 0; i < pickups.Length; i++)
            {
#pragma warning disable CS0618
                options[i] = new PickupPickerController.Option
                {
                    pickupIndex = pickups[i],
                    available   = money >= (uint)Price(marker, marker.BaseCosts![i]),
                };
#pragma warning restore CS0618
            }
            return options;
        }

        private static ItemTier GetItemTab(ItemDef def)
        {
            if (_redPriceOverrides.Contains(def.name)) return ItemTier.Tier3;
            return def.tier switch
            {
                ItemTier.Tier1 or ItemTier.VoidTier1              => ItemTier.Tier1,
                ItemTier.Tier2 or ItemTier.VoidTier2              => ItemTier.Tier2,
                _ /* Tier3, VoidTier3, Lunar, unknown */          => ItemTier.Tier3,
            };
        }

        private static ItemTier GetEquipmentTab(EquipmentDef def)
        {
            if (GooseEgg.IsEgg(def))                        return ItemTier.Tier1; // every egg at white prices
            if (def == BloodTribute.StaticEquipmentDef)     return ItemTier.Tier2;
            if (def == Counterspell.StaticEquipmentDef)     return ItemTier.Tier2;
            if (def == EtherealShift.StaticEquipmentDef)    return ItemTier.Tier2;
            if (def == ArcticBlast.StaticEquipmentDef)      return ItemTier.Tier2;
            if (def == CultistSacrifice.StaticEquipmentDef) return ItemTier.Tier2;
            if (def == MajesticLeap.StaticEquipmentDef)     return ItemTier.Tier2;
            return ItemTier.Tier3;
        }

        // ── Hover prompt ──────────────────────────────────────────────────────
        private static string GetContextString(
            On.RoR2.PickupPickerController.orig_GetContextString orig, PickupPickerController self, Interactor activator)
        {
            var shop = self.GetComponent<MapShopMarker>();
            if (shop?.TierCosts == null) return orig(self, activator);
            int[] tiers = shop.TierCosts;
            return "Browse Curiosities — " +
                   $"<color=#ffffff>{Price(shop, tiers[0])}g</color> / " +
                   $"<color=#77ff17>{Price(shop, tiers[1])}g</color> / " +
                   $"<color=#e7543a>{Price(shop, tiers[2])}g</color>";
        }

        // ── Record the shopper and grey out what they can't afford ────────────
        private static void OnInteractionBegin(
            On.RoR2.PickupPickerController.orig_OnInteractionBegin orig, PickupPickerController self, Interactor activator)
        {
            var marker = self.GetComponent<MapShopMarker>();
            if (marker != null && NetworkServer.active)
            {
                var body = activator?.GetComponent<CharacterBody>();
                if (body == null) return;
                marker.ActivatorBody = body;
                self.SetOptionsServer(BuildOptions(marker, body.master?.money ?? 0));
            }
            orig(self, activator);
        }

        // ── Intercept selection: gold check → drop item → keep the menu open ──
        private static void SubmitChoice(
            On.RoR2.PickupPickerController.orig_SubmitChoice orig, PickupPickerController self, int index)
        {
            var marker = self.GetComponent<MapShopMarker>();
            if (marker == null || !NetworkServer.active)
            {
                orig(self, index);
                return;
            }

            // Do NOT call orig — that would give a free item and close the shop.
            var pickups = marker.Pickups;
            if (pickups == null || index < 0 || index >= pickups.Length) return;

            var body = marker.ActivatorBody;
            if (body == null || body.master == null) return;

            int cost = Price(marker, marker.BaseCosts![index]); // live price at the moment of purchase
            if (body.master.money >= (uint)cost)
            {
                body.master.money -= (uint)cost;
                marker.Buys++; // every later buy here costs more
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo
                    {
                        pickupIndex = pickups[index],
                        rotation    = Quaternion.identity,
                    },
                    body.transform.position + Vector3.up * 1.5f,
                    Vector3.up * 15f + Random.insideUnitSphere * 3f);
            }
            // Not enough gold: nothing to say, the option is already greyed out (user 3.0).

            // The shop stays put so the player can keep spending; just re-grey
            // whatever their remaining gold no longer covers.
            self.SetOptionsServer(BuildOptions(marker, body.master.money));
        }
    }

    public class MapShopMarker : MonoBehaviour
    {
        public PickupIndex[]? Pickups;
        public int[]?         BaseCosts; // unscaled, parallel to Pickups; price is scaled live per purchase
        public CharacterBody? ActivatorBody;
        public int[]?         TierCosts; // base white / green / red, for the hover prompt
        public int            Buys;      // items bought here this stage (server): each raises the prices by Shop Tax Per Purchase
    }
}
