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
        public override string ItemPickupDesc    => "Unlock a personal shop. Can no longer open chests or multishops.";

        public override string ItemFullDescription =>
            "At the start of each stage, a <style=cIsUtility>personal shop</style> appears nearby with " +
            "three browsable tiers of Curiosity items, starting at " +
            $"<style=cIsHealing>{WhiteCost}g whites</style>, " +
            $"<style=cIsDamage>{GreenCost}g greens</style>, " +
            $"<style=cIsUtility>{RedCost}g reds</style>. " +
            "<style=cIsHealth>Prices rise as the run grows more difficult, so shop early. " +
            "Cannot open chests, multishops, or equipment barrels.</style>";

        public override string ItemLore =>
            "A small leather-bound catalogue, stamped with a golden apple. " +
            "Flipping through it, you hear a voice: " +
            "'Everything in here can be yours. Everything out there... not so much.'";

        public override ItemTier  Tier     => ItemTier.Lunar;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string? IconFileName => "icon_curiositycatalog.png";

        // ── Shelf pricing ─────────────────────────────────────────────────────
        // Base costs are anchored to the base game's guaranteed-tier chests so item
        // access tracks normal run progression: Small Chest (25) → white,
        // Large Chest (50) → green, Legendary Chest (400) → red. A catalog purchase
        // is *better* than its chest (you pick the exact item, and the shop is
        // repeatable), so each tier carries a modest premium over its anchor.
        //
        // These are BASE costs. The price actually charged is run through
        // Run.GetDifficultyScaledCost (cost = base × coeff^1.25) at purchase time,
        // NOT at stage start — so lingering to farm a cleared stage steadily raises
        // prices. Rush the teleporter and cash in while it's cheap.
        public static int WhiteCost;
        public static int GreenCost;
        public static int RedCost;

        // How far from the teleporter the shop spawns. Small enough to sit right next to it,
        // large enough to clear the charge zone so the cubes don't block the boss fight.
        public static float ShopTeleporterOffset;

        public static ItemDef? StaticItemDef;

        // Items that ignore their natural tier and always appear in the Red tab
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

        // ── CommandCube prefab (loaded once, re-used every spawn) ─────────────
        private static GameObject? _commandCubePrefab;

        private static GameObject? CommandCubePrefab =>
            _commandCubePrefab != null
                ? _commandCubePrefab
                : _commandCubePrefab = Resources.Load<GameObject>(
                      "Prefabs/NetworkedObjects/CommandCube");

        // ── ItemBase lifecycle ────────────────────────────────────────────────
        public override void Init(ConfigFile config)
        {
            WhiteCost = config.Bind("Curiosity Catalog", "White Base Cost", 30,
                "Base gold cost of a white-tier purchase (scaled live by difficulty).").Value;
            GreenCost = config.Bind("Curiosity Catalog", "Green Base Cost", 75,
                "Base gold cost of a green-tier purchase (scaled live by difficulty).").Value;
            RedCost = config.Bind("Curiosity Catalog", "Red Base Cost", 400,
                "Base gold cost of a red-tier purchase (scaled live by difficulty).").Value;
            ShopTeleporterOffset = config.Bind("Curiosity Catalog", "Teleporter Offset (m)", 6f,
                "Distance from the teleporter the shop spawns. Keeps it next to the teleporter "
                + "while clearing the charge zone.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.Stage.Start                               += Stage_Start;
            On.RoR2.PurchaseInteraction.OnInteractionBegin    += PurchaseInteraction_Block;
            On.RoR2.PickupPickerController.GetContextString   += PickupPicker_GetContextString;
            On.RoR2.PickupPickerController.OnInteractionBegin += PickupPicker_OnInteractionBegin;
            On.RoR2.PickupPickerController.SubmitChoice       += PickupPicker_SubmitChoice;
        }

        // ── Stage start: spawn the shelf near a catalog holder ────────────────
        private IEnumerator Stage_Start(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            var result = orig(self);
            if (NetworkServer.active)
                self.StartCoroutine(SpawnShelfDelayed());
            return result;
        }

        private IEnumerator SpawnShelfDelayed()
        {
            yield return new WaitForSeconds(1.5f);
            if (StaticItemDef == null) yield break;

            // Require at least one player holding the catalog
            bool anyHolder = false;
            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                var body = pcmc.master?.GetBody();
                if (body?.inventory != null
                    && body.inventory.GetItemCountEffective(StaticItemDef) > 0)
                {
                    anyHolder = true;
                    break;
                }
            }
            if (!anyHolder) yield break;

            // Prefer the teleporter as an anchor; fall back to the first holder's position
            // on stages that have no teleporter (Bazaar, Void Fields, etc.)
            Vector3 anchor;
            if (TeleporterInteraction.instance != null)
            {
                // Spawn right next to the teleporter — close enough to read as "at the teleporter,"
                // but offset so the cubes don't sit on top of it and block the boss-fight zone.
                anchor = TeleporterInteraction.instance.transform.position
                       + Vector3.forward * ShopTeleporterOffset
                       + Vector3.up * 1.5f;
            }
            else
            {
                CharacterBody? fallback = null;
                foreach (var pcmc in PlayerCharacterMasterController.instances)
                {
                    var b = pcmc.master?.GetBody();
                    if (b?.inventory != null
                        && b.inventory.GetItemCountEffective(StaticItemDef) > 0)
                    { fallback = b; break; }
                }
                if (fallback == null) yield break;
                anchor = fallback.transform.position
                       + fallback.transform.forward * 5f
                       + Vector3.up * 1.5f;
            }

            // Store BASE costs on the cubes; the scaled price is computed live at
            // browse/purchase time (see ScaledCost) so it climbs with difficulty as
            // the player lingers, rather than being frozen at stage start.
            SpawnTierCube(ItemTier.Tier1, anchor + Vector3.left  * 2.5f, WhiteCost);
            SpawnTierCube(ItemTier.Tier2, anchor,                         GreenCost);
            SpawnTierCube(ItemTier.Tier3, anchor + Vector3.right * 2.5f, RedCost);
        }

        // Live difficulty-scaled price for a base cost (cost = base × coeff^1.25),
        // evaluated against the current difficulty coefficient.
        internal static int ScaledCost(int baseCost) =>
            Run.instance != null ? Run.instance.GetDifficultyScaledCost(baseCost) : baseCost;

        // ── Cube spawning ─────────────────────────────────────────────────────
        internal static void SpawnTierCube(ItemTier tab, Vector3 pos, int baseCost)
        {
            var prefab = CommandCubePrefab;
            if (prefab == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning(
                    "[CuriosityCatalog] CommandCube prefab not found.");
                return;
            }

            var options = BuildOptions(tab);
            if (options.Length == 0) return;

            var obj = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);

            var marker      = obj.AddComponent<CuriosityPickerMarker>();
            marker.Tab      = tab;
            marker.BaseCost = baseCost;
            marker.Options  = options;

            var ppc = obj.GetComponent<PickupPickerController>();
            if (ppc != null) ppc.SetOptionsServer(options);

            NetworkServer.Spawn(obj);
        }

        // ── Build item list for a given tab ──────────────────────────────────
        private static PickupPickerController.Option[] BuildOptions(ItemTier tab)
        {
            var list = new List<PickupPickerController.Option>();

            foreach (var itemDef in CursedAppleCuriositiesPlugin.ModItemDefs)
            {
                if (itemDef == null || itemDef == StaticItemDef) continue;
                if (GetItemTab(itemDef) != tab) continue;

                var pidx = PickupCatalog.FindPickupIndex(itemDef.itemIndex);
                if (pidx == PickupIndex.none) continue;
#pragma warning disable CS0618
                list.Add(new PickupPickerController.Option { pickupIndex = pidx, available = true });
#pragma warning restore CS0618
            }

            foreach (var eqDef in CursedAppleCuriositiesPlugin.ModEquipmentDefs)
            {
                if (eqDef == null) continue;
                if (GetEquipmentTab(eqDef) != tab) continue;

                var pidx = PickupCatalog.FindPickupIndex(eqDef.equipmentIndex);
                if (pidx == PickupIndex.none) continue;
#pragma warning disable CS0618
                list.Add(new PickupPickerController.Option { pickupIndex = pidx, available = true });
#pragma warning restore CS0618
            }

            return list.ToArray();
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
            if (def == GoldenGooseEgg.StaticEquipmentDef) return ItemTier.Tier1;
            if (def == BloodTribute.StaticEquipmentDef)   return ItemTier.Tier2;
            if (def == Counterspell.StaticEquipmentDef)   return ItemTier.Tier2;
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

        // ── Custom hover prompt on our cubes ──────────────────────────────────
        private string PickupPicker_GetContextString(
            On.RoR2.PickupPickerController.orig_GetContextString orig,
            PickupPickerController self, Interactor activator)
        {
            var marker = self.GetComponent<CuriosityPickerMarker>();
            if (marker != null)
            {
                // Only the catalog's owner can shop here.
                if (!HoldsCatalog(activator?.GetComponent<CharacterBody>()))
                    return "Requires the Curiosity Catalog";

                string tierLabel = marker.Tab switch
                {
                    ItemTier.Tier1 => "White",
                    ItemTier.Tier2 => "Green",
                    _              => "Red",
                };
                return $"Browse {tierLabel} Items — {ScaledCost(marker.BaseCost)}g";
            }
            return orig(self, activator);
        }

        // ── Record the activator so SubmitChoice can deduct their gold ────────
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
                        "<color=#8888ff>[Curiosity Catalog] Only the catalog's owner can shop here.</color>");
                    return;
                }
                marker.ActivatorBody = body;
            }
            orig(self, activator);
        }

        // ── Intercept selection: gold check → give item → respawn cube ────────
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

            var options = marker.Options;
            if (options == null || index < 0 || index >= options.Length) return;

