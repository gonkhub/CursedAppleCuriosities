using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs): his model, animation table and the
    /// Lash-only animation rules. Entity states play animation states through <see cref="Play"/>.
    /// </summary>
    public static class LashModelSwap
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = LashData.Hero,
            DisplayName = "Lash",
            DlModel = LashData.DlModel,
            HideMeshes = new[] { "sparkles" },
            ModelHeightScale = LashData.ModelHeightScale,
            BaseSpeed = LashData.BaseSpeed,
            MuzzleBone = LashData.MuzzleBone,
            MuzzleName = LashSurvivor.MuzzleName,
            Anims = LashData.Anims,
            RootLocked = LashData.Locked,
            // Death Slam holds its lift pose through the channel and the hold until the throw.
            HoldSeconds = id => id.StartsWith("ds_") ? LashData.DeathSlamChannel + LashData.DeathSlamPull + LashData.DeathSlamHang + 1f : 3f,
            LoopRule = c =>
                c.State == "grapple_air" && c.Grounded && c.StateTime > 0.2f ? "grapple_land" :
                c.State == "gs_dive" && c.GroundedFor > 0.3f ? c.Loco : null,
            ItemAnchors = LashItemDisplays.Anchors,
        };

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
