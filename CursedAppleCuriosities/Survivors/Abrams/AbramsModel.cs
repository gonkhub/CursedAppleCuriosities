using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Abrams
{
    /// <summary>
    /// Abrams on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs): his model, animation table and the
    /// Abrams-only animation rules. Entity states play animation states through <see cref="Play"/>.
    /// </summary>
    public static class AbramsModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = AbramsData.Hero,
            DisplayName = "Abrams",
            DlModel = AbramsData.DlModel,
            ModelHeightScale = AbramsData.ModelHeightScale,
            BaseSpeed = AbramsData.BaseSpeed,
            MuzzleBone = AbramsData.MuzzleBone,
            MuzzleName = AbramsSurvivor.MuzzleName,
            // His shotgun hangs off root_motion; the clips pose it in his right hand, and it follows the hand through the aim bend.
            HandIK = ("weapon", "hand_R"),
            Anims = AbramsData.Anims,
            RootLocked = AbramsData.Locked,
            HoldSeconds = id => id == "melee_start" ? 1f : 3f, // the punch follows the wind-up
            LoopRule = c =>
                // Charging on the ground or through the air (Deadlock: BullCharge_Ground / BullCharge_InAir).
                c.State == "charge" && !c.Grounded && c.AirTime > 0.15f ? "charge_air" :
                c.State == "charge_air" && c.Grounded ? "charge" :
                // The crash's brace pose ends with the impact clip; if that never came, back to moving.
                c.State == "leap_brace" && c.GroundedFor > 0.3f ? c.Loco : null,
            // Siphon Life: his drain locomotion while it lasts.
            LocoSet = body => AbramsSurvivor.SiphonBuff != null && body.HasBuff(AbramsSurvivor.SiphonBuff) ? "siphon" : null,
            ItemAnchors = AbramsItemDisplays.Anchors,
        };

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
