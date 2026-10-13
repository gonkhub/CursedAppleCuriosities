using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleMapConvert
{
    /// <summary>A Deadlock material as the converter exported it (glTF material + the vmat's extras).</summary>
    internal class MaterialInfo
    {
        public string Name = "";
        public string? TexturePng;     // absolute path of the exported base colour PNG
        public string Shader = "";
        public bool Cutout, Translucent, Glass, DoubleSided, VertexPaint, SelfIllum, Skip;
        public float Cutoff = 0.5f;
        public C32 Tint = C32.White;
    }

    /// <summary>One primitive (a draw call) of a map model, in Source units and axes (z up).</summary>
    internal class Prim
    {
        public int Material;
        public string Node = "";       // the glTF node's name (physics layers are named by their collision group)
        public V3[] Pos = null!;
        public V3[]? Normal;
        public V2[]? Uv;
        public C32[]? Color;
        public int[] Tris = null!;
    }

    internal class Model
    {
        public readonly List<MaterialInfo> Materials = new List<MaterialInfo>();
        public readonly List<Prim> Prims = new List<Prim>(); // draw-call order
    }

    /// <summary>
    /// Reads a GLB written by Source2Viewer-CLI for a map model. Positions come back in Source units and axes:
    /// glTF (x, y, z) metres = Source (y, z, x) * 0.0254.
    /// </summary>
    internal static class Glb
    {
        public static Model Read(string file)
        {
            byte[] b = File.ReadAllBytes(file);
            if (BitConverter.ToUInt32(b, 0) != 0x46546C67) throw new InvalidDataException("not a GLB: " + file);
            int jsonLen = BitConverter.ToInt32(b, 12);
            var j = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(b, 20, jsonLen));
            int bin = 20 + jsonLen + 8;
            string dir = Path.GetDirectoryName(file);
            var model = new Model();

            var images = MiniJson.Arr(j, "images");
            var textures = MiniJson.Arr(j, "textures");
            foreach (object m in MiniJson.Arr(j, "materials") ?? new List<object>())
                model.Materials.Add(Material(m, images, textures, dir));

            var nodes = MiniJson.Arr(j, "nodes") ?? new List<object>();
            var meshes = MiniJson.Arr(j, "meshes") ?? new List<object>();
            var parent = new int[nodes.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
                if (MiniJson.Arr(nodes[i], "children") is List<object> kids)
                    foreach (object k in kids) parent[(int)(double)k] = i;
            var shared = new Dictionary<(int, int, int), Array>(); // draws of a merged model share one vertex buffer: decode it once
            for (int i = 0; i < nodes.Count; i++)
            {
                int? mesh = MiniJson.Int(nodes[i], "mesh");
                if (mesh == null) continue;
                M4 world = M4.Identity();
                for (int n = i; n >= 0; n = parent[n]) world = Local(nodes[n]) * world;
                foreach (object prim in MiniJson.Arr(meshes[mesh.Value], "primitives") ?? new List<object>())
                {
                    if ((MiniJson.Int(prim, "mode") ?? 4) != 4 || !MiniJson.Has(prim, "indices")) continue;
                    Prim rp = ReadPrim(j, b, bin, prim, world, i, shared);
                    rp.Node = MiniJson.Str(nodes[i], "name") ?? "";
                    model.Prims.Add(rp);
                }
            }
            return model;
        }

        private static Prim ReadPrim(Dictionary<string, object> j, byte[] b, int bin, object prim, M4 world, int node, Dictionary<(int, int, int), Array> shared)
        {
            T[] Shared<T>(int accessor, int kind, Func<T[]> make)
            {
                if (!shared.TryGetValue((accessor, node, kind), out Array a)) shared[(accessor, node, kind)] = a = make();
                return (T[])a;
            }
            var attrs = MiniJson.Obj(prim, "attributes")!;
            var p = new Prim { Material = MiniJson.Int(prim, "material") ?? -1 };
            float[] idx = Floats(j, b, bin, MiniJson.Int(prim, "indices")!.Value, out _);
            p.Tris = new int[idx.Length / 3 * 3];
            for (int k = 0; k < p.Tris.Length; k++) p.Tris[k] = (int)idx[k];

            int posAcc = MiniJson.Int(attrs, "POSITION")!.Value;
            p.Pos = Shared(posAcc, 0, () =>
            {
                float[] pos = Floats(j, b, bin, posAcc, out _);
                var o = new V3[pos.Length / 3];
                const float toUnits = 1f / 0.0254f;
                for (int k = 0; k < o.Length; k++)
                {
                    V3 g = world.Point(pos[k * 3], pos[k * 3 + 1], pos[k * 3 + 2]);
                    o[k] = new V3(g.Z, g.X, g.Y) * toUnits;
                }
                return o;
            });
            if (MiniJson.Int(attrs, "NORMAL") is int ni)
                p.Normal = Shared(ni, 1, () =>
                {
                    float[] nv = Floats(j, b, bin, ni, out _);
                    var o = new V3[nv.Length / 3];
                    for (int k = 0; k < o.Length; k++)
                    {
                        V3 g = world.Dir(nv[k * 3], nv[k * 3 + 1], nv[k * 3 + 2]);
                        o[k] = new V3(g.Z, g.X, g.Y).Normalized;
                    }
                    return o;
                });
            if (MiniJson.Int(attrs, "TEXCOORD_0") is int ti)
                p.Uv = Shared(ti, 2, () =>
                {
                    float[] uv = Floats(j, b, bin, ti, out _);
                    var o = new V2[uv.Length / 2];
                    for (int k = 0; k < o.Length; k++) o[k] = new V2(uv[k * 2], 1f - uv[k * 2 + 1]); // glTF UV origin is top-left
                    return o;
                });
            if (MiniJson.Int(attrs, "COLOR_0") is int ci)
                p.Color = Shared(ci, 3, () =>
                {
                    float[] c = Floats(j, b, bin, ci, out int width);
                    var o = new C32[c.Length / width];
                    for (int k = 0; k < o.Length; k++)
                        o[k] = C32.FromFloats(c[k * width], c[k * width + 1], c[k * width + 2], width > 3 ? c[k * width + 3] : 1f);
                    return o;
                });
            return p;
        }

        private static M4 Local(object node)
        {
            if (MiniJson.Arr(node, "matrix") is List<object> m && m.Count == 16)
            {
                var c = new float[16];
                for (int i = 0; i < 16; i++) c[i] = MiniJson.F(m[i]);
                return M4.FromColumnMajor(c);
            }
            V3 t = new V3(0, 0, 0), s = new V3(1, 1, 1);
            float qx = 0, qy = 0, qz = 0, qw = 1;
            if (MiniJson.Arr(node, "translation") is List<object> tl) t = new V3(MiniJson.F(tl[0]), MiniJson.F(tl[1]), MiniJson.F(tl[2]));
            if (MiniJson.Arr(node, "scale") is List<object> sl) s = new V3(MiniJson.F(sl[0]), MiniJson.F(sl[1]), MiniJson.F(sl[2]));
            if (MiniJson.Arr(node, "rotation") is List<object> rl) { qx = MiniJson.F(rl[0]); qy = MiniJson.F(rl[1]); qz = MiniJson.F(rl[2]); qw = MiniJson.F(rl[3]); }
            return M4.Trs(t, qx, qy, qz, qw, s);
        }

        private static MaterialInfo Material(object m, List<object>? images, List<object>? textures, string dir)
        {
            var info = new MaterialInfo { Name = MiniJson.Str(m, "name") ?? "mat" };
            var pbr = MiniJson.Obj(m, "pbrMetallicRoughness");
            int? tex = MiniJson.Int(MiniJson.Obj(pbr, "baseColorTexture"), "index");
            if (tex != null && textures != null && images != null && tex.Value < textures.Count)
            {
                int img = MiniJson.Int(textures[tex.Value], "source") ?? -1;
                string? uri = img >= 0 && img < images.Count ? MiniJson.Str(images[img], "uri") : null;
                if (uri != null) info.TexturePng = Path.Combine(dir, Uri.UnescapeDataString(uri));
            }
            if (MiniJson.Arr(pbr, "baseColorFactor") is List<object> f && f.Count >= 3)
                info.Tint = C32.FromFloats(MiniJson.F(f[0]), MiniJson.F(f[1]), MiniJson.F(f[2]));
            string alpha = MiniJson.Str(m, "alphaMode") ?? "OPAQUE";
            info.Cutout = alpha == "MASK";
            info.Translucent = alpha == "BLEND";
            if (m is Dictionary<string, object> md && md.TryGetValue("alphaCutoff", out object co) && co is double cutoff) info.Cutoff = (float)cutoff;
            info.DoubleSided = MiniJson.Bool(m, "doubleSided");
            var vmat = MiniJson.Obj(MiniJson.Obj(m, "extras"), "vmat");
            info.Shader = MiniJson.Str(vmat, "ShaderName") ?? "";
            var ints = MiniJson.Obj(vmat, "IntParams");
            bool Flag(string k) => ints != null && ints.TryGetValue(k, out object v) && v is double d && d != 0;
            info.Cutout |= Flag("F_ALPHA_TEST");
            info.Translucent |= Flag("F_TRANSLUCENT");
            info.Glass = Flag("F_GLASS");
            info.DoubleSided |= Flag("F_RENDER_BACKFACES");
            info.VertexPaint = Flag("F_PAINT_VERTEX_COLORS") || Flag("F_VERTEX_COLOR");
            info.SelfIllum = Flag("F_SELF_ILLUM");
            info.Skip = info.Shader.StartsWith("cosmic_veil") || info.Shader.StartsWith("black") || info.Shader.Contains("sky");
            var vec = MiniJson.Obj(vmat, "VectorParams");
            if (vec != null && (vec.TryGetValue("g_vColorTint", out object ct) || vec.TryGetValue("g_vColorTint1", out ct)) && ct is List<object> c && c.Count >= 3)
                info.Tint = info.Tint * C32.FromFloats(MiniJson.F(c[0]), MiniJson.F(c[1]), MiniJson.F(c[2]));
            return info;
        }

        /// <summary>An accessor as floats (normalized integers scaled to 0-1); width = its component count.</summary>
        private static float[] Floats(Dictionary<string, object> j, byte[] glb, int bin, int accessor, out int width)
        {
            object a = MiniJson.Arr(j, "accessors")![accessor];
            int count = MiniJson.Int(a, "count")!.Value;
            int type = MiniJson.Int(a, "componentType")!.Value;
            bool normalized = MiniJson.Bool(a, "normalized");
            width = (MiniJson.Str(a, "type") ?? "SCALAR") switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
            object view = MiniJson.Arr(j, "bufferViews")![MiniJson.Int(a, "bufferView")!.Value];
            int size = type == 5126 || type == 5125 ? 4 : type == 5123 || type == 5122 ? 2 : 1;
            int stride = MiniJson.Int(view, "byteStride") ?? size * width;
            int offset = bin + (MiniJson.Int(view, "byteOffset") ?? 0) + (MiniJson.Int(a, "byteOffset") ?? 0);
            var o = new float[count * width];
            for (int i = 0; i < count; i++)
                for (int c = 0; c < width; c++)
                {
                    int p = offset + i * stride + c * size;
                    o[i * width + c] = type switch
                    {
                        5126 => BitConverter.ToSingle(glb, p),
                        5125 => BitConverter.ToUInt32(glb, p),
                        5123 => normalized ? BitConverter.ToUInt16(glb, p) / 65535f : BitConverter.ToUInt16(glb, p),
                        5122 => normalized ? Math.Max(BitConverter.ToInt16(glb, p) / 32767f, -1f) : BitConverter.ToInt16(glb, p),
                        5121 => normalized ? glb[p] / 255f : glb[p],
                        _ => normalized ? Math.Max((sbyte)glb[p] / 127f, -1f) : (sbyte)glb[p],
                    };
                }
            return o;
        }
    }
}
