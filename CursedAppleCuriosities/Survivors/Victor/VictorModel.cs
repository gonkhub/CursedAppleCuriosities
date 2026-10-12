using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor on the shared Deadlock model layer: his model and animation table. His gun hangs off root_motion
    /// (weaponPivot/weapon) like Abrams' shotgun; the clips pose it in his hands and HandIK keeps it on hand_R through
    /// the aim bend. No item displays fitted yet: items use the automatic anchors until tools/itempass runs on him.
    /// </summary>
    public static class VictorModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = VictorData.Hero,
            DisplayName = "Victor",
            DlModel = VictorData.DlModel,
            ModelHeightScale = VictorData.ModelHeightScale,
            BaseSpeed = VictorData.BaseSpeed,
            MuzzleBone = VictorData.MuzzleBone,
            MuzzleName = VictorSurvivor.MuzzleName,
            HandIK = ("weapon", "hand_R"),
            Anims = VictorData.Anims,
            RootLocked = VictorData.Locked,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
