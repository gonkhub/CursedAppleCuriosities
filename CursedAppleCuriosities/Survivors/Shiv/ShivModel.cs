using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>Shiv on the shared Deadlock model layer: his model and animation table.</summary>
    public static class ShivModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = ShivData.Hero,
            DisplayName = "Shiv",
            DlModel = ShivData.DlModel,
            ModelHeightScale = ShivData.ModelHeightScale,
            BaseSpeed = ShivData.BaseSpeed,
            MuzzleBone = ShivData.MuzzleBone,
            MuzzleName = ShivSurvivor.MuzzleName,
            Anims = ShivData.Anims,
            RootLocked = ShivData.Locked,
            ItemAnchors = ShivItemDisplays.Anchors,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
