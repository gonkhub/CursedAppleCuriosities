using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Celeste on the shared Deadlock model layer: her model and animation table. Her blades are thrown from the right
    /// hand (no held gun to keep in place). Item displays fitted by tools/itempass (CelesteItemDisplays).
    /// </summary>
    public static class CelesteModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = CelesteData.Hero,
            DisplayName = "Celeste",
            DlModel = CelesteData.DlModel,
            ModelHeightScale = CelesteData.ModelHeightScale,
            BaseSpeed = CelesteData.BaseSpeed,
            MuzzleBone = CelesteData.MuzzleBone,
            MuzzleName = CelesteSurvivor.MuzzleName,
            Anims = CelesteData.Anims,
            RootLocked = CelesteData.Locked,
            // Mint Celeste (skin 1): her pink hair and skirt turned mint and teal; the leather jacket stays.
            // Spirit Guardian (skin 2): TSelky's model, from their Deadlock mod (CelesteData.GuardianKey).
            Skins = new[]
            {
                new DeadlockSkin { Id = "mint1", Recolor = DeadlockSkin.HueShift(270f, 325f, -125f, 0.08f) },
                new DeadlockSkin { Id = "guardian", Model = CelesteData.GuardianKey, ModelName = CelesteData.GuardianName },
            },
            ItemAnchors = CelesteItemDisplays.Anchors,
            // Her horn's glow shell (a blank white material in the converted model): a soft self-lit purple (user).
            MaterialGlows = new System.Collections.Generic.Dictionary<string, Color> { ["unicorn_glow"] = new Color(0.72f, 0.5f, 1f) },
            // AnimGraph2 jump and air-dash clips span the whole time in the air (Victor's rule): they loop here and end on
            // landing, or hand over to the in-air loop once the rise is over.
            LoopRule = c =>
                !(c.State.StartsWith("dash_air_") || c.State == "jump" || c.State == "air_jump" || c.State == "wall_jump") ? null :
                c.Grounded && c.GroundedFor > 0.05f ? c.Loco :
                c.StateTime > (c.State.StartsWith("dash_air_") ? CelesteData.AirDashPose : CelesteData.JumpPose) ? "in_air" : null,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
