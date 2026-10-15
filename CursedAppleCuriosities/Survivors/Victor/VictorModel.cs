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
            // Green Monster (skin 1, his mastery, user 3.0): green skin (upper body, head, legs) and purple trousers (the greys of
            // his lower texture; its brass stays). Hair and gun keep their colours.
            Skins = new[] { new DeadlockSkin { Id = "greenmonster1", Only = new[] { "frank_upper", "frank_head", "frank_basebody_lower", "frank_lower" }, RecolorFile = GreenMonster } },
            // His Deadlock jump clips (3.1 s / 2.9 s) and air dashes (1.33 s) span the whole time in the air, so as one-shots
            // they ran on after he landed (playtest 2026-10-12). They loop here and end by this rule: on landing, back to
            // moving; still airborne once the rise is over, the in-air loop.
            LoopRule = c =>
                !(c.State.StartsWith("dash_air_") || c.State == "jump" || c.State == "air_jump" || c.State == "wall_jump") ? null :
                c.Grounded && c.GroundedFor > 0.05f ? c.Loco :
                c.StateTime > (c.State.StartsWith("dash_air_") ? VictorData.AirDashPose : VictorData.JumpPose) ? "in_air" : null,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);

        private static readonly System.Func<byte[], bool> GreenSkin = DeadlockSkin.SetHue(15f, 45f, 105f, 1.6f, 0.35f, 0.95f);
        private static readonly System.Func<byte[], bool> PurpleTrousers = DeadlockSkin.TintGreys(0.25f, 280f, 0.6f, 1.3f);

        private static bool GreenMonster(string file, byte[] px, int w, int h) =>
            file.StartsWith("frank_lower", System.StringComparison.OrdinalIgnoreCase) ? PurpleTrousers(px) : GreenSkin(px);
    }
}
