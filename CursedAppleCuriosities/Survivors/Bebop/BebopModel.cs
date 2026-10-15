using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>Bebop on the shared Deadlock model layer: his model and animation table.</summary>
    public static class BebopModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = BebopData.Hero,
            DisplayName = "Bebop",
            DlModel = BebopData.DlModel,
            ModelHeightScale = BebopData.ModelHeightScale,
            BaseSpeed = BebopData.BaseSpeed,
            MuzzleBone = BebopData.MuzzleBone,
            MuzzleName = BebopSurvivor.MuzzleName,
            Anims = BebopData.Anims,
            RootLocked = BebopData.Locked,
            // Skin 1, Gold Plated (his mastery, user 3.0): his orange-red paint turns rich gold; dark metal, greys and the green
            // lights stay (his deepest reds too). Skin 2, Chrome (unlocked by its own challenge, user 3.0): the paint turns a cool
            // silver, kept darker than white so his panel details still read.
            Skins = new[]
            {
                new DeadlockSkin { Id = "goldplated1", Recolor = DeadlockSkin.SetHue(-40f, 60f, 44f, 1.3f, 0.6f, 1.25f) },
                new DeadlockSkin { Id = "chrome1", Recolor = DeadlockSkin.SetHue(-40f, 60f, 210f, 0.12f, 0.04f, 1.3f) },
            },
        };

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
