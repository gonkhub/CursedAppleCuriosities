using BepInEx;
using BepInEx.Logging;
using CursedAppleCuriosities.Artifacts;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Abrams;
using CursedAppleCuriosities.Survivors.Celeste;
using CursedAppleCuriosities.Survivors.GreyTalon;
using CursedAppleCuriosities.Survivors.Lash;
using CursedAppleCuriosities.Survivors.Seven;
using CursedAppleCuriosities.Survivors.Victor;
using CursedAppleCuriosities.Survivors.Viscous;
using CursedAppleCuriosities.Survivors.Vyper;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Lets the game's console find this assembly's [ConCommand]s (the Cursed Broker debug commands).
[assembly: HG.Reflection.SearchableAttribute.OptIn]

namespace CursedAppleCuriosities
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("com.bepis.r2api.items", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.language", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.content_management", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.recalculatestats", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.prefab", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.networking", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.proctype", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ModSettings.RiskOfOptionsGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class CursedAppleCuriositiesPlugin : BaseUnityPlugin
    {
        public static CursedAppleCuriositiesPlugin? Instance { get; private set; }
        internal static new ManualLogSource? Logger { get; private set; }

        // All ItemDefs and EquipmentDefs registered by this mod — populated after init.
        // Used by CuriosityShop to filter drop tables to mod-only items.
        public static HashSet<ItemDef>      ModItemDefs      { get; } = new HashSet<ItemDef>();
        public static HashSet<EquipmentDef> ModEquipmentDefs { get; } = new HashSet<EquipmentDef>();

        private readonly List<ItemBase>      _items      = new List<ItemBase>();
        private readonly List<EquipmentBase> _equipments = new List<EquipmentBase>();
        private readonly List<ArtifactBase>  _artifacts  = new List<ArtifactBase>();

        // Preloaded hero models become Unity objects a few milliseconds per frame.
        private void Update() => DeadlockInstall.Pump();

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Testing.SelfTest.TryStart(gameObject);

            // Strip any ItemRelationshipProvider with a null relationshipType before it reaches
            // ItemCatalog.SetItemRelationships, which throws ArgumentNullException on null keys.
            // This guards against buggy providers from other mods (e.g. Nautilus NT_ITEMRELATIONSHIPPROVIDER).
            On.RoR2.ItemCatalog.SetItemRelationships += (orig, providers) =>
            {
                if (providers != null)
                    providers = providers.Where(p => p?.relationshipType != null).ToArray();
                orig(providers);
            };

            // Item tiers can only be resolved once ItemTierCatalog is populated. The tier
            // setter does _itemTierDef = ItemTierCatalog.GetItemTierDef(value), which returns
            // null this early in Awake — so we set deprecatedTier now and re-resolve here,
            // right after the catalog initializes, to populate _itemTierDef (fixes white tiers).
            On.RoR2.ItemTierCatalog.Init += (orig) =>
            {
                orig();
                // The tier catalog is populated now, so .tier resolves correctly (populates
                // _itemTierDef, the field the color/drop/logbook systems read). Must run before
                // ItemCatalog.Init reads it.
                foreach (var item in _items)
                    item.ResolveTier();
            };

            // Dispatch equipment activation to the matching EquipmentBase. Returning true here
            // starts the cooldown; our OnUse returns true on a successful activation.
            On.RoR2.EquipmentSlot.PerformEquipmentAction += (orig, self, equipmentDef) =>
            {
                if (equipmentDef != null)
                    foreach (var eq in _equipments)
                        if (eq.EquipmentDef == equipmentDef)
                            using (DurationExtender.From(self.characterBody)) return eq.OnUse(self);
                return orig(self, equipmentDef);
            };

            try { CuriositiesExpansion.Init(); } // before items: their defs require it
            catch (Exception e) { Logger.LogError($"  ! Expansion failed to init: {e}"); }
            InitItems();
            Logger.LogInfo($"  items done at {clock.ElapsedMilliseconds} ms");
            InitEquipments();
            Logger.LogInfo($"  equipment done at {clock.ElapsedMilliseconds} ms");
            InitArtifacts();
            InitSurvivors();
            Logger.LogInfo($"  survivors done at {clock.ElapsedMilliseconds} ms");

            try { Stages.DeadlockMap.DeadlockMaps.Init(Config); Logger.LogInfo("  + [Stage] Deadlock map (Sunken Plaza)"); }
            catch (Exception e) { Logger.LogError($"  ! [Stage] Deadlock map failed to init: {e}"); }
            try { Broker.CursedBroker.Init(Config); Logger.LogInfo("  + [Interactable] Cursed Broker"); }
            catch (Exception e) { Logger.LogError($"  ! [Interactable] Cursed Broker failed to init: {e}"); }
            try { Interactables.SoulUrn.Init(Config); Logger.LogInfo("  + [Interactable] Soul Urn"); }
            catch (Exception e) { Logger.LogError($"  ! [Interactable] Soul Urn failed to init: {e}"); }
            try { if (Interactables.SoulUrn.Enabled) Interactables.SoulUrnVoice.Init(gameObject); }
            catch (Exception e) { Logger.LogError($"  ! [Interactable] Soul Urn voice failed to init: {e}"); }

            // Populate the sets used by CuriosityShop to filter drop tables.
            foreach (var item in _items)
                if (item.ItemDef != null) ModItemDefs.Add(item.ItemDef);
            foreach (var eq in _equipments)
                if (eq.EquipmentDef != null) ModEquipmentDefs.Add(eq.EquipmentDef);

            DurationExtender.HookSources(); // last: wraps every other hook on hits and damage

            try { ModSettings.TryRegister(Config, ItemBase.LoadEmbeddedIcon("icon_curiositycatalog.png")); }
            catch (Exception e) { Logger.LogError($"  ! Settings page failed: {e}"); }

            Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded in {clock.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Deadlock heroes as survivors. Their models, animations, sounds and icons are read from the player's
        /// own Deadlock install (read-only, never while Deadlock runs, never shipped) on a background thread.
        /// </summary>
        private void InitSurvivors()
        {
            DeadlockInstall.Bind(Config);
            DeadlockSounds.Ensure(gameObject);
            var heroes = new (string Name, Action<BepInEx.Configuration.ConfigFile, UnityEngine.GameObject> Init)[]
            {
                ("Lash", LashSurvivor.Init),
                ("Abrams", AbramsSurvivor.Init),
                ("Seven", SevenSurvivor.Init),
                ("Vyper", VyperSurvivor.Init),
                ("Grey Talon", GreyTalonSurvivor.Init),
                ("Victor", VictorSurvivor.Init),
                ("Viscous", ViscousSurvivor.Init),
                ("Celeste", CelesteSurvivor.Init),
            };
            foreach (var hero in heroes)
            {
                if (!IsReleased(hero.Name))
                {
                    Logger?.LogInfo($"  - [Survivor] {hero.Name} (not in this release)");
                    continue;
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    hero.Init(Config, gameObject);
                    Logger?.LogInfo($"  + [Survivor] {hero.Name} ({clock.ElapsedMilliseconds} ms)");
                }
                catch (Exception e)
                {
                    Logger?.LogError($"  ! [Survivor] {hero.Name} failed to init: {e}");
                }
            }
            foreach (var eq in _equipments) // registered before the reader starts so it copies them
            {
                try { eq.RegisterSounds(); }
                catch (Exception e) { Logger?.LogError($"  ! [Equipment] {eq.EquipmentName} sounds failed: {e}"); }
            }
            DeadlockInstall.Start();
        }

        /// <summary>
        /// Survivors ship one at a time: Release builds (the Thunderstore package) register only these.
        /// Debug builds (development) register every hero.
        /// </summary>
        private static readonly string[] ReleasedSurvivors = { "Lash", "Abrams", "Seven" };

        private static bool IsReleased(string hero)
        {
#if DEBUG
            return true;
#else
            return Array.IndexOf(ReleasedSurvivors, hero) >= 0;
#endif
        }

        private void InitEquipments()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => !t.IsAbstract && typeof(EquipmentBase).IsAssignableFrom(t));

            foreach (var type in types)
            {
                try
                {
                    var eq = (EquipmentBase)Activator.CreateInstance(type);
                    eq.Init(Config);
                    _equipments.Add(eq);
                    Logger?.LogInfo($"  + [Equipment] {eq.EquipmentName}");
                }
                catch (Exception e)
                {
                    // One bad equipment must not abort the whole mod's registration.
                    Logger?.LogError($"  ! [Equipment] {type.Name} failed to init: {e}");
                }
            }
        }

        private void InitArtifacts()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => !t.IsAbstract && typeof(ArtifactBase).IsAssignableFrom(t));

            foreach (var type in types)
            {
                try
                {
                    var artifact = (ArtifactBase)Activator.CreateInstance(type);
                    artifact.Init(Config);
                    _artifacts.Add(artifact);
                    Logger?.LogInfo($"  + [Artifact] {artifact.ArtifactName}");
                }
                catch (Exception e)
                {
                    Logger?.LogError($"  ! [Artifact] {type.Name} failed to init: {e}");
                }
            }
        }

        private void InitItems()
        {
            // Auto-discover every concrete ItemBase subclass in this assembly
            var itemTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => !t.IsAbstract && typeof(ItemBase).IsAssignableFrom(t));

            foreach (var type in itemTypes)
            {
                try
                {
                    var item = (ItemBase)Activator.CreateInstance(type);
                    item.Init(Config);
                    _items.Add(item);
                    Logger?.LogInfo($"  + {item.ItemName}");
                }
                catch (Exception e)
                {
                    // One bad item must not abort the whole mod (e.g. ModItemDefs population).
                    Logger?.LogError($"  ! Item {type.Name} failed to init: {e}");
                }
            }
        }
    }

    internal static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.cursedapple.curiosities";
        public const string PLUGIN_NAME = "Cursed Apple Curiosities";
        public const string PLUGIN_VERSION = "1.9.0";
    }
}
