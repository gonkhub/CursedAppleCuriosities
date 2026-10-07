using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;

namespace CursedAppleCuriosities.Artifacts
{
    public class CuriosityShop : ArtifactBase
    {
        public override string ArtifactName            => "Artifact of the Curio";
        public override string ArtifactLangTokenName   => "CURIOSITYSHOP";
        public override string ArtifactDescription     =>
            "Only items from the Curiosity Shop are available during the run.";

        protected override Color EnabledIconColor  => new Color(1f, 0.75f, 0.15f);
        protected override Color DisabledIconColor => new Color(0.35f, 0.35f, 0.35f);

        public override void Init(ConfigFile config)
        {
            CreateLang();
            CreateArtifact();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.Run.BuildDropTable += Run_BuildDropTable;
        }

        private void Run_BuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self)
        {
            orig(self);

            if (ArtifactDef == null) return;
            if (RunArtifactManager.instance == null) return;
            if (!RunArtifactManager.instance.IsArtifactEnabled(ArtifactDef)) return;

            FilterItems(self.availableTier1DropList);
            FilterItems(self.availableTier2DropList);
            FilterItems(self.availableTier3DropList);
            FilterItems(self.availableLunarItemDropList);
            FilterItems(self.availableVoidTier1DropList);
            FilterItems(self.availableVoidTier2DropList);
            FilterItems(self.availableVoidTier3DropList);
            FilterEquipment(self.availableEquipmentDropList);
            FilterEquipment(self.availableLunarEquipmentDropList);
        }

        private static void FilterItems(List<PickupIndex> list)
        {
            if (list == null) return;
            list.RemoveAll(pickup =>
            {
                var def = PickupCatalog.GetPickupDef(pickup);
                if (def == null || def.itemIndex == ItemIndex.None) return true;
                var itemDef = ItemCatalog.GetItemDef(def.itemIndex);
                return !CursedAppleCuriositiesPlugin.ModItemDefs.Contains(itemDef);
            });
        }

        private static void FilterEquipment(List<PickupIndex> list)
        {
            if (list == null) return;
            list.RemoveAll(pickup =>
            {
                var def = PickupCatalog.GetPickupDef(pickup);
                if (def == null || def.equipmentIndex == EquipmentIndex.None) return true;
                var eqDef = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                return !CursedAppleCuriositiesPlugin.ModEquipmentDefs.Contains(eqDef);
            });
        }
    }
}