#pragma warning disable CS0618
            var pickup = options[index].pickupIndex;
#pragma warning restore CS0618
            var body     = marker.ActivatorBody;
            int baseCost = marker.BaseCost;
            int cost     = ScaledCost(baseCost); // live price at the moment of purchase
            var pos      = self.transform.position;
            var tab      = marker.Tab;

            // Safety net: never let a non-holder complete a purchase.
            if (!HoldsCatalog(body)) return;

            bool canAfford = body?.master != null && body.master.money >= (uint)cost;
            if (canAfford)
            {
                body!.master.money -= (uint)cost;
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo
                    {
                        pickupIndex = pickup,
                        rotation    = Quaternion.identity,
                    },
                    body.transform.position + Vector3.up * 1.5f,
                    Vector3.up * 15f + UnityEngine.Random.insideUnitSphere * 3f);
            }
            else
            {
                Chat.AddMessage($"<color=#ff6644>Not enough gold — need {cost}g.</color>");
            }

            // Destroy this instance and immediately put a fresh cube in its place.
            // Respawn with the BASE cost so the replacement re-scales live too.
            // Do NOT call orig — that would give a free item and also destroy the object.
            NetworkServer.Destroy(self.gameObject);
            SpawnTierCube(tab, pos, baseCost);
        }
    }

    public class CuriosityPickerMarker : MonoBehaviour
    {
        public ItemTier                      Tab;
        public int                           BaseCost; // unscaled; price is scaled live per purchase
        public CharacterBody?                ActivatorBody;
        public PickupPickerController.Option[]? Options;
    }
}
