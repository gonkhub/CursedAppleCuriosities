using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace CursedAppleMapConvert
{
    /// <summary>
    /// The map's models, exported from Deadlock once and kept in a compact form every district reads (the expensive part of a
    /// conversion is Source2Viewer's export, not cutting a district out). Shared by all of a map's districts; valid while its
    /// fingerprint (map build, store format, the list of districts) holds, wiped otherwise. Textures are already converted
    /// into the shared texture folder: materials carry their cached texture names.
    /// </summary>
    internal sealed class ModelStore
    {
        public const int Version = 2; // 2: deflated (a quarter of the size)
        private const uint Magic = 0x4C444D43; // "CMDL"

        private readonly string _dir, _fingerprint;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string IndexFile => Path.Combine(_dir, "index.txt");

        public ModelStore(string dir, string fingerprint)
        {
            _dir = dir;
            _fingerprint = fingerprint;
            Directory.CreateDirectory(dir);
            string[] lines = File.Exists(IndexFile) ? File.ReadAllLines(IndexFile) : Array.Empty<string>();
            if (lines.Length == 0 || lines[0] != fingerprint)
            {
                if (lines.Length > 0) Program.Log($"model store out of date ({lines[0]} -> {fingerprint}): starting it over");
                foreach (string f in Directory.GetFiles(dir, "*.cmdl")) File.Delete(f);
                File.WriteAllText(IndexFile, fingerprint + "\n");
                return;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                string[] f = lines[i].Split('\t');
                if (f.Length == 2 && File.Exists(Path.Combine(dir, f[1]))) _files[f[0]] = f[1];
            }
        }

        public int Count => _files.Count;
        public bool Has(string model) => _files.ContainsKey(model);

        public void Save(string model, Model mm)
        {
            string file = $"{(uint)model.ToLowerInvariant().GetHashCode():x8}_{Path.GetFileNameWithoutExtension(model)}.cmdl";
            string path = Path.Combine(_dir, file);
            using (FileStream fs = File.Create(path + ".part"))
            {
                var head = new BinaryWriter(fs);
                head.Write(Magic); head.Write(Version);
                head.Flush();
                using var bw = new BinaryWriter(new BufferedStream(new DeflateStream(fs, CompressionLevel.Fastest, true), 1 << 18));
                bw.Write(mm.Materials.Count);
                foreach (MaterialInfo m in mm.Materials) WriteMaterial(bw, m);
                bw.Write(mm.Prims.Count);
                var seen = new Dictionary<object, int>(ReferenceEqualityComparer.Instance); // a merged model's draws share their vertex arrays: written once
                foreach (Prim p in mm.Prims)
                {
                    bw.Write(p.Material); bw.Write(p.Node);
                    Arr(bw, p.Pos, seen); Arr(bw, p.Normal, seen); Arr(bw, p.Uv, seen); Arr(bw, p.Lightmap, seen); Arr(bw, p.Blend, seen);
                    Arr(bw, p.Color, seen); Arr(bw, p.Tris, seen);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".part", path);
            _files[model] = file;
            File.AppendAllText(IndexFile, model + "\t" + file + "\n");
        }

        /// <summary>A stored model, its draws sharing their vertex arrays as they did in the glTF (Emit transforms a shared one once).</summary>
        public Model Load(string model)
        {
            using FileStream fs = File.OpenRead(Path.Combine(_dir, _files[model]));
            var head = new BinaryReader(fs);
            if (head.ReadUInt32() != Magic || head.ReadInt32() != Version) throw new InvalidDataException("not a model of this store: " + model);
            using var br = new BinaryReader(new BufferedStream(new DeflateStream(fs, CompressionMode.Decompress, true), 1 << 18));
            var mm = new Model();
            int mats = br.ReadInt32();
            for (int i = 0; i < mats; i++) mm.Materials.Add(ReadMaterial(br));
            int prims = br.ReadInt32();
            var read = new List<Array>();
            for (int i = 0; i < prims; i++)
            {
                var p = new Prim { Material = br.ReadInt32(), Node = br.ReadString() };
                p.Pos = Arr<V3>(br, read) ?? Array.Empty<V3>();
                p.Normal = Arr<V3>(br, read); p.Uv = Arr<V2>(br, read); p.Lightmap = Arr<V2>(br, read); p.Blend = Arr<float>(br, read); p.Color = Arr<C32>(br, read);
                p.Tris = Arr<int>(br, read) ?? Array.Empty<int>();
                mm.Prims.Add(p);
            }
            return mm;
        }

        private static void WriteMaterial(BinaryWriter bw, MaterialInfo m)
        {
            bw.Write(m.Name); bw.Write(m.TextureCac ?? ""); bw.Write(m.Shader);
            bw.Write(m.Cutout); bw.Write(m.Translucent); bw.Write(m.Glass); bw.Write(m.DoubleSided); bw.Write(m.VertexPaint); bw.Write(m.SelfIllum); bw.Write(m.Skip);
            bw.Write(m.Cutoff); W(bw, m.Tint);
            bw.Write(m.Color2 ?? ""); bw.Write(m.Reveal2 ?? ""); bw.Write(m.RevealOffset2); bw.Write(m.RevealSoftness2); bw.Write(m.RevealInvert2); W(bw, m.Tint2);
            bw.Write(m.Overlay ?? ""); bw.Write(m.OverlayTiling); bw.Write(m.OverlayDark); bw.Write(m.OverlayBright);
            bw.Write(m.OverlayStrength1); bw.Write(m.OverlayStrength2); bw.Write(m.OverlayMask1); bw.Write(m.OverlayMask2);
        }

        private static MaterialInfo ReadMaterial(BinaryReader br)
        {
            string? N(string s) => s.Length > 0 ? s : null;
            var m = new MaterialInfo { Name = br.ReadString(), TextureCac = N(br.ReadString()), Shader = br.ReadString() };
            m.Cutout = br.ReadBoolean(); m.Translucent = br.ReadBoolean(); m.Glass = br.ReadBoolean(); m.DoubleSided = br.ReadBoolean();
            m.VertexPaint = br.ReadBoolean(); m.SelfIllum = br.ReadBoolean(); m.Skip = br.ReadBoolean();
            m.Cutoff = br.ReadSingle(); m.Tint = C(br);
            m.Color2 = N(br.ReadString()); m.Reveal2 = N(br.ReadString()); m.RevealOffset2 = br.ReadSingle(); m.RevealSoftness2 = br.ReadSingle();
            m.RevealInvert2 = br.ReadBoolean(); m.Tint2 = C(br);
            m.Overlay = N(br.ReadString()); m.OverlayTiling = br.ReadSingle(); m.OverlayDark = br.ReadSingle(); m.OverlayBright = br.ReadSingle();
            m.OverlayStrength1 = br.ReadSingle(); m.OverlayStrength2 = br.ReadSingle(); m.OverlayMask1 = br.ReadByte(); m.OverlayMask2 = br.ReadByte();
            return m;
        }

        private static void W(BinaryWriter bw, C32 c) { bw.Write(c.R); bw.Write(c.G); bw.Write(c.B); bw.Write(c.A); }
        private static C32 C(BinaryReader br) => new C32(br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte());

        /// <summary>
        /// An array: -1 = null, -2 then an index = one already written in this model, else its length then its raw bytes
        /// (blittable structs laid out like Unity's, copied through a pinned handle).
        /// </summary>
        private static void Arr<T>(BinaryWriter bw, T[]? a, Dictionary<object, int> seen) where T : struct
        {
            if (a == null) { bw.Write(-1); return; }
            if (seen.TryGetValue(a, out int index)) { bw.Write(-2); bw.Write(index); return; }
            seen[a] = seen.Count;
            bw.Write(a.Length);
            int size = Marshal.SizeOf<T>() * a.Length;
            var bytes = new byte[size];
            GCHandle h = GCHandle.Alloc(a, GCHandleType.Pinned);
            try { Marshal.Copy(h.AddrOfPinnedObject(), bytes, 0, size); }
            finally { h.Free(); }
            bw.Write(bytes);
        }

        private static T[]? Arr<T>(BinaryReader br, List<Array> read) where T : struct
        {
            int n = br.ReadInt32();
            if (n == -1) return null;
            if (n == -2) return (T[])read[br.ReadInt32()];
            var a = new T[n];
            int size = Marshal.SizeOf<T>() * n;
            byte[] bytes = br.ReadBytes(size);
            if (bytes.Length != size) throw new EndOfStreamException();
            GCHandle h = GCHandle.Alloc(a, GCHandleType.Pinned);
            try { Marshal.Copy(bytes, 0, h.AddrOfPinnedObject(), size); }
            finally { h.Free(); }
            read.Add(a);
            return a;
        }

        /// <summary>Reference identity (net472 has no ReferenceEqualityComparer).</summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
