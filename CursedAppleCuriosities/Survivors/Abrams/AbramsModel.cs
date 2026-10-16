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
            // Green Abrams (skin 1): his blues (coat, skin, trousers) turned green; leather, brass and paper stay.
            Skins = new[]
            {
                new DeadlockSkin { Id = "green1", Recolor = DeadlockSkin.HueShift(180f, 275f, -95f) },
                // Crimson (skin 2, user 3.0): his blues swapped for Apollo's red, skin included; a deep crimson set straight (a hue
                // rotation landed on raspberry). His slate trousers stay grey (they're under the coat's saturation), his purples stay.
                new DeadlockSkin { Id = "crimson1", Only = new[] { "abrams_coat", "abrams_upper_body", "abrams_lower_body", "abrams_head" }, RecolorFile = Crimson },
            },
        };

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);

        private static readonly System.Func<byte[], bool> CrimsonCoat = DeadlockSkin.SetHue(185f, 250f, 356f, 1.15f, 0f, 0.82f, gate: 0.38f);
        private static readonly System.Func<byte[], bool> CrimsonAll = DeadlockSkin.SetHue(185f, 250f, 356f, 1.15f, 0f, 0.82f, gate: 0.1f);

        /// <summary>Crimson: on his trousers' texture only the saturated blues (not the slate cloth); everywhere else, every blue.</summary>
        private static bool Crimson(string file, byte[] px, int w, int h) =>
            file.IndexOf("abrams_lower_body", System.StringComparison.OrdinalIgnoreCase) >= 0 ? CrimsonCoat(px) : CrimsonAll(px);

        /// <summary>Crimson's icon: crimson coat, his brown leather and tan, red skin.</summary>
        public static Sprite CrimsonIcon => _crimson ??= CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.SkinIcon(new Color(0.6f, 0.08f, 0.1f), new Color(0.45f, 0.27f, 0.15f), new Color(0.78f, 0.68f, 0.45f), new Color(0.75f, 0.35f, 0.38f));
        private static Sprite? _crimson;
    }
}
