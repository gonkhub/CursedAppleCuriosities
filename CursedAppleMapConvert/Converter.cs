using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CursedAppleCuriosities.Stages.DeadlockMap;

namespace CursedAppleMapConvert
{
    /// <summary>
    /// Converts one section of a Deadlock map: the playable area in full (render + collision), the rest of the map as a
    /// simplified render-only backdrop. Read-only on Deadlock (Source2Viewer-CLI and its text dumps).
    /// </summary>
    internal class Converter
    {
        // Backdrop simplification (Source units): small pieces and anything underground are left out.
        private const float BackdropMinSize = 160f;   // ~4 m
        private const float BackdropMinZ = -100f;
        private const float ChunkPlay = 24f, ChunkBack = 64f; // metres
        private const int BatchSize = 30;

        private readonly MapSectionDef _def;
        private readonly string _cli, _citadel, _out, _fingerprint;
        private readonly int _parallel;
        private string TexFolder => Path.Combine(_out, "tex");

        private V2[] _outline = Array.Empty<V2>();
        private V2 _oMin, _oMax;
        private V3 _origin;
        private readonly List<(string Name, string Tex, byte Flags, float Cutoff)> _materials = new List<(string, string, byte, float)>();
        private readonly List<MaterialInfo> _matInfo = new List<MaterialInfo>();
        private readonly Dictionary<string, int> _materialIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _textureDone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(bool Back, int X, int Z), Dictionary<int, SubBuilder>> _chunks = new Dictionary<(bool, int, int), Dictionary<int, SubBuilder>>();
        private int _subIds;

        public Converter(MapSectionDef def, string cli, string citadel, string outFolder, string fingerprint, int parallel)
        {
            _def = def; _cli = cli; _citadel = citadel; _out = outFolder; _fingerprint = fingerprint; _parallel = Math.Max(1, parallel);
        }

        public void Run()
        {
            string mapVpk = Path.Combine(_citadel, "maps", _def.Map + ".vpk");
            _outline = new V2[_def.Outline.Length / 2];
            for (int i = 0; i < _outline.Length; i++) _outline[i] = new V2(_def.Outline[i * 2], _def.Outline[i * 2 + 1]);
            _oMin = new V2(_outline.Min(p => p.X), _outline.Min(p => p.Y));
            _oMax = new V2(_outline.Max(p => p.X), _outline.Max(p => p.Y));
            _origin = new V3((_oMin.X + _oMax.X) / 2f, (_oMin.Y + _oMax.Y) / 2f, 0f);

            // 1. What the world node places, and every world model's bounds (text dumps: seconds).
            Program.Status("reading the map's layout");
            var node = new WorldNodeParser.NodeReader();
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/worldnodes/n0.vwnod_c\" -b DATA", node.Line);
            Program.Mem("world node read");
            var bounds = new WorldNodeParser.BoundsReader();
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/worldnodes/\" -e vmdl_c -b MDAT", bounds.Line);
            Program.Mem("bounds read");
            GC.Collect(); GC.WaitForPendingFinalizers();
            Program.Mem("after collect");
            Program.Log($"world node: {node.Instances.Count} placed models, {bounds.Models.Count} model bounds");

            // 2. Which placements matter: everything near the playable area, and the backdrop's larger pieces.
            var byModel = new Dictionary<string, List<WorldInstance>>(StringComparer.OrdinalIgnoreCase);
            var near = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            foreach (WorldInstance inst in node.Instances)
            {
                if (!Wanted(inst.Model)) { skipped++; continue; }
                if (bounds.Models.TryGetValue(inst.Model, out ModelBounds mb))
                {
                    bool draw = inst.DrawCall >= 0 && inst.DrawCall < mb.Draws.Count;
                    inst.SetBounds(draw ? mb.Draws[inst.DrawCall].Min : mb.Min, draw ? mb.Draws[inst.DrawCall].Max : mb.Max);
                }
                else inst.SetBounds(new V3(-400, -400, -400), new V3(400, 400, 400)); // a prop: cropped per triangle anyway
                bool isNear = inst.Max.X >= _oMin.X - 64 && inst.Min.X <= _oMax.X + 64 && inst.Max.Y >= _oMin.Y - 64 && inst.Min.Y <= _oMax.Y + 64;
                V3 size = inst.Max - inst.Min;
                bool backdrop = Math.Max(size.X, Math.Max(size.Y, size.Z)) >= BackdropMinSize && inst.Max.Z > BackdropMinZ;
                if (!isNear && !backdrop) { skipped++; continue; }
                if (!byModel.TryGetValue(inst.Model, out var list)) byModel[inst.Model] = list = new List<WorldInstance>();
                list.Add(inst);
                if (isNear) near.Add(inst.Model);
            }
            Program.Log($"{byModel.Count} models to convert ({near.Count} near the playable area), {skipped} placements left out");

