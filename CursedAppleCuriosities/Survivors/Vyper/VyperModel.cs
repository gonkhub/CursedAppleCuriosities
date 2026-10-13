using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Vyper on the shared Deadlock model layer: her model and animation table. Her gun sits in the right hand's bone
    /// chain (weapon_hand_R under hand_R), so it follows the hand without HandIK. She has a tail (tail_0..7).
    /// </summary>
    public static class VyperModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = VyperData.Hero,
            DisplayName = "Vyper",
            DlModel = VyperData.DlModel,
            ModelHeightScale = VyperData.ModelHeightScale,
            BaseSpeed = VyperData.BaseSpeed,
            MuzzleBone = VyperData.MuzzleBone,
            MuzzleName = VyperSurvivor.MuzzleName,
            Anims = VyperData.Anims,
            RootLocked = VyperData.Locked,
            // Coral Vyper (skin 1): her green scales turned coral red; the gold armour and white belly stay.
            Skins = new[] { new DeadlockSkin { Id = "coral1", Recolor = DeadlockSkin.HueShift(82f, 150f, -80f) } },
            ItemAnchors = VyperItemDisplays.Anchors,
            // Her gun's material (borrowed from Deadlock's "tengu") is white x g_vColorTint1: dark gunmetal.
            MaterialTints = new System.Collections.Generic.Dictionary<string, Color> { ["tengu_gun"] = new Color(0.317647f, 0.290196f, 0.290196f) },
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
