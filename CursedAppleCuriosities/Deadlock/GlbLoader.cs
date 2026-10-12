using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>A loaded Deadlock hero model: an inactive template to instantiate.</summary>
    public class GlbModel
    {
        public GameObject Root = null!;               // inactive template
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Matrix4x4> RestInRoot = new Dictionary<string, Matrix4x4>(); // bone rest pose, root space
        public float Height;                           // model height (root space)
    }

    /// <summary>One animation clip: per bone, keyframed local rotation and position (mirrored into Unity space).</summary>
    public class AnimClip
    {
        public string Name = "";
        public float Length;
        public readonly List<AnimTrack> Tracks = new List<AnimTrack>();
    }

    public class AnimTrack
    {
        public string Bone = "";
        public float[]? RotTimes, PosTimes;
        public Quaternion[]? Rot;
        public Vector3[]? Pos;

        public bool SampleRot(float t, out Quaternion q)
        {
            q = Quaternion.identity;
            if (Rot == null || Rot.Length == 0) return false;
            int i = Find(RotTimes!, t, out float f);
            q = i + 1 < Rot.Length ? Quaternion.Slerp(Rot[i], Rot[i + 1], f) : Rot[i];
            return true;
        }

        public bool SamplePos(float t, out Vector3 p)
        {
            p = Vector3.zero;
            if (Pos == null || Pos.Length == 0) return false;
            int i = Find(PosTimes!, t, out float f);
            p = i + 1 < Pos.Length ? Vector3.Lerp(Pos[i], Pos[i + 1], f) : Pos[i];
            return true;
        }

        private static int Find(float[] times, float t, out float f)
        {
            f = 0f;
            if (times.Length < 2 || t <= times[0]) return 0;
            int lo = 0, hi = times.Length - 1;
            if (t >= times[hi]) return hi;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (times[mid] <= t) lo = mid; else hi = mid; }
            f = (t - times[lo]) / Mathf.Max(1e-5f, times[hi] - times[lo]);
            return lo;
        }
    }

    /// <summary>
    /// Minimal binary glTF (GLB) loader for one skinned Deadlock hero. glTF is
    /// right-handed, Unity left-handed: X is mirrored and winding reversed. Every primitive gets
    /// its own renderer with a single submesh: RoR2's CharacterModel gives each renderer one material plus its
    /// overlays (hit flash, elites, cloak), which only works on single-submesh renderers.
    /// </summary>
    public static class GlbLoader
    {
        private class Glb
        {
            public Dictionary<string, object> J = null!;
            public byte[] Bytes = null!;
            public int BinStart;
        }

        private static Glb Read(string file) => Read(File.ReadAllBytes(file), file);

        private static Glb Read(byte[] glb, string name)
        {
            if (BitConverter.ToUInt32(glb, 0) != 0x46546C67) throw new InvalidDataException("not a GLB: " + name);
            int jsonLen = BitConverter.ToInt32(glb, 12);
            return new Glb
            {
                J = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(glb, 20, jsonLen)),
                Bytes = glb,
                BinStart = 20 + jsonLen + 8,
            };
        }

        /// <summary>Animation clips of a GLB, keyed by clip name (the last part of Deadlock's clip path).</summary>
        public static Dictionary<string, AnimClip> LoadClips(string file)
        {
            Glb g = Read(file);
            var nodes = MiniJson.Arr(g.J, "nodes")!;
            var clips = new Dictionary<string, AnimClip>();
            var anims = MiniJson.Arr(g.J, "animations");
            if (anims == null) return clips;
            // Which bones hang off root_motion (Abrams' pelvis does; Lash's hangs off the model root), and its rest turn.
            var underRoot = new HashSet<string>();
            var besideRoot = new HashSet<string>(); // root_motion's siblings (Lash's, Seven's pelvis)
            Quaternion rootRest = Quaternion.identity;
            foreach (object n in nodes)
                if (MiniJson.Arr(n, "children") is List<object> kids &&
                    kids.Exists(c => MiniJson.Str(nodes[Convert.ToInt32(c)], "name") == "root_motion"))
                    foreach (object c in kids)
                    {
                        string sib = MiniJson.Str(nodes[Convert.ToInt32(c)], "name") ?? "";
                        if (sib != "root_motion") besideRoot.Add(sib);
                    }
            foreach (object n in nodes)
            {
                if (MiniJson.Str(n, "name") != "root_motion") continue;
                if (MiniJson.Arr(n, "rotation") is List<object> r && r.Count == 4)
                    rootRest = new Quaternion(Convert.ToSingle(r[0]), -Convert.ToSingle(r[1]), -Convert.ToSingle(r[2]), Convert.ToSingle(r[3]));
                if (MiniJson.Arr(n, "children") is List<object> kids)
                    foreach (object c in kids) underRoot.Add(MiniJson.Str(nodes[Convert.ToInt32(c)], "name") ?? "");
            }
            foreach (object a in anims)
            {
                string name = (MiniJson.Str(a, "name") ?? "").Replace('\\', '/');
                name = name.Substring(name.LastIndexOf('/') + 1);
                if (clips.ContainsKey(name)) continue;
                var clip = new AnimClip { Name = name };
                var byBone = new Dictionary<string, AnimTrack>();
                var root = new AnimTrack { Bone = "root_motion" }; // read, never played: RoR2 moves the body itself
                var samplers = MiniJson.Arr(a, "samplers")!;
                foreach (object ch in MiniJson.Arr(a, "channels")!)
                {
                    var target = MiniJson.Obj(ch, "target");
                    string? path = MiniJson.Str(target, "path");
                    if (path != "rotation" && path != "translation") continue;
                    string bone = MiniJson.Str(nodes[MiniJson.Int(target, "node")!.Value], "name") ?? "";
                    if (bone.EndsWith("_IKTARGET")) continue;
                    object s = samplers[MiniJson.Int(ch, "sampler")!.Value];
                    float[] times = ReadFloats(g, MiniJson.Int(s, "input")!.Value, 1);
                    AnimTrack track;
                    if (bone == "root_motion") track = root;
                    else if (!byBone.TryGetValue(bone, out track)) { track = new AnimTrack { Bone = bone }; byBone[bone] = track; clip.Tracks.Add(track); }
                    if (path == "rotation")
                    {
                        float[] v = ReadFloats(g, MiniJson.Int(s, "output")!.Value, 4);
                        track.RotTimes = times;
                        track.Rot = new Quaternion[times.Length];
                        for (int k = 0; k < times.Length; k++) track.Rot[k] = new Quaternion(v[k * 4], -v[k * 4 + 1], -v[k * 4 + 2], v[k * 4 + 3]);
                    }
                    else
                    {
                        float[] v = ReadFloats(g, MiniJson.Int(s, "output")!.Value, 3);
                        track.PosTimes = times;
                        track.Pos = new Vector3[times.Length];
                        for (int k = 0; k < times.Length; k++) track.Pos[k] = new Vector3(-v[k * 3], v[k * 3 + 1], v[k * 3 + 2]);
                    }
                    if (times.Length > 0 && track != root) clip.Length = Mathf.Max(clip.Length, times[times.Length - 1]);
                }
                RemoveRootTravel(clip, root, rootRest, underRoot, besideRoot);
                clips[name] = clip;
            }
            return clips;
        }

        /// <summary>
        /// root_motion is never played (RoR2 moves the body itself), so what it carries has to be moved into the bones:
        /// <list type="bullet">
        /// <item>Its travel (a run moves it 4.7 m a loop) is dropped. Source 2 cloth points ($cloth_*, Abrams' coat) hang
        /// off the model root beside it and carry that travel too: take it out of them, or the coat runs ahead and
        /// snaps back each loop.</item>
        /// <item>Its resting turn is kept. Most clips leave it at the skeleton's rest, but newer AnimGraph2 clips
        /// (Abrams' dash_ground) bake an axis turn into it, with the bones under it (his pelvis) in that turned frame.
        /// Left out, the hero lies on his side: the turn goes into those bones instead.</item>
        /// <item>Some heroes' clips (Seven's, AnimGraph2 heroes_staging) also bake the travel into the bones beside
        /// root_motion: his pelvis moves 4.35 m a run loop along with it. Those clips get the travel taken out of every
        /// sibling bone, like the cloth. Lash's pelvis sits beside root_motion too but stays put: only clips whose
        /// pelvis really follows root_motion are touched.</item>
        /// </list>
        /// </summary>
        private static void RemoveRootTravel(AnimClip clip, AnimTrack root, Quaternion rootRest, HashSet<string> underRoot, HashSet<string> besideRoot)
        {
            if ((root.Pos == null || root.Pos.Length == 0) && (root.Rot == null || root.Rot.Length == 0)) return;
            // Additive clips (the shot, landings, Abrams' wall impact) have no travel and a converter axis turn on
            // root_motion: they store offsets, leave them alone. Their pelvis starts at zero offset (a full clip's
            // stands ~1.6 m up).
            AnimTrack? pelvis = clip.Tracks.Find(x => x.Bone == "pelvis");
            if (pelvis != null && pelvis.Pos != null && pelvis.Pos.Length > 0 && pelvis.Pos[0].magnitude < 0.05f) return;

            Quaternion turn0 = root.Rot != null && root.Rot.Length > 0 ? root.Rot[0] : rootRest; // the clip's resting turn
            Quaternion toRest = Quaternion.Inverse(rootRest) * turn0;
            if (Quaternion.Angle(Quaternion.identity, toRest) > 1f)
                foreach (AnimTrack t in clip.Tracks)
                {
                    if (!underRoot.Contains(t.Bone)) continue;
                    if (t.Pos != null) for (int k = 0; k < t.Pos.Length; k++) t.Pos[k] = toRest * t.Pos[k];
                    if (t.Rot != null) for (int k = 0; k < t.Rot.Length; k++) t.Rot[k] = toRest * t.Rot[k];
                }

            // Travel baked into a sibling (Seven's runs): only its horizontal travel comes out, nothing else, which is what
            // the animator's root lock already did for the clips it pins (Lash's sprints, dash, wall jump: unchanged).
            if (PelvisFollowsRoot(pelvis, root, besideRoot))
            {
                Vector3 start = root.Pos![0];
                foreach (AnimTrack t in clip.Tracks)
                {
                    // Cloth points are taken into root_motion's frame below (travel and all): taking the travel out here
                    // too did it twice, and Grey Talon's skirt ran off and snapped back each loop.
                    if (!besideRoot.Contains(t.Bone) || t.Pos == null || t.Bone.StartsWith("$cloth", StringComparison.Ordinal)) continue;
                    for (int k = 0; k < t.Pos.Length; k++)
                    {
                        root.SamplePos(t.PosTimes![k], out Vector3 rp);
                        Vector3 travel = rp - start;
                        t.Pos[k] -= new Vector3(travel.x, 0f, travel.z);
                    }
                }
            }

            foreach (AnimTrack t in clip.Tracks)
            {
                if (!t.Bone.StartsWith("$cloth", StringComparison.Ordinal)) continue;
                // Into root_motion's frame (no travel), then back out by its resting turn (kept, as for the skeleton).
                if (t.Pos != null)
                    for (int k = 0; k < t.Pos.Length; k++)
                    {
                        float at = t.PosTimes![k];
                        root.SamplePos(at, out Vector3 rp);
                        Quaternion rr = root.SampleRot(at, out Quaternion q) ? q : turn0;
                        t.Pos[k] = turn0 * Quaternion.Inverse(rr) * (t.Pos[k] - rp);
                    }
                if (t.Rot != null && root.Rot != null && root.Rot.Length > 0)
                    for (int k = 0; k < t.Rot.Length; k++)
                    {
                        root.SampleRot(t.RotTimes![k], out Quaternion rr);
                        t.Rot[k] = turn0 * Quaternion.Inverse(rr) * t.Rot[k];
                    }
            }
        }

        /// <summary>
        /// A pelvis beside root_motion that travels with it: root_motion moves over half a metre in the clip and the
        /// pelvis covers at least half of that the same way.
        /// </summary>
        private static bool PelvisFollowsRoot(AnimTrack? pelvis, AnimTrack root, HashSet<string> besideRoot)
        {
            if (pelvis == null || !besideRoot.Contains("pelvis") || pelvis.Pos == null || pelvis.Pos.Length < 2 || root.Pos == null || root.Pos.Length < 2) return false;
            Vector3 rootMove = root.Pos[root.Pos.Length - 1] - root.Pos[0];
            Vector3 pelvisMove = pelvis.Pos[pelvis.Pos.Length - 1] - pelvis.Pos[0];
            rootMove.y = 0f;
            pelvisMove.y = 0f;
            return rootMove.magnitude > 0.5f && Vector3.Dot(pelvisMove, rootMove.normalized) > rootMove.magnitude * 0.5f;
        }

        /// <summary>
        /// Load the model. makeMaterial turns a base-colour texture (or null) into the material each renderer uses.
        /// </summary>
        public static GlbModel Load(string file, Func<string, Texture2D?, Material> makeMaterial, ICollection<string> hideMeshes)
            => Load(Read(file), Path.GetFileNameWithoutExtension(file), Path.GetDirectoryName(file), makeMaterial, hideMeshes);

        /// <summary>Load a model from GLB bytes (e.g. an embedded resource). External image files are not resolved.</summary>
        public static GlbModel Load(byte[] glb, string name, Func<string, Texture2D?, Material> makeMaterial)
            => Load(Read(glb, name), name, null, makeMaterial, Array.Empty<string>());

        private static GlbModel Load(Glb g, string name, string? dir, Func<string, Texture2D?, Material> makeMaterial, ICollection<string> hideMeshes)
        {
            var j = g.J;

            var model = new GlbModel();
            model.Root = new GameObject(name);
            model.Root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(model.Root);

            // Nodes -> transforms (mirrored X).
            var nodes = MiniJson.Arr(j, "nodes")!;
            var tf = new Transform[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                object n = nodes[i];
                var go = new GameObject(MiniJson.Str(n, "name") ?? $"node{i}");
                tf[i] = go.transform;
                if (MiniJson.Arr(n, "translation") is List<object> t) go.transform.localPosition = new Vector3(-MiniJson.F(t[0]), MiniJson.F(t[1]), MiniJson.F(t[2]));
                if (MiniJson.Arr(n, "rotation") is List<object> r) go.transform.localRotation = new Quaternion(MiniJson.F(r[0]), -MiniJson.F(r[1]), -MiniJson.F(r[2]), MiniJson.F(r[3]));
                if (MiniJson.Arr(n, "scale") is List<object> sc) go.transform.localScale = new Vector3(MiniJson.F(sc[0]), MiniJson.F(sc[1]), MiniJson.F(sc[2]));
            }
            var hasParent = new bool[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
                if (MiniJson.Arr(nodes[i], "children") is List<object> kids)
                    foreach (object k in kids) { int c = (int)(double)k; tf[c].SetParent(tf[i], false); hasParent[c] = true; }
            for (int i = 0; i < nodes.Count; i++) if (!hasParent[i]) tf[i].SetParent(model.Root.transform, false);

            // Materials (base colour only).
            var materials = new List<Material>();
            var textures = new Dictionary<int, Texture2D>();
            var images = MiniJson.Arr(j, "images");
            var texs = MiniJson.Arr(j, "textures");
            foreach (object m in MiniJson.Arr(j, "materials") ?? new List<object>())
            {
                string matName = MiniJson.Str(m, "name") ?? "mat";
                Texture2D? tex = null;
                int? texIndex = MiniJson.Int(MiniJson.Obj(MiniJson.Obj(m, "pbrMetallicRoughness"), "baseColorTexture"), "index");
                if (texIndex != null && texs != null && images != null)
                {
                    int img = MiniJson.Int(texs[texIndex.Value], "source") ?? 0;
                    if (!textures.TryGetValue(img, out tex))
                    {
                        string? uri = MiniJson.Str(images[img], "uri");
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = uri ?? "tex" };
                        if (uri != null && dir != null && File.Exists(Path.Combine(dir, uri))) LoadPng(tex, File.ReadAllBytes(Path.Combine(dir, uri)));
                        textures[img] = tex;
                    }
                }
                materials.Add(makeMaterial(matName, tex));
            }

            // Skin: joints and their rest pose (inverse of the mirrored inverse-bind matrix).
            var skins = MiniJson.Arr(j, "skins");
            object? skin = skins != null && skins.Count > 0 ? skins[0] : null;
            Transform[]? joints = null;
            Matrix4x4[]? bindposes = null;
            if (skin != null)
            {
                var jointIdx = MiniJson.Arr(skin, "joints")!;
                joints = new Transform[jointIdx.Count];
                for (int k = 0; k < joints.Length; k++) joints[k] = tf[(int)(double)jointIdx[k]];
                float[] ibm = ReadFloats(g, MiniJson.Int(skin, "inverseBindMatrices")!.Value, 16);
                bindposes = new Matrix4x4[joints.Length];
                for (int k = 0; k < joints.Length; k++)
                {
                    var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m[r, c] = ibm[k * 16 + c * 4 + r]; // column-major
                    bindposes[k] = Mirror(m);
                    model.Bones[joints[k].name] = joints[k];
                    model.RestInRoot[joints[k].name] = bindposes[k].inverse;
                }
            }

            // Meshes: one renderer per primitive.
            var meshes = MiniJson.Arr(j, "meshes")!;
            float top = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                int? meshIndex = MiniJson.Int(nodes[i], "mesh");
                if (meshIndex == null) continue;
                object mesh = meshes[meshIndex.Value];
                string[] parts = (MiniJson.Str(mesh, "name") ?? "mesh").Split('.');
                string meshName = parts[parts.Length - 1];
                if (hideMeshes.Contains(meshName)) continue;
                int primIndex = 0;
                foreach (object prim in MiniJson.Arr(mesh, "primitives")!)
                {
                    var attrs = MiniJson.Obj(prim, "attributes")!;
                    Mesh um = BuildMesh(g, prim, attrs, joints, bindposes, meshName + "_" + primIndex);
                    top = Mathf.Max(top, um.bounds.max.y);
                    int? matIndex = MiniJson.Int(prim, "material");
                    Material mat = matIndex != null && matIndex.Value < materials.Count ? materials[matIndex.Value] : materials[0];
                    var holder = new GameObject(um.name);
                    holder.transform.SetParent(model.Root.transform, false);
                    if (joints != null)
                    {
                        var smr = holder.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMesh = um;
                        smr.bones = joints;
                        smr.rootBone = joints[0];
                        smr.sharedMaterial = mat;
                        smr.updateWhenOffscreen = true;
                    }
                    else
                    {
                        holder.AddComponent<MeshFilter>().sharedMesh = um;
                        holder.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                    primIndex++;
                }
            }
            model.Height = top;
            return model;
        }

        /// <summary>One primitive as a compact single-submesh Unity mesh (only the vertices its triangles use).</summary>
        private static Mesh BuildMesh(Glb g, object prim, Dictionary<string, object> attrs, Transform[]? joints, Matrix4x4[]? bindposes, string name)
        {
            float[] idx = ReadFloats(g, MiniJson.Int(prim, "indices")!.Value, 1);
            int pos = MiniJson.Int(attrs, "POSITION")!.Value;
            Vector3[] allV = ToVec3(ReadFloats(g, pos, 3));
            Vector3[]? allN = MiniJson.Has(attrs, "NORMAL") ? ToVec3(ReadFloats(g, MiniJson.Int(attrs, "NORMAL")!.Value, 3)) : null;
            float[]? allUv = MiniJson.Has(attrs, "TEXCOORD_0") ? ReadFloats(g, MiniJson.Int(attrs, "TEXCOORD_0")!.Value, 2) : null;
            BoneWeight[]? allW = joints != null && MiniJson.Has(attrs, "JOINTS_0") && MiniJson.Has(attrs, "WEIGHTS_0") ? Weights(g, attrs, allV.Length) : null;

            // Remap to the vertices this primitive uses.
            var remap = new Dictionary<int, int>();
            var used = new List<int>();
            var tris = new int[idx.Length];
            for (int k = 0; k < idx.Length; k++)
            {
                int v = (int)idx[k];
                if (!remap.TryGetValue(v, out int n)) { n = used.Count; remap[v] = n; used.Add(v); }
                tris[k] = n;
            }
            for (int k = 0; k < tris.Length; k += 3) { int t = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = t; } // reverse winding

            var um = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new Vector3[used.Count];
            for (int k = 0; k < used.Count; k++) verts[k] = allV[used[k]];
            um.vertices = verts;
            if (allN != null) { var n = new Vector3[used.Count]; for (int k = 0; k < used.Count; k++) n[k] = allN[used[k]]; um.normals = n; }
            if (allUv != null)
            {
                var uvs = new Vector2[used.Count];
                for (int k = 0; k < used.Count; k++) uvs[k] = new Vector2(allUv[used[k] * 2], 1f - allUv[used[k] * 2 + 1]); // glTF UV origin is top-left
                um.uv = uvs;
            }
            if (allW != null)
            {
                var w = new BoneWeight[used.Count];
                for (int k = 0; k < used.Count; k++) w[k] = allW[used[k]];
                um.boneWeights = w;
                um.bindposes = bindposes;
            }
            um.SetTriangles(tris, 0);
            um.RecalculateBounds();
            return um;
        }

        /// <summary>
        /// Up to 8 influences (JOINTS_1/WEIGHTS_1). Unity skins with 4: keep the strongest 4 and rescale them to
        /// sum to 1 (dropping weight would pull vertices toward the origin).
        /// </summary>
        private static BoneWeight[] Weights(Glb g, Dictionary<string, object> attrs, int count)
        {
            bool eight = MiniJson.Has(attrs, "JOINTS_1") && MiniJson.Has(attrs, "WEIGHTS_1");
            float[] jt = ReadFloats(g, MiniJson.Int(attrs, "JOINTS_0")!.Value, 4);
            float[] wt = ReadFloats(g, MiniJson.Int(attrs, "WEIGHTS_0")!.Value, 4);
            float[]? jt1 = eight ? ReadFloats(g, MiniJson.Int(attrs, "JOINTS_1")!.Value, 4) : null;
            float[]? wt1 = eight ? ReadFloats(g, MiniJson.Int(attrs, "WEIGHTS_1")!.Value, 4) : null;
            var bw = new BoneWeight[count];
            var ji = new int[8];
            var wi = new float[8];
            for (int k = 0; k < count; k++)
            {
                int n = eight ? 8 : 4;
                for (int q = 0; q < n; q++)
                {
                    ji[q] = (int)(q < 4 ? jt[k * 4 + q] : jt1![k * 4 + q - 4]);
                    wi[q] = q < 4 ? wt[k * 4 + q] : wt1![k * 4 + q - 4];
                }
                for (int a = 0; a < 4; a++)
                    for (int c = a + 1; c < n; c++)
                        if (wi[c] > wi[a]) { (wi[a], wi[c]) = (wi[c], wi[a]); (ji[a], ji[c]) = (ji[c], ji[a]); }
                float sum = wi[0] + wi[1] + wi[2] + wi[3];
                float s = sum > 1e-6f ? 1f / sum : 0f;
                bw[k] = new BoneWeight
                {
                    boneIndex0 = ji[0], weight0 = wi[0] * s,
                    boneIndex1 = ji[1], weight1 = wi[1] * s,
                    boneIndex2 = ji[2], weight2 = wi[2] * s,
                    boneIndex3 = ji[3], weight3 = wi[3] * s,
                };
            }
            return bw;
        }

        internal static void LoadPng(Texture2D tex, byte[] png) => ImageConversion.LoadImage(tex, png, false);

        private static Matrix4x4 Mirror(Matrix4x4 m)
        {
            var f = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            return f * m * f;
        }

        private static Vector3[] ToVec3(float[] f)
        {
            var v = new Vector3[f.Length / 3];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return v;
        }

        /// <summary>Read an accessor as floats (any component type; normalized integers scaled to 0-1).</summary>
        private static float[] ReadFloats(Glb g, int accessor, int width)
        {
            object a = MiniJson.Arr(g.J, "accessors")![accessor];
            int count = MiniJson.Int(a, "count")!.Value;
            int type = MiniJson.Int(a, "componentType")!.Value;
            bool normalized = MiniJson.Bool(a, "normalized");
            object view = MiniJson.Arr(g.J, "bufferViews")![MiniJson.Int(a, "bufferView")!.Value];
            int size = type == 5126 || type == 5125 ? 4 : type == 5123 || type == 5122 ? 2 : 1;
            int stride = MiniJson.Int(view, "byteStride") ?? size * width;
            int offset = g.BinStart + (MiniJson.Int(view, "byteOffset") ?? 0) + (MiniJson.Int(a, "byteOffset") ?? 0);
            byte[] glb = g.Bytes;
            var o = new float[count * width];
            for (int i = 0; i < count; i++)
                for (int c = 0; c < width; c++)
                {
                    int p = offset + i * stride + c * size;
                    float v;
                    switch (type)
                    {
                        case 5126: v = BitConverter.ToSingle(glb, p); break;
                        case 5125: v = BitConverter.ToUInt32(glb, p); break;
                        case 5123: v = BitConverter.ToUInt16(glb, p); if (normalized) v /= 65535f; break;
                        case 5122: v = BitConverter.ToInt16(glb, p); if (normalized) v = Math.Max(v / 32767f, -1f); break;
                        case 5121: v = glb[p]; if (normalized) v /= 255f; break;
                        default: v = (sbyte)glb[p]; if (normalized) v = Math.Max(v / 127f, -1f); break;
                    }
                    o[i * width + c] = v;
                }
            return o;
        }
    }
}
