using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// Turns a converted section into Unity objects: one renderer per chunk (a submesh per material), mesh colliders on the
    /// playable chunks (cooked on worker threads), and the barrier along the outline.
    /// </summary>
    internal static class MapBuilder
    {
        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);

        private static Material? _base;

        /// <summary>Glass: our see-through shader (fresnel edge), the surface's texture and tints.</summary>
        private static Material MakeGlass(MapMaterial m, Texture2D? tex)
        {
            var mat = new Material(DeadlockStages.GlassMaterial) { name = "matDeadlockGlass_" + m.Name };
            mat.mainTexture = tex;
            return mat;
        }

        private static Texture2D? _lightmap;
        private static string _lightmapFor = "";
        private static readonly int LightmapId = Shader.PropertyToID("_CacLightmap"), LightmapOnId = Shader.PropertyToID("_CacLightmapOn"),
            ExposureId = Shader.PropertyToID("_CacExposure"), DynamicId = Shader.PropertyToID("_CacDynamic"), FallbackId = Shader.PropertyToID("_CacFallback");

        /// <summary>
        /// Deadlock's baked irradiance (raw BC6H blocks straight to the GPU: no decoding, ~64 MB of video memory for the
        /// whole map), kept for every later stage. Null when the cache has none or the GPU can't sample BC6H.
        /// </summary>
        private static Texture2D? Lightmap(MapCache cache, string texFolder)
        {
            string file = texFolder.Length > 0 ? System.IO.Path.Combine(texFolder, cache.LightmapFile) : "";
            if (_lightmap != null && _lightmapFor == file) return _lightmap;
            if (cache.LightmapFile.Length == 0 || cache.LightmapSize <= 0 || !File.Exists(file)) return null;
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H)) { Log("this GPU can't sample BC6H: the map is lit without its lightmap"); return null; }
            byte[] raw = File.ReadAllBytes(file);
            var tex = new Texture2D(cache.LightmapSize, cache.LightmapSize, TextureFormat.BC6H, false, true)
            {
                name = "DeadlockLightmap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.LoadRawTextureData(raw); // Direct3D's rows run top-down, as do Deadlock's lightmap UVs: no flip needed
            tex.Apply(false, true);      // the CPU copy goes
            if (_lightmap != null) UnityEngine.Object.Destroy(_lightmap);
            _lightmap = tex;
            _lightmapFor = file;
            return tex;
        }

        /// <summary>True once Deadlock's lightmap lights the map (the world shader is in use).</summary>
        public static bool Lit => _lightmap != null && DeadlockStages.WorldMaterial != null;

        /// <summary>The lighting globals the world shader reads (config: exposure, the dynamic lights' share).</summary>
        public static void ApplyLighting()
        {
            bool on = _lightmap != null && DeadlockStages.WorldMaterial != null;
            Shader.SetGlobalTexture(LightmapId, on ? (Texture)_lightmap! : Texture2D.blackTexture);
            Shader.SetGlobalFloat(LightmapOnId, on ? 1f : 0f);
            Shader.SetGlobalFloat(ExposureId, DeadlockStages.Exposure?.Value ?? 1f);
            Shader.SetGlobalFloat(DynamicId, DeadlockStages.DynamicLight?.Value ?? 0.15f);
            Shader.SetGlobalColor(FallbackId, new Color(0.45f, 0.38f, 0.4f)); // dusk: props off the lightmap, the far backdrop
        }

        /// <summary>A world material on our Deadlock shader: layer 2 and the world overlay when the material has them.</summary>
        private static Material MakeWorld(MapMaterial m, Func<string, Texture2D?> tex)
        {
            var mat = new Material(DeadlockStages.WorldMaterial) { name = "matDeadlock_" + m.Name };
            mat.mainTexture = tex(m.Texture);
            mat.color = Color.white;
            mat.SetColor("_Tint1", m.Tint);
            // Cut out: alpha is the opacity. Otherwise it is the tint mask (where tints and vertex paint apply).
            if ((m.Flags & MapMatFlags.Cutout) != 0) { mat.EnableKeyword("_ALPHATEST_ON"); mat.SetFloat("_Cutoff", m.Cutoff); mat.SetFloat("_TintMask", 0f); }
            else { mat.DisableKeyword("_ALPHATEST_ON"); mat.SetFloat("_TintMask", 1f); }
            mat.SetFloat("_Cull", (m.Flags & MapMatFlags.DoubleSided) != 0 ? (float)CullMode.Off : (float)CullMode.Back);
            mat.SetFloat("_SelfIllum", (m.Flags & MapMatFlags.SelfIllum) != 0 ? 0.8f : 0f);
            Texture2D? t2 = tex(m.Texture2);
            if (t2 != null)
            {
                mat.EnableKeyword("CAC_LAYER2");
                mat.SetTexture("_Tex2", t2);
                mat.SetColor("_Tint2", m.Tint2);
                Texture2D? reveal = tex(m.Reveal2);
                mat.SetTexture("_Reveal2", reveal != null ? reveal : Texture2D.grayTexture);
                mat.SetFloat("_RevealOffset", m.RevealOffset2);
                mat.SetFloat("_RevealSoftness", m.RevealSoftness2);
                mat.SetFloat("_RevealInvert", m.RevealInvert2 ? 1f : 0f);
            }
            Texture2D? ov = tex(m.Overlay);
            if (ov != null)
            {
                mat.EnableKeyword("CAC_OVERLAY");
                mat.SetTexture("_Overlay", ov);
                mat.SetFloat("_OverlayTiling", Mathf.Max(0.1f, m.OverlayTiling));
                mat.SetFloat("_OverlayDark", m.OverlayDark);
                mat.SetFloat("_OverlayBright", m.OverlayBright);
                mat.SetFloat("_OverlayStrength1", m.OverlayStrength1);
                mat.SetFloat("_OverlayStrength2", m.OverlayStrength2);
                mat.SetFloat("_OverlayMask1", m.OverlayMask1);
                mat.SetFloat("_OverlayMask2", m.OverlayMask2);
            }
            return mat;
        }

        /// <summary>A world material: RoR2's own standard shader (so the map is lit like the game), vertex colours on.</summary>
        public static Material MakeMaterial(MapMaterial m, Texture2D? tex)
        {
            if (_base == null) _base = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>("RoR2/Base/Commando/matCommandoDualies.mat").WaitForCompletion();
            var mat = new Material(_base) { name = "matDeadlock_" + m.Name };
            mat.mainTexture = tex;
            mat.color = m.Tint;
            foreach (string p in new[] { "_NormalTex", "_EmTex", "_FresnelRamp", "_PrintRamp", "_SplatmapTex", "_FlowHeightmap", "_FlowHeightRamp" })
                if (mat.HasProperty(p)) mat.SetTexture(p, null);
            if (mat.HasProperty("_EmPower")) mat.SetFloat("_EmPower", 0f);
            if (mat.HasProperty("_NormalStrength")) mat.SetFloat("_NormalStrength", 0f);
            if (mat.HasProperty("_SpecularStrength")) mat.SetFloat("_SpecularStrength", 0.05f);
            mat.DisableKeyword("FORCE_SPEC");
            // hgstandard switches (when it has them): vertex colours, cutout, two-sided.
            if (mat.HasProperty("_VertexColorOn")) mat.SetFloat("_VertexColorOn", 1f);
            mat.EnableKeyword("VERTEXCOLOR");
            if ((m.Flags & MapMatFlags.Cutout) != 0)
            {
                if (mat.HasProperty("_EnableCutout")) mat.SetFloat("_EnableCutout", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", m.Cutoff);
                mat.EnableKeyword("CUTOUT");
            }
            if ((m.Flags & MapMatFlags.DoubleSided) != 0 && mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            return mat;
        }

        /// <summary>A built section: the root (inactive) and where things are.</summary>
        public class Built
        {
            public GameObject Root = null!;
            public Vector3 OriginUnits;
            public Bounds Playable;      // section space: the playable chunks' bounds
            public Bounds Hidden;        // section space: where the hidden materials were (the replaced statue)
            public bool HasHidden;
            public readonly List<Texture2D> Textures = new List<Texture2D>(); // this district's own (the lightmap is shared)

            /// <summary>
            /// Frees everything built for this district: its objects, and the meshes, materials and textures they use (made at
            /// runtime, so destroying the objects alone would leave them behind). The shared lightmap stays.
            /// </summary>
            public void Release()
            {
                if (Root == null) return;
                var meshes = new HashSet<Mesh>();
                var materials = new HashSet<Material>();
                foreach (MeshFilter f in Root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) meshes.Add(f.sharedMesh);
                foreach (MeshCollider c in Root.GetComponentsInChildren<MeshCollider>(true)) if (c.sharedMesh != null) meshes.Add(c.sharedMesh);
                foreach (Renderer r in Root.GetComponentsInChildren<Renderer>(true)) foreach (Material m in r.sharedMaterials) if (m != null) materials.Add(m);
                // Immediately, not at the end of the frame: the next district is built right after, and two districts at once
                // is gigabytes more than the game can spare.
                UnityEngine.Object.DestroyImmediate(Root);
                foreach (Mesh m in meshes) if (m != null) UnityEngine.Object.DestroyImmediate(m);
                foreach (Material m in materials) if (m != null) UnityEngine.Object.DestroyImmediate(m);
                foreach (Texture2D t in Textures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
                Textures.Clear();
                Root = null!;
            }
        }

        /// <summary>
        /// Main thread: the whole section under one root (inactive until the caller turns it on). Streams the cache: one
        /// texture and one chunk in memory at a time, so loading never holds the whole section's data at once.
        /// </summary>
        public static Built Build(MapConverter conv, bool colliders)
        {
            var watch = Stopwatch.StartNew();
            var root = new GameObject("DeadlockMapSection");
            root.SetActive(false);
            using MapCache cache = MapCache.Open(conv.CacheFile);
            var built = new Built { Root = root, OriginUnits = cache.OriginUnits };

            var textures = new Dictionary<string, Texture2D?>();
            // Shared by the map's districts; a cache from before textures were shared still has its own folder.
            string TexPath(string name)
            {
                string shared = System.IO.Path.Combine(conv.TexFolder, name);
                return File.Exists(shared) ? shared : System.IO.Path.Combine(conv.OwnTexFolder, name);
            }
            Texture2D? Tex(string name)
            {
                if (name.Length == 0) return null;
                if (textures.TryGetValue(name, out Texture2D? t)) return t;
                TexData? td = HeroTextures.ReadStandalone(TexPath(name));
                if (td != null) { t = HeroTextures.Make(td); t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4; built.Textures.Add(t); }
                textures[name] = t;
                return t;
            }
            bool lit = DeadlockStages.WorldMaterial != null && cache.LightmapFile.Length > 0
                && Lightmap(cache, System.IO.Path.GetDirectoryName(TexPath(cache.LightmapFile)) ?? "") != null;
            ApplyLighting();
            var mats = new Material?[cache.Materials.Count];
            var hidden = new bool[cache.Materials.Count];
            for (int i = 0; i < hidden.Length; i++) hidden[i] = Array.IndexOf(conv.Def.HiddenMaterials, cache.Materials[i].Name) >= 0;
            for (int i = 0; i < mats.Length; i++)
            {
                MapMaterial m = cache.Materials[i];
                if (hidden[i]) continue;
                bool glass = (m.Flags & MapMatFlags.Translucent) != 0;
                if (glass && DeadlockStages.GlassMaterial == null) continue; // no glass shader: leave it undrawn (still solid)
                mats[i] = glass ? MakeGlass(m, Tex(m.Texture)) : lit ? MakeWorld(m, Tex) : MakeMaterial(m, Tex(m.Texture));
            }

            // The far backdrop was simplified by merging vertices, so its lightmap UVs are scrambled (a glittering speckle):
            // past ~40 m from the outline it takes a flat dusk light instead.
            var outline = new List<Vector2>();
            float[] ol = conv.Def.Outline;
            for (int i = 0; i + 1 < ol.Length; i += 2)
            {
                Vector3 u = MapCache.ToUnity(new Vector3(ol[i], ol[i + 1], 0f), cache.OriginUnits);
                outline.Add(new Vector2(u.x, u.z));
            }
            int flatBackdrop = 0;
            var bake = new List<(Mesh Mesh, GameObject Holder)>();
            int world = LayerIndex.world.intVal;
            long tris = 0, backTris = 0;
            bool any = false;
            Bounds play = default;
            int hiddenVerts = 0;
            foreach (MapChunk c in cache.Chunks())
            {
                var go = new GameObject(c.Backdrop ? "Backdrop" : "Chunk");
                go.transform.SetParent(root.transform, false);
                go.layer = world;
                if (!c.Backdrop)
                {
                    var b = new Bounds((c.Min + c.Max) / 2f, c.Max - c.Min);
                    if (any) play.Encapsulate(b); else { play = b; any = true; }
                }
                // Render mesh: every drawn material as a submesh, sharing one vertex buffer.
                var subs = new List<MapSub>();
                foreach (MapSub s in c.Subs)
                {
                    if (hidden[s.Material])
                    {
                        // The statue's own placement is its biggest piece (the same material is used by small bits elsewhere).
                        if (s.Pos.Length > hiddenVerts)
                        {
                            hiddenVerts = s.Pos.Length;
                            built.Hidden = new Bounds(s.Pos[0], Vector3.zero);
                            foreach (Vector3 p in s.Pos) built.Hidden.Encapsulate(p);
                            built.HasHidden = true;
                        }
                        continue;
                    }
                    if (mats[s.Material] != null) subs.Add(s);
                }
                if (subs.Count > 0)
                {
                    // From the chunk's centre, as the converter chose how coarsely to merge it (finest within 40 m).
                    bool flat = c.Backdrop && DistanceToOutline(outline, (c.Min + c.Max) / 2f, 0f) >= 40f;
                    if (flat) flatBackdrop++;
                    Mesh mesh = Combine(subs, out var used, flat);
                    mesh.name = go.name;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    var rm = new Material[used.Count];
                    for (int i = 0; i < rm.Length; i++) rm[i] = mats[used[i]]!;
                    r.sharedMaterials = rm;
                    r.shadowCastingMode = c.Backdrop ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    foreach (MapSub s in subs) { if (c.Backdrop) backTris += s.Tris.Length / 3; else tris += s.Tris.Length / 3; }
                }
                if (c.Backdrop || !colliders) continue;
                // Collision: the solid materials (glass included: it's only not drawn).
                var solid = new List<MapSub>();
                foreach (MapSub s in c.Subs) if ((cache.Materials[s.Material].Flags & MapMatFlags.NoCollide) == 0 && !hidden[s.Material]) solid.Add(s);
                if (solid.Count == 0) continue;
                var holder = new GameObject("Collision");
                holder.transform.SetParent(go.transform, false);
                holder.layer = world;
                bake.Add((CollisionMesh(solid), holder));
            }
            built.Playable = play;
            // Deadlock's player clips: invisible, solid only for characters (bullets still hit the real steps).
            var (clipPos, clipTris) = cache.Clips();
            if (colliders && clipTris.Length > 0)
            {
                var clipMesh = new Mesh { name = "playerclip", indexFormat = clipPos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                clipMesh.vertices = clipPos;
                clipMesh.triangles = clipTris;
                clipMesh.RecalculateBounds();
                var clip = new GameObject("PlayerClip") { layer = LayerIndex.collideWithCharacterHullOnly.intVal };
                clip.transform.SetParent(root.transform, false);
                bake.Add((clipMesh, clip));
            }
            // Cook the colliders on worker threads (Physics.BakeMesh is thread-safe), then attach them.
            var ids = new int[bake.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = bake[i].Mesh.GetInstanceID();
            Parallel.For(0, ids.Length, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i => Physics.BakeMesh(ids[i], false));
            foreach (var (mesh, holder) in bake) holder.AddComponent<MeshCollider>().sharedMesh = mesh;
            Log(lit ? $"lighting: Deadlock's baked lightmap ({cache.LightmapSize}x{cache.LightmapSize}), {flatBackdrop} far backdrop chunks flat-lit" : "lighting: RoR2's standard shader (no lightmap)");
            Log($"section built in {watch.ElapsedMilliseconds} ms: {tris} playable + {backTris} backdrop triangles drawn, {bake.Count} collision meshes ({clipTris.Length / 3} clip triangles), {textures.Count} textures" + (built.HasHidden ? $", hidden statue at {built.Hidden.center} size {built.Hidden.size}" : ", nothing hidden"));
            return built;
        }

        /// <summary>Horizontal distance from a chunk (centre, radius) to the nearest outline edge, 0 if it reaches it.</summary>
        private static float DistanceToOutline(List<Vector2> outline, Vector3 centre, float radius)
        {
            var p = new Vector2(centre.x, centre.z);
            float best = float.MaxValue;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                Vector2 a = outline[j], ab = outline[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return Mathf.Max(0f, best - radius);
        }

        private static Mesh Combine(List<MapSub> subs, out List<int> materials, bool flatLight = false)
        {
            materials = new List<int>();
            int n = 0;
            foreach (MapSub s in subs) n += s.Pos.Length;
            var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n]; var lm = new Vector2[n]; var col = new Color32[n];
            int at = 0;
            foreach (MapSub s in subs)
            {
                Array.Copy(s.Pos, 0, pos, at, s.Pos.Length);
                Array.Copy(s.Normal, 0, nrm, at, s.Normal.Length);
                Array.Copy(s.Uv, 0, uv, at, s.Uv.Length);
                if (flatLight) for (int k = 0; k < s.Pos.Length; k++) lm[at + k] = new Vector2(-1f, -1f); // the shader's fallback light
                else Array.Copy(s.Lightmap, 0, lm, at, s.Lightmap.Length);
                Array.Copy(s.Color, 0, col, at, s.Color.Length);
                at += s.Pos.Length;
            }
            var mesh = new Mesh { indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv; mesh.colors32 = col;
            mesh.SetUVs(3, lm); // the world shader's lightmap UVs
            mesh.subMeshCount = subs.Count;
            at = 0;
            for (int i = 0; i < subs.Count; i++)
            {
                mesh.SetTriangles(subs[i].Tris, i, false, at);
                materials.Add(subs[i].Material);
                at += subs[i].Pos.Length;
            }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Mesh CollisionMesh(List<MapSub> subs)
        {
            int n = 0, t = 0;
            foreach (MapSub s in subs) { n += s.Pos.Length; t += s.Tris.Length; }
            var pos = new Vector3[n];
            var tris = new int[t];
            int vat = 0, tat = 0;
            foreach (MapSub s in subs)
            {
                Array.Copy(s.Pos, 0, pos, vat, s.Pos.Length);
                for (int i = 0; i < s.Tris.Length; i++) tris[tat + i] = s.Tris[i] + vat;
                vat += s.Pos.Length; tat += s.Tris.Length;
            }
            var mesh = new Mesh { name = "collision", indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = pos;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh; // kept readable: the collider cooks from it
        }
    }
}
