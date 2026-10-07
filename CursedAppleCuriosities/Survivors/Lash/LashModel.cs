using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Sits on the stand-in model (body and character-select display). When this player's Deadlock model is
    /// converted, Lash is put on the stand-in's feet facing its way, its renderers become the CharacterModel's
    /// renderers (hit flash, elite and item overlays, cloaking all work) and the stand-in's are hidden. The stand-in
    /// skeleton keeps animating underneath for item displays. Without Deadlock the stand-in simply stays.
    /// </summary>
    public class LashModelSwap : MonoBehaviour
    {
        public bool IsDisplay;

        private CharacterModel? _model;
        private CharacterBody? _body;
        private GameObject? _lash;
        private CharacterModel.RendererInfo[] _lashInfos = System.Array.Empty<CharacterModel.RendererInfo>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private float _nextTry;
        private int _tries;

        public LashAnimator? Animator { get; private set; }

        private void Awake()
        {
            _model = GetComponent<CharacterModel>();
            var skins = GetComponent<ModelSkinController>();
            if (skins != null) skins.onSkinApplied += _ => Reassert();
        }

        private void Start()
        {
            _body = _model != null ? _model.body : null;
            TryAttach();
            if (IsDisplay) DeadlockSounds.Play("lash_vo_select", transform.position);
        }

        private void Update()
        {
            // First launch: the model may still be converting. Keep trying for a few minutes, then give up.
            if (_lash == null && _tries < 200 && Time.unscaledTime >= _nextTry) { _nextTry = Time.unscaledTime + 1f; TryAttach(); }
        }

        private void TryAttach()
        {
            _tries++;
            GlbModel? template = DeadlockInstall.Model(LashData.Hero, LashSurvivor.MakeMaterial);
            if (template == null) return;

            Transform frame = transform.parent != null ? transform.parent : transform; // ModelBase: turned by CharacterDirection
            _lash = Instantiate(template.Root, transform, false);
            _lash.name = "CursedApple_Lash";

            // Face the frame's forward: Lash's hips run left to right along the frame's right.
            Quaternion yaw = Quaternion.identity;
            if (template.RestInRoot.TryGetValue("leg_upper_L", out Matrix4x4 ll) && template.RestInRoot.TryGetValue("leg_upper_R", out Matrix4x4 lr))
            {
                Vector3 right = (Vector3)lr.GetColumn(3) - (Vector3)ll.GetColumn(3);
                right.y = 0f;
                if (right.sqrMagnitude > 1e-6f) yaw = Quaternion.FromToRotation(right.normalized, Vector3.right);
            }
            float height = 1.9f * frame.lossyScale.y; // lobby display: follow the display's own scale
            Vector3 feet = transform.position;
            if (_body != null)
            {
                var capsule = _body.GetComponent<CapsuleCollider>();
                if (capsule != null) height = capsule.height * _body.transform.lossyScale.y * LashData.ModelHeightScale;
                feet = _body.footPosition;
            }
            _lash.transform.SetPositionAndRotation(feet, frame.rotation * yaw);
            float scale = height / Mathf.Max(0.1f, template.Height);
            _lash.transform.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, transform.lossyScale.x));
            _lash.SetActive(true);

            // Animations first: nothing below can leave Lash frozen in his bind pose again.
            Animator = _lash.AddComponent<LashAnimator>();
            Animator.Init(_body, frame, IsDisplay);

            var infos = new List<CharacterModel.RendererInfo>();
            foreach (Renderer r in _lash.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = gameObject.layer;
                infos.Add(new CharacterModel.RendererInfo
                {
                    renderer = r,
                    defaultMaterial = r.sharedMaterial,
                    defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ignoreOverlays = false,
                });
            }
            _lashInfos = infos.ToArray();

            // The muzzle on Lash's glove, findable by the gun state.
            var locator = GetComponent<ChildLocator>();
            Transform? muzzle = FindDeep(_lash.transform, LashData.MuzzleBone);
            // ChildLocators are locked after load (AddChild throws): the body reserves the slot at build time and the
            // muzzle is pointed at the glove here.
            try { if (locator != null && muzzle != null) SetChild(locator, LashSurvivor.MuzzleName, muzzle); }
            catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Lash] muzzle: " + e.Message); }

            // The stand-in's animator still drives its (hidden) skeleton: item displays hang off it.
            var standIn = GetComponent<UnityEngine.Animator>(); // (the stand-in's, not Lash's)
            if (standIn != null) standIn.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!IsDisplay)
            {
                try { MoveItemPoints(locator); }
                catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Lash] item points: " + e); }
            }
            try { Reassert(); }
            catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Lash] renderers: " + e); }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Lash] Deadlock model attached ({(IsDisplay ? "display" : "body")}, {_lashInfos.Length} renderers, scale {scale:0.00})");
        }

        /// <summary>After any skin is applied (it sets the stand-in's renderers), make Lash's renderers the model's again.</summary>
        private void Reassert()
        {
            if (_lash == null || _model == null) return;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(_lash.transform) || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (r.GetComponentInParent<ItemDisplay>() != null) continue; // items hang off the stand-in skeleton: keep them
                if (r.enabled) { r.enabled = false; _hidden.Add(r); }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _model.baseRendererInfos = _lashInfos;
            _model.forceUpdate = true;
        }

        private float _nextHide;

        private void LateUpdate()
        {
            if (_lash == null) return;
            // Skins, item displays and death effects can switch stand-in renderers back on.
            if (Time.unscaledTime >= _nextHide)
            {
                _nextHide = Time.unscaledTime + 0.5f;
                if (_model != null && _model.baseRendererInfos != _lashInfos) Reassert();
                AdoptItemDisplays();
            }
        }

        // ---------------- items on Lash ----------------

        /// <summary>Stand-in bone -> the anchor on Lash that replaced it in the ChildLocator.</summary>
        private readonly Dictionary<Transform, Transform> _itemAnchors = new Dictionary<Transform, Transform>();

        /// <summary>
        /// Item displays hang off ChildLocator points (Head, Chest, HandL...) authored for the stand-in skeleton,
        /// which is hidden. Each point is repointed (entry count can't change at runtime) to an anchor on Lash's
        /// matching bone, turned and scaled like the stand-in bone was, so items keep their authored offsets but
        /// ride on Lash.
        /// </summary>
        private void MoveItemPoints(ChildLocator? locator)
        {
            if (locator == null || _lash == null) return;
            var lashBones = new Dictionary<string, Transform>();
            foreach (Transform t in _lash.GetComponentsInChildren<Transform>(true)) if (!lashBones.ContainsKey(t.name)) lashBones[t.name] = t;
            var pairs = (ChildLocator.NameTransformPair[])PairsField.GetValue(locator);
            int moved = 0;
            for (int i = 0; i < pairs.Length; i++)
            {
                Transform standIn = pairs[i].transform;
                if (standIn == null || pairs[i].name == LashSurvivor.MuzzleName || standIn.IsChildOf(_lash.transform)) continue;
                string? lashName = LashBoneFor(pairs[i].name);
                if (lashName == null || !lashBones.TryGetValue(lashName, out Transform bone)) continue;
                if (!_itemAnchors.TryGetValue(standIn, out Transform anchor))
                {
                    anchor = new GameObject("LashItemAnchor_" + pairs[i].name).transform;
                    anchor.SetParent(bone, false);
                    anchor.SetPositionAndRotation(bone.position, standIn.rotation);
                    Vector3 want = standIn.lossyScale, have = bone.lossyScale;
                    anchor.localScale = new Vector3(want.x / Mathf.Max(1e-4f, have.x), want.y / Mathf.Max(1e-4f, have.y), want.z / Mathf.Max(1e-4f, have.z));
                    _itemAnchors[standIn] = anchor;
                }
                pairs[i].transform = anchor;
                moved++;
            }
            AdoptItemDisplays();
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Lash] {moved} item attach points moved onto Lash");
        }

        /// <summary>Item displays already hanging on the stand-in skeleton move to Lash's anchors (keeping their local offsets).</summary>
        private void AdoptItemDisplays()
        {
            foreach (var kv in _itemAnchors)
            {
                if (kv.Key == null || kv.Value == null) continue;
                for (int c = kv.Key.childCount - 1; c >= 0; c--)
                {
                    Transform child = kv.Key.GetChild(c);
                    if (child.GetComponent<ItemDisplay>() != null) child.SetParent(kv.Value, false);
                }
            }
        }

        /// <summary>Lash's bone for a stand-in ChildLocator name (Commando's: Head, Chest, ThighL, HandR, GunL...).</summary>
        private static string? LashBoneFor(string name)
        {
            string n = name.ToLowerInvariant();
            string side = n.EndsWith("l") || n.Contains("left") ? "_L" : n.EndsWith("r") || n.Contains("right") ? "_R" : "";
            if (n.Contains("head")) return "head";
            if (n.Contains("neck")) return "neck_0";
            if (n.Contains("chest") || n.Contains("upperbody")) return "spine_3";
            if (n.Contains("stomach") || n.Contains("spine")) return "spine_1";
            if (n.Contains("pelvis") || n.Contains("hip")) return "pelvis";
            if (side == "") return n == "base" || n == "root" ? "pelvis" : null;
            if (n.Contains("clavicle") || n.Contains("shoulder")) return "clavicle" + side;
            if (n.Contains("upperarm")) return "arm_upper" + side;
            if (n.Contains("lowerarm") || n.Contains("forearm") || n.Contains("elbow")) return "arm_lower" + side;
            if (n.Contains("hand") || n.Contains("gun") || n.Contains("muzzle")) return "hand" + side;
            if (n.Contains("thigh")) return "leg_upper" + side;
            if (n.Contains("calf") || n.Contains("shin") || n.Contains("knee")) return "leg_lower" + side;
            if (n.Contains("foot") || n.Contains("toe")) return "ankle" + side;
            return null;
        }

        private static readonly System.Reflection.FieldInfo PairsField =
            typeof(ChildLocator).GetField("transformPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>Repoint an existing ChildLocator entry (the entry count can't change at runtime).</summary>
        private static void SetChild(ChildLocator locator, string name, Transform t)
        {
            var pairs = (ChildLocator.NameTransformPair[])PairsField.GetValue(locator);
            for (int i = 0; i < pairs.Length; i++)
                if (pairs[i].name == name) { pairs[i].transform = t; return; }
        }

        private static Transform? FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Lash's animator on a body, for entity states (null when this player has no Deadlock model).</summary>
        public static LashAnimator? AnimatorOf(GameObject bodyObject)
        {
            var locator = bodyObject != null ? bodyObject.GetComponent<ModelLocator>() : null;
            Transform? model = locator != null ? locator.modelTransform : null;
            return model != null && model.TryGetComponent(out LashModelSwap swap) ? swap.Animator : null;
        }

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => AnimatorOf(bodyObject)?.Play(state);
    }

    /// <summary>
    /// Lash's Deadlock footsteps, on the stand-in skeleton's footstep animation events (the same events RoR2's
    /// FootstepHandler uses for dust; its own Wwise footstep is blanked).
    /// </summary>
    public class LashFootsteps : MonoBehaviour
    {
        private CharacterModel? _model;

        private void Awake() => _model = GetComponent<CharacterModel>();

        public void Footstep(AnimationEvent e) => Step(); // same signature as FootstepHandler's animation event

        private void Step()
        {
            CharacterBody? body = _model != null ? _model.body : null;
            if (body == null || body.characterMotor == null || !body.characterMotor.isGrounded) return;
            DeadlockSounds.Play("lash_footstep", body.footPosition, body.gameObject);
        }
    }

    /// <summary>
    /// Plays Lash's own Deadlock clips on his Deadlock skeleton (ported from HeroBrew's HeroAnimator). Locomotion
    /// comes from how the body moves; one-shots are requested by entity states, which run on every client.
    /// </summary>
    public class LashAnimator : MonoBehaviour
    {
        private class Row { public string State = "", Clip = "", Next = "", Layer = ""; public bool Loop; }
        private class Layer { public Row Row = null!; public AnimClip Clip = null!; public float T; public bool Valid; }

        private CharacterBody? _body;
        private Transform _frame = null!;
        private bool _display;
        private Dictionary<string, AnimClip>? _clips;
        private readonly Dictionary<string, Row> _states = new Dictionary<string, Row>();
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<AnimClip, Dictionary<string, AnimTrack>> _index = new Dictionary<AnimClip, Dictionary<string, AnimTrack>>();
        private Layer _cur = new Layer(), _prev = new Layer(), _over = new Layer();
        private float _fade = 1f, _fadeTime = 0.15f, _overWeight, _stateTime, _airTime, _groundedFor, _nextClipTry;
        private Vector3 _lastPos, _vel;
        private bool _wasGrounded = true;
        private Transform? _spine1, _spine2, _spine3;

        public bool Ready => _clips != null && _cur.Valid;

        public void Init(CharacterBody? body, Transform frame, bool display)
        {
            _body = body;
            _frame = frame;
            _display = display;
            foreach (var a in LashData.Anims) _states[a.State] = new Row { State = a.State, Clip = a.Clip, Loop = a.Loop, Next = a.Next, Layer = a.Layer };
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
            _bones.TryGetValue("spine_1", out _spine1);
            _bones.TryGetValue("spine_2", out _spine2);
            _bones.TryGetValue("spine_3", out _spine3);
            _lastPos = transform.position;
            TryClips();
        }

        private void TryClips()
        {
            if (_clips != null) return;
            _clips = DeadlockInstall.Clips(LashData.Hero);
            if (_clips != null && _clips.Count == 0) _clips = null;
            if (_clips != null) Switch("idle", 0f);
        }

        // ---- states ----

        public void Play(string id)
        {
            if (_clips == null || !_states.TryGetValue(id, out Row row)) return;
            if (row.Layer == "additive") { SetLayer(_over, row); _overWeight = Mathf.Max(_overWeight, 0.01f); }
            else Switch(id, 0.08f);
        }

        /// <summary>Only while just moving around (doesn't cut into an ability), e.g. a jump or landing.</summary>
        private void PlaySoft(string id)
        {
            if (_cur.Valid && !IsLocomotion(_cur.Row.State) && _cur.Row.State != "in_air" && _cur.Row.State != "falling") return;
            Play(id);
        }

        private bool SetLayer(Layer l, Row row)
        {
            l.Row = row;
            l.T = 0f;
            l.Valid = _clips!.TryGetValue(row.Clip, out l.Clip);
            return l.Valid;
        }

        private bool Switch(string id, float blend)
        {
            if (!_states.TryGetValue(id, out Row row)) return false;
            if (_cur.Valid && _cur.Row.State == id && row.Loop) return true; // already looping it
            var old = _prev;
            _prev = _cur;
            _cur = old;
            if (!SetLayer(_cur, row)) { var swap = _prev; _prev = _cur; _cur = swap; return false; }
            _fadeTime = Mathf.Max(0.01f, blend);
            _fade = _prev.Valid ? 0f : 1f;
            _stateTime = 0f;
            return true;
        }

        private static bool IsLocomotion(string id) => id == "idle" || id.StartsWith("run_") || id.StartsWith("sprint_");

        private bool Grounded => _body == null || _body.characterMotor == null || _body.characterMotor.isGrounded;
        private bool Dead => _body != null && _body.healthComponent != null && !_body.healthComponent.alive;

        private string Locomotion(out float speedScale)
        {
            speedScale = 1f;
            if (!Grounded && _groundedFor <= 0f) return _vel.y > 0.5f ? "in_air" : "falling";
            Vector3 v = _vel;
            v.y = 0f;
            if (v.magnitude < 0.6f) return "idle";
            float move = _body != null ? Mathf.Max(1f, _body.moveSpeed) : LashData.BaseSpeed;
            speedScale = Mathf.Clamp(v.magnitude / move, 0.5f, 1.8f);
            Vector3 fwd = _frame.forward;
            fwd.y = 0f;
            float angle = Vector3.SignedAngle(fwd, v, Vector3.up);
            string[] dirs = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };
            string dir = dirs[((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8];
            bool sprint = _body != null && _body.isSprinting && dir != "s" && dir != "se" && dir != "sw";
            return (sprint ? "sprint_" : "run_") + dir;
        }

        private float Decide(float dt)
        {
            bool grounded = Grounded;
            _groundedFor = grounded ? _groundedFor + dt : 0f;
            _airTime = grounded ? 0f : _airTime + dt;
            _stateTime += dt;
            string id = _cur.Row.State;

            if (Dead) { if (id != "death") Switch("death", 0.1f); return 1f; }

            // Jumps and landings, read from movement (works for every client).
            if (_wasGrounded && !grounded && _vel.y > 2f) { PlaySoft("jump"); if (_body != null) DeadlockSounds.Play("lash_jump", transform.position, _body.gameObject); }
            if (grounded && !_wasGrounded && _airTime <= 0f && _lastAir > 0.35f)
            {
                PlaySoft(_lastFallSpeed < -18f ? "hard_land" : "land");
                if (_body != null) DeadlockSounds.Play("lash_land", transform.position, _body.gameObject);
            }
            _wasGrounded = grounded;

            string loco = Locomotion(out float scale);
            if (!_cur.Row.Loop && _cur.T >= _cur.Clip.Length)
            {
                string next = _cur.Row.Next;
                if (next == "hold") { if (_stateTime > 3f) Switch(loco, 0.2f); }
                else if (!Switch(next == "locomotion" ? loco : next, 0.15f)) Switch(loco, 0.15f); // never freeze on a missing clip
                return 1f;
            }
            if (!_cur.Row.Loop) return 1f; // one-shot still playing
            switch (id)
            {
                case "grapple_air": if (grounded && _stateTime > 0.2f) Switch("grapple_land", 0.05f); break;
                case "gs_dive": if (_groundedFor > 0.3f) Switch(loco, 0.1f); break;
                case "slide": if (!grounded && _airTime > 0.3f) Switch(loco, 0.15f); break; // the slide state asks for its getup
                case "ds_channel": if (_stateTime > LashData.DeathSlamChannel + 0.5f) Switch(loco, 0.15f); break;
                case "in_air":
                case "falling":
                    if (grounded && _groundedFor > 0.05f) Switch(loco, 0.1f);
                    else if (id != loco && (loco == "in_air" || loco == "falling")) Switch(loco, 0.25f);
                    break;
                default:
                    if (IsLocomotion(id) && loco != id) Switch(loco, 0.15f);
                    break;
            }
            return IsLocomotion(_cur.Row.State) && _cur.Row.State != "idle" ? scale : 1f;
        }

        private float _lastAir, _lastFallSpeed;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_clips == null)
            {
                if (Time.unscaledTime >= _nextClipTry) { _nextClipTry = Time.unscaledTime + 2f; TryClips(); }
                if (_clips == null) return;
            }
            // Velocity from how the model actually moved: right for remote players too.
            Vector3 v = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            if (v.magnitude < 60f) _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt * 12f));
            if (!Grounded) { _lastAir = _airTime + dt; _lastFallSpeed = Mathf.Min(_lastFallSpeed, _vel.y); }
            else if (_groundedFor > 0.2f) _lastFallSpeed = 0f;

            float scale = _display ? 1f : Decide(dt);
            Advance(_cur, dt * scale);
            if (_prev.Valid) Advance(_prev, dt);
            _fade = Mathf.Min(1f, _fade + dt / _fadeTime);
            if (_over.Valid)
            {
                Advance(_over, dt);
                bool done = _over.T >= _over.Clip.Length;
                _overWeight = Mathf.MoveTowards(_overWeight, done ? 0f : 1f, dt / 0.08f);
                if (done && _overWeight <= 0f) _over.Valid = false;
            }
            Apply();
            AimPitch();
        }

        private static void Advance(Layer l, float dt)
        {
            if (!l.Valid) return;
            l.T += dt;
            if (l.Row.Loop && l.Clip.Length > 0f) l.T %= l.Clip.Length;
            else l.T = Mathf.Min(l.T, l.Clip.Length);
        }

        private void Apply()
        {
            float w = _prev.Valid ? Mathf.SmoothStep(0f, 1f, _fade) : 1f;
            foreach (AnimTrack tr in _cur.Clip.Tracks)
            {
                if (!_bones.TryGetValue(tr.Bone, out Transform bone)) continue;
                bool hasR = tr.SampleRot(_cur.T, out Quaternion r);
                bool hasP = SamplePos(_cur, tr, out Vector3 p);
                if (w < 1f && _prev.Valid && Find(_prev.Clip, tr.Bone) is AnimTrack pt)
                {
                    if (hasR && pt.SampleRot(_prev.T, out Quaternion pr)) r = Quaternion.Slerp(pr, r, w);
                    if (hasP && SamplePos(_prev, pt, out Vector3 pp)) p = Vector3.Lerp(pp, p, w);
                }
                if (_overWeight > 0f && _over.Valid && Find(_over.Clip, tr.Bone) is AnimTrack ut)
                {
                    // Deadlock's additive clips store offsets on top of the current pose.
                    if (hasR && ut.SampleRot(_over.T, out Quaternion ur))
                    {
                        // Root-level bones carry the converter's axis turn instead of an offset: read them relative to the first frame.
                        if (Quaternion.Angle(Quaternion.identity, ut.Rot![0]) > 45f) ur = Quaternion.Inverse(ut.Rot[0]) * ur;
                        r = r * Quaternion.Slerp(Quaternion.identity, ur, _overWeight);
                    }
                    if (hasP && ut.SamplePos(_over.T, out Vector3 up)) p += up * _overWeight;
                }
                if (hasR) bone.localRotation = r;
                if (hasP) bone.localPosition = p;
            }
            if (w >= 1f) _prev.Valid = false;
        }

        /// <summary>
        /// A bone's position in a layer. Root-locked clips (Deadlock moves heroes with these animations; RoR2 moves
        /// the body itself) keep the pelvis at its first-frame horizontal spot, or the model runs ahead of Lash.
        /// </summary>
        private static bool SamplePos(Layer l, AnimTrack t, out Vector3 p)
        {
            if (!t.SamplePos(l.T, out p)) return false;
            if (t.Bone == "pelvis" && t.Pos != null && t.Pos.Length > 0 && LashData.RootLocked(l.Row.State)) { p.x = t.Pos[0].x; p.z = t.Pos[0].z; }
            return true;
        }

        /// <summary>Bend the spine toward where the player aims (Deadlock clips only aim straight ahead).</summary>
        private void AimPitch()
        {
            if (_body == null || _body.inputBank == null || Dead || _spine1 == null) return;
            if (!IsLocomotion(_cur.Row.State) && _cur.Row.State != "in_air" && _cur.Row.State != "falling") return;
            Vector3 aim = _body.inputBank.aimDirection;
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg, -70f, 70f);
            Vector3 right = _frame.right;
            Quaternion q = Quaternion.AngleAxis(-pitch / 3f, right);
            foreach (Transform? s in new[] { _spine1, _spine2, _spine3 })
                if (s != null) s.rotation = q * s.rotation;
        }

        private AnimTrack? Find(AnimClip clip, string bone)
        {
            if (!_index.TryGetValue(clip, out var map))
            {
                map = new Dictionary<string, AnimTrack>();
                foreach (AnimTrack t in clip.Tracks) map[t.Bone] = t;
                _index[clip] = map;
            }
            return map.TryGetValue(bone, out AnimTrack tr) ? tr : null;
        }
    }
}
