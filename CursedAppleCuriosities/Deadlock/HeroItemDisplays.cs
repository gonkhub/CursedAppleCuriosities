using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// One item display rule on a hero: rule <see cref="Index"/> of the item's (or equipment's) group in Commando's
    /// rule set, moved to <see cref="Child"/> at this local pose. <see cref="From"/> is the attach point Commando's
    /// rule uses, checked so a game update that reorders the rules is skipped, not misapplied.
    /// </summary>
    public readonly struct HeroItemRule
    {
        public readonly string Key;
        public readonly int Index;
        public readonly string From, Child;
        public readonly Vector3 LocalPos, LocalAngles, LocalScale;

        public HeroItemRule(string key, int index, string from, string child, Vector3 localPos, Vector3 localAngles, Vector3 localScale)
        {
            Key = key; Index = index; From = from; Child = child;
            LocalPos = localPos; LocalAngles = localAngles; LocalScale = localScale;
        }
    }

    /// <summary>
    /// Gives a hero its own copy of Commando's item display rule set (the asset is shared, so editing it in place
    /// would move items on Commando too) with the hero's rules applied.
    /// </summary>
    public static class HeroItemDisplays
    {
        private const string Tag = "[HeroItemDisplays]";

        private static bool _hooked;

        /// <summary>Applies once the catalogs and vanilla rule sets are ready (item indices don't exist before).</summary>
        /// <param name="hidden">Items or equipment (by name) that show nothing on this hero.</param>
        public static void Register(string heroName, Func<GameObject?[]> prefabs, HeroItemRule[] rules, string[]? hidden = null)
        {
            if (!_hooked) { _hooked = true; Hook(); }
            RoR2Application.onLoad += () =>
            {
                try { Apply(heroName, prefabs(), rules, hidden ?? Array.Empty<string>()); }
                catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} {heroName}: {e}"); }
            };
        }

        private static void Apply(string heroName, GameObject?[] prefabs, HeroItemRule[] rules, string[] hidden)
        {
            var models = new List<CharacterModel>();
            foreach (GameObject? p in prefabs)
            {
                var cm = p != null ? p.GetComponentInChildren<CharacterModel>(true) : null;
                if (cm != null && cm.itemDisplayRuleSet != null) models.Add(cm);
            }
            if (models.Count == 0) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} {heroName}: no rule set to copy"); return; }

            ItemDisplayRuleSet source = models[0].itemDisplayRuleSet;
            // Copy the saved rule list (not just the runtime tables): the game and R2API regenerate the runtime
            // tables from it, so overrides baked in here survive any regeneration.
            var groups = new List<ItemDisplayRuleSet.KeyAssetRuleGroup>(source.keyAssetRuleGroups);

            int applied = 0;
            var missing = new List<string>();
            foreach (HeroItemRule r in rules)
            {
                UnityEngine.Object? key = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(r.Key));
                if (key == null) key = EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(r.Key));
                if (key == null) { missing.Add(r.Key); continue; }

                int i = groups.FindIndex(g => g.keyAsset == key);
                if (i < 0)
                {
                    // Not resolved in the source: a new entry, listed last so it wins.
                    groups.Add(new ItemDisplayRuleSet.KeyAssetRuleGroup { keyAsset = key, displayRuleGroup = FindRules(source, key) });
                    i = groups.Count - 1;
                }
                ItemDisplayRuleSet.KeyAssetRuleGroup group = groups[i];
                if (Place(ref group.displayRuleGroup, r)) { groups[i] = group; applied++; }
                else missing.Add($"{r.Key}[{r.Index}]");
            }

            foreach (string name in hidden)
            {
                UnityEngine.Object? key = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(name));
                if (key == null) key = EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(name));
                if (key == null) { missing.Add(name); continue; }
                groups.RemoveAll(g => g.keyAsset == key);
                // An empty group listed last, so nothing from the source shows either.
                groups.Add(new ItemDisplayRuleSet.KeyAssetRuleGroup { keyAsset = key, displayRuleGroup = DisplayRuleGroup.empty });
            }

            var idrs = ScriptableObject.CreateInstance<ItemDisplayRuleSet>();
            idrs.name = "idrsCursedApple" + heroName;
            idrs.keyAssetRuleGroups = groups.ToArray();
            RoR2Application.instance.StartCoroutine(idrs.GenerateRuntimeValuesAsync()); // finishes at once: every key asset is loaded
            foreach (CharacterModel cm in models) cm.itemDisplayRuleSet = idrs;
            _ruleSets[models[0].GetComponentInParent<CharacterBody>(true)?.name ?? heroName] = idrs;
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"{Tag} {heroName}: {applied}/{rules.Length} item display rules applied");
            if (missing.Count > 0) CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} {heroName}: skipped {string.Join(", ", missing)}");
        }

        /// <summary>The source's current rules for a key asset, read from its runtime tables.</summary>
        private static DisplayRuleGroup FindRules(ItemDisplayRuleSet source, UnityEngine.Object key) =>
            key is ItemDef item ? source.GetItemDisplayRuleGroup(item.itemIndex)
            : key is EquipmentDef equipment ? source.GetEquipmentDisplayRuleGroup(equipment.equipmentIndex)
            : DisplayRuleGroup.empty;

        /// <summary>Rewrites one rule's placement. The rules array is replaced, never edited, so Commando's stays as it was.</summary>
        private static bool Place(ref DisplayRuleGroup group, HeroItemRule r)
        {
            if (group.rules == null || r.Index >= group.rules.Length || group.rules[r.Index].childName != r.From) return false;
            var rules = (ItemDisplayRule[])group.rules.Clone();
            rules[r.Index].childName = r.Child;
            rules[r.Index].localPos = r.LocalPos;
            rules[r.Index].localAngles = r.LocalAngles;
            rules[r.Index].localScale = r.LocalScale;
            group.rules = rules;
            return true;
        }

        // ---------------- check on spawn ----------------

        private static readonly Dictionary<string, ItemDisplayRuleSet> _ruleSets = new Dictionary<string, ItemDisplayRuleSet>();
        private static readonly HashSet<string> _checked = new HashSet<string>();

        /// <summary>Logs once per hero whether the spawned body really carries the hero's rule set.</summary>
        internal static void Hook()
        {
            CharacterBody.onBodyStartGlobal += body =>
            {
                string name = body.name.Replace("(Clone)", "");
                if (!_ruleSets.TryGetValue(name, out ItemDisplayRuleSet want) || !_checked.Add(name)) return;
                var cm = body.modelLocator != null && body.modelLocator.modelTransform != null ? body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
                string has = cm != null && cm.itemDisplayRuleSet != null ? cm.itemDisplayRuleSet.name : "none";
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"{Tag} {name} spawned with rule set {has}" + (cm != null && cm.itemDisplayRuleSet == want ? " (custom placements active)" : " - NOT the custom one"));
            };
        }
    }
}
