using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>Shiv's skins: the default, and Oxblood (his brown leather jacket and trousers dyed a dried-blood red; skin, hair and gun untouched).</summary>
    public static class ShivSkins
    {
        public static readonly DeadlockSkin[] All =
        {
            new DeadlockSkin { Id = "oxblood1", Recolor = DeadlockSkin.HueShift(15f, 45f, -32f), Only = new[] { "shiv_jacket1", "shiv_legs1" } },
        };

        // Icons in his own palette: brown leather, black, his red shirt, skin tone.
        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.45f, 0.32f, 0.2f), new Color(0.1f, 0.1f, 0.1f), new Color(0.62f, 0.12f, 0.12f), new Color(0.75f, 0.47f, 0.33f));
        public static Sprite OxbloodIcon => _oxblood ??= Kit.SkinIcon(new Color(0.4f, 0.16f, 0.18f), new Color(0.1f, 0.1f, 0.1f), new Color(0.62f, 0.12f, 0.12f), new Color(0.75f, 0.47f, 0.33f));
        private static Sprite? _default, _oxblood;
    }
}
