using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    [Flags]
    internal enum MapMatFlags : byte
    {
        None = 0,
        Cutout = 1,
        Translucent = 2,   // glass and other see-through surfaces: drawn with the glass shader, still solid
        DoubleSided = 4,
        NoCollide = 8,
        SelfIllum = 16,
    }

    internal class MapMaterial
    {
        public string Name = "";
        public string Texture = "";   // file name in the section's tex folder ("" = none)
        public MapMatFlags Flags;
        public float Cutoff = 0.5f;
        public Color32 Tint = new Color32(255, 255, 255, 255); // layer 1's material tint (the vertex colour has the object's and the paint)
        // Layer 2 (Deadlock's environment_blend): its colour and reveal mask (file names, "" = none), the threshold's shape, its tint.
        public string Texture2 = "", Reveal2 = "";
        public float RevealOffset2, RevealSoftness2 = 0.125f;
        public bool RevealInvert2;
        public Color32 Tint2 = new Color32(255, 255, 255, 255);
        // World overlay: grime projected in world space (tiling in metres), modulating both layers.
        public string Overlay = "";
        public float OverlayTiling = 13f, OverlayDark = 1f, OverlayBright = 1f, OverlayStrength1 = 1f, OverlayStrength2 = 1f;
        public byte OverlayMask1 = 1, OverlayMask2 = 1;  // 0 = unmasked, 1 = by the layer's tint mask (texture alpha), 2 = inverted
    }

    /// <summary>One material's triangles in one chunk (Unity space, metres, relative to the section origin).</summary>
    internal class MapSub
    {
        public int Material;
        public Vector3[] Pos = Array.Empty<Vector3>();
        public Vector3[] Normal = Array.Empty<Vector3>();
        public Vector2[] Uv = Array.Empty<Vector2>();
        public Vector2[] Lightmap = Array.Empty<Vector2>(); // Deadlock's baked lightmap UVs (0-1); (-1, -1) = none
        public Color32[] Color = Array.Empty<Color32>();     // tint (rgb); alpha = layer 2's blend weight on blended materials
        public int[] Tris = Array.Empty<int>();
    }

    internal class MapChunk
    {
        public bool Backdrop;            // render only, no collision
        public Vector3 Min, Max;
        public readonly List<MapSub> Subs = new List<MapSub>();
    }

    /// <summary>
    /// A converted section (written by CursedAppleMapConvert.exe into the player's cache, never shipped). Read as a header
    /// (materials, origin), then chunk by chunk, so only one chunk's data is in memory at a time.
    /// </summary>
    internal sealed class MapCache : IDisposable
    {
        public const uint Magic = 0x4D434143; // "CACM"
        public const int Version = 3;

        public string Fingerprint = "";
        public Vector3 OriginUnits;       // Source units: the section's origin (Unity 0,0,0)
        public string LightmapFile = "";  // raw BC6H irradiance lightmap in the tex folder ("" = none)
        public int LightmapSize;
        public readonly List<MapMaterial> Materials = new List<MapMaterial>();
        public int ChunkCount;
        private BinaryReader? _br;

        /// <summary>Source units -> Unity metres relative to the origin (mirrors one axis).</summary>
        public static Vector3 ToUnity(Vector3 s, Vector3 originUnits) =>
            new Vector3(-(s.y - originUnits.y), s.z - originUnits.z, s.x - originUnits.x) * 0.0254f;

        /// <summary>Just the fingerprint (null if the file isn't a readable cache of this version).</summary>
        public static string? ReadFingerprint(string file)
        {
            try
            {
                using var br = new BinaryReader(File.OpenRead(file));
                return br.ReadUInt32() == Magic && br.ReadInt32() == Version ? br.ReadString() : null;
            }
            catch { return null; }
        }

        /// <summary>Opens the file and reads its header; then <see cref="Chunks"/> streams the chunks.</summary>
        public static MapCache Open(string file)
        {
            var c = new MapCache { _br = new BinaryReader(new BufferedStream(File.OpenRead(file), 1 << 20)) };
            BinaryReader br = c._br;
            if (br.ReadUInt32() != Magic || br.ReadInt32() != Version) { c.Dispose(); throw new InvalidDataException("not a map cache of this version"); }
            c.Fingerprint = br.ReadString();
            c.OriginUnits = V(br);
            c.LightmapFile = br.ReadString();
            c.LightmapSize = br.ReadInt32();
            int mats = br.ReadInt32();
            for (int i = 0; i < mats; i++)
                c.Materials.Add(new MapMaterial
                {
                    Name = br.ReadString(), Texture = br.ReadString(), Flags = (MapMatFlags)br.ReadByte(), Cutoff = br.ReadSingle(),
                    Tint = new Color32(br.ReadByte(), br.ReadByte(), br.ReadByte(), 255),
                    Texture2 = br.ReadString(), Reveal2 = br.ReadString(), RevealOffset2 = br.ReadSingle(), RevealSoftness2 = br.ReadSingle(),
                    RevealInvert2 = br.ReadBoolean(), Tint2 = new Color32(br.ReadByte(), br.ReadByte(), br.ReadByte(), 255),
                    Overlay = br.ReadString(), OverlayTiling = br.ReadSingle(), OverlayDark = br.ReadSingle(), OverlayBright = br.ReadSingle(),
                    OverlayStrength1 = br.ReadSingle(), OverlayStrength2 = br.ReadSingle(), OverlayMask1 = br.ReadByte(), OverlayMask2 = br.ReadByte(),
                });
            c.ChunkCount = br.ReadInt32();
            return c;
        }

        /// <summary>Each chunk in turn (read once: the stream moves on).</summary>
        public IEnumerable<MapChunk> Chunks()
        {
            BinaryReader br = _br ?? throw new ObjectDisposedException(nameof(MapCache));
            for (int i = 0; i < ChunkCount; i++)
            {
                var ch = new MapChunk { Backdrop = br.ReadBoolean(), Min = V(br), Max = V(br) };
                int subs = br.ReadInt32();
                for (int k = 0; k < subs; k++)
                    ch.Subs.Add(new MapSub
                    {
                        Material = br.ReadInt32(),
                        Pos = Arr<Vector3>(br), Normal = Arr<Vector3>(br), Uv = Arr<Vector2>(br), Lightmap = Arr<Vector2>(br), Color = Arr<Color32>(br), Tris = Arr<int>(br),
                    });
                yield return ch;
            }
        }

        /// <summary>After the chunks: Deadlock's player clips (character-only collision: ramps over stairs, movement walls).</summary>
        public (Vector3[] Pos, int[] Tris) Clips()
        {
            BinaryReader br = _br ?? throw new ObjectDisposedException(nameof(MapCache));
            return (Arr<Vector3>(br), Arr<int>(br));
        }

        public void Dispose() { _br?.Dispose(); _br = null; }

        private static Vector3 V(BinaryReader br) => new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        private static T[] Arr<T>(BinaryReader br) where T : struct
        {
            int n = br.ReadInt32();
            var a = new T[n];
            Span<byte> bytes = MemoryMarshal.AsBytes(a.AsSpan());
            int got = 0;
            while (got < bytes.Length)
            {
                int r = br.Read(bytes.Slice(got));
                if (r <= 0) throw new EndOfStreamException();
                got += r;
            }
            return a;
        }
    }
}
