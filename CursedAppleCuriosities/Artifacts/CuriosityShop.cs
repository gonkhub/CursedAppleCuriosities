using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>Artifact of the Curio: only this mod's items and equipment drop (chests, bosses, printers, Command's picks).</summary>
    public class CuriosityShop : ArtifactBase
    {
        public override string ArtifactName          => "Artifact of the Curio";
        public override string ArtifactLangTokenName => "CURIOSITYSHOP";
        public override string ArtifactDescription   => "Only Cursed Apple items and equipment appear during the run.";

        protected override Color EnabledIconColor  => new Color(1f, 0.75f, 0.15f);
        protected override Color DisabledIconColor => new Color(0.35f, 0.35f, 0.35f);

        public static ArtifactDef? Def;

        public static bool Active =>
            Def != null && RunArtifactManager.instance != null && RunArtifactManager.instance.IsArtifactEnabled(Def);

        public override void Init(ConfigFile config)
        {
            CreateLang();
            CreateArtifact();
            Def = ArtifactDef;
            Hooks();
        }

        public override void Hooks() => On.RoR2.Run.BuildDropTable += Run_BuildDropTable;

        private static void Run_BuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self)
        {
            orig(self);
            if (!Active) return;
            // The availability masks too, so Command's picks, printers and anything else asking "is this available" agree.
            foreach (ItemIndex i in new List<ItemIndex>(ItemsIn(self.availableItems)))
                if (!CursedAppleCuriositiesPlugin.ModItemDefs.Contains(ItemCatalog.GetItemDef(i))) self.availableItems.Remove(i);
            foreach (EquipmentIndex e in new List<EquipmentIndex>(EquipmentIn(self.availableEquipment)))
                if (!CursedAppleCuriositiesPlugin.ModEquipmentDefs.Contains(EquipmentCatalog.GetEquipmentDef(e))) self.availableEquipment.Remove(e);
            foreach (List<PickupIndex> list in new[] { self.availableTier1DropList, self.availableTier2DropList, self.availableTier3DropList,
                self.availableLunarItemDropList, self.availableVoidTier1DropList, self.availableVoidTier2DropList, self.availableVoidTier3DropList,
                self.availableBossDropList, self.availableEquipmentDropList, self.availableLunarEquipmentDropList })
                list?.RemoveAll(p => !IsModPickup(p));
        }

        private static IEnumerable<ItemIndex> ItemsIn(ItemMask mask)
        {
            for (ItemIndex i = 0; (int)i < ItemCatalog.itemCount; i++) if (mask.Contains(i)) yield return i;
        }

        private static IEnumerable<EquipmentIndex> EquipmentIn(EquipmentMask mask)
        {
            for (EquipmentIndex e = 0; (int)e < EquipmentCatalog.equipmentCount; e++) if (mask.Contains(e)) yield return e;
        }

        public static bool IsModPickup(PickupIndex pickup)
        {
            PickupDef? def = PickupCatalog.GetPickupDef(pickup);
            if (def == null) return false;
            if (def.itemIndex != ItemIndex.None) return CursedAppleCuriositiesPlugin.ModItemDefs.Contains(ItemCatalog.GetItemDef(def.itemIndex));
            if (def.equipmentIndex != EquipmentIndex.None) return CursedAppleCuriositiesPlugin.ModEquipmentDefs.Contains(EquipmentCatalog.GetEquipmentDef(def.equipmentIndex));
            return true; // coins and the like
        }
    }
}
