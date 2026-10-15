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
        /// <summary>Per extra skin (skin index 1, 2...): base material -> that skin's material. Materials not listed stay.</summary>
        public readonly List<Dictionary<Material, Material>> SkinMaterials = new List<Dictionary<Material, Material>>();
    }

    /// <summary>A GLB model read into plain data (no Unity objects), so it can be read off the main thread.</summary>
    public class GlbData
    {
        public struct Node { public string Name; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; public int Parent; }
        /// <summary>Skins: per extra skin, its recoloured texture (null: the base texture).</summary>
        public class Mat { public string Name = ""; public TexData? Tex; public TexData?[] Skins = Array.Empty<TexData?>(); }
        public class Prim
        {
            public string Name = "";
            public int Material;
            public Vector3[] V = null!;
            public Vector3[]? N;
            public Vector2[]? Uv;
            public BoneWeight[]? W;
            public int[] Tris = null!;
        }

        public string Name = "";
        public Node[] Nodes = Array.Empty<Node>();
        public readonly List<Mat> Mats = new List<Mat>();
        public int[]? Joints;
        public Matrix4x4[]? Bindposes;
        public readonly List<Prim> Prims = new List<Prim>();
        public float Height;
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
            // Rest translations (the converter's axes), for the bones PinStrayBones puts back.
            var restPos = new Dictionary<string, Vector3>();
            var restRot = new Dictionary<string, Quaternion>();
            foreach (object n in nodes)
            {
                if (MiniJson.Str(n, "name") is not string nn) continue;
                if (MiniJson.Arr(n, "translation") is List<object> t && t.Count == 3)
                    restPos[nn] = new Vector3(-Convert.ToSingle(t[0]), Convert.ToSingle(t[1]), Convert.ToSingle(t[2]));
                restRot[nn] = MiniJson.Arr(n, "rotation") is List<object> q && q.Count == 4
                    ? new Quaternion(Convert.ToSingle(q[0]), -Convert.ToSingle(q[1]), -Convert.ToSingle(q[2]), Convert.ToSingle(q[3]))
                    : Quaternion.identity;
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
                PinStrayBones(clip, restPos, restRot, underRoot, besideRoot);
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
        /// A physics bone deep in the skeleton that a clip bakes in world space: Shiv's rage reliquary (reliquary_0, on his
        /// belt) carries all 12.7 m of dash_ground's travel, so it flew off him and snapped back, as Abrams' coat once did.
        /// Real bones stray at most ~3.7 m from where they start (Grey Talon's owl, weapon hands): past 5 m a track is put back at its rest spot.
        /// </summary>
        private static void PinStrayBones(AnimClip clip, Dictionary<string, Vector3> restPos, Dictionary<string, Quaternion> restRot, HashSet<string> underRoot, HashSet<string> besideRoot)
        {
            foreach (AnimTrack t in clip.Tracks)
            {
                if (t.Pos == null || t.Pos.Length < 2 || underRoot.Contains(t.Bone) || besideRoot.Contains(t.Bone)) continue;
                if (t.Bone.StartsWith("$cloth", StringComparison.Ordinal) || !restPos.TryGetValue(t.Bone, out Vector3 rest)) continue;
                float stray = 0f;
                foreach (Vector3 p in t.Pos) stray = Mathf.Max(stray, (p - t.Pos[0]).magnitude);
                if (stray < 5f) continue;
                for (int k = 0; k < t.Pos.Length; k++) t.Pos[k] = rest;
                if (t.Rot != null && restRot.TryGetValue(t.Bone, out Quaternion rr))
                    for (int k = 0; k < t.Rot.Length; k++) t.Rot[k] = rr; // its tilt was baked in too
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
        /// Load the model now (parse and build). makeMaterial turns a base-colour texture (or null) into the material each renderer uses.
        /// </summary>
        public static GlbModel Load(string file, Func<string, Texture2D?, Material> makeMaterial, ICollection<string> hideMeshes)
            => BuildNow(Parse(file, hideMeshes), makeMaterial);

        /// <summary>Load a model from GLB bytes (e.g. an embedded resource). External image files are not resolved.</summary>
        public static GlbModel Load(byte[] glb, string name, Func<string, Texture2D?, Material> makeMaterial)
            => BuildNow(Parse(Read(glb, name), name, null, Array.Empty<string>()), makeMaterial);

        public static GlbModel BuildNow(GlbData d, Func<string, Texture2D?, Material> makeMaterial)
        {
            var model = new GlbModel();
            foreach (bool _ in Build(d, makeMaterial, model)) { }
            return model;
        }

        /// <summary>
        /// Everything that doesn't make Unity objects: safe on a background thread (texture decoding included). Each of
        /// skins (the hero's extra skins) gets its recolour of every texture.
        /// </summary>
        public static GlbData Parse(string file, ICollection<string> hideMeshes, IList<DeadlockSkin>? skins = null)
            => Parse(Read(file), Path.GetFileNameWithoutExtension(file), Path.GetDirectoryName(file), hideMeshes, skins);

        private static GlbData Parse(Glb g, string name, string? dir, ICollection<string> hideMeshes, IList<DeadlockSkin>? skinRecolors = null)
        {
            var j = g.J;
            var d = new GlbData { Name = name };

            // Nodes (mirrored X).
            var nodes = MiniJson.Arr(j, "nodes")!;
            d.Nodes = new GlbData.Node[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                object n = nodes[i];
                var node = new GlbData.Node { Name = MiniJson.Str(n, "name") ?? $"node{i}", Rot = Quaternion.identity, Scale = Vector3.one, Parent = -1 };
                if (MiniJson.Arr(n, "translation") is List<object> t) node.Pos = new Vector3(-MiniJson.F(t[0]), MiniJson.F(t[1]), MiniJson.F(t[2]));
                if (MiniJson.Arr(n, "rotation") is List<object> r) node.Rot = new Quaternion(MiniJson.F(r[0]), -MiniJson.F(r[1]), -MiniJson.F(r[2]), MiniJson.F(r[3]));
                if (MiniJson.Arr(n, "scale") is List<object> sc) node.Scale = new Vector3(MiniJson.F(sc[0]), MiniJson.F(sc[1]), MiniJson.F(sc[2]));
                d.Nodes[i] = node;
            }
            for (int i = 0; i < nodes.Count; i++)
                if (MiniJson.Arr(nodes[i], "children") is List<object> kids)
                    foreach (object k in kids) d.Nodes[(int)(double)k].Parent = i;

            // Materials (base colour only).
            var images = MiniJson.Arr(j, "images");
            var texs = MiniJson.Arr(j, "textures");
            var byImage = new Dictionary<int, TexData>();
            var skinsByImage = new Dictionary<int, TexData?[]>();
            int skinCount = skinRecolors?.Count ?? 0;
            foreach (object m in MiniJson.Arr(j, "materials") ?? new List<object>())
            {
                var mat = new GlbData.Mat { Name = MiniJson.Str(m, "name") ?? "mat" };
                int? texIndex = MiniJson.Int(MiniJson.Obj(MiniJson.Obj(m, "pbrMetallicRoughness"), "baseColorTexture"), "index");
                if (texIndex != null && texs != null && images != null)
                {
                    int img = MiniJson.Int(texs[texIndex.Value], "source") ?? 0;
                    if (!byImage.TryGetValue(img, out TexData tex))
                    {
                        string? uri = MiniJson.Str(images[img], "uri");
                        string? path = uri != null && dir != null ? Path.Combine(dir, uri) : null;
                        bool found = path != null && File.Exists(path);
                        tex = found ? HeroTextures.Prepare(path!) : new TexData { Key = name + "/" + img, Name = uri ?? "tex" };
                        byImage[img] = tex;
                        var skinTex = new TexData?[skinCount];
                        for (int k = 0; k < skinCount && found; k++)
                            if (skinRecolors![k].Applies(Path.GetFileName(path!)))
                                skinTex[k] = HeroTextures.PrepareSkin(path!, skinRecolors[k].Id, skinRecolors[k].Recolor);
                        skinsByImage[img] = skinTex;
                    }
                    mat.Tex = tex;
                    mat.Skins = skinsByImage[img];
                }
                d.Mats.Add(mat);
            }

            // Skin: joints and their rest pose (inverse of the mirrored inverse-bind matrix).
            var skins = MiniJson.Arr(j, "skins");
            object? skin = skins != null && skins.Count > 0 ? skins[0] : null;
            if (skin != null)
            {
                var jointIdx = MiniJson.Arr(skin, "joints")!;
                d.Joints = new int[jointIdx.Count];
                for (int k = 0; k < d.Joints.Length; k++) d.Joints[k] = (int)(double)jointIdx[k];
                float[] ibm = ReadFloats(g, MiniJson.Int(skin, "inverseBindMatrices")!.Value, 16);
                d.Bindposes = new Matrix4x4[d.Joints.Length];
                for (int k = 0; k < d.Joints.Length; k++)
                {
                    var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m[r, c] = ibm[k * 16 + c * 4 + r]; // column-major
                    d.Bindposes[k] = Mirror(m);
                }
            }

            // Meshes: one renderer per primitive.
            var meshes = MiniJson.Arr(j, "meshes")!;
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
                    GlbData.Prim p = ParsePrim(g, prim, attrs, d.Joints != null, meshName + "_" + primIndex);
                    p.Material = MiniJson.Int(prim, "material") ?? -1;
                    d.Prims.Add(p);
                    foreach (Vector3 v in p.V) d.Height = Mathf.Max(d.Height, v.y);
                    primIndex++;
                }
            }
            return d;
        }

        /// <summary>
        /// Main thread: the Unity objects, yielding after each new texture and each mesh so the work can be spread over
        /// frames. The model's Root is set only by the last step.
        /// </summary>
        public static IEnumerable<bool> Build(GlbData d, Func<string, Texture2D?, Material> makeMaterial, GlbModel model)
        {
            var textures = new Dictionary<TexData, Texture2D>();
            foreach (GlbData.Mat m in d.Mats)
                if (m.Tex != null && !textures.ContainsKey(m.Tex))
                {
                    bool fresh = !HeroTextures.IsMade(m.Tex.Key);
                    textures[m.Tex] = HeroTextures.Make(m.Tex);
                    if (fresh) yield return true;
                }
            var materials = new List<Material>();
            foreach (GlbData.Mat m in d.Mats) materials.Add(makeMaterial(m.Name, m.Tex != null ? textures[m.Tex] : null));

            // Extra skins: a material of their own wherever their texture differs.
            int skins = 0;
            foreach (GlbData.Mat m in d.Mats) skins = Math.Max(skins, m.Skins.Length);
            for (int k = 0; k < skins; k++)
            {
                var swap = new Dictionary<Material, Material>();
                for (int i = 0; i < d.Mats.Count; i++)
                {
                    TexData? st = k < d.Mats[i].Skins.Length ? d.Mats[i].Skins[k] : null;
                    if (st == null || swap.ContainsKey(materials[i])) continue;
                    bool fresh = !HeroTextures.IsMade(st.Key);
                    swap[materials[i]] = makeMaterial(d.Mats[i].Name, HeroTextures.Make(st));
                    if (fresh) yield return true;
                }
                model.SkinMaterials.Add(swap);
            }

            var root = new GameObject(d.Name);
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var tf = new Transform[d.Nodes.Length];
            for (int i = 0; i < d.Nodes.Length; i++)
            {
                GlbData.Node n = d.Nodes[i];
                tf[i] = new GameObject(n.Name).transform;
                tf[i].localPosition = n.Pos;
                tf[i].localRotation = n.Rot;
                tf[i].localScale = n.Scale;
            }
            for (int i = 0; i < d.Nodes.Length; i++) tf[i].SetParent(d.Nodes[i].Parent >= 0 ? tf[d.Nodes[i].Parent] : root.transform, false);

            Transform[]? joints = null;
            if (d.Joints != null)
            {
                joints = new Transform[d.Joints.Length];
                for (int k = 0; k < joints.Length; k++)
                {
                    joints[k] = tf[d.Joints[k]];
                    model.Bones[joints[k].name] = joints[k];
                    model.RestInRoot[joints[k].name] = d.Bindposes![k].inverse;
                }
            }

            foreach (GlbData.Prim p in d.Prims)
            {
                yield return true;
                var um = new Mesh { name = p.Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                um.vertices = p.V;
                if (p.N != null) um.normals = p.N;
                if (p.Uv != null) um.uv = p.Uv;
                if (p.W != null) { um.boneWeights = p.W; um.bindposes = d.Bindposes; }
                um.SetTriangles(p.Tris, 0);
                um.RecalculateBounds();
                Material mat = p.Material >= 0 && p.Material < materials.Count ? materials[p.Material] : materials[0];
                var holder = new GameObject(um.name);
                holder.transform.SetParent(root.transform, false);
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
            }
            model.Height = d.Height;
            model.Root = root;
        }

        /// <summary>One primitive as compact single-submesh mesh data (only the vertices its triangles use).</summary>
        private static GlbData.Prim ParsePrim(Glb g, object prim, Dictionary<string, object> attrs, bool skinned, string name)
        {
            float[] idx = ReadFloats(g, MiniJson.Int(prim, "indices")!.Value, 1);
            int pos = MiniJson.Int(attrs, "POSITION")!.Value;
            Vector3[] allV = ToVec3(ReadFloats(g, pos, 3));
            Vector3[]? allN = MiniJson.Has(attrs, "NORMAL") ? ToVec3(ReadFloats(g, MiniJson.Int(attrs, "NORMAL")!.Value, 3)) : null;
            float[]? allUv = MiniJson.Has(attrs, "TEXCOORD_0") ? ReadFloats(g, MiniJson.Int(attrs, "TEXCOORD_0")!.Value, 2) : null;
            BoneWeight[]? allW = skinned && MiniJson.Has(attrs, "JOINTS_0") && MiniJson.Has(attrs, "WEIGHTS_0") ? Weights(g, attrs, allV.Length) : null;

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

            var p = new GlbData.Prim { Name = name, Tris = tris, V = new Vector3[used.Count] };
            for (int k = 0; k < used.Count; k++) p.V[k] = allV[used[k]];
            if (allN != null) { p.N = new Vector3[used.Count]; for (int k = 0; k < used.Count; k++) p.N[k] = allN[used[k]]; }
            if (allUv != null)
            {
                p.Uv = new Vector2[used.Count];
                for (int k = 0; k < used.Count; k++) p.Uv[k] = new Vector2(allUv[used[k] * 2], 1f - allUv[used[k] * 2 + 1]); // glTF UV origin is top-left
            }
            if (allW != null) { p.W = new BoneWeight[used.Count]; for (int k = 0; k < used.Count; k++) p.W[k] = allW[used[k]]; }
            return p;
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
