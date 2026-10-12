using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor on the shared Deadlock model layer: his model and animation table. His gun hangs off root_motion
    /// (weaponPivot/weapon) like Abrams' shotgun; the clips pose it in his hands and HandIK keeps it on hand_R through
    /// the aim bend. Item displays fitted by tools/itempass (VictorItemDisplays).
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
            ItemAnchors = VictorItemDisplays.Anchors,
            // His Deadlock jump clips (3.1 s / 2.9 s) and air dashes (1.33 s) span the whole time in the air, so as one-shots
            // they ran on after he landed (playtest 2026-10-12). They loop here and end by this rule: on landing, back to
            // moving; still airborne once the rise is over, the in-air loop.
            LoopRule = c =>
                !(c.State.StartsWith("dash_air_") || c.State == "jump" || c.State == "air_jump" || c.State == "wall_jump") ? null :
                c.Grounded && c.GroundedFor > 0.05f ? c.Loco :
                c.StateTime > (c.State.StartsWith("dash_air_") ? VictorData.AirDashPose : VictorData.JumpPose) ? "in_air" : null,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
