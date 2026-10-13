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

        /// <summary>A world material: RoR2's own standard shader (so the map is lit like the game), vertex colours on.</summary>
        public static Material MakeMaterial(MapMaterial m, Texture2D? tex)
        {
            if (_base == null) _base = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>("RoR2/Base/Commando/matCommandoDualies.mat").WaitForCompletion();
            var mat = new Material(_base) { name = "matDeadlock_" + m.Name };
            mat.mainTexture = tex;
            mat.color = Color.white;
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
            var mats = new Material?[cache.Materials.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                MapMaterial m = cache.Materials[i];
                if ((m.Flags & MapMatFlags.NoRender) != 0) continue;
                Texture2D? tex = null;
                if (m.Texture.Length > 0 && !textures.TryGetValue(m.Texture, out tex))
                {
                    TexData? td = HeroTextures.ReadStandalone(System.IO.Path.Combine(conv.TexFolder, m.Texture));
                    if (td != null) { tex = HeroTextures.Make(td); tex.wrapMode = TextureWrapMode.Repeat; tex.anisoLevel = 4; }
                    textures[m.Texture] = tex;
                }
                mats[i] = MakeMaterial(m, tex);
            }

            var bake = new List<(Mesh Mesh, GameObject Holder)>();
            int world = LayerIndex.world.intVal;
            long tris = 0, backTris = 0;
            bool any = false;
            Bounds play = default;
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
                foreach (MapSub s in c.Subs) if (mats[s.Material] != null) subs.Add(s);
                if (subs.Count > 0)
                {
                    Mesh mesh = Combine(subs, out var used);
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
                foreach (MapSub s in c.Subs) if ((cache.Materials[s.Material].Flags & MapMatFlags.NoCollide) == 0) solid.Add(s);
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
            Log($"section built in {watch.ElapsedMilliseconds} ms: {tris} playable + {backTris} backdrop triangles drawn, {bake.Count} collision meshes ({clipTris.Length / 3} clip triangles), {textures.Count} textures");
            return built;
        }

        private static Mesh Combine(List<MapSub> subs, out List<int> materials)
        {
            materials = new List<int>();
            int n = 0;
            foreach (MapSub s in subs) n += s.Pos.Length;
            var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n]; var col = new Color32[n];
            int at = 0;
            foreach (MapSub s in subs)
            {
                Array.Copy(s.Pos, 0, pos, at, s.Pos.Length);
                Array.Copy(s.Normal, 0, nrm, at, s.Normal.Length);
                Array.Copy(s.Uv, 0, uv, at, s.Uv.Length);
                Array.Copy(s.Color, 0, col, at, s.Color.Length);
                at += s.Pos.Length;
            }
            var mesh = new Mesh { indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv; mesh.colors32 = col;
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
