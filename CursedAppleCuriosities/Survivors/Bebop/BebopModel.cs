using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>Bebop on the shared Deadlock model layer: his model and animation table.</summary>
    public static class BebopModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = BebopData.Hero,
            DisplayName = "Bebop",
            DlModel = BebopData.DlModel,
            ModelHeightScale = BebopData.ModelHeightScale,
            BaseSpeed = BebopData.BaseSpeed,
            MuzzleBone = BebopData.MuzzleBone,
            MuzzleName = BebopSurvivor.MuzzleName,
            Anims = BebopData.Anims,
            RootLocked = BebopData.Locked,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
