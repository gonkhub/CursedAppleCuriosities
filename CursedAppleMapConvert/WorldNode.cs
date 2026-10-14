using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CursedAppleMapConvert
{
    /// <summary>One placed model of a Deadlock world node (Source units, row-major 3x4 transform).</summary>
    internal class WorldInstance
    {
        public string Model = "";               // resource path, ".vmdl_c"
        public float[] Transform = Identity();
        public C32 Tint = C32.White;
        public int DrawCall = -1;               // -1: every draw of the model
        public Dictionary<int, C32>? DrawTints; // merged aggregates: per draw call
        public HashSet<int>? SkipDraws;         // merged aggregates: draws of a lower level of detail (the full one is drawn)
        public V3 Min, Max;                     // world bounds (Source units)

        public static float[] Identity() => new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };

        public V3 Apply(V3 p)
        {
            float[] t = Transform;
            return new V3(t[0] * p.X + t[1] * p.Y + t[2] * p.Z + t[3], t[4] * p.X + t[5] * p.Y + t[6] * p.Z + t[7], t[8] * p.X + t[9] * p.Y + t[10] * p.Z + t[11]);
        }

        public V3 Rotate(V3 n)
        {
            float[] t = Transform;
            return new V3(t[0] * n.X + t[1] * n.Y + t[2] * n.Z, t[4] * n.X + t[5] * n.Y + t[6] * n.Z, t[8] * n.X + t[9] * n.Y + t[10] * n.Z);
        }

        public void SetBounds(V3 lo, V3 hi)
        {
            Min = new V3(float.MaxValue, float.MaxValue, float.MaxValue);
            Max = -Min;
            for (int i = 0; i < 8; i++)
            {
                V3 c = Apply(new V3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z));
                Min = V3.Min(Min, c);
                Max = V3.Max(Max, c);
            }
        }
    }

    internal class ModelBounds
    {
        public V3 Min, Max;
        public readonly List<(V3 Min, V3 Max)> Draws = new List<(V3, V3)>();
    }

    /// <summary>
    /// Reads Source2Viewer-CLI's text dumps: a world node's DATA block (scene objects and aggregates) and the MDAT
    /// blocks of its models (bounds). Line-based, as the CLI prints them.
    /// </summary>
    internal static class WorldNodeParser
    {
        private static readonly Regex Numbers = new Regex(@"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
        private static readonly Regex Resource = new Regex("resource:\"([^\"]*)\"", RegexOptions.Compiled);

        private static float[] Nums(string line)
        {
            var ms = Numbers.Matches(line);
            var o = new float[ms.Count];
            for (int i = 0; i < o.Length; i++) o[i] = float.Parse(ms[i].Value, CultureInfo.InvariantCulture);
            return o;
        }

        private static byte B(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));

        /// <summary>Feed it every line of the world node dump, then read Instances.</summary>
        public class NodeReader
        {
            public readonly List<WorldInstance> Instances = new List<WorldInstance>();
            private enum Part { None, Scene, Aggregate, Done }
            private Part _part;
            private readonly List<float> _rows = new List<float>();
            private bool _inTransform;
            private C32 _tint = C32.White;
            private readonly List<(bool Has, int Draw, C32 Tint, bool Lod)> _meshes = new List<(bool, int, C32, bool)>();
            private bool _has;
            private int _draw, _lodMask;
            private bool _inFragments;
            private readonly List<float> _frag = new List<float>();

            public void Line(string raw)
            {
                string l = raw.Trim();
                if (l.StartsWith("m_sceneObjects =")) { _part = Part.Scene; Reset(); return; }
                if (l.StartsWith("m_aggregateSceneObjects =")) { _part = Part.Aggregate; Reset(); return; }
                if (l.StartsWith("m_clutterSceneObjects")) { _part = Part.Done; return; }
                if (_part == Part.Scene) SceneLine(l);
                else if (_part == Part.Aggregate) AggregateLine(l);
            }

            private void Reset()
            {
                _rows.Clear(); _inTransform = false; _tint = C32.White;
                _meshes.Clear(); _inFragments = false; _frag.Clear();
            }

            private void SceneLine(string l)
            {
                if (l.StartsWith("m_vTransform")) { _inTransform = true; _rows.Clear(); return; }
                if (_inTransform)
                {
                    if (l.StartsWith("[") && l.Length > 2) { _rows.AddRange(Nums(l)); if (_rows.Count >= 12) _inTransform = false; }
                    return;
                }
                if (l.StartsWith("m_vTintColor"))
                {
                    float[] c = Nums(l);
                    if (c.Length >= 3) _tint = new C32(B(c[0] * 255f), B(c[1] * 255f), B(c[2] * 255f), 255);
                    return;
                }
                if (l.StartsWith("m_renderableModel"))
                {
                    Match m = Resource.Match(l);
                    if (m.Success && m.Groups[1].Value.Length > 0)
                        Instances.Add(new WorldInstance
                        {
                            Model = m.Groups[1].Value + "_c",
                            Transform = _rows.Count >= 12 ? _rows.GetRange(0, 12).ToArray() : WorldInstance.Identity(),
                            Tint = _tint,
                        });
                    Reset();
                }
            }

            private void AggregateLine(string l)
            {
                if (l.StartsWith("m_bHasTransform")) { _has = l.EndsWith("true"); return; }
                if (l.StartsWith("m_nDrawCallIndex")) { float[] n = Nums(l); _draw = n.Length > 0 ? (int)n[0] : 0; return; }
                if (l.StartsWith("m_nLODGroupMask")) { float[] n = Nums(l); _lodMask = n.Length > 0 ? (int)n[0] : 0; return; }
                if (l.StartsWith("m_vTintColor"))
                {
                    float[] c = Nums(l);
                    // A draw only in the lower levels of detail duplicates the full one: left out.
                    _meshes.Add((_has, _draw, c.Length >= 3 ? new C32(B(c[0]), B(c[1]), B(c[2]), 255) : C32.White, _lodMask != 0 && (_lodMask & 1) == 0));
                    _lodMask = 0;
                    return;
                }
                if (l.StartsWith("m_fragmentTransforms")) { _inFragments = !l.EndsWith("[  ]"); return; }
                if (_inFragments && !l.StartsWith("m_"))
                {
                    if (l.Length > 2 && (char.IsDigit(l[0]) || l[0] == '-')) _frag.AddRange(Nums(l));
                    return;
                }
                if (l.StartsWith("m_renderableModel"))
                {
                    Match m = Resource.Match(l);
                    if (m.Success && m.Groups[1].Value.Length > 0) Emit(m.Groups[1].Value + "_c");
                    Reset();
                }
            }

            private void Emit(string model)
            {
                WorldInstance? merged = null;
                int f = 0;
                foreach (var (has, draw, tint, lod) in _meshes)
                {
                    if (has)
                    {
                        float[] t = f * 12 + 12 <= _frag.Count ? _frag.GetRange(f * 12, 12).ToArray() : WorldInstance.Identity();
                        f++; // every fragment has its transform, drawn or not
                        if (!lod) Instances.Add(new WorldInstance { Model = model, Transform = t, Tint = tint, DrawCall = draw });
                    }
                    else
                    {
                        merged ??= new WorldInstance { Model = model, DrawTints = new Dictionary<int, C32>() };
                        if (lod) (merged.SkipDraws ??= new HashSet<int>()).Add(draw);
                        else merged.DrawTints![draw] = tint;
                    }
                }
                if (merged != null) Instances.Add(merged);
            }
        }

        /// <summary>Feed it the MDAT dump of many models ("[i/N] path" headers): their bounds.</summary>
        public class BoundsReader
        {
            public readonly Dictionary<string, ModelBounds> Models = new Dictionary<string, ModelBounds>(StringComparer.OrdinalIgnoreCase);
            private ModelBounds? _cur;
            private bool _haveModel, _inDraws, _haveLo;
            private V3 _lo;

            public void Line(string raw)
            {
                string l = raw.Trim();
                if (l.StartsWith("[") && l.Contains("] ") && l.EndsWith(".vmdl_c"))
                {
                    string path = l.Substring(l.IndexOf("] ", StringComparison.Ordinal) + 2).Replace('\\', '/');
                    _cur = new ModelBounds();
                    Models[path] = _cur;
                    _haveModel = _inDraws = _haveLo = false;
                    return;
                }
                if (_cur == null) return;
                if (l.StartsWith("m_drawBounds")) { _inDraws = true; return; }
                if (l.StartsWith("m_vMinBounds")) { float[] n = Nums(l); if (n.Length >= 3) { _lo = new V3(n[0], n[1], n[2]); _haveLo = true; } return; }
                if (l.StartsWith("m_vMaxBounds") && _haveLo)
                {
                    float[] n = Nums(l);
                    if (n.Length < 3) return;
                    var hi = new V3(n[0], n[1], n[2]);
                    if (_inDraws) _cur.Draws.Add((_lo, hi));
                    else if (!_haveModel) { _cur.Min = _lo; _cur.Max = hi; _haveModel = true; }
                    _haveLo = false;
                    return;
                }
                if (_inDraws && l.StartsWith("]")) _inDraws = false;
            }
        }
    }
}
