using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Broker
{
    public struct CurseEntry
    {
        public ItemIndex Item;
        public CursePenalty Penalty;
        public float Value;
    }

    /// <summary>
    /// A player's curses for the run, on their CharacterMaster (which survives stage changes). The server owns it and
    /// mirrors it to every client with <see cref="CurseLedgerMessage"/>, because clients compute their own effective
    /// item counts, movement and attack speed. The curse stays even if every copy of the item is lost.
    /// </summary>
    public class CurseLedger : MonoBehaviour
    {
        public readonly List<CurseEntry> Entries = new List<CurseEntry>();

        private static readonly Dictionary<CharacterMaster, CurseLedger> ByMaster = new Dictionary<CharacterMaster, CurseLedger>();
        private static readonly Dictionary<Inventory, CurseLedger> ByInventory = new Dictionary<Inventory, CurseLedger>();
        private static int _totalEntries;

        /// <summary>True once anyone holds a curse (lets every hook skip its lookups otherwise).</summary>
        public static bool Any => _totalEntries > 0;
        public static bool AnyGravity { get; private set; }

        // Server only: copies of a revive item whose bonus revive has been spent (each copy revives twice).
        private readonly HashSet<ItemIndex> _bonusReviveUsed = new HashSet<ItemIndex>();

        private CharacterMaster? _master;
        private Inventory? _inventory;

        private void Awake()
        {
            _master = GetComponent<CharacterMaster>();
            _inventory = GetComponent<Inventory>();
            if (_master != null) ByMaster[_master] = this;
            if (_inventory != null) ByInventory[_inventory] = this;
        }

        private void OnDestroy()
        {
            if (_master != null) ByMaster.Remove(_master);
            if (_inventory != null) ByInventory.Remove(_inventory);
            Recount();
        }

        public static CurseLedger? For(CharacterMaster? master) =>
            master != null && ByMaster.TryGetValue(master, out CurseLedger l) ? l : null;

        public static CurseLedger? For(Inventory? inventory) =>
            inventory != null && ByInventory.TryGetValue(inventory, out CurseLedger l) ? l : null;

        public static CurseLedger GetOrAdd(CharacterMaster master) =>
            For(master) ?? master.gameObject.AddComponent<CurseLedger>();

        public bool TryGet(ItemIndex item, out CurseEntry entry)
        {
            foreach (CurseEntry e in Entries)
                if (e.Item == item) { entry = e; return true; }
            entry = default;
            return false;
        }

        public bool IsCursed(ItemIndex item) => TryGet(item, out _);

        private static void Recount()
        {
            int n = 0;
            bool gravity = false;
            foreach (CurseLedger l in ByMaster.Values)
            {
                n += l.Entries.Count;
                foreach (CurseEntry e in l.Entries) gravity |= e.Penalty == CursePenalty.Gravity;
            }
            _totalEntries = n;
            AnyGravity = gravity;
        }

        /// <summary>Server: curse an item for this player and tell everyone.</summary>
        public void AddServer(CurseEntry entry)
        {
            if (!NetworkServer.active || IsCursed(entry.Item)) return;
            Entries.Add(entry);
            Recount();
            Refresh();
            Broadcast();
        }

        /// <summary>Recompute effective item counts and stats after the curse list changed.</summary>
        public void Refresh()
        {
            if (_inventory != null)
            {
                try
                {
                    UpdateAllEffective?.Invoke(_inventory, null);
                    HandleInventoryChanged?.Invoke(_inventory, null);
                }
                catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedBroker] Inventory refresh failed: {e}"); }
            }
            CharacterBody? body = _master != null ? _master.GetBody() : null;
            if (body != null) body.MarkAllStatsDirty();
        }

        public void Broadcast()
        {
            if (!NetworkServer.active || _master == null) return;
            new CurseLedgerMessage { Master = _master.gameObject, Entries = Entries.ToArray() }.Send(NetworkDestination.Clients);
        }

        internal void ApplyFromServer(CurseEntry[] entries)
        {
            Entries.Clear();
            Entries.AddRange(entries);
            Recount();
            Refresh();
        }

        public static void BroadcastAll()
        {
            if (!NetworkServer.active) return;
            foreach (CurseLedger l in ByMaster.Values) l.Broadcast();
        }

        // ───────────────────────────────────── hooks ─────────────────────────────────────

        private static readonly MethodInfo? UpdateAllEffective =
            typeof(Inventory).GetMethod("UpdateAllEffectiveItemStacks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly MethodInfo? HandleInventoryChanged =
            typeof(Inventory).GetMethod("HandleInventoryChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? DisplayInventory =
            typeof(ItemInventoryDisplay).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? DisplayStacks =
            typeof(ItemInventoryDisplay).GetField("itemStacks", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? DisplayIcons =
            typeof(ItemInventoryDisplay).GetField("itemIcons", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Hook()
        {
            NetworkingAPI.RegisterMessageType<CurseLedgerMessage>();

            // The buff itself: a cursed item's effective count (what every item reads) becomes copies × Mult + Add.
            IL.RoR2.Inventory.UpdateEffectiveItemStacks += il =>
            {
                var c = new ILCursor(il);
                if (!c.TryGotoNext(i => i.MatchLdflda<Inventory>("effectiveItemStacks"))
                    || !c.TryGotoNext(i => i.MatchCallOrCallvirt(out MethodReference m) && m.Name == "SetStackValue"))
                {
                    CursedAppleCuriositiesPlugin.Logger?.LogError("[CursedBroker] Inventory.UpdateEffectiveItemStacks: hook point not found; curses will not buff items.");
                    return;
                }
                // Stack: [ref effectiveItemStacks, itemIndex, count] -> count = Adjust(count, this, itemIndex)
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldarg_1);
                c.Emit(OpCodes.Call, typeof(CurseLedger).GetMethod(nameof(AdjustEffective), BindingFlags.Static | BindingFlags.Public));
            };

            // Revive items: each cursed copy revives twice. The consumed copy is restored once, shortly after the revive.
            On.RoR2.CharacterMaster.RespawnExtraLife += (orig, self) =>
            {
                orig(self);
                For(self)?.BonusRevive(RoR2Content.Items.ExtraLife, RoR2Content.Items.ExtraLifeConsumed);
            };
            On.RoR2.CharacterMaster.RespawnExtraLifeVoid += (orig, self) =>
            {
                orig(self);
                For(self)?.BonusRevive(DLC1Content.Items.ExtraLifeVoid, DLC1Content.Items.ExtraLifeVoidConsumed);
            };

            // Like a void item's "corrupts all X": the curse is on the item, so new copies join the cursed stack.
            Inventory.onServerItemGiven += (inv, item, count) =>
            {
                if (!Any || count <= 0 || !(For(inv) is CurseLedger l) || !l.IsCursed(item)) return;
                CharacterMaster? m = l._master;
                if (m == null || m.playerCharacterMasterController == null) return;
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                {
                    baseToken = $"<color={CurseColor}>{m.playerCharacterMasterController.GetDisplayName()}'s new " +
                                $"{Language.GetString(ItemCatalog.GetItemDef(item).nameToken)} joins the curse.</color>",
                });
            };

            Stage.onServerStageBegin += _ => BroadcastAll();
            NetworkUser.onPostNetworkUserStart += _ =>
            {
                if (NetworkServer.active && CursedAppleCuriositiesPlugin.Instance != null)
                    CursedAppleCuriositiesPlugin.Instance.StartCoroutine(BroadcastLater());
            };

            // HUD and scoreboard: show the real number of copies, a purple glow, and the curse in the tooltip.
            On.RoR2.UI.ItemInventoryDisplay.UpdateDisplay += (orig, self) =>
            {
                CurseLedger? ledger = Any && DisplayInventory != null ? For(DisplayInventory.GetValue(self) as Inventory) : null;
                if (ledger != null && DisplayStacks?.GetValue(self) is int[] stacks && ledger._inventory != null)
                    foreach (CurseEntry e in ledger.Entries)
                    {
                        int i = (int)e.Item;
                        if (i >= 0 && i < stacks.Length && stacks[i] > 0) stacks[i] = ledger._inventory.GetItemCountTotal(e.Item);
                    }
                orig(self);
                if (DisplayIcons?.GetValue(self) is List<ItemIcon> icons)
                    foreach (ItemIcon icon in icons) DecorateIcon(icon, ledger);
            };
        }

        private static IEnumerator BroadcastLater()
        {
            yield return new WaitForSeconds(3f);
            BroadcastAll();
        }

        public static int AdjustEffective(int count, Inventory inventory, ItemIndex item)
        {
            if (count <= 0 || !Any) return count;
            CurseLedger? l = For(inventory);
            if (l == null || !l.IsCursed(item)) return count;
            return CurseRules.Get(item).Apply(count);
        }

        private void BonusRevive(ItemDef? item, ItemDef? consumed)
        {
            if (!NetworkServer.active || item == null || consumed == null || !IsCursed(item.itemIndex)) return;
            if (_bonusReviveUsed.Remove(item.itemIndex)) return; // this copy's second revive: let it go
            StartCoroutine(RestoreRevive(item, consumed));
        }

        private IEnumerator RestoreRevive(ItemDef item, ItemDef consumed)
        {
            yield return new WaitForSeconds(0.5f); // the consumed copy is swapped in when the revive completes
            if (_inventory == null || _inventory.GetItemCountPermanent(consumed) <= 0) yield break;
            _inventory.RemoveItemPermanent(consumed);
            _inventory.GiveItemPermanent(item);
            _bonusReviveUsed.Add(item.itemIndex);
            Chat.AddMessage($"<color=#b06cff>The curse will not let go: {Language.GetString(item.nameToken)} endures for one more death.</color>");
        }

        // ───────────────────────────────────── text ─────────────────────────────────────

        public const string CurseColor = "#b06cff";

        /// <summary>The curse block shown under an item: what it improves, and the penalty for the run.</summary>
        public static string CurseText(ItemIndex item, CursePenalty penalty, float value, bool offer)
        {
            CurseRule rule = CurseRules.Get(item);
            string count = rule.CountText();
            string name = Language.GetString(ItemCatalog.GetItemDef(item).nameToken);
            return $"<color={CurseColor}>{(offer ? "CURSE OFFER" : "CURSED")}: curses every {name} you hold or pick up later.</color>\n" +
                   $"<style=cIsUtility>Improves:</style> {rule.Improves}{(count.Length > 0 ? " <style=cStack>(" + count + ")</style>" : "")}\n" +
                   $"<style=cDeath>Penalty for the rest of the run: {CursePenalties.Describe(penalty, value)}.</style>" +
                   (offer ? "\n<style=cStack>The penalty stays even if you lose the item.</style>" : "");
        }

        private static void DecorateIcon(ItemIcon icon, CurseLedger? ledger)
        {
            if (icon == null || icon.tooltipProvider == null) return;
            CursedIconMark? mark = icon.GetComponent<CursedIconMark>();
            if (ledger != null && ledger.TryGet(icon.itemIndex, out CurseEntry e))
            {
                ItemDef def = ItemCatalog.GetItemDef(e.Item);
                if (mark == null) mark = icon.gameObject.AddComponent<CursedIconMark>();
                icon.tooltipProvider.overrideTitleText = $"<color={CurseColor}>Cursed</color> {Language.GetString(def.nameToken)}";
                icon.tooltipProvider.overrideBodyText = Language.GetString(def.pickupToken) + "\n\n" + CurseText(e.Item, e.Penalty, e.Value, offer: false);
                if (icon.glowImage != null) icon.glowImage.color = new Color(0.69f, 0.42f, 1f, 0.9f);
                mark.Item = e.Item;
            }
            else if (mark != null)
            {
                icon.tooltipProvider.overrideTitleText = "";
                icon.tooltipProvider.overrideBodyText = "";
                ItemDef? def = ItemCatalog.GetItemDef(icon.itemIndex);
                ItemTierDef? tier = def != null ? ItemTierCatalog.GetItemTierDef(def.tier) : null;
                if (icon.glowImage != null && tier != null)
                {
                    Color c = ColorCatalog.GetColor(tier.darkColorIndex);
                    icon.glowImage.color = new Color(c.r, c.g, c.b, 0.75f);
                }
                UnityEngine.Object.Destroy(mark);
            }
        }
    }

    /// <summary>Tags an inventory icon this mod decorated, so it can be undone when the slot shows another item.</summary>
    public class CursedIconMark : MonoBehaviour
    {
        public ItemIndex Item = ItemIndex.None;
    }

    public class CurseLedgerMessage : INetMessage
    {
        public GameObject? Master;
        public CurseEntry[] Entries = Array.Empty<CurseEntry>();

        public void Serialize(NetworkWriter w)
        {
            w.Write(Master);
            w.Write(Entries.Length);
            foreach (CurseEntry e in Entries)
            {
                w.Write((int)e.Item);
                w.Write((byte)e.Penalty);
                w.Write(e.Value);
            }
        }

        public void Deserialize(NetworkReader r)
        {
            Master = r.ReadGameObject();
            Entries = new CurseEntry[r.ReadInt32()];
            for (int i = 0; i < Entries.Length; i++)
                Entries[i] = new CurseEntry { Item = (ItemIndex)r.ReadInt32(), Penalty = (CursePenalty)r.ReadByte(), Value = r.ReadSingle() };
        }

        public void OnReceived()
        {
            if (NetworkServer.active || Master == null) return; // the server's copy is the source
            CharacterMaster? m = Master.GetComponent<CharacterMaster>();
            if (m != null) CurseLedger.GetOrAdd(m).ApplyFromServer(Entries);
        }
    }
}
