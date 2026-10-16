using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Seven on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs): his model, animation table and the
    /// Seven-only animation rules. His gun sits in his right hand's bone chain (weapon_hand_R under hand_R), so it
    /// follows the hand without Abrams' HandIK.
    /// </summary>
    public static class SevenModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = SevenData.Hero,
            DisplayName = "Seven",
            DlModel = SevenData.DlModel,
            ModelHeightScale = SevenData.ModelHeightScale,
            BaseSpeed = SevenData.BaseSpeed,
            MuzzleBone = SevenData.MuzzleBone,
            MuzzleName = SevenSurvivor.MuzzleName,
            Anims = SevenData.Anims,
            RootLocked = SevenData.Locked,
            ItemAnchors = SevenItemDisplays.Anchors,
            // Overcharged (skin 1, his mastery, user 3.0): his oranges turned to a pale electric blue (hue +185, 40% toward white);
            // his own blue accents, leather and greys stay.
            Skins = new[]
            {
                new DeadlockSkin { Id = "overcharged1", Recolor = DeadlockSkin.HueShift(5f, 50f, 185f, 0.12f, 0.4f) },
                // Bunny Seven (skin 2): funnymonkeyman's model, from their Deadlock mod (SevenData.BunnyKey).
                new DeadlockSkin { Id = "bunny", Model = SevenData.BunnyKey, ModelName = SevenData.BunnyName },
            },
            // His Storm Cloud skeleton (Deadlock's blue_illum): orange and glowing (user). Shown only while channeling: SevenStormLook.
            MaterialGlows = new System.Collections.Generic.Dictionary<string, Color> { ["blue_illum"] = SevenSurvivor.Orange },
            LoopRule = c =>
                // Storm Cloud's channel loop ends with storm_end; if that never came (interrupted), back to moving.
                c.State == "storm" && c.StateTime > SevenData.StormChannel + 0.5f ? c.Loco : null,
        };

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
