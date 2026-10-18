using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>Mina on the shared Deadlock model layer: her model, animation table and skins.</summary>
    public static class MinaModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = MinaData.Hero,
            DisplayName = "Mina",
            DlModel = MinaData.DlModel,
            ModelHeightScale = MinaData.ModelHeightScale,
            BaseSpeed = MinaData.BaseSpeed,
            MuzzleBone = MinaData.MuzzleBone,
            MuzzleName = MinaSurvivor.MuzzleName,
            Anims = MinaData.Anims,
            RootLocked = MinaData.Locked,
            ItemAnchors = MinaItemDisplays.Anchors,
            // Hovering on her umbrella (the synced buff): her in-air loops become the glide.
            LocoSet = b => MinaSurvivor.HoverBuff != null && b.HasBuff(MinaSurvivor.HoverBuff) ? "hover" : null,
            HideMeshes = new[] { "head_ui" }, // the menu-portrait head, not part of her in game
            // Nightshade (skin 1): her red umbrella, bows, dress trim and gun (its red trim and blood vial, user 3.0) turned violet; her
            // pale skin is left alone (minSat). Her black dress becomes a night sky, deep indigo to midnight blue (user 3.0). Id 2: the
            // recolour changed, so the cached nightshade1 textures are left behind.
            Skins = new[]
            {
                new DeadlockSkin { Id = "nightshade2", Only = new[] { "mina_umbrella", "mina_upper", "mina_lower", "mina_hair", "mina_bag", "mina_charm", "mina_gun" }, RecolorFile = Nightshade },
                // Blackmore Colors (skin 2, user 3.0, after Apollo's): Graves' Blackmore Academy uniform. Her navy coat goes burgundy, her
                // reds (umbrella, bows, bag, charm, gun trim, blood vial) forest green; her black dress stays charcoal, skin and hair untouched.
                new DeadlockSkin { Id = "blackmore1", Only = new[] { "mina_umbrella", "mina_upper", "mina_lower", "mina_bag", "mina_charm", "mina_gun" }, RecolorFile = Blackmore },
            },
        };

        private static readonly System.Func<byte[], bool> Violet = DeadlockSkin.HueShift(-25f, 20f, -75f, 0.35f);
        private static readonly System.Func<byte[], bool> Night = DeadlockSkin.NightSky();

        /// <summary>Nightshade: reds to violet everywhere it applies; the night sky only on her dress (upper and lower body textures).</summary>
        private static bool Nightshade(string file, byte[] px, int w, int h)
        {
            bool dress = file.IndexOf("mina_upper", System.StringComparison.OrdinalIgnoreCase) >= 0 || file.IndexOf("mina_lower", System.StringComparison.OrdinalIgnoreCase) >= 0;
            return Violet(px) | (dress && Night(px));
        }

        /// <summary>
        /// Blackmore Colors: Graves' palette as Apollo's skin uses it (burgundy ~120, 25, 28; forest green ~32, 42, 30). Reds to forest
        /// green only when strongly coloured (her pale skin is a faint red); the navy coat to burgundy only on her clothes (her gun's
        /// steel and her hair have a faint blue too).
        /// </summary>
        private static bool Blackmore(string file, byte[] px, int w, int h)
        {
            bool cloth = file.IndexOf("mina_upper", System.StringComparison.OrdinalIgnoreCase) >= 0 || file.IndexOf("mina_lower", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float redGate = file.IndexOf("mina_gun", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 0.3f : 0.4f; // the gun's trim is a dark, duller red
            bool changed = false;
            for (int i = 0; i < px.Length; i += 4)
            {
                Color.RGBToHSV(new Color32(px[i], px[i + 1], px[i + 2], 255), out float hue, out float sat, out float val);
                if (val <= 0f) continue;
                float deg = hue * 360f;
                float red = deg > 330f ? deg - 360f : deg;
                float wr = Mathf.Clamp01((red + 40f) / 10f) * Mathf.Clamp01((30f - red) / 10f) * Mathf.Clamp01((sat - redGate) / 0.08f);
                float wn = cloth ? Mathf.Clamp01((deg - 185f) / 10f) * Mathf.Clamp01((275f - deg) / 10f) * Mathf.Clamp01((sat - 0.25f) / 0.08f) : 0f;
                Color n;
                float wgt;
                if (wr > 0f) { n = Color.HSVToRGB(105f / 360f, Mathf.Min(1f, Mathf.Max(sat * 0.4f, 0.25f)), Mathf.Min(1f, val * 0.45f)); wgt = wr; }
                else if (wn > 0f) { n = Color.HSVToRGB(357f / 360f, Mathf.Min(1f, Mathf.Max(sat * 1.8f, 0.65f)), Mathf.Min(1f, val * 1.5f)); wgt = wn; }
                else continue;
                px[i] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i], n.r * 255f, wgt));
                px[i + 1] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i + 1], n.g * 255f, wgt));
                px[i + 2] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i + 2], n.b * 255f, wgt));
                changed = true;
            }
            return changed;
        }

        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.8f, 0.08f, 0.1f), new Color(0.1f, 0.1f, 0.14f), new Color(0.18f, 0.2f, 0.3f), new Color(0.93f, 0.85f, 0.85f));
        public static Sprite NightshadeIcon => _night ??= Kit.SkinIcon(new Color(0.5f, 0.1f, 0.8f), new Color(0.07f, 0.09f, 0.32f), new Color(0.18f, 0.2f, 0.3f), new Color(0.93f, 0.85f, 0.85f));
        // Burgundy blazer, forest green, charcoal, her pale skin.
        public static Sprite BlackmoreIcon => _blackmore ??= Kit.SkinIcon(new Color(0.47f, 0.1f, 0.11f), new Color(0.13f, 0.17f, 0.12f), new Color(0.2f, 0.19f, 0.19f), new Color(0.93f, 0.85f, 0.85f));
        private static Sprite? _default, _night, _blackmore;

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