            // 3. Export in batches (a couple of converter processes at once), near models first; read each batch as it lands.
            string tmp = Path.Combine(_out, "tmp");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(TexFolder);
            var order = byModel.Keys.OrderBy(m => near.Contains(m) ? 0 : 1).ThenBy(m => m, StringComparer.Ordinal).ToList();
            var batches = new List<List<string>>();
            foreach (bool fromMap in new[] { true, false })
            {
                var models = order.Where(m => m.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) == fromMap).ToList();
                for (int i = 0; i < models.Count; i += BatchSize) batches.Add(models.GetRange(i, Math.Min(BatchSize, models.Count - i)));
            }
            if (batches.Count > Program.MaxBatches) batches.RemoveRange(Program.MaxBatches, batches.Count - Program.MaxBatches);
            var done = new BlockingCollection<(int Index, string Dir)>(boundedCapacity: _parallel);
            var exporter = Task.Run(() =>
            {
                Parallel.For(0, batches.Count, new ParallelOptions { MaxDegreeOfParallelism = _parallel }, bi =>
                {
                    string dir = Path.Combine(tmp, "b" + bi);
                    List<string> models = batches[bi];
                    string input = models[0].StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ? mapVpk : Path.Combine(_citadel, "pak01_dir.vpk");
                    try { RunCli($"-i \"{input}\" -f \"{string.Join(",", models)}\" -o \"{dir}\" -d --gltf_export_format glb --gltf_export_materials --gltf_textures_adapt", _ => { }); }
                    catch (Exception e) { Program.Log($"batch {bi}: {e.Message}"); }
                    done.Add((bi, dir)); // blocks while the reader is behind: at most a few batches on disk at once
                });
                done.CompleteAdding();
            });
            int read = 0, missing = 0, batchesDone = 0;
            foreach (var (bi, dir) in done.GetConsumingEnumerable())
            {
                try
                {
                    foreach (string model in batches[bi])
                    {
                        string glb = Path.Combine(dir, model.Replace(".vmdl_c", ".glb").Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(glb)) { missing++; continue; }
                        Model mm;
                        try { mm = Glb.Read(glb); }
                        catch (Exception e) { Program.Log($"{model}: {e.Message}"); missing++; continue; }
                        Program.Mem($"read {Path.GetFileName(glb)} ({new FileInfo(glb).Length / 1024} KB, {mm.Prims.Sum(x => x.Tris.Length / 3)} tris, {byModel[model].Count} placements)");
                        int[] mats = MaterialIndices(mm, near.Contains(model));
                        Program.Mem("  textures done");
                        foreach (WorldInstance inst in byModel[model]) Emit(mm, mats, inst);
                        Program.Mem($"  emitted ({_chunks.Count} chunks, {_subIds} subs)");
                        read++;
                    }
                }
                finally { try { Directory.Delete(dir, true); } catch (Exception e) { Program.Log($"couldn't delete {dir}: {e.Message}"); } }
                batchesDone++;
                Program.Status($"converting: {batchesDone}/{batches.Count} batches");
            }
            exporter.Wait();
            Program.Log($"{read} models read, {missing} missing");

            // 4. Deadlock's player clips: the invisible ramps over its stairs and its movement walls (character-only collision).
            Program.Status("reading the player clips");
            ReadClips(mapVpk, Path.Combine(tmp, "physics"));
            try { Directory.Delete(tmp, true); } catch { }

            Program.Status("writing the cache");
            Write(Path.Combine(_out, "section.bin"));
        }

        private readonly List<V3> _clipPos = new List<V3>();
        private readonly List<int> _clipTris = new List<int>();

        /// <summary>
        /// The world's physics layers that only stop players and NPCs ("playerclip": Source's ramps over stairs, walls
        /// keeping heroes off ledges), cropped to the outline. The sky clip and NPC-only clips are left out.
        /// </summary>
        private void ReadClips(string mapVpk, string dir)
        {
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/world_physics.vmdl_c\" -o \"{dir}\" -d --gltf_export_format glb", _ => { });
            string? glb = Directory.Exists(dir) ? Directory.GetFiles(dir, "*_physics.glb", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (glb == null) { Program.Log("no physics export: no player clips"); return; }
            Model phys = Glb.Read(glb);
            var map = new Dictionary<int, int>();
            foreach (Prim p in phys.Prims)
            {
                string n = p.Node.ToLowerInvariant();
                if (!n.Contains("playerclip") || n.Contains("skyclip")) continue;
                map.Clear();
                for (int t = 0; t + 2 < p.Tris.Length; t += 3)
                {
                    int a = p.Tris[t], b = p.Tris[t + 1], c = p.Tris[t + 2];
                    V3 cen = (p.Pos[a] + p.Pos[b] + p.Pos[c]) / 3f;
                    if (!Inside(cen.X, cen.Y)) continue;
                    foreach (int k in new[] { a, c, b }) // reversed winding (one axis is mirrored)
                    {
                        if (!map.TryGetValue(k, out int i)) { i = _clipPos.Count; _clipPos.Add(ToUnity(p.Pos[k])); map[k] = i; }
                        _clipTris.Add(i);
                    }
                }
            }
            Program.Log($"player clips: {_clipTris.Count / 3} triangles in the playable area");
        }

        private void RunCli(string args, Action<string> onLine)
        {
            var psi = new ProcessStartInfo(_cli, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using Process p = Process.Start(psi);
            Program.Children[p.Id] = p;
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            p.ErrorDataReceived += (o, e) => { };
            p.BeginErrorReadLine();
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null) onLine(line);
            p.WaitForExit();
            Program.Children.TryRemove(p.Id, out _);
        }

        /// <summary>Props that are never part of the scenery: characters, effects, particles.</summary>
        private static bool Wanted(string model) =>
            !(model.StartsWith("models/npc", StringComparison.OrdinalIgnoreCase) || model.StartsWith("models/particle", StringComparison.OrdinalIgnoreCase)
              || model.StartsWith("models/effects", StringComparison.OrdinalIgnoreCase) || model.StartsWith("models/heroes", StringComparison.OrdinalIgnoreCase)
              || model.Contains("/entities/"));

        // Material flags, as the mod's MapMatFlags.
        private const byte Cutout = 1, NoRender = 2, DoubleSided = 4, NoCollide = 8, SelfIllum = 16;

        /// <summary>The cache's material index for each of the model's materials (-1 = not drawn at all); converts new textures.</summary>
        private int[] MaterialIndices(Model mm, bool near)
        {
            var o = new int[mm.Materials.Count];
            var convert = new List<(string Png, string Cac)>();
            for (int i = 0; i < o.Length; i++)
            {
                MaterialInfo info = mm.Materials[i];
                if (info.Skip) { o[i] = -1; continue; }
                if (!_materialIndex.TryGetValue(info.Name, out int idx))
                {
                    byte flags = 0;
                    if (info.Cutout) flags |= Cutout | NoCollide;
                    if (info.Translucent || info.Glass) flags |= NoRender;
                    if (info.DoubleSided) flags |= DoubleSided;
                    if (info.SelfIllum) flags |= SelfIllum;
                    string tex = "";
                    if (info.TexturePng != null)
                    {
                        string png = Path.GetFileName(info.TexturePng);
                        if (!_textureDone.TryGetValue(png, out tex))
                        {
                            tex = Path.GetFileNameWithoutExtension(png) + ".cac";
                            if (File.Exists(info.TexturePng)) convert.Add((info.TexturePng, Path.Combine(TexFolder, tex)));
                            else { Program.Log($"texture missing: {png}"); tex = ""; }
                            _textureDone[png] = tex;
                        }
                    }
                    idx = _materials.Count;
                    _materials.Add((info.Name, tex, flags, info.Cutoff));
                    _matInfo.Add(info);
                    _materialIndex[info.Name] = idx;
                }
                o[i] = idx;
            }
            int size = near ? 1024 : 256;
            Parallel.ForEach(convert, new ParallelOptions { MaxDegreeOfParallelism = 2 }, t =>
            {
                try { if (!Textures.ConvertToCache(t.Png, t.Cac, size)) Program.Log($"texture unreadable: {Path.GetFileName(t.Png)}"); }
                catch (Exception e) { Program.Log($"texture {Path.GetFileName(t.Png)}: {e.Message}"); }
            });
            return o;
        }

        private V3 ToUnity(V3 s) => new V3(-(s.Y - _origin.Y), s.Z - _origin.Z, s.X - _origin.X) * 0.0254f;
        private static V3 DirToUnity(V3 n) => new V3(-n.Y, n.Z, n.X);

        /// <summary>One placement of a model: its triangles into the playable chunks (inside the outline) or the backdrop.</summary>
        private void Emit(Model mm, int[] mats, WorldInstance inst)
        {
            var remap = new Dictionary<long, int>(); // (sub id, vertex) -> index in that sub, for this placement
            var placed = new Dictionary<V3[], (V3[] Src, V3[] U, V3[]? N)>();
            for (int d = 0; d < mm.Prims.Count; d++)
            {
                if (inst.DrawCall >= 0 && d != inst.DrawCall) continue;
                Prim p = mm.Prims[d];
                int mat = p.Material >= 0 && p.Material < mats.Length ? mats[p.Material] : -1;
                if (mat < 0) continue;
                MaterialInfo info = _matInfo[mat];
                C32 tint = inst.DrawTints != null && inst.DrawTints.TryGetValue(d, out C32 dt) ? dt : inst.Tint;
                C32 baseCol = tint * info.Tint;
                // Vertex colour RGB tints every Deadlock environment shader that has it (the "simple" and vertex-colour
                // materials get all their colour from it); alpha is a layer blend weight, not used here.
                bool paint = p.Color != null;
                bool seeThrough = info.Translucent || info.Glass;

                // Draws of a merged model share one vertex buffer: transform it once per placement.
                if (!placed.TryGetValue(p.Pos, out var xf))
                {
                    int n = p.Pos.Length;
                    var src0 = new V3[n];
                    var u0 = new V3[n];
                    for (int k = 0; k < n; k++) { src0[k] = inst.Apply(p.Pos[k]); u0[k] = ToUnity(src0[k]); }
                    V3[]? nrm = null;
                    if (p.Normal != null) { nrm = new V3[n]; for (int k = 0; k < n; k++) nrm[k] = DirToUnity(inst.Rotate(p.Normal[k]).Normalized); }
                    placed[p.Pos] = xf = (src0, u0, nrm);
                }
                var v = new Verts { U = xf.U, N = xf.N, Uv = p.Uv, Paint = paint ? p.Color : null, Base = baseCol };
                V3[] src = xf.Src, u = xf.U;
                remap.Clear();
                for (int t = 0; t + 2 < p.Tris.Length; t += 3)
                {
                    int a = p.Tris[t], b = p.Tris[t + 1], c = p.Tris[t + 2];
                    V3 cen = (src[a] + src[b] + src[c]) / 3f;
                    bool inside = Inside(cen.X, cen.Y);
                    if (inside && cen.Z < _def.FloorZ) continue;
                    if (!inside && (cen.Z < BackdropMinZ || seeThrough)) continue;
                    V3 uc = (u[a] + u[b] + u[c]) / 3f;
                    float size = inside ? ChunkPlay : ChunkBack;
                    var key = (!inside, (int)Math.Floor(uc.X / size), (int)Math.Floor(uc.Z / size));
                    if (!_chunks.TryGetValue(key, out var subs)) _chunks[key] = subs = new Dictionary<int, SubBuilder>();
                    if (!subs.TryGetValue(mat, out SubBuilder sb))
                        subs[mat] = sb = new SubBuilder(_subIds++, inside ? 0f : CellFor(new V2((key.Item2 + 0.5f) * size, (key.Item3 + 0.5f) * size)));
                    if (sb.Cell > 0f) sb.AddClustered(v, a, c, b); // reversed winding (one axis is mirrored)
                    else sb.AddExact(v, remap, a, c, b);
                }
            }
        }

        /// <summary>Point in the outline (Source units).</summary>
        private bool Inside(float x, float y)
        {
            if (x < _oMin.X || x > _oMax.X || y < _oMin.Y || y > _oMax.Y) return false;
            bool inside = false;
            for (int i = 0, j = _outline.Length - 1; i < _outline.Length; j = i++)
            {
                V2 pi = _outline[i], pj = _outline[j];
                if ((pi.Y > y) != (pj.Y > y) && x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X) inside = !inside;
            }
            return inside;
        }

        /// <summary>Backdrop vertex-merge cell (metres) by distance from the playable area: finer close by.</summary>
        private float CellFor(V2 unityXz)
        {
            float best = float.MaxValue;
            for (int i = 0, j = _outline.Length - 1; i < _outline.Length; j = i++)
            {
                V2 a = U2(_outline[j]), b = U2(_outline[i]);
                V2 ab = b - a;
                float t = Math.Max(0f, Math.Min(1f, V2.Dot(unityXz - a, ab) / Math.Max(1e-4f, ab.SqrLength)));
                best = Math.Min(best, (a + ab * t - unityXz).Length);
            }
            return best < 40f ? 0.2f : best < 120f ? 0.5f : best < 300f ? 1.2f : 2.5f;
        }

        private V2 U2(V2 s) { V3 u = ToUnity(new V3(s.X, s.Y, 0f)); return new V2(u.X, u.Z); }

        // ---------------------------------------------------------------- cache file (read by the mod's MapCache)

        private void Write(string file)
        {
            long tris = 0, backTris = 0;
            int chunkCount = 0;
            string tmp = file + ".part";
            using (var bw = new BinaryWriter(new BufferedStream(File.Create(tmp), 1 << 20)))
            {
                bw.Write(0x4D434143u); bw.Write(2);
                bw.Write(_fingerprint);
                W(bw, _origin);
                bw.Write(_materials.Count);
                foreach (var m in _materials) { bw.Write(m.Name); bw.Write(m.Tex); bw.Write(m.Flags); bw.Write(m.Cutoff); }
                var chunks = _chunks.Where(kv => kv.Value.Values.Any(s => s.TriCount > 0)).ToList();
                bw.Write(chunks.Count);
                foreach (var kv in chunks)
                {
                    var subs = kv.Value.Where(s => s.Value.TriCount > 0).ToList();
                    V3 lo = new V3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                    foreach (var s in subs) { lo = V3.Min(lo, s.Value.Min); hi = V3.Max(hi, s.Value.Max); }
                    bw.Write(kv.Key.Back); W(bw, lo); W(bw, hi);
                    bw.Write(subs.Count);
                    foreach (var s in subs)
                    {
                        bw.Write(s.Key);
                        s.Value.WriteArrays(bw);
                        if (kv.Key.Back) backTris += s.Value.TriCount; else tris += s.Value.TriCount;
                        s.Value.Release();
                    }
                    chunkCount++;
                }
                // Player clips (version 2): one collision mesh, character-only.
                bw.Write(_clipPos.Count); foreach (V3 v in _clipPos) W(bw, v);
                bw.Write(_clipTris.Count); foreach (int i in _clipTris) bw.Write(i);
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
            Program.Log($"cache written: {chunkCount} chunks, {tris} playable + {backTris} backdrop triangles, {_materials.Count} materials, {new FileInfo(file).Length / (1024 * 1024)} MB");
        }

        private static void W(BinaryWriter bw, V3 v) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }

        /// <summary>One draw's vertices as placed: positions/normals in Unity space, colour = base x paint.</summary>
        private struct Verts
        {
            public V3[] U;
            public V3[]? N;
            public V2[]? Uv;
            public C32[]? Paint;
            public C32 Base;
        }

        /// <summary>One material's triangles in one chunk, while converting.</summary>
        private class SubBuilder
        {
            public readonly int Id;
            public readonly float Cell;
            private List<V3> _p = new List<V3>(), _n = new List<V3>();
            private List<V2> _uv = new List<V2>();
            private List<C32> _c = new List<C32>();
            private List<int> _t = new List<int>();
            private Dictionary<long, int>? _cells;
            public V3 Min = new V3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new V3(float.MinValue, float.MinValue, float.MinValue);
            public int TriCount => _t.Count / 3;

            public SubBuilder(int id, float cell) { Id = id; Cell = cell; if (cell > 0f) _cells = new Dictionary<long, int>(); }

            private int Add(Verts v, int k)
            {
                V3 p = v.U[k];
                _p.Add(p); _n.Add(v.N != null ? v.N[k] : V3.Up); _uv.Add(v.Uv != null ? v.Uv[k] : default); _c.Add(v.Paint != null ? Rgb(v.Base, v.Paint[k]) : v.Base);
                Min = V3.Min(Min, p); Max = V3.Max(Max, p);
                return _p.Count - 1;
            }

            /// <summary>Base colour tinted by a vertex colour's RGB (its alpha is a blend weight: the base's alpha stays).</summary>
            private static C32 Rgb(C32 b, C32 paint) { C32 c = b * paint; c.A = b.A; return c; }

            public void AddExact(Verts v, Dictionary<long, int> remap, int a, int b, int d)
            {
                _t.Add(Map(v, remap, a)); _t.Add(Map(v, remap, b)); _t.Add(Map(v, remap, d));
            }

            private int Map(Verts v, Dictionary<long, int> remap, int k)
            {
                long key = ((long)Id << 32) | (uint)k;
                if (!remap.TryGetValue(key, out int i)) remap[key] = i = Add(v, k);
                return i;
            }

            /// <summary>Vertex clustering: every vertex snaps to the first one seen in its grid cell; collapsed triangles drop.</summary>
            public void AddClustered(Verts v, int a, int b, int d)
            {
                int ia = Cluster(v, a), ib = Cluster(v, b), id = Cluster(v, d);
                if (ia == ib || ib == id || ia == id) return;
                _t.Add(ia); _t.Add(ib); _t.Add(id);
            }

            private int Cluster(Verts v, int k)
            {
                V3 p = v.U[k];
                long key = ((long)((int)Math.Floor(p.X / Cell) & 0x1FFFFF) << 42) | ((long)((int)Math.Floor(p.Y / Cell) & 0x1FFFFF) << 21) | (long)((int)Math.Floor(p.Z / Cell) & 0x1FFFFF);
                if (!_cells!.TryGetValue(key, out int i)) _cells[key] = i = Add(v, k);
                return i;
            }

            public void WriteArrays(BinaryWriter bw)
            {
                bw.Write(_p.Count); foreach (V3 v in _p) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
                bw.Write(_n.Count); foreach (V3 v in _n) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
                bw.Write(_uv.Count); foreach (V2 v in _uv) { bw.Write(v.X); bw.Write(v.Y); }
                bw.Write(_c.Count); foreach (C32 v in _c) { bw.Write(v.R); bw.Write(v.G); bw.Write(v.B); bw.Write(v.A); }
                bw.Write(_t.Count); foreach (int i in _t) bw.Write(i);
            }

            public void Release() { _p = null!; _n = null!; _uv = null!; _c = null!; _t = new List<int>(); _cells = null; }
        }
    }
}
