using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs): his model, animation table and the
    /// Lash-only animation rules. Entity states play animation states through <see cref="Play"/>.
    /// </summary>
    public static class LashModelSwap
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = LashData.Hero,
            DisplayName = "Lash",
            DlModel = LashData.DlModel,
            HideMeshes = new[] { "sparkles" },
            ModelHeightScale = LashData.ModelHeightScale,
            BaseSpeed = LashData.BaseSpeed,
            MuzzleBone = LashData.MuzzleBone,
            MuzzleName = LashSurvivor.MuzzleName,
            Anims = LashData.Anims,
            RootLocked = LashData.Locked,
            // Death Slam holds its lift pose through the channel and the hold until the throw.
            HoldSeconds = id => id.StartsWith("ds_") ? LashData.DeathSlamChannel + LashData.DeathSlamPull + LashData.DeathSlamHang + 1f : 3f,
            LoopRule = c =>
                c.State == "grapple_air" && c.Grounded && c.StateTime > 0.2f ? "grapple_land" :
                c.State == "gs_dive" && c.GroundedFor > 0.3f ? c.Loco : null,
            ItemAnchors = LashItemDisplays.Anchors,
            // Gilded (skin 1, his mastery, user 3.0): his bright blue cloth (gear texture) turns gold, and his hair does too:
            // the dark pixels of his body texture, except his face (eyes, nose, mouth and beard keep their colours).
            Skins = new[] { new DeadlockSkin { Id = "gilded1", Only = new[] { "lash_gear", "lash_body" }, RecolorFile = Gilded } },
        };

        private static readonly System.Func<byte[], bool> GildCloth = DeadlockSkin.HueShift(185f, 222f, -167f, 0.3f, 0.1f);

        private static bool Gilded(string file, byte[] px, int w, int h)
        {
            if (file.IndexOf("lash_gear", System.StringComparison.OrdinalIgnoreCase) >= 0) return GildCloth(px);
            if (file.IndexOf("lash_body", System.StringComparison.OrdinalIgnoreCase) < 0 || w <= 0 || h <= 0) return false;
            // The face's box in the texture (u right, v down from the top), found on his body texture 2026-10-15.
            const float U0 = 0.13f, U1 = 0.45f, V0 = 0.25f, V1 = 0.58f, Cut = 0.45f;
            bool changed = false;
            for (int row = 0; row < h; row++)
            {
                float v = 1f - (row + 0.5f) / h; // rows are bottom first
                for (int col = 0; col < w; col++)
                {
                    float u = (col + 0.5f) / w;
                    if (u >= U0 && u <= U1 && v >= V0 && v <= V1) continue;
                    int i = (row * w + col) * 4;
                    float val = Mathf.Max(px[i], Mathf.Max(px[i + 1], px[i + 2])) / 255f;
                    float k = Mathf.Clamp01((Cut - val) / 0.1f);
                    if (k <= 0f) continue;
                    Color gold = Color.HSVToRGB(43f / 360f, 0.75f, Mathf.Min(1f, 0.35f + val * 1.6f));
                    px[i] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i], gold.r * 255f, k));
                    px[i + 1] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i + 1], gold.g * 255f, k));
                    px[i + 2] = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i + 2], gold.b * 255f, k));
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
