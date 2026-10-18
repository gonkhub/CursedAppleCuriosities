using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>Shiv's skin icons (the Oxblood recolour was dropped, user 2.5) and his Aloha mastery skin (user 3.0).</summary>
    public static class ShivSkins
    {
        // His own palette: brown leather, black, his red shirt, skin tone.
        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.45f, 0.32f, 0.2f), new Color(0.1f, 0.1f, 0.1f), new Color(0.62f, 0.12f, 0.12f), new Color(0.75f, 0.47f, 0.33f));
        private static Sprite? _default;

        // Aloha: teal print, white jacket, khaki trousers, hibiscus pink.
        public static Sprite AlohaIcon => _aloha ??= Kit.SkinIcon(new Color(0.12f, 0.59f, 0.67f), new Color(0.93f, 0.93f, 0.9f), new Color(0.72f, 0.6f, 0.38f), new Color(0.94f, 0.31f, 0.51f));
        private static Sprite? _aloha;

        /// <summary>
        /// Aloha (skin 1, his mastery, user 3.0): a Hawaiian shirt, a white jacket and khaki trousers. The shirt is the light,
        /// nearly grey cloth of his body texture, painted with a generated print (hibiscus and leaves on teal) shaded by the
        /// cloth's own brightness; the jacket mesh's texture goes white; the trousers' browns go khaki.
        /// </summary>
        public static readonly DeadlockSkin Aloha = new DeadlockSkin
        {
            Id = "aloha1",
            Only = new[] { "shiv_body1", "shiv_jacket1", "shiv_legs1" },
            RecolorFile = (file, px, w, h) =>
                file.StartsWith("shiv_jacket1", System.StringComparison.OrdinalIgnoreCase) ? Whiten(px, 0.72f) :
                file.StartsWith("shiv_legs1", System.StringComparison.OrdinalIgnoreCase) ? Khaki(px) :
                Shirt(px, w, h),
        };

        private static readonly System.Func<byte[], bool> Khaki = DeadlockSkin.SetHue(-10f, 50f, 38f, 0.9f, 0.38f, 1.6f);

        private static readonly Color Teal = new Color(30 / 255f, 150 / 255f, 170 / 255f);
        private const float Cells = 6f; // print repeats across the texture

        private static bool Shirt(byte[] px, int w, int h)
        {
            if (w <= 0 || h <= 0) return false;
            bool changed = false;
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    int i = (row * w + col) * 4;
                    int max = Mathf.Max(px[i], Mathf.Max(px[i + 1], px[i + 2])), min = Mathf.Min(px[i], Mathf.Min(px[i + 1], px[i + 2]));
                    float v = max / 255f, s = max > 0 ? (max - min) / (float)max : 0f;
                    if (v < 0.55f || s > 0.15f) continue; // his shirt: light, nearly grey cloth
                    Color p = Print((float)col / w * Cells, (float)row / h * Cells);
                    float shade = Mathf.Clamp(v / 0.8f, 0.45f, 1.15f);
                    px[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(p.r * shade * 255f), 0, 255);
                    px[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(p.g * shade * 255f), 0, 255);
                    px[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(p.b * shade * 255f), 0, 255);
                    changed = true;
                }
            return changed;
        }

        private static bool Whiten(byte[] px, float to)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                float v = Mathf.Max(px[i], Mathf.Max(px[i + 1], px[i + 2])) / 255f;
                float nv = Mathf.Min(1f, to + v * (1f - to));
                px[i] = px[i + 1] = (byte)Mathf.RoundToInt(nv * 255f);
                px[i + 2] = (byte)Mathf.RoundToInt(nv * 248f); // a touch warm
            }
            return px.Length > 0;
        }

        private static float Hash(int i, int j, int k)
        {
            unchecked
            {
                uint x = (uint)(i * 374761393 + j * 668265263 + k * 2147483647);
                x = (x ^ (x >> 13)) * 1274126177;
                return (x ^ (x >> 16)) % 10000 / 10000f;
            }
        }

        /// <summary>The print at (u, v) in print cells: leaves under hibiscus flowers on teal, jittered one per cell.</summary>
        private static Color Print(float u, float v)
        {
            Color col = Teal;
            int ci = Mathf.FloorToInt(u), cj = Mathf.FloorToInt(v);
            for (int layer = 0; layer < 2; layer++)
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int i = ci + di, j = cj + dj;
                        float cx = i + 0.2f + 0.6f * Hash(i, j, layer * 7 + 1), cy = j + 0.2f + 0.6f * Hash(i, j, layer * 7 + 2);
                        float dx = u - cx, dy = v - cy, r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (layer == 0)
                        {
                            // a leaf: an ellipse along a random direction, with a darker midrib
                            float ang = Hash(i, j, 11) * Mathf.PI, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                            float lx = (dx * ca + dy * sa) / 0.42f, ly = (-dx * sa + dy * ca) / 0.13f;
                            if (lx * lx + ly * ly < 1f) col = Mathf.Abs(ly) < 0.12f ? new Color(25 / 255f, 95 / 255f, 45 / 255f) : new Color(40 / 255f, 150 / 255f, 70 / 255f);
                        }
                        else
                        {
                            float a = Mathf.Atan2(dy, dx) + Hash(i, j, 10) * 2f * Mathf.PI;
                            float size = 0.22f + 0.12f * Hash(i, j, 13);
                            float petal = size * (0.62f + 0.38f * Mathf.Abs(Mathf.Cos(2.5f * a)));
                            if (r >= petal) continue;
                            float pick = Hash(i, j, 17);
                            Color pc = pick < 0.45f ? new Color(0.94f, 0.31f, 0.51f) : pick < 0.8f ? new Color(0.88f, 0.18f, 0.22f) : new Color(0.98f, 0.96f, 0.92f);
                            col = pc * (0.7f + 0.3f * r / petal); // darker toward the centre, like hibiscus
                            col.a = 1f;
                            if (r < size * 0.16f) col = new Color(0.98f, 0.82f, 0.24f); // stamen
                        }
                    }
            return col;
        }
    }
}
