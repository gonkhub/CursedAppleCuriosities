using BepInEx;
using BepInEx.Logging;
using CursedAppleCuriosities.Artifacts;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Abrams;
using CursedAppleCuriosities.Survivors.GreyTalon;
using CursedAppleCuriosities.Survivors.Lash;
using CursedAppleCuriosities.Survivors.Seven;
using CursedAppleCuriosities.Survivors.Vyper;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

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

            InitItems();
            InitEquipments();
            InitArtifacts();
            InitSurvivors();

            // Populate the sets used by CuriosityShop to filter drop tables.
            foreach (var item in _items)
                if (item.ItemDef != null) ModItemDefs.Add(item.ItemDef);
            foreach (var eq in _equipments)
                if (eq.EquipmentDef != null) ModEquipmentDefs.Add(eq.EquipmentDef);

            DurationExtender.HookSources(); // last: wraps every other hook on hits and damage

            try { ModSettings.TryRegister(Config, ItemBase.LoadEmbeddedIcon("icon_curiositycatalog.png")); }
            catch (Exception e) { Logger.LogError($"  ! Settings page failed: {e}"); }

            Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded.");
        }

        /// <summary>
        /// Deadlock heroes as survivors. Their models, animations, sounds and icons are read from the player's
        /// own Deadlock install (read-only, never while Deadlock runs, never shipped) on a background thread.
        /// </summary>
        private void InitSurvivors()
        {
            DeadlockInstall.Bind(Config);
            DeadlockSounds.Ensure(gameObject);
            try
            {
                LashSurvivor.Init(Config, gameObject);
                Logger?.LogInfo("  + [Survivor] Lash");
            }
            catch (Exception e)
            {
                Logger?.LogError($"  ! [Survivor] Lash failed to init: {e}");
            }
            try
            {
                AbramsSurvivor.Init(Config, gameObject);
                Logger?.LogInfo("  + [Survivor] Abrams");
            }
            catch (Exception e)
            {
                Logger?.LogError($"  ! [Survivor] Abrams failed to init: {e}");
            }
            try
            {
                SevenSurvivor.Init(Config, gameObject);
                Logger?.LogInfo("  + [Survivor] Seven");
            }
            catch (Exception e)
            {
                Logger?.LogError($"  ! [Survivor] Seven failed to init: {e}");
            }
            try
            {
                VyperSurvivor.Init(Config, gameObject);
                Logger?.LogInfo("  + [Survivor] Vyper");
            }
            catch (Exception e)
            {
                Logger?.LogError($"  ! [Survivor] Vyper failed to init: {e}");
            }
            try
            {
                GreyTalonSurvivor.Init(Config, gameObject);
                Logger?.LogInfo("  + [Survivor] Grey Talon");
            }
            catch (Exception e)
            {
                Logger?.LogError($"  ! [Survivor] Grey Talon failed to init: {e}");
            }
            foreach (var eq in _equipments) // registered before the reader starts so it copies them
            {
                try { eq.RegisterSounds(); }
                catch (Exception e) { Logger?.LogError($"  ! [Equipment] {eq.EquipmentName} sounds failed: {e}"); }
            }
            DeadlockInstall.Start();
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
        public const string PLUGIN_VERSION = "1.3.0";
    }
}
