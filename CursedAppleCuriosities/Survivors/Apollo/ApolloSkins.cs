using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>Apollo's Blackmore Colors mastery skin (user 3.0): a Blackmore Academy uniform after Graves' jacket.</summary>
    public static class ApolloSkins
    {
        // His default fencing colours (user 3.0: not the stand-in's Commando icon): white jacket, crimson panels, white trousers, his skin tone.
        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.92f, 0.9f, 0.87f), new Color(0.68f, 0.1f, 0.14f), new Color(0.8f, 0.79f, 0.77f), new Color(0.85f, 0.65f, 0.52f));
        private static Sprite? _default;

        // Sapphire (user 3.0): Abrams' blue in place of his reds. White jacket, blue panels, white trousers, blue skin.
        public static Sprite SapphireIcon => _sapphire ??= Kit.SkinIcon(new Color(0.92f, 0.9f, 0.87f), new Color(0.1f, 0.25f, 0.7f), new Color(0.8f, 0.79f, 0.77f), new Color(0.35f, 0.45f, 0.85f));
        private static Sprite? _sapphire;
        private static readonly System.Func<byte[], bool> RedToBlueCloth = DeadlockSkin.HueShift(-20f, 12f, -140f, 0.45f);
        private static readonly System.Func<byte[], bool> RedToBlueSkin = DeadlockSkin.HueShift(-25f, 25f, -140f, 0.2f);

        /// <summary>Sapphire: on his clothes only the strong reds (his pale skin peeks through there); his head and gem, every red.</summary>
        public static bool Sapphire(string file, byte[] px, int w, int h) =>
            file.StartsWith("fencer_upper", System.StringComparison.OrdinalIgnoreCase) || file.StartsWith("fencer_lower", System.StringComparison.OrdinalIgnoreCase)
                ? RedToBlueCloth(px) : RedToBlueSkin(px);

        // Burgundy blazer, forest-green panels, charcoal trousers, his skin tone.
        public static Sprite BlackmoreIcon => _blackmore ??= Kit.SkinIcon(new Color(0.47f, 0.1f, 0.11f), new Color(0.13f, 0.17f, 0.12f), new Color(0.2f, 0.19f, 0.19f), new Color(0.85f, 0.65f, 0.52f));
        private static Sprite? _blackmore;

        // Graves' palette (Deadlock necro_upper_color, mid-tones): burgundy ~ (120, 25, 28), forest green ~ (32, 42, 30).
        private static readonly System.Func<byte[], bool> CrimsonToGreenA = DeadlockSkin.SetHue(330f, 360f, 105f, 0.4f, 0.25f, 0.45f);
        private static readonly System.Func<byte[], bool> CrimsonToGreenB = DeadlockSkin.SetHue(-5f, 12f, 105f, 0.4f, 0.25f, 0.45f);
        private static readonly System.Func<byte[], bool> WhiteToBurgundy = DeadlockSkin.DyeWhites(0.3f, 0.35f, 357f, 0.8f, 0.5f);
        private static readonly System.Func<byte[], bool> CrimsonToBurgundyA = DeadlockSkin.SetHue(330f, 360f, 357f, 1f, 0.7f, 0.75f);
        private static readonly System.Func<byte[], bool> CrimsonToBurgundyB = DeadlockSkin.SetHue(-5f, 12f, 357f, 1f, 0.7f, 0.75f);
        private static readonly System.Func<byte[], bool> WhiteToCharcoal = DeadlockSkin.DarkenGreys(0.3f, 0.28f);

        /// <summary>Upper: crimson to green first (so the new burgundy isn't caught), then the whites to burgundy. Lower: crimson to burgundy, whites to charcoal.</summary>
        public static bool Blackmore(string file, byte[] px, int w, int h)
        {
            if (file.StartsWith("fencer_upper", System.StringComparison.OrdinalIgnoreCase))
                return CrimsonToGreenA(px) | CrimsonToGreenB(px) | WhiteToBurgundy(px);
            return CrimsonToBurgundyA(px) | CrimsonToBurgundyB(px) | WhiteToCharcoal(px);
        }
    }
}
