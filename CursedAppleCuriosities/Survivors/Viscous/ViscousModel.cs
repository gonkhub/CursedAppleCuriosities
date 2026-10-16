using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// Viscous on the shared Deadlock model layer. His gun is part of his hand (weapon_bone_R under hand_R), so no HandIK.
    /// His model carries the Goo Ball too (mesh group "inflated", renderers inflated_N), hidden except while he rolls
    /// (ViscousBallLook). His colours are in his base-colour textures (flat greens), which the loader keeps.
    /// Item displays fitted by tools/itempass (ViscousItemDisplays).
    /// </summary>
    public static class ViscousModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = ViscousData.Hero,
            DisplayName = "Viscous",
            DlModel = ViscousData.DlModel,
            ModelHeightScale = ViscousData.ModelHeightScale * ViscousData.BallHeightFix,
            BaseSpeed = ViscousData.BaseSpeed,
            MuzzleBone = ViscousData.MuzzleBone,
            MuzzleName = ViscousSurvivor.MuzzleName,
            Anims = ViscousData.Anims,
            RootLocked = ViscousData.Locked,
            // Blue Viscous (skin 1): his green goo turned blue.
            Skins = new[]
            {
                new DeadlockSkin { Id = "blue1", Recolor = DeadlockSkin.HueShift(70f, 170f, 95f) },
                // Pisscous (skin 2, user 3.0): his greens set to a bright yellow. A plain hue rotation kept their brightness and came out
                // olive; yellow has to be lighter than green to read as yellow (previewed on his textures, the cube's included).
                new DeadlockSkin { Id = "pisscous1", Recolor = DeadlockSkin.SetHue(70f, 170f, 54f, 1.1f, 0.55f, 1.25f) },
            },
            ItemAnchors = ViscousItemDisplays.Anchors,
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
