using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>A hero texture made ready off the main thread: GPU-ready mip chain (or, failing that, the PNG bytes).</summary>
    public class TexData
    {
        public string Key = "";   // file name + size: the same texture under two models (Victor and his reassembly) is shared
        public string Name = "";
        public int W, H;
        public TextureFormat Format;
        public bool Mips;
        public byte[]? Raw;       // full mip chain, bottom row first, as LoadRawTextureData takes it
        public byte[]? Png;       // decoder couldn't read it: LoadImage on the main thread, as before
    }

    /// <summary>
    /// Hero textures without main-thread work. Deadlock's base colours are 2K-4K PNGs: LoadImage decoded them on the main
    /// thread (the freeze on first picking a hero) into uncompressed RGBA32 (89 MB of VRAM for a 4K). Here they are decoded,
    /// mipmapped and block-compressed (DXT1, DXT5 when the alpha is used) on a background thread, once: the result is
    /// cached next to the PNG, and later launches only read it. The main thread just uploads.
    /// </summary>
    public static class HeroTextures
    {
        private const uint Magic = 0x54434143; // "CACT"
        private const int Version = 1;

        private static readonly Dictionary<string, Texture2D> Made = new Dictionary<string, Texture2D>();

        public static string KeyOf(string pngFile) => Path.GetFileName(pngFile) + ":" + new FileInfo(pngFile).Length;

        /// <summary>Background thread: the texture for a PNG file, from the cache or converted (and cached) now.</summary>
        public static TexData Prepare(string pngFile)
        {
            var fi = new FileInfo(pngFile);
            var t = new TexData { Key = KeyOf(pngFile), Name = fi.Name };
            string cache = pngFile + ".cac";
            if (ReadCache(cache, fi, t)) return t;

            byte[] png = File.ReadAllBytes(pngFile);
            byte[]? rgba = null;
            int w = 0, h = 0;
            try { rgba = Png.Decode(png, out w, out h); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] {fi.Name}: {e.Message}"); }
            if (rgba == null) { t.Png = png; return t; }

            Encode(t, rgba, w, h);
            try { WriteCache(cache, fi, t); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] couldn't cache {fi.Name}: {e.Message}"); }
            return t;
        }

        /// <summary>
        /// Background thread: a skin's recolour of a PNG (recolor edits the RGBA pixels, true if it changed any), cached
        /// like the base texture under the skin's id. Null when the recolour leaves the texture as it was: the skin uses
        /// the base texture there (remembered as an empty cache entry).
        /// </summary>
        public static TexData? PrepareSkin(string pngFile, string id, Func<byte[], bool> recolor)
        {
            var fi = new FileInfo(pngFile);
            var t = new TexData { Key = KeyOf(pngFile) + ":" + id, Name = Path.GetFileNameWithoutExtension(fi.Name) + "_" + id };
            string cache = pngFile + "." + id + ".cac";
            if (ReadCache(cache, fi, t)) return t.Raw!.Length > 0 ? t : null;

            byte[]? rgba = null;
            int w = 0, h = 0;
            try { rgba = Png.Decode(File.ReadAllBytes(pngFile), out w, out h); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] {fi.Name} ({id}): {e.Message}"); }
            if (rgba == null) return null;
            bool changed = recolor(rgba);
            if (changed) Encode(t, rgba, w, h);
            else t.Raw = Array.Empty<byte>();
            try { WriteCache(cache, fi, t); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] couldn't cache {fi.Name} ({id}): {e.Message}"); }
            return changed ? t : null;
        }

        /// <summary>Background thread: a standalone cache file (map textures, written by CursedAppleMapConvert; null if unreadable).</summary>
        public static TexData? ReadStandalone(string cacheFile)
        {
            var t = new TexData { Key = cacheFile, Name = Path.GetFileNameWithoutExtension(cacheFile) };
            if (!File.Exists(cacheFile)) return null;
            try
            {
                using var br = new BinaryReader(File.OpenRead(cacheFile));
                if (br.ReadUInt32() != Magic || br.ReadInt32() != Version) return null;
                br.ReadInt64(); br.ReadInt64(); // no source file
                t.W = br.ReadInt32(); t.H = br.ReadInt32(); t.Format = (TextureFormat)br.ReadInt32(); t.Mips = br.ReadBoolean();
                int n = br.ReadInt32();
                t.Raw = br.ReadBytes(n);
                return t.Raw.Length == n ? t : null;
            }
            catch { return null; }
        }

        /// <summary>Mipmapped and block-compressed (DXT1, DXT5 when the alpha is used), or RGBA32 when the size isn't a multiple of 4.</summary>
        private static void Encode(TexData t, byte[] rgba, int w, int h)
        {
            t.W = w; t.H = h; t.Mips = true;
            if (w % 4 == 0 && h % 4 == 0)
            {
                bool alpha = false;
                for (int i = 3; i < rgba.Length && !alpha; i += 4) alpha = rgba[i] != 255;
                t.Format = alpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
                t.Raw = Bc.EncodeChain(rgba, w, h, alpha);
            }
            else
            {
                t.Format = TextureFormat.RGBA32;
                t.Raw = MipChain(rgba, w, h);
            }
        }

        /// <summary>Main thread: the Unity texture (made once per key).</summary>
        public static Texture2D Make(TexData t)
        {
            if (Made.TryGetValue(t.Key, out Texture2D tex) && tex != null) return tex;
            if (t.Raw != null)
            {
                tex = new Texture2D(t.W, t.H, t.Format, t.Mips) { name = t.Name };
                tex.LoadRawTextureData(t.Raw);
                tex.Apply(false, true); // no CPU copy kept
                t.Raw = null;
            }
            else
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = t.Name };
                if (t.Png != null) GlbLoader.LoadPng(tex, t.Png);
                t.Png = null;
            }
            Made[t.Key] = tex;
            return tex;
        }

        public static bool IsMade(string key) => Made.TryGetValue(key, out Texture2D tex) && tex != null;

        // ---- cache file: header, then the raw mip chain ----

        private static bool ReadCache(string file, FileInfo png, TexData t)
        {
            if (!File.Exists(file)) return false;
            try
            {
                using var br = new BinaryReader(File.OpenRead(file));
                if (br.ReadUInt32() != Magic || br.ReadInt32() != Version || br.ReadInt64() != png.Length || br.ReadInt64() != png.LastWriteTimeUtc.Ticks) return false;
                t.W = br.ReadInt32();
                t.H = br.ReadInt32();
                t.Format = (TextureFormat)br.ReadInt32();
                t.Mips = br.ReadBoolean();
                int n = br.ReadInt32();
                t.Raw = br.ReadBytes(n);
                return t.Raw.Length == n;
            }
            catch { return false; }
        }

        private static void WriteCache(string file, FileInfo png, TexData t)
        {
            string tmp = file + ".part";
            using (var bw = new BinaryWriter(File.Create(tmp)))
            {
                bw.Write(Magic); bw.Write(Version); bw.Write(png.Length); bw.Write(png.LastWriteTimeUtc.Ticks);
                bw.Write(t.W); bw.Write(t.H); bw.Write((int)t.Format); bw.Write(t.Mips);
                bw.Write(t.Raw!.Length); bw.Write(t.Raw);
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        // ---- mipmaps ----

        /// <summary>Each level down to 1x1 (as Unity counts them), 2x2 box filtered.</summary>
        internal static List<(byte[] Px, int W, int H)> Levels(byte[] rgba, int w, int h)
        {
            var l = new List<(byte[], int, int)> { (rgba, w, h) };
            while (w > 1 || h > 1)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                var d = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                {
                    int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                        int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, e = (y1 * w + x1) * 4, o = (y * nw + x) * 4;
                        for (int k = 0; k < 4; k++) d[o + k] = (byte)((rgba[a + k] + rgba[b + k] + rgba[c + k] + rgba[e + k] + 2) >> 2);
                    }
                }
                rgba = d; w = nw; h = nh;
                l.Add((d, w, h));
            }
            return l;
        }

        private static byte[] MipChain(byte[] rgba, int w, int h)
        {
            var levels = Levels(rgba, w, h);
            int n = 0;
            foreach (var lv in levels) n += lv.Px.Length;
            var o = new byte[n];
            int at = 0;
            foreach (var lv in levels) { Buffer.BlockCopy(lv.Px, 0, o, at, lv.Px.Length); at += lv.Px.Length; }
            return o;
        }

        // ---- PNG (8/16-bit grey, RGB, palette, grey+alpha, RGBA; not interlaced) ----

        private static class Png
        {
            /// <summary>RGBA32 pixels, bottom row first (Unity's order). Null for a PNG it doesn't read.</summary>
            public static byte[]? Decode(byte[] f, out int w, out int h)
            {
                w = h = 0;
                if (f.Length < 33 || f[0] != 0x89 || f[1] != (byte)'P') return null;
                int depth = 0, type = 0;
                byte[]? plte = null, trns = null;
                var idat = new MemoryStream();
                int p = 8;
                while (p + 8 <= f.Length)
                {
                    int len = BE(f, p);
                    string kind = System.Text.Encoding.ASCII.GetString(f, p + 4, 4);
                    int d = p + 8;
                    if (kind == "IHDR")
                    {
                        w = BE(f, d); h = BE(f, d + 4); depth = f[d + 8]; type = f[d + 9];
                        if (f[d + 12] != 0) return null; // interlaced
                    }
                    else if (kind == "PLTE") { plte = new byte[len]; Buffer.BlockCopy(f, d, plte, 0, len); }
                    else if (kind == "tRNS") { trns = new byte[len]; Buffer.BlockCopy(f, d, trns, 0, len); }
                    else if (kind == "IDAT") idat.Write(f, d, len);
                    else if (kind == "IEND") break;
                    p = d + len + 4;
                }
                int ch = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                if (ch == 0 || w <= 0 || h <= 0 || (depth != 8 && depth != 16) || (type == 3 && (plte == null || depth != 8))) return null;

                int bpp = ch * depth / 8, stride = w * bpp;
                var raw = new byte[(stride + 1) * h];
                idat.Position = 2; // zlib header
                using (var z = new DeflateStream(idat, CompressionMode.Decompress))
                {
                    int got = 0, r;
                    while (got < raw.Length && (r = z.Read(raw, got, raw.Length - got)) > 0) got += r;
                    if (got < raw.Length) return null;
                }

                var px = new byte[w * h * 4];
                var prev = new byte[stride];
                var cur = new byte[stride];
                int step = depth / 8; // 16-bit: the high byte of each sample
                for (int y = 0; y < h; y++)
                {
                    int src = y * (stride + 1);
                    Unfilter(raw[src], raw, src + 1, cur, prev, stride, bpp);
                    int o = (h - 1 - y) * w * 4; // PNG rows run top-down
                    for (int x = 0; x < w; x++, o += 4)
                    {
                        int s = x * bpp;
                        switch (type)
                        {
                            case 0: px[o] = px[o + 1] = px[o + 2] = cur[s]; px[o + 3] = 255; break;
                            case 2: px[o] = cur[s]; px[o + 1] = cur[s + step]; px[o + 2] = cur[s + 2 * step]; px[o + 3] = 255; break;
                            case 3:
                                int i = cur[s];
                                if (i * 3 + 2 < plte!.Length) { px[o] = plte[i * 3]; px[o + 1] = plte[i * 3 + 1]; px[o + 2] = plte[i * 3 + 2]; }
                                px[o + 3] = trns != null && i < trns.Length ? trns[i] : (byte)255;
                                break;
                            case 4: px[o] = px[o + 1] = px[o + 2] = cur[s]; px[o + 3] = cur[s + step]; break;
                            default: px[o] = cur[s]; px[o + 1] = cur[s + step]; px[o + 2] = cur[s + 2 * step]; px[o + 3] = cur[s + 3 * step]; break;
                        }
                    }
                    (prev, cur) = (cur, prev);
                }
                return px;
            }

            private static void Unfilter(byte filter, byte[] raw, int at, byte[] cur, byte[] prev, int stride, int bpp)
            {
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int v = raw[at + i];
                    switch (filter)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4:
                            int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
                            v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                            break;
                    }
                    cur[i] = (byte)v;
                }
            }

            private static int BE(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        }

        // ---- DXT1 / DXT5 ----

        private static class Bc
        {
            public static byte[] EncodeChain(byte[] rgba, int w, int h, bool alpha)
            {
                int blockBytes = alpha ? 16 : 8;
                var levels = Levels(rgba, w, h);
                int n = 0;
                foreach (var lv in levels) n += ((lv.W + 3) / 4) * ((lv.H + 3) / 4) * blockBytes;
                var o = new byte[n];
                int at = 0;
                var block = new byte[64];
                foreach (var (px, lw, lh) in levels)
                    for (int by = 0; by < lh; by += 4)
                        for (int bx = 0; bx < lw; bx += 4)
                        {
                            for (int y = 0; y < 4; y++)
                                for (int x = 0; x < 4; x++)
                                    Buffer.BlockCopy(px, (Math.Min(by + y, lh - 1) * lw + Math.Min(bx + x, lw - 1)) * 4, block, (y * 4 + x) * 4, 4);
                            if (alpha) { Alpha(block, o, at); at += 8; }
                            Color(block, o, at);
                            at += 8;
                        }
                return o;
            }

            /// <summary>Endpoints along the block's main colour axis (a few power iterations), then the nearest of 4 colours.</summary>
            private static void Color(byte[] b, byte[] o, int at)
            {
                float mr = 0, mg = 0, mb = 0;
                for (int i = 0; i < 16; i++) { mr += b[i * 4]; mg += b[i * 4 + 1]; mb += b[i * 4 + 2]; }
                mr /= 16f; mg /= 16f; mb /= 16f;
                float rr = 0, rg = 0, rb = 0, gg = 0, gb = 0, bb = 0;
                for (int i = 0; i < 16; i++)
                {
                    float r = b[i * 4] - mr, g = b[i * 4 + 1] - mg, bl = b[i * 4 + 2] - mb;
                    rr += r * r; rg += r * g; rb += r * bl; gg += g * g; gb += g * bl; bb += bl * bl;
                }
                float vr = 1f, vg = 1f, vb = 1f;
                for (int k = 0; k < 4; k++)
                {
                    float nr = rr * vr + rg * vg + rb * vb, ng = rg * vr + gg * vg + gb * vb, nb = rb * vr + gb * vg + bb * vb;
                    float m = Math.Max(Math.Abs(nr), Math.Max(Math.Abs(ng), Math.Abs(nb)));
                    if (m < 1e-6f) break;
                    vr = nr / m; vg = ng / m; vb = nb / m;
                }
                int lo = 0, hi = 0;
                float dlo = float.MaxValue, dhi = float.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    float d = b[i * 4] * vr + b[i * 4 + 1] * vg + b[i * 4 + 2] * vb;
                    if (d < dlo) { dlo = d; lo = i; }
                    if (d > dhi) { dhi = d; hi = i; }
                }
                ushort c0 = To565(b, hi * 4), c1 = To565(b, lo * 4);
                if (c0 < c1) (c0, c1) = (c1, c0);
                o[at] = (byte)c0; o[at + 1] = (byte)(c0 >> 8); o[at + 2] = (byte)c1; o[at + 3] = (byte)(c1 >> 8);
                uint bits = 0;
                if (c0 != c1)
                {
                    var pal = new int[12];
                    From565(c0, pal, 0); From565(c1, pal, 3);
                    for (int k = 0; k < 3; k++) { pal[6 + k] = (2 * pal[k] + pal[3 + k]) / 3; pal[9 + k] = (pal[k] + 2 * pal[3 + k]) / 3; }
                    for (int i = 0; i < 16; i++)
                    {
                        int best = 0, bestD = int.MaxValue;
                        for (int c = 0; c < 4; c++)
                        {
                            int dr = b[i * 4] - pal[c * 3], dg = b[i * 4 + 1] - pal[c * 3 + 1], db = b[i * 4 + 2] - pal[c * 3 + 2];
                            int d = dr * dr + dg * dg + db * db;
                            if (d < bestD) { bestD = d; best = c; }
                        }
                        bits |= (uint)best << (i * 2);
                    }
                }
                o[at + 4] = (byte)bits; o[at + 5] = (byte)(bits >> 8); o[at + 6] = (byte)(bits >> 16); o[at + 7] = (byte)(bits >> 24);
            }

            /// <summary>Min/max alpha endpoints, 8-value mode, nearest index.</summary>
            private static void Alpha(byte[] b, byte[] o, int at)
            {
                int a0 = 0, a1 = 255;
                for (int i = 0; i < 16; i++) { a0 = Math.Max(a0, b[i * 4 + 3]); a1 = Math.Min(a1, b[i * 4 + 3]); }
                o[at] = (byte)a0; o[at + 1] = (byte)a1;
                ulong bits = 0;
                if (a0 != a1)
                {
                    var pal = new int[8];
                    pal[0] = a0; pal[1] = a1;
                    for (int k = 1; k <= 6; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                    for (int i = 0; i < 16; i++)
                    {
                        int best = 0, bestD = int.MaxValue;
                        for (int c = 0; c < 8; c++) { int d = Math.Abs(b[i * 4 + 3] - pal[c]); if (d < bestD) { bestD = d; best = c; } }
                        bits |= (ulong)best << (i * 3);
                    }
                }
                for (int k = 0; k < 6; k++) o[at + 2 + k] = (byte)(bits >> (k * 8));
            }

            private static ushort To565(byte[] b, int i) => (ushort)(((b[i] * 31 + 127) / 255 << 11) | ((b[i + 1] * 63 + 127) / 255 << 5) | ((b[i + 2] * 31 + 127) / 255));

            private static void From565(ushort c, int[] pal, int at)
            {
                int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
                pal[at] = (r << 3) | (r >> 2); pal[at + 1] = (g << 2) | (g >> 4); pal[at + 2] = (b << 3) | (b >> 2);
            }
        }
    }
}
