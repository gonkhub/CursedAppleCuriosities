using System;
using System.Collections.Generic;
using RoR2;

namespace CursedAppleCuriosities.Survivors
{
    /// <summary>
    /// The character select roster in alphabetical order by English name, vanilla survivors and every mod's included (user 3.0:
    /// the Deadlock heroes mixed in, not a block at the end). Set as the catalog sorts, so it overrides every desiredSortPosition.
    /// </summary>
    internal static class RosterOrder
    {
        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.SurvivorCatalog.SetSurvivorDefs += (orig, defs) =>
            {
                try { Sort(defs); }
                catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Roster] alphabetical order skipped: {e.Message}"); }
                orig(defs);
            };
        }

        private static void Sort(SurvivorDef[] defs)
        {
            var named = new List<(SurvivorDef Def, string Name)>();
            foreach (SurvivorDef d in defs) if (d != null) named.Add((d, Name(d)));
            named.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < named.Count; i++) named[i].Def.desiredSortPosition = i;
            CursedAppleCuriositiesPlugin.Logger?.LogInfo("[Roster] alphabetical: " + string.Join(", ", named.ConvertAll(n => n.Name)));
        }

        /// <summary>The English display name ("The " dropped, so The Huntress-style names sort by the name itself), else the def's own name.</summary>
        private static string Name(SurvivorDef d)
        {
            string token = d.displayNameToken ?? "";
            string name = token.Length > 0 && Language.english != null ? Language.english.GetLocalizedStringByToken(token) : "";
            if (string.IsNullOrEmpty(name) || name == token) name = d.cachedName ?? "";
            // Sort by the letters only: rich-text tags (other mods colour their names: <color=#..>Nero</color>) and symbols
            // (vanilla's 「V??oid Fiend』) put names at the very start or end of the roster.
            name = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "");
            name = System.Text.RegularExpressions.Regex.Replace(name, @"[^\p{L}\p{N} ]", "").Trim();
            if (name.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
            return name.Length > 0 ? name.Trim() : d.cachedName ?? "";
        }
    }
}
