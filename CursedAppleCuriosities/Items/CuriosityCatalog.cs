using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class CuriosityCatalog : ItemBase
    {
        public override string ItemName          => "Curiosity Catalog";
        public override string ItemLangTokenName => "CURIOSITYCATALOG";
        public override string ItemPickupDesc    => "Completing the teleporter drops a Curiosity shop. Can no longer open chests or multishops.";

        public override string ItemFullDescription =>
            "When the <style=cIsUtility>teleporter event</style> is completed, a " +
            "<style=cIsUtility>Curiosity shop</style> drops. Spend your gold on any Curiosity item: " +
            $"<style=cIsHealing>{WhiteCost}g whites</style>, " +
            $"<style=cIsDamage>{GreenCost}g greens</style>, " +
            $"<style=cIsUtility>{RedCost}g reds</style>, as many times as you can afford. " +
            "<style=cIsHealth>Prices rise as the run grows more difficult. " +
            "Cannot open chests, multishops, or equipment barrels.</style>";

        public override string ItemLore =>
            "A small leather-bound catalogue, stamped with a golden apple. " +
            "Flipping through it, you hear a voice: " +
            "'Everything in here can be yours. Everything out there... not so much.'";

        public override ItemTier  Tier     => ItemTier.Lunar;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string? IconFileName => "icon_curiositycatalog.png";

        // ── Shop pricing ──────────────────────────────────────────────────────
        // Base costs are anchored to the base game's guaranteed-tier chests so item
        // access tracks normal run progression: Small Chest (25) → white,
        // Large Chest (50) → green, Legendary Chest (400) → red. A catalog purchase
        // is *better* than its chest (you pick the exact item), so each tier carries
        // a modest premium over its anchor.
        //
        // These are BASE costs. The price actually charged is run through
        // Run.GetDifficultyScaledCost (cost = base × coeff^1.25) at purchase time,
        // so lingering on a cleared stage steadily raises prices.
        public static int WhiteCost;
        public static int GreenCost;
        public static int RedCost;

        public static ItemDef? StaticItemDef;

        // Items that ignore their natural tier and always price as red
        private static readonly HashSet<string> _redPriceOverrides = new HashSet<string>(
            System.StringComparer.Ordinal)
        {
            "ITEM_CURSEDAPPLE_SPIRITBURN",  // VoidTier2 — manually priced as red
        };

        // ── Interactable-block predicate ──────────────────────────────────────
        // ChestBehavior covers small/large/category/equipment barrels.
        // ShopTerminalBehavior is shared by multishop terminals AND 3D printers — but multishops
        // cost Money while printers cost an item, so we gate on Money to block multishops while
        // still allowing printers. Scrappers use ScrapperController (not checked here) and lunar
        // pods cost lunar coins, so both remain usable.
        private static bool IsBlockedInteractable(PurchaseInteraction pi)
        {
            var go = pi.gameObject;
            if (go.GetComponent<ChestBehavior>()           != null) return true;
            if (go.GetComponent<RouletteChestController>() != null) return true;
            if (go.GetComponent<ShopTerminalBehavior>()    != null
                && pi.costType == CostTypeIndex.Money)              return true;
            return false;
        }

        // True when the body is currently holding the Curiosity Catalog.
        private static bool HoldsCatalog(CharacterBody? body) =>
            body?.inventory != null && StaticItemDef != null
            && body.inventory.GetItemCountEffective(StaticItemDef) > 0;

        private static bool AnyPlayerHoldsCatalog()
        {
            foreach (var pcmc in PlayerCharacterMasterController.instances)
                if (HoldsCatalog(pcmc.master?.GetBody())) return true;
            return false;
        }

        // ── Prefabs (loaded once, re-used every drop) ─────────────────────────
        private static GameObject? _commandCubePrefab;
        private static GameObject? _dropletPrefab;

        private static GameObject? CommandCubePrefab =>
            _commandCubePrefab != null
                ? _commandCubePrefab
                : _commandCubePrefab = Resources.Load<GameObject>(
                      "Prefabs/NetworkedObjects/CommandCube");

        private static GameObject? DropletPrefab =>
            _dropletPrefab != null
                ? _dropletPrefab
                : _dropletPrefab = Resources.Load<GameObject>(
                      "Prefabs/NetworkedObjects/PickupDroplet");

        // ── ItemBase lifecycle ────────────────────────────────────────────────
        public override void Init(ConfigFile config)
        {
            WhiteCost = config.Bind("Curiosity Catalog", "White Base Cost", 30,
                "Base gold cost of a white-tier purchase (scaled live by difficulty).").Value;
            GreenCost = config.Bind("Curiosity Catalog", "Green Base Cost", 75,
                "Base gold cost of a green-tier purchase (scaled live by difficulty).").Value;
            RedCost = config.Bind("Curiosity Catalog", "Red Base Cost", 400,
                "Base gold cost of a red-tier purchase (scaled live by difficulty).").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            TeleporterInteraction.onTeleporterChargedGlobal   += OnTeleporterCharged;
            On.RoR2.PickupDropletController.CreatePickup      += PickupDroplet_CreatePickup;
            On.RoR2.PurchaseInteraction.OnInteractionBegin    += PurchaseInteraction_Block;
            On.RoR2.PickupPickerController.GetContextString   += PickupPicker_GetContextString;
            On.RoR2.PickupPickerController.OnInteractionBegin += PickupPicker_OnInteractionBegin;
            On.RoR2.PickupPickerController.SubmitChoice       += PickupPicker_SubmitChoice;
        }

        // ── Teleporter event completed: drop the shop ─────────────────────────
        // "Completed" = charged AND the teleporter boss is dead, the same moment the
        // boss reward drops. The boss can die before or after the charge finishes, so
        // wait on whichever comes last. Gold is only converted to XP once the player
        // activates the teleporter to leave, so there's a window to spend it all.
        private static void OnTeleporterCharged(TeleporterInteraction tp)
        {
            if (!NetworkServer.active || tp == null) return;
            tp.StartCoroutine(DropShopWhenCleared(tp));
        }

        private static IEnumerator DropShopWhenCleared(TeleporterInteraction tp)
        {
            while (tp != null && tp.bossGroup != null && tp.bossGroup.enabled)
                yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f);
            if (tp == null || !AnyPlayerHoldsCatalog()) yield break;

            var origin = tp.bossGroup != null && tp.bossGroup.dropPosition != null
                ? tp.bossGroup.dropPosition.position
                : tp.transform.position + Vector3.up * 4f;
            var velocity = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up)
                         * (Vector3.up * 25f + Vector3.forward * 6f);
            DropShop(origin, velocity);
        }

        // Launch a droplet that lands as the shop. Shown as the catalog's own lunar
        // droplet; the CreatePickup hook swaps the landing pickup for our cube.
        internal static void DropShop(Vector3 position, Vector3 velocity)
        {
            var prefab = DropletPrefab;
            if (prefab == null || StaticItemDef == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning(
                    "[CuriosityCatalog] PickupDroplet prefab not found.");
                return;
            }

            var obj = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            obj.AddComponent<CuriosityShopDroplet>();

            var pdc = obj.GetComponent<PickupDropletController>();
            if (pdc != null)
                pdc.NetworkpickupState = new UniquePickup(
                    PickupCatalog.FindPickupIndex(StaticItemDef.itemIndex));

            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = velocity;
                rb.AddTorque(Random.Range(120f, 150f) * Random.onUnitSphere);
            }
            NetworkServer.Spawn(obj);
        }

        private void PickupDroplet_CreatePickup(
            On.RoR2.PickupDropletController.orig_CreatePickup orig, PickupDropletController self)
        {
            if (self.GetComponent<CuriosityShopDroplet>() != null)
            {
                SpawnShop(self.transform.position);
                return;
            }
            orig(self);
        }

        // Live difficulty-scaled price for a base cost (cost = base × coeff^1.25),
        // evaluated against the current difficulty coefficient.
        internal static int ScaledCost(int baseCost) =>
            Run.instance != null ? Run.instance.GetDifficultyScaledCost(baseCost) : baseCost;

        // ── Shop spawning ─────────────────────────────────────────────────────
        internal static void SpawnShop(Vector3 pos)
        {
            var prefab = CommandCubePrefab;
            if (prefab == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning(
                    "[CuriosityCatalog] CommandCube prefab not found.");
                return;
            }

            BuildStock(out var pickups, out var baseCosts);
            if (pickups.Length == 0) return;

            var obj = UnityEngine.Object.Instantiate(prefab, pos + Vector3.up * 1.5f, Quaternion.identity);

            var marker       = obj.AddComponent<CuriosityPickerMarker>();
            marker.Pickups   = pickups;
            marker.BaseCosts = baseCosts;

            // Vanilla cubes inherit the pickup of the droplet that made them; other mods
            // (e.g. WolfoFixes' SetOptionsInternal hook) assume it's set and NRE otherwise.
            // Using the catalog itself also gives the cube a lunar look.
            var networker = obj.GetComponent<PickupIndexNetworker>();
            if (networker != null && StaticItemDef != null)
                networker.NetworkpickupState = new UniquePickup(
                    PickupCatalog.FindPickupIndex(StaticItemDef.itemIndex));

            var ppc = obj.GetComponent<PickupPickerController>();
            if (ppc != null) ppc.SetOptionsServer(BuildOptions(marker, money: uint.MaxValue));

            NetworkServer.Spawn(obj);
        }

        // ── Build the combined stock, ordered white → green → red ─────────────
        private static void BuildStock(out PickupIndex[] pickups, out int[] baseCosts)
        {
            var stock = new List<(PickupIndex pickup, ItemTier tab)>();

            foreach (var itemDef in CursedAppleCuriositiesPlugin.ModItemDefs)
            {
                if (itemDef == null || itemDef == StaticItemDef) continue;
                var pidx = PickupCatalog.FindPickupIndex(itemDef.itemIndex);
                if (pidx != PickupIndex.none) stock.Add((pidx, GetItemTab(itemDef)));
            }

            foreach (var eqDef in CursedAppleCuriositiesPlugin.ModEquipmentDefs)
            {
                if (eqDef == null) continue;
                var pidx = PickupCatalog.FindPickupIndex(eqDef.equipmentIndex);
                if (pidx != PickupIndex.none) stock.Add((pidx, GetEquipmentTab(eqDef)));
            }

            // Grouped by tier; registration order is kept within each tier.
            var sorted = new List<(PickupIndex pickup, ItemTier tab)>();
            foreach (var tab in new[] { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3 })
                foreach (var entry in stock)
                    if (entry.tab == tab) sorted.Add(entry);

            pickups   = new PickupIndex[sorted.Count];
            baseCosts = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                pickups[i]   = sorted[i].pickup;
                baseCosts[i] = TabBaseCost(sorted[i].tab);
            }
        }

        // Options are greyed out when the shopper can't afford them at the current price.
        private static PickupPickerController.Option[] BuildOptions(CuriosityPickerMarker marker, uint money)
        {
            var pickups = marker.Pickups ?? System.Array.Empty<PickupIndex>();
            var options = new PickupPickerController.Option[pickups.Length];
            for (int i = 0; i < pickups.Length; i++)
            {
#pragma warning disable CS0618
                options[i] = new PickupPickerController.Option
                {
                    pickupIndex = pickups[i],
                    available   = money >= (uint)ScaledCost(marker.BaseCosts![i]),
                };
#pragma warning restore CS0618
            }
            return options;
        }

        private static int TabBaseCost(ItemTier tab) => tab switch
        {
            ItemTier.Tier1 => WhiteCost,
            ItemTier.Tier2 => GreenCost,
            _              => RedCost,
        };

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
            if (def == GoldenGooseEgg.StaticEquipmentDef) return ItemTier.Tier1;
            if (def == BloodTribute.StaticEquipmentDef)   return ItemTier.Tier2;
            if (def == Counterspell.StaticEquipmentDef)   return ItemTier.Tier2;
            if (def == EtherealShift.StaticEquipmentDef)  return ItemTier.Tier2;
            if (def == ArcticBlast.StaticEquipmentDef)    return ItemTier.Tier2;
            if (def == CultistSacrifice.StaticEquipmentDef) return ItemTier.Tier2;
            return ItemTier.Tier3;
        }

        // ── Block chests / multishops for catalog holders ─────────────────────
        private void PurchaseInteraction_Block(
            On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig,
            PurchaseInteraction self, Interactor activator)
        {
            if (NetworkServer.active && StaticItemDef != null)
            {
                var body = activator?.GetComponent<CharacterBody>();
                if (HoldsCatalog(body) && IsBlockedInteractable(self))
                {
                    Chat.AddMessage(
                        "<color=#8888ff>[Curiosity Catalog] The catalog has replaced your shopping habits.</color>");
                    return;
                }
            }
            orig(self, activator);
        }

        // ── Custom hover prompt on the shop ───────────────────────────────────
        private string PickupPicker_GetContextString(
            On.RoR2.PickupPickerController.orig_GetContextString orig,
            PickupPickerController self, Interactor activator)
        {
            if (self.GetComponent<CuriosityPickerMarker>() != null)
            {
                // Only catalog holders can shop here.
                if (!HoldsCatalog(activator?.GetComponent<CharacterBody>()))
                    return "Requires the Curiosity Catalog";

                return "Browse Curiosities — " +
                       $"<color=#ffffff>{ScaledCost(WhiteCost)}g</color> / " +
                       $"<color=#77ff17>{ScaledCost(GreenCost)}g</color> / " +
                       $"<color=#e7543a>{ScaledCost(RedCost)}g</color>";
            }
            return orig(self, activator);
        }

        // ── Record the shopper and grey out what they can't afford ────────────
        private void PickupPicker_OnInteractionBegin(
            On.RoR2.PickupPickerController.orig_OnInteractionBegin orig,
            PickupPickerController self, Interactor activator)
        {
            var marker = self.GetComponent<CuriosityPickerMarker>();
            if (marker != null && NetworkServer.active)
            {
                // Non-holders can't open the shop — bounce them before the picker opens.
                var body = activator?.GetComponent<CharacterBody>();
                if (!HoldsCatalog(body))
                {
                    Chat.AddMessage(
                        "<color=#8888ff>[Curiosity Catalog] Only catalog holders can shop here.</color>");
                    return;
                }
                marker.ActivatorBody = body;
                self.SetOptionsServer(BuildOptions(marker, body!.master?.money ?? 0));
            }
            orig(self, activator);
        }

        // ── Intercept selection: gold check → drop item → keep the menu open ──
        private void PickupPicker_SubmitChoice(
            On.RoR2.PickupPickerController.orig_SubmitChoice orig,
            PickupPickerController self, int index)
        {
            var marker = self.GetComponent<CuriosityPickerMarker>();
            if (marker == null || !NetworkServer.active)
            {
                orig(self, index);
                return;
            }

            // Do NOT call orig — that would give a free item and close the shop.
            var pickups = marker.Pickups;
            if (pickups == null || index < 0 || index >= pickups.Length) return;

            var body = marker.ActivatorBody;
            // Safety net: never let a non-holder complete a purchase.
            if (!HoldsCatalog(body) || body!.master == null) return;

            int cost = ScaledCost(marker.BaseCosts![index]); // live price at the moment of purchase
            if (body.master.money >= (uint)cost)
            {
                body.master.money -= (uint)cost;
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo
                    {
                        pickupIndex = pickups[index],
                        rotation    = Quaternion.identity,
                    },
                    body.transform.position + Vector3.up * 1.5f,
                    Vector3.up * 15f + UnityEngine.Random.insideUnitSphere * 3f);
            }
            else
            {
                Chat.AddMessage($"<color=#ff6644>Not enough gold — need {cost}g.</color>");
            }

            // The shop stays put so the player can keep spending; just re-grey
            // whatever their remaining gold no longer covers.
            self.SetOptionsServer(BuildOptions(marker, body.master.money));
        }
    }

    public class CuriosityPickerMarker : MonoBehaviour
    {
        public PickupIndex[]? Pickups;
        public int[]?         BaseCosts; // unscaled, parallel to Pickups; price is scaled live per purchase
        public CharacterBody? ActivatorBody;
    }

    // Tags the droplet that lands as the shop (server-side only).
    public class CuriosityShopDroplet : MonoBehaviour { }
}
