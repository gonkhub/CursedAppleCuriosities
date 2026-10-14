using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>Paradox on the shared Deadlock model layer: her model, animation table and skins.</summary>
    public static class ParadoxModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = ParadoxData.Hero,
            DisplayName = "Paradox",
            DlModel = ParadoxData.DlModel,
            ModelHeightScale = ParadoxData.ModelHeightScale,
            BaseSpeed = ParadoxData.BaseSpeed,
            MuzzleBone = ParadoxData.MuzzleBone,
            MuzzleName = ParadoxSurvivor.MuzzleName,
            Anims = ParadoxData.Anims,
            RootLocked = ParadoxData.Locked,
            ItemAnchors = ParadoxItemDisplays.Anchors,
            HideMeshes = new[] { "chrono_gun_on_hip" }, // the holstered copy of her rifle (she always has it out here)
            // Teal (skin 1): her maroon coat and trim turned deep teal; the tan leather, her skin and the brass stay (a narrow
            // range: her atlas holds all of them).
            Skins = new[] { new DeadlockSkin { Id = "teal1", Recolor = DeadlockSkin.HueShift(-30f, -5f, -150f), Only = new[] { "chrono_v2_color" } } },
        };

        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.45f, 0.1f, 0.15f), new Color(0.12f, 0.12f, 0.13f), new Color(0.75f, 0.5f, 0.3f), new Color(0.9f, 0.87f, 0.85f));
        public static Sprite TealIcon => _teal ??= Kit.SkinIcon(new Color(0.08f, 0.32f, 0.38f), new Color(0.12f, 0.12f, 0.13f), new Color(0.75f, 0.5f, 0.3f), new Color(0.9f, 0.87f, 0.85f));
        private static Sprite? _default, _teal;

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
