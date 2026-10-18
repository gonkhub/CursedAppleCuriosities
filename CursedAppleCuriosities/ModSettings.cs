using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using UnityEngine;

namespace CursedAppleCuriosities
{
    /// <summary>
    /// The in-game "Mod Options" page (Risk Of Options, a soft dependency): every entry of the mod's config file,
    /// grouped by its section. Values are read when the game starts (they're baked into skills, items and
    /// descriptions), so every option is marked restart-required.
    /// </summary>
    internal static class ModSettings
    {
        public const string RiskOfOptionsGuid = "com.rune580.riskofoptions";

        public static void TryRegister(ConfigFile config, Sprite? icon)
        {
            if (!Chainloader.PluginInfos.ContainsKey(RiskOfOptionsGuid))
            {
                CursedAppleCuriositiesPlugin.Logger?.LogInfo("Risk Of Options not installed: no in-game settings page.");
                return;
            }
            Register(config, icon); // separate method: RiskOfOptions types load only when it's installed
        }

        /// <summary>
        /// Drops every key in the cfg file that nothing binds any more (3.0, user: only keybinds, volumes and the Deadlock
        /// install paths are settings now). BepInEx otherwise keeps old keys in the file forever. Call after every Bind.
        /// </summary>
        public static void PurgeOrphans(ConfigFile config)
        {
            var orphans = (System.Collections.Generic.Dictionary<ConfigDefinition, string>?)typeof(ConfigFile)
                .GetProperty("OrphanedEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?.GetValue(config);
            if (orphans == null || orphans.Count == 0) return;
            int n = orphans.Count;
            orphans.Clear();
            config.Save();
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"Config: removed {n} old settings from the cfg file.");
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void Register(ConfigFile config, Sprite? icon)
        {
            ModSettingsManager.SetModDescription(
                "Deadlock's items and heroes in Risk of Rain 2. Changes apply after restarting the game.");
            if (icon != null) ModSettingsManager.SetModIcon(icon);

            int added = 0;
            // Debug sections last, everything else in the file's own order (tiers, equipment, heroes).
            foreach (ConfigEntryBase entry in config.Select(kv => kv.Value).OrderBy(e => e.Definition.Section.Contains("Debug") ? 1 : 0))
            {
                BaseOption? option = entry switch
                {
                    ConfigEntry<float> f => new FloatFieldOption(f, true),
                    ConfigEntry<int> i => new IntFieldOption(i, true),
                    ConfigEntry<bool> b => new CheckBoxOption(b, true),
                    ConfigEntry<string> s => new StringInputFieldOption(s, true),
                    ConfigEntry<KeyboardShortcut> k => new KeyBindOption(k, false), // read live
                    _ when entry.SettingType.IsEnum => new ChoiceOption(entry, true),
                    _ => null,
                };
                if (option == null) continue;
                ModSettingsManager.AddOption(option);
                added++;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"Risk Of Options: {added} settings on the mod's options page.");
        }
    }
}
