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

        // Baked lighting: Deadlock's irradiance lightmap (raw BC6H blocks, copied as they are) and its UV scale.
        private string _lightmapFile = "";
        private int _lightmapSize;
        private float _lightmapUvScale = 1f;
        // Placements without lightmap UVs (props, instanced pieces) take the lightmap texel of the nearest lightmapped
        // surface to their centre: their "probes" (Unity space), resolved once every surface is in.
        private readonly List<V3> _probes = new List<V3>();
        private V2[] _probeUv = Array.Empty<V2>();
        // Per material: the cached names of layer 2's colour, its reveal mask and the world overlay ("" = none).
        private readonly List<(string Tex2, string Reveal, string Overlay)> _matExtra = new List<(string, string, string)>();
        // Extra textures to export by resource path (Deadlock's .vtex) -> wanted near the playable area.
        private readonly Dictionary<string, bool> _extraTextures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public Converter(MapSectionDef def, string cli, string citadel, string outFolder, string fingerprint, int parallel)
        {
            _def = def; _cli = cli; _citadel = citadel; _out = outFolder; _fingerprint = fingerprint; _parallel = Math.Max(1, parallel);
        }

        private void Init()
        {
            _outline = new V2[_def.Outline.Length / 2];
            for (int i = 0; i < _outline.Length; i++) _outline[i] = new V2(_def.Outline[i * 2], _def.Outline[i * 2 + 1]);
            _oMin = new V2(_outline.Min(p => p.X), _outline.Min(p => p.Y));
            _oMax = new V2(_outline.Max(p => p.X), _outline.Max(p => p.Y));
            _origin = _def.Origin.Length >= 3 ? new V3(_def.Origin[0], _def.Origin[1], _def.Origin[2]) : new V3((_oMin.X + _oMax.X) / 2f, (_oMin.Y + _oMax.Y) / 2f, 0f);
        }

        /// <summary>
        /// Only (re)adds the player clips to an existing cache whose clip section is empty (a cache written before clips
        /// could be read): seconds instead of a full conversion.
        /// </summary>
        public void PatchClips()
        {
            Init();
            string file = Path.Combine(_out, "section.bin");
            string tmp = Path.Combine(_out, "tmp");
            ReadClips(Path.Combine(_citadel, "maps", _def.Map + ".vpk"), Path.Combine(tmp, "physics"));
            try { DeleteTree(tmp); } catch { }
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite))
            {
                var br = new BinaryReader(fs);
                if (br.ReadUInt32() != 0x4D434143u || br.ReadInt32() != 2 || br.ReadString() != _fingerprint) throw new InvalidDataException("cache doesn't match");
                fs.Position = fs.Length - 8;
                if (br.ReadInt32() != 0 || br.ReadInt32() != 0) throw new InvalidDataException("cache already has clips");
                fs.SetLength(fs.Length - 8);
                fs.Position = fs.Length;
                var bw = new BinaryWriter(fs);
                bw.Write(_clipPos.Count); foreach (V3 v in _clipPos) W(bw, v);
                bw.Write(_clipTris.Count); foreach (int i in _clipTris) bw.Write(i);
                bw.Flush();
            }
            Program.Log($"clips patched into {file}");
        }

        public void Run()
        {
            Init();
            string mapVpk = Path.Combine(_citadel, "maps", _def.Map + ".vpk");

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
            if (node.Instances.Count == 0 || bounds.Models.Count == 0)
                throw new InvalidDataException("the map's world node couldn't be read (a Deadlock update newer than this Source2Viewer-CLI?)");

            ReadLighting(mapVpk, Path.Combine(_out, "tmp_light"));

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
            DeleteTree(tmp);
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
                    const string flags = "-d --gltf_export_format glb --gltf_export_materials --gltf_textures_adapt";
                    try { RunCli($"-i \"{input}\" -f \"{string.Join(",", models)}\" -o \"{dir}\" {flags}", _ => { }); }
                    catch (Exception e) { Program.Log($"batch {bi}: {e.Message}"); }
                    // A model the CLI chokes on stops the rest of its batch: retry each missing one alone, then without materials.
                    foreach (string m in models)
                    {
                        string glb = Path.Combine(dir, m.Replace(".vmdl_c", ".glb").Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(glb)) continue;
                        try
                        {
                            RunCli($"-i \"{input}\" -f \"{m}\" -o \"{glb}\" {flags}", _ => { });
                            if (!File.Exists(glb)) RunCli($"-i \"{input}\" -f \"{m}\" -o \"{glb}\" -d --gltf_export_format glb", _ => { });
                            Program.Log(File.Exists(glb) ? $"retried alone: {m}" : $"export failed: {m}");
                        }
                        catch (Exception e) { Program.Log($"retry {m}: {e.Message}"); }
                    }
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
                        if (!File.Exists(glb)) { missing++; Program.Log($"missing: {model} ({byModel[model].Count} placements)"); continue; }
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
                finally { try { DeleteTree(dir); } catch (Exception e) { Program.Log($"couldn't delete {dir}: {e.Message}"); } }
                batchesDone++;
                Program.Status($"converting: {batchesDone}/{batches.Count} batches");
                // The builders' big arrays fragment the large-object heap: compact it now and then, so the process's memory
                // tracks what it really holds.
                if (batchesDone % 4 == 0)
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                }
            }
            exporter.Wait();
            Program.Log($"{read} models read, {missing} missing");
            ExportExtraTextures(mapVpk, Path.Combine(tmp, "extra"));

            // 4. Deadlock's player clips: the invisible ramps over its stairs and its movement walls (character-only collision).
            Program.Status("reading the player clips");
            ReadClips(mapVpk, Path.Combine(tmp, "physics"));
            try { DeleteTree(tmp); } catch { }

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
            // One file in: the CLI takes -o as that file's name (and writes the physics next to it as *_physics.glb).
            Directory.CreateDirectory(dir);
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/world_physics.vmdl_c\" -o \"{Path.Combine(dir, "world_physics.glb")}\" -d --gltf_export_format glb", _ => { });
            string glb = Path.Combine(dir, "world_physics_physics.glb");
            if (!File.Exists(glb)) { Program.Log("no physics export: no player clips"); return; }
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

        /// <summary>Deletes a folder and everything in it, long paths included (exported texture names can be very long).</summary>
        private static void DeleteTree(string dir)
        {
            string full = Path.GetFullPath(dir);
            string longPath = full.StartsWith(@"\\?\") ? full : @"\\?\" + full;
            if (Directory.Exists(longPath)) Directory.Delete(longPath, true);
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
        private const byte Cutout = 1, Translucent = 2, DoubleSided = 4, NoCollide = 8, SelfIllum = 16;

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
                    // Overlays are decals (posters, grates, puddles, signs) floating just off the surface: never solid, drawn cut out by their alpha.
                    if (info.Shader.StartsWith("citadel_overlay")) flags |= Cutout | NoCollide;
                    if (info.Translucent || info.Glass) flags |= Translucent; // drawn with the glass shader, still solid
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
                    _matExtra.Add((Extra(info.Color2, near), Extra(info.Reveal2, near), Extra(info.Overlay, near)));
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

        /// <summary>The cache name of an extra Deadlock texture (exported after the models), or "".</summary>
        private string Extra(string? resource, bool near)
        {
            if (resource == null) return "";
            _extraTextures[resource] = near || (_extraTextures.TryGetValue(resource, out bool n) && n);
            return Path.GetFileNameWithoutExtension(resource) + ".cac";
        }

        /// <summary>Layer 2 colours, reveal masks and world overlays: not in the glTF export, so exported here as PNGs and cached.</summary>
        private void ExportExtraTextures(string mapVpk, string dir)
        {
            var todo = _extraTextures.Where(kv => !File.Exists(Path.Combine(TexFolder, Path.GetFileNameWithoutExtension(kv.Key) + ".cac"))).ToList();
            if (todo.Count == 0) return;
            Program.Status($"exporting {todo.Count} layer textures");
            foreach (bool fromMap in new[] { true, false })
            {
                var list = todo.Where(kv => kv.Key.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) == fromMap).ToList();
                for (int i = 0; i < list.Count; i += 60)
                {
                    var part = list.GetRange(i, Math.Min(60, list.Count - i));
                    string input = fromMap ? mapVpk : Path.Combine(_citadel, "pak01_dir.vpk");
                    try { RunCli($"-i \"{input}\" -f \"{string.Join(",", part.Select(kv => kv.Key + "_c"))}\" -o \"{dir}\" -d", _ => { }); }
                    catch (Exception e) { Program.Log($"layer textures: {e.Message}"); }
                    Parallel.ForEach(part, new ParallelOptions { MaxDegreeOfParallelism = 2 }, kv =>
                    {
                        string png = Path.Combine(dir, Path.ChangeExtension(kv.Key, ".png").Replace('/', Path.DirectorySeparatorChar));
                        string cac = Path.Combine(TexFolder, Path.GetFileNameWithoutExtension(kv.Key) + ".cac");
                        try
                        {
                            if (!File.Exists(png)) Program.Log($"layer texture missing: {kv.Key}");
                            else if (!Textures.ConvertToCache(png, cac, kv.Value ? 1024 : 256)) Program.Log($"layer texture unreadable: {kv.Key}");
                        }
                        catch (Exception e) { Program.Log($"layer texture {kv.Key}: {e.Message}"); }
                    });
                }
            }
            try { DeleteTree(dir); } catch { }
            Program.Log($"{todo.Count} layer textures exported");
        }

        /// <summary>
        /// The world's baked lighting: its lightmap UV scale, and the irradiance lightmap copied as raw BC6H blocks (the mod
        /// uploads them to the GPU as they are). Without either the map is lit as before.
        /// </summary>
        private void ReadLighting(string mapVpk, string dir)
        {
            bool inLighting = false, has = false;
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/world.vwrld_c\" -b DATA", line =>
            {
                string l = line.Trim();
                if (l.StartsWith("m_worldLightingInfo")) inLighting = true;
                if (!inLighting) return;
                if (l.StartsWith("m_bHasLightmaps")) has = l.EndsWith("true");
                if (l.StartsWith("m_vLightmapUvScale"))
                {
                    var n = l.Substring(l.IndexOf('[') + 1).TrimEnd(']', ' ').Split(',');
                    if (n.Length > 0 && float.TryParse(n[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc)) _lightmapUvScale = sc;
                }
            });
            if (!has) { Program.Log("no baked lightmaps in this map"); return; }
            Directory.CreateDirectory(dir);
            string raw = Path.Combine(dir, "irradiance.vtex_c");
            RunCli($"-i \"{mapVpk}\" -f \"maps/{_def.Map}/lightmaps/directional_irradiance_sh2_dc.vtex_c\" -o \"{raw}\"", _ => { });
            try
            {
                if (!File.Exists(raw)) { Program.Log("irradiance lightmap not exported"); return; }
                Directory.CreateDirectory(TexFolder);
                string dst = Path.Combine(TexFolder, "lightmap_irradiance.bc6h");
                int size = Lightmaps.ExtractBc6h(raw, dst);
                if (size > 0) { _lightmapFile = Path.GetFileName(dst); _lightmapSize = size; }
                Program.Log(size > 0 ? $"lightmap: {size}x{size} BC6H, UV scale {_lightmapUvScale}" : "lightmap: not a raw BC6H texture, left out");
            }
            finally { try { DeleteTree(dir); } catch { } }
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
                if (inst.SkipDraws != null && inst.SkipDraws.Contains(d)) continue;
                Prim p = mm.Prims[d];
                int mat = p.Material >= 0 && p.Material < mats.Length ? mats[p.Material] : -1;
                if (mat < 0) continue;
                MaterialInfo info = _matInfo[mat];
                C32 tint = inst.DrawTints != null && inst.DrawTints.TryGetValue(d, out C32 dt) ? dt : inst.Tint;
                C32 baseCol = tint; // the material's own tint goes with the material (the shader masks each layer's by its texture)
                // Vertex colour RGB tints every Deadlock environment shader that has it (the "simple" and vertex-colour
                // materials get all their colour from it); alpha is a layer blend weight, not used here.
                bool paint = p.Color != null;

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
                // Lightmapped geometry carries its own lightmap UVs; anything else is lit from the nearest lightmapped surface
                // to its centre (one probe per placement and draw).
                // Only Deadlock's merged world geometry is lightmapped (props and instanced pieces are probe-lit): elsewhere a UV set
                // that happens to sit inside the atlas range is not a lightmap.
                V2[]? lightmap = inst.Model.IndexOf("_agg_merge_", StringComparison.OrdinalIgnoreCase) >= 0 ? p.Lightmap : null;
                int probe = -1;
                if (lightmap == null)
                {
                    V3 lo = new V3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo, slo = lo, shi = hi;
                    foreach (int k in p.Tris) { lo = V3.Min(lo, xf.U[k]); hi = V3.Max(hi, xf.U[k]); slo = V3.Min(slo, xf.Src[k]); shi = V3.Max(shi, xf.Src[k]); }
                    if (Inside((slo.X + shi.X) / 2f, (slo.Y + shi.Y) / 2f)) // backdrop props: the fallback light
                    {
                        probe = _probes.Count;
                        _probes.Add((lo + hi) / 2f);
                    }
                }
                var v = new Verts
                {
                    U = xf.U, N = xf.N, Uv = p.Uv, Paint = paint ? p.Color : null, Base = baseCol,
                    Lm = lightmap, LmScale = _lightmapUvScale, Probe = probe, Blend = info.Color2 != null ? p.Blend : null,
                };
                V3[] src = xf.Src, u = xf.U;
                remap.Clear();
                for (int t = 0; t + 2 < p.Tris.Length; t += 3)
                {
                    int a = p.Tris[t], b = p.Tris[t + 1], c = p.Tris[t + 2];
                    V3 cen = (src[a] + src[b] + src[c]) / 3f;
                    // Playable (drawn and solid) when any corner is inside: a big floor triangle straddling the outline with its
                    // centre outside would otherwise be drawn without collision, a hole between the barrier and solid ground.
                    bool inside = Inside(cen.X, cen.Y) || Inside(src[a].X, src[a].Y) || Inside(src[b].X, src[b].Y) || Inside(src[c].X, src[c].Y);
                    if (inside && cen.Z < _def.FloorZ) continue;
                    if (!inside && cen.Z < BackdropMinZ) continue;
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
            ResolveProbes();
            string tmp = file + ".part";
            using (var bw = new BinaryWriter(new BufferedStream(File.Create(tmp), 1 << 20)))
            {
                bw.Write(0x4D434143u); bw.Write(3);
                bw.Write(_fingerprint);
                W(bw, _origin);
                bw.Write(_lightmapFile); bw.Write(_lightmapSize);
                bw.Write(_materials.Count);
                for (int i = 0; i < _materials.Count; i++)
                {
                    var m = _materials[i];
                    MaterialInfo info = _matInfo[i];
                    var x = _matExtra[i];
                    bw.Write(m.Name); bw.Write(m.Tex); bw.Write(m.Flags); bw.Write(m.Cutoff);
                    // Version 3: layer 2 (colour, reveal mask, its threshold, tint), the world overlay (tiling in metres).
                    bw.Write(info.Tint.R); bw.Write(info.Tint.G); bw.Write(info.Tint.B);
                    bw.Write(x.Tex2); bw.Write(x.Reveal); bw.Write(info.RevealOffset2); bw.Write(info.RevealSoftness2); bw.Write(info.RevealInvert2);
                    bw.Write(info.Tint2.R); bw.Write(info.Tint2.G); bw.Write(info.Tint2.B);
                    bw.Write(x.Overlay); bw.Write(info.OverlayTiling * 0.0254f); bw.Write(info.OverlayDark); bw.Write(info.OverlayBright);
                    bw.Write(info.OverlayStrength1); bw.Write(info.OverlayStrength2); bw.Write(info.OverlayMask1); bw.Write(info.OverlayMask2);
                }
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
                        s.Value.WriteArrays(bw, _probeUv);
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

        /// <summary>
        /// Each probe takes the lightmap UV of the nearest lightmapped surface within a few metres (a surface it faces is
        /// preferred over one it is behind). Only triangles near some probe are indexed. Unresolved probes keep (-1, -1).
        /// </summary>
        private void ResolveProbes()
        {
            _probeUv = new V2[_probes.Count];
            for (int i = 0; i < _probeUv.Length; i++) _probeUv[i] = new V2(-1f, -1f);
            if (_probes.Count == 0 || _lightmapFile.Length == 0) return;
            Program.Status("lighting the props");
            const float cell = 2.5f;
            const int reach = 3; // cells: up to ~8 m (the nearest ~5 m are searched first)
            long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
            int C(float v) => (int)Math.Floor(v / cell);
            var wanted = new HashSet<long>();
            foreach (V3 p in _probes)
                for (int dx = -reach; dx <= reach; dx++) for (int dy = -reach; dy <= reach; dy++) for (int dz = -reach; dz <= reach; dz++)
                    wanted.Add(Key(C(p.X) + dx, C(p.Y) + dy, C(p.Z) + dz));
            // The index holds references (builder, triangle), not copies: the geometry stays where it is.
            var builders = new List<SubBuilder>();
            var grid = new Dictionary<long, List<long>>();
            int indexed = 0;
            foreach (var chunk in _chunks)
                if (!chunk.Key.Back)
                foreach (SubBuilder sb in chunk.Value.Values)
                {
                    long owner = (long)builders.Count << 32;
                    builders.Add(sb);
                    sb.LightmappedTriangles((t, a, b, c) =>
                    {
                        V3 lo = V3.Min(a, V3.Min(b, c)), hi = V3.Max(a, V3.Max(b, c));
                        int x0 = C(lo.X), x1 = C(hi.X), y0 = C(lo.Y), y1 = C(hi.Y), z0 = C(lo.Z), z1 = C(hi.Z);
                        if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096) return; // a huge sliver: never the nearest anyway
                        bool any = false;
                        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                        {
                            long k = Key(x, y, z);
                            if (!wanted.Contains(k)) continue;
                            if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<long>(8);
                            list.Add(owner | (uint)t);
                            any = true;
                        }
                        if (any) indexed++;
                    });
                }
            wanted = null!;
            GC.Collect();
            int resolved = 0;
            for (int i = 0; i < _probes.Count; i++)
            {
                V3 p = _probes[i];
                float best = float.MaxValue;
                int px = C(p.X), py = C(p.Y), pz = C(p.Z);
                for (int r = 2; r <= reach && best == float.MaxValue; r++)
                for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) for (int dz = -r; dz <= r; dz++)
                {
                    if (r > 2 && Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) < r) continue; // the inner cells were searched
                    if (!grid.TryGetValue(Key(px + dx, py + dy, pz + dz), out var list)) continue;
                    foreach (long refr in list)
                    {
                        SubBuilder sb = builders[(int)(refr >> 32)];
                        sb.Triangle((int)(uint)refr, out V3 a, out V3 b, out V3 c, out V2 ua, out V2 ub, out V2 uc, out V3 n);
                        V3 q = ClosestOnTriangle(p, a, b, c, out float wa, out float wb, out float wc);
                        V3 d = p - q;
                        float dist = d.Length;
                        if (d.X * n.X + d.Y * n.Y + d.Z * n.Z < 0f) dist = dist * 3f + 1f; // behind the surface: likely another room
                        if (dist >= best) continue;
                        best = dist;
                        _probeUv[i] = ua * wa + ub * wb + uc * wc;
                    }
                }
                if (best < float.MaxValue) resolved++;
            }
            Program.Log($"props lit: {resolved} of {_probes.Count} placements from {indexed} nearby lightmapped triangles");
        }

        /// <summary>The point of triangle abc closest to p, with its barycentric weights (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private static V3 ClosestOnTriangle(V3 p, V3 a, V3 b, V3 c, out float wa, out float wb, out float wc)
        {
            static float Dot(V3 x, V3 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z;
            V3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Dot(ab, ap), d2 = Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { wa = 1; wb = 0; wc = 0; return a; }
            V3 bp = p - b;
            float d3 = Dot(ab, bp), d4 = Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { wa = 0; wb = 1; wc = 0; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); wa = 1 - v; wb = v; wc = 0; return a + ab * v; }
            V3 cp = p - c;
            float d5 = Dot(ab, cp), d6 = Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { wa = 0; wb = 0; wc = 1; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); wa = 1 - w; wb = 0; wc = w; return a + ac * w; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                wa = 0; wb = 1 - w; wc = w;
                return b + (c - b) * w;
            }
            float denom = 1f / (va + vb + vc);
            wb = vb * denom; wc = vc * denom; wa = 1f - wb - wc;
            return a + ab * wb + ac * wc;
        }

        /// <summary>One draw's vertices as placed: positions/normals in Unity space, colour = base x paint.</summary>
        private struct Verts
        {
            public V3[] U;
            public V3[]? N;
            public V2[]? Uv;
            public C32[]? Paint;
            public C32 Base;
            public V2[]? Lm;        // lightmap UVs (before scaling), or null: then Probe says which probe lights it
            public float LmScale;
            public int Probe;
            public float[]? Blend;  // layer 2's vertex blend weight (to the colour's alpha), or null
        }

        /// <summary>One material's triangles in one chunk, while converting.</summary>
        private class SubBuilder
        {
            public readonly int Id;
            public readonly float Cell;
            private List<V3> _p = new List<V3>(), _n = new List<V3>();
            private List<V2> _uv = new List<V2>(), _lm = new List<V2>();
            private List<C32> _c = new List<C32>();
            private List<int> _t = new List<int>();
            private Dictionary<long, int>? _cells;
            public V3 Min = new V3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new V3(float.MinValue, float.MinValue, float.MinValue);
            public int TriCount => _t.Count / 3;

            public SubBuilder(int id, float cell) { Id = id; Cell = cell; if (cell > 0f) _cells = new Dictionary<long, int>(); }

            private int Add(Verts v, int k)
            {
                V3 p = v.U[k];
                _p.Add(p); _n.Add(v.N != null ? v.N[k] : V3.Up); _uv.Add(v.Uv != null ? v.Uv[k] : default);
                // A probe's index is kept as a negative U until the probes are resolved (written out by WriteArrays).
                _lm.Add(v.Lm != null ? v.Lm[k] * v.LmScale : v.Probe >= 0 ? new V2(-2f - v.Probe, -1f) : new V2(-1f, -1f));
                C32 c = v.Paint != null ? Paint(v.Base, v.Paint[k]) : v.Base;
                if (v.Blend != null) c.A = C32.Byte(v.Blend[k]);
                _c.Add(c);
                Min = V3.Min(Min, p); Max = V3.Max(Max, p);
                return _p.Count - 1;
            }

            /// <summary>Base colour tinted by vertex paint as Deadlock does: the paint's alpha says how much of its colour applies.</summary>
            private static C32 Paint(C32 b, C32 paint)
            {
                int a = paint.A;
                var mixed = new C32((byte)(255 - (255 - paint.R) * a / 255), (byte)(255 - (255 - paint.G) * a / 255), (byte)(255 - (255 - paint.B) * a / 255), 255);
                C32 c = b * mixed; c.A = b.A;
                return c;
            }

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

            /// <summary>Every lightmapped triangle (its offset in the index list and positions), for indexing the probes' surroundings.</summary>
            public void LightmappedTriangles(Action<int, V3, V3, V3> each)
            {
                for (int t = 0; t + 2 < _t.Count; t += 3)
                {
                    int a = _t[t], b = _t[t + 1], c = _t[t + 2];
                    if (_lm[a].X < 0f || _lm[b].X < 0f || _lm[c].X < 0f) continue;
                    each(t, _p[a], _p[b], _p[c]);
                }
            }

            /// <summary>One triangle (by its offset in the index list): positions, lightmap UVs, averaged normal.</summary>
            public void Triangle(int t, out V3 a, out V3 b, out V3 c, out V2 ua, out V2 ub, out V2 uc, out V3 n)
            {
                int i = _t[t], j = _t[t + 1], k = _t[t + 2];
                a = _p[i]; b = _p[j]; c = _p[k];
                ua = _lm[i]; ub = _lm[j]; uc = _lm[k];
                n = (_n[i] + _n[j] + _n[k]).Normalized;
            }

            public void WriteArrays(BinaryWriter bw, V2[] probeUv)
            {
                bw.Write(_p.Count); foreach (V3 v in _p) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
                bw.Write(_n.Count); foreach (V3 v in _n) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
                bw.Write(_uv.Count); foreach (V2 v in _uv) { bw.Write(v.X); bw.Write(v.Y); }
                bw.Write(_lm.Count);
                foreach (V2 v in _lm)
                {
                    V2 o = v.X >= 0f ? v : v.X <= -1.5f ? probeUv[(int)Math.Round(-v.X) - 2] : new V2(-1f, -1f); // (-1, -1): the shader's fallback
                    bw.Write(o.X); bw.Write(o.Y);
                }
                bw.Write(_c.Count); foreach (C32 v in _c) { bw.Write(v.R); bw.Write(v.G); bw.Write(v.B); bw.Write(v.A); }
                bw.Write(_t.Count); foreach (int i in _t) bw.Write(i);
            }

            public void Release() { _p = null!; _n = null!; _uv = null!; _lm = null!; _c = null!; _t = new List<int>(); _cells = null; }
        }
    }
}
