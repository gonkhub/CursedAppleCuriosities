using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>Shiv's skin icon (his default look; the Oxblood recolour was dropped, user 2.5).</summary>
    public static class ShivSkins
    {
        // His own palette: brown leather, black, his red shirt, skin tone.
        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.45f, 0.32f, 0.2f), new Color(0.1f, 0.1f, 0.1f), new Color(0.62f, 0.12f, 0.12f), new Color(0.75f, 0.47f, 0.33f));
        private static Sprite? _default;
    }
}
