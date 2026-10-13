using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>
    /// Server, as each stage starts (Artifact of the Catalog): every item chest and multishop the director placed is
    /// shuffled and dealt its tier, WhiteShare / GreenShare / RedShare of them each, then swapped in place for that
    /// tier's vanilla chest (small, large, legendary; multishops become chests too) and priced at that tier's souls. Equipment barrels, shrines, drones, lunar pods, void
    /// containers and free chests are left alone (still priced in souls).
    /// </summary>
    public static class SoulChests
    {
        private static readonly ItemTier[] Tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3 };

        private static GameObject? Chest(ItemTier tier) => Load(tier switch
        {
            ItemTier.Tier2 => "RoR2/Base/Chest2/Chest2.prefab",
            ItemTier.Tier3 => "RoR2/Base/GoldChest/GoldChest.prefab",
            _ => "RoR2/Base/Chest1/Chest1.prefab",
        });


        private static readonly Dictionary<string, GameObject?> Prefabs = new Dictionary<string, GameObject?>();
        private static GameObject? Load(string key)
        {
            if (!Prefabs.TryGetValue(key, out GameObject? p))
            {
                try { p = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion(); }
                catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Catalog] {key}: {e.Message}"); }
                Prefabs[key] = p;
            }
            return p;
        }

        public static void Hook()
        {
            SceneDirector.onPostPopulateSceneServer += _ => Retier();
        }

        private static void Retier()
        {
            if (!NetworkServer.active || !CatalogArtifact.Active || Run.instance == null) return;

            // Multishops become chests too (user): one spot each.
            var spots = new List<GameObject>();
            foreach (ChestBehavior c in Object.FindObjectsOfType<ChestBehavior>())
                if (IsItemChest(c.gameObject)) spots.Add(c.gameObject);
            foreach (MultiShopController m in Object.FindObjectsOfType<MultiShopController>())
                if (!m.doEquipmentInstead && !m.isTripleDroneVendor && m.costType == CostTypeIndex.Money) spots.Add(m.gameObject);
            if (spots.Count == 0) return;

            // Deal the tiers by share (largest remainder), then shuffle which spot gets which.
            ItemTier[] deal = Deal(spots.Count);
            Xoroshiro128Plus rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
            for (int i = deal.Length - 1; i > 0; i--)
            {
                int j = rng.RangeInt(0, i + 1);
                (deal[i], deal[j]) = (deal[j], deal[i]);
            }

            int[] counts = new int[3];
            for (int i = 0; i < spots.Count; i++)
            {
                ItemTier tier = deal[i];
                if (Swap(spots[i], tier)) counts[System.Array.IndexOf(Tiers, tier)]++;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Catalog] {spots.Count} chests re-tiered: {counts[0]} white, {counts[1]} green, {counts[2]} red; " +
                $"a basic chest is {Souls.Unit} gold = {Souls.PerChest} souls");
        }

        /// <summary>A chest that drops items for gold (not equipment barrels, free stealth chests, lunar pods or void cradles).</summary>
        private static bool IsItemChest(GameObject go)
        {
            if (!go.TryGetComponent(out PurchaseInteraction pi) || pi.costType != CostTypeIndex.Money || pi.cost <= 0) return false;
            if (go.GetComponentInParent<MultiShopController>() != null) return false; // its terminals: the multishop is dealt as one
            string n = go.name;
            return !n.StartsWith("EquipmentBarrel") && !n.StartsWith("LunarChest") && !n.StartsWith("VoidChest") && !n.StartsWith("VoidTriple");
        }

        private static ItemTier[] Deal(int n)
        {
            float[] share = { CatalogArtifact.WhiteShare, CatalogArtifact.GreenShare, CatalogArtifact.RedShare };
            float total = Mathf.Max(0.0001f, share[0] + share[1] + share[2]);
            int[] count = new int[3];
            float[] rest = new float[3];
            int dealt = 0;
            for (int t = 0; t < 3; t++)
            {
                float exact = n * share[t] / total;
                count[t] = Mathf.FloorToInt(exact);
                rest[t] = exact - count[t];
                dealt += count[t];
            }
            while (dealt < n)
            {
                int best = 0;
                for (int t = 1; t < 3; t++) if (rest[t] > rest[best]) best = t;
                count[best]++; rest[best] = -1f; dealt++;
            }
            var deal = new List<ItemTier>(n);
            for (int t = 0; t < 3; t++) for (int k = 0; k < count[t]; k++) deal.Add(Tiers[t]);
            return deal.ToArray();
        }

        /// <summary>Replaces the chest or multishop with its tier's chest at the same spot, priced in that tier's souls.</summary>
        private static bool Swap(GameObject old, ItemTier tier)
        {
            GameObject? prefab = Chest(tier);
            if (prefab == null) return false;
            Transform t = old.transform;
            GameObject fresh = Object.Instantiate(prefab, t.position, t.rotation);
            if (fresh.TryGetComponent(out PurchaseInteraction pi)) pi.Networkcost = Souls.GoldFor(CatalogArtifact.Mult(tier));
            NetworkServer.Spawn(fresh);
            DestroyWithTerminals(old);
            return true;
        }

        /// <summary>A multishop's terminals are their own networked objects: they go with it.</summary>
        private static void DestroyWithTerminals(GameObject old)
        {
            if (old.TryGetComponent(out MultiShopController shop))
                foreach (GameObject t in shop.terminalGameObjectsList)
                    if (t != null) NetworkServer.Destroy(t);
            NetworkServer.Destroy(old);
        }
    }
}
