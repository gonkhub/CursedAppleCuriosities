using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// What the shared model layer needs to know about one Deadlock hero: its model, its animation table and the few
    /// hero-specific animation rules. Registered once by the hero's survivor; components look it up by Hero key (a
    /// string survives Unity cloning the body prefab, a reference to this object would not).
    /// </summary>
    public class DeadlockHeroDef
    {
        public string Hero = "";
        public string DisplayName = "";
        public string DlModel = "";
        public string[] HideMeshes = Array.Empty<string>();
        public float ModelHeightScale = 1f;   // the hero's height over the stand-in capsule
        public float BaseSpeed = 7f;          // run speed when the body has none (display)
        /// <summary>The bone the gun fires from, and the ChildLocator slot the body reserved for it.</summary>
        public string? MuzzleBone;
        public string MuzzleName = "";

        /// <summary>State, Deadlock clip, loop, next state ("locomotion", "hold", a state, or "" for loops), layer ("full" / "additive" /
        /// "delta": a full clip played as an overlay, as its motion from its first frame; "upper": a full clip played over the
        /// locomotion from the spine up, the legs left running, as Deadlock's ability masks do).</summary>
        public (string State, string Clip, bool Loop, string Next, string Layer)[] Anims = Array.Empty<(string, string, bool, string, string)>();
        /// <summary>
        /// Clips whose own travel is removed (root lock): Deadlock moves heroes with these animations, RoR2
        /// moves the body itself, so the model would run ahead. Sprints are always locked.
        /// </summary>
        public HashSet<string> RootLocked = new HashSet<string>();
        /// <summary>How long a finished one-shot whose next is "hold" keeps its last frame (default 3 s).</summary>
        public Func<string, float>? HoldSeconds;
        /// <summary>Hero loop exits: given the looping state, the state to switch to (null = keep it).</summary>
        public Func<DeadlockAnimator.Ctx, string?>? LoopRule;
        /// <summary>
        /// A held weapon on its own bone chain (off root_motion, not under the arm): (weapon bone, hand bone), e.g.
        /// Abrams' ("weapon", "hand_R"). The clips already put its grip in the hand; this keeps the hand-to-weapon
        /// offset the clip gave it after the aim bend moves the hand. Null for a hero whose gun is part of the arm (Lash's glove).
        /// </summary>
        public (string Weapon, string Hand)? HandIK;
        /// <summary>
        /// While an overlay (the shot) plays, keep the grip the base pose had instead of the overlay's. Apollo's swing is a delta
        /// clip on both his arm and his sword's own chain: added onto another pose they disagree and the sword flicked a metre
        /// past his hand (user 2.5). His arm swings; the sword stays in his hand.
        /// </summary>
        public bool HandGripFromBase;
        /// <summary>
        /// The weapon sits exactly on the HandIK bone (no grip offset): Apollo's clips put his sword's root exactly on
        /// weapon_target_hand_R (0.1 deg apart in every clip), so it's snapped there each frame, whatever layers play.
        /// </summary>
        public bool HandSnap;
        /// <summary>
        /// Stand the idle over the spot the runs use: Ivy's idle holds her hips ~0.2 m to the side of where her run clips do, so she
        /// stepped sideways off her body whenever she stopped (user 2.5). The idle's pelvis is moved horizontally onto run_n's.
        /// </summary>
        public bool CenterIdle;
        /// <summary>A locomotion set, e.g. "siphon": locomotion state X becomes "siphon.X" when the table has it.</summary>
        public Func<CharacterBody, string?>? LocoSet;
        /// <summary>
        /// Fitted item attach points: ChildLocator name -> (hero bone, local position, angles, scale on that bone).
        /// Points not listed sit on the bone HeroBoneFor picks, turned like the stand-in's point at attach time.
        /// </summary>
        public Dictionary<string, (string Bone, Vector3 Pos, Vector3 Angles, Vector3 Scale)>? ItemAnchors;
        /// <summary>
        /// Material colour tints by glTF material name. Some Deadlock materials are a blank white texture coloured by a
        /// tint parameter (g_vColorTint1), which the converter drops: e.g. Vyper's gun (tengu_gun) came out white.
        /// </summary>
        public Dictionary<string, Color>? MaterialTints;
        /// <summary>Self-lit materials by glTF material name: flat colour that glows (e.g. Seven's Storm Cloud skeleton, blue_illum, made orange).</summary>
        public Dictionary<string, Color>? MaterialGlows;
        /// <summary>Extra skins, in skin order after the default (skin index 1, 2...): recolours of the hero's textures.</summary>
        public DeadlockSkin[] Skins = Array.Empty<DeadlockSkin>();

        public string Sound(string key) => Hero + "_" + key;
        public bool IsRootLocked(string state) => RootLocked.Contains(state) || state.StartsWith("sprint_") || state.Contains(".sprint_");

        public string[] ClipNames()
        {
            var l = new List<string>();
            foreach (var a in Anims) if (!l.Contains(a.Clip)) l.Add(a.Clip);
            return l.ToArray();
        }

        public DeadlockInstall.ModelRequest ModelRequest() =>
            new DeadlockInstall.ModelRequest { Hero = Hero, DlModel = DlModel, Clips = ClipNames(), HideMeshes = HideMeshes };
    }

    /// <summary>
    /// A hero skin made by recolouring the base-colour textures (Deadlock ships no alternate colours to load). Recolor
    /// edits RGBA pixels in place and says whether it changed any; it runs once per texture on the preloader thread and
    /// the result is cached under Id, so bump Id when the recolour changes.
    /// </summary>
    public class DeadlockSkin
    {
        public string Id = "";
        public Func<byte[], bool> Recolor = _ => false;
        /// <summary>Only textures whose file name contains one of these are recoloured (empty = every texture): a jacket can change without the skin tones.</summary>
        public string[] Only = Array.Empty<string>();

        public bool Applies(string file)
        {
            if (Only.Length == 0) return true;
            foreach (string part in Only) if (file.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Turns hues between fromLo and fromHi degrees by shift degrees (feathered over 15 degrees at each end),
        /// leaving greys (saturation under minSat) alone. fromLo may be negative for a range that wraps through red. Rotated about the grey axis, so brightness is kept.
        /// </summary>
        public static Func<byte[], bool> HueShift(float fromLo, float fromHi, float shift, float minSat = 0.12f) => px =>
        {
            const float Feather = 15f;
            bool changed = false;
            var mats = new Dictionary<int, (float, float, float)>(); // rotation by whole-degree amount
            for (int i = 0; i < px.Length; i += 4)
            {
                int r = px[i], g = px[i + 1], b = px[i + 2];
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                if (max == 0 || max - min < minSat * max) continue;
                float d = max - min, h;
                if (max == r) h = 60f * ((g - b) / d);
                else if (max == g) h = 60f * ((b - r) / d + 2f);
                else h = 60f * ((r - g) / d + 4f);
                if (h < 0f) h += 360f;
                if (fromLo < 0f && h > fromLo + 360f - Feather) h -= 360f; // a range across red (e.g. -25..20) wraps around 0
                float w = Mathf.Clamp01((h - (fromLo - Feather)) / Feather) * Mathf.Clamp01(((fromHi + Feather) - h) / Feather);
                if (w <= 0f) continue;
                int deg = Mathf.RoundToInt(shift * w);
                if (deg == 0) continue;
                if (!mats.TryGetValue(deg, out var m))
                {
                    float c = Mathf.Cos(deg * Mathf.Deg2Rad), sn = Mathf.Sin(deg * Mathf.Deg2Rad);
                    m = (c + (1f - c) / 3f, (1f - c) / 3f - Mathf.Sqrt(1f / 3f) * sn, (1f - c) / 3f + Mathf.Sqrt(1f / 3f) * sn);
                    mats[deg] = m;
                }
                (float a0, float a1, float a2) = m; // rotation about (1,1,1): rows are cyclic shifts of (a0, a1, a2)
                px[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(a0 * r + a1 * g + a2 * b), 0, 255);
                px[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(a2 * r + a0 * g + a1 * b), 0, 255);
                px[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(a1 * r + a2 * g + a0 * b), 0, 255);
                changed = true;
            }
            return changed;
        };
    }

    public static class DeadlockHeroes
    {
        private static readonly Dictionary<string, DeadlockHeroDef> Defs = new Dictionary<string, DeadlockHeroDef>();
        private static Material? _baseMaterial;

        public static void Register(DeadlockHeroDef def) => Defs[def.Hero] = def;
        public static DeadlockHeroDef? Get(string hero) => Defs.TryGetValue(hero, out DeadlockHeroDef d) ? d : null;

        /// <summary>
        /// A hero material: Commando's opaque deferred character material (RoR2's own shader, overlays and all) with
        /// the hero's base colour texture. A from-scratch hgstandard material renders see-through, so it is cloned.
        /// </summary>
        public static Material MakeMaterial(string hero, string name, Texture2D? tex)
        {
            if (_baseMaterial == null)
                _baseMaterial = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>("RoR2/Base/Commando/matCommandoDualies.mat").WaitForCompletion();
            Material m = new Material(_baseMaterial);
            m.name = "mat" + hero + "_" + name;
            m.mainTexture = tex;
            m.color = Color.white;
            foreach (string p in new[] { "_NormalTex", "_EmTex", "_FresnelRamp", "_PrintRamp", "_SplatmapTex", "_FlowHeightmap", "_FlowHeightRamp" })
                if (m.HasProperty(p)) m.SetTexture(p, null);
            if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 0f);
            if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0f);
            if (m.HasProperty("_SpecularStrength")) m.SetFloat("_SpecularStrength", 0.15f);
            m.DisableKeyword("FORCE_SPEC");
            return m;
        }

        /// <summary>
        /// The material for one of a model's glTF materials: the registered hero's tints and glows applied (a model key
        /// with no hero registered, e.g. Grey Talon's owl, gets the plain material). One recipe per model key, so the
        /// model loads the same whoever asks for it first.
        /// </summary>
        public static Material HeroMaterial(string key, string name, Texture2D? tex)
        {
            DeadlockHeroDef? def = Get(key);
            Material m = MakeMaterial(def != null ? def.DisplayName : key, name, tex);
            if (def?.MaterialTints != null && def.MaterialTints.TryGetValue(name, out Color tint)) m.color = tint;
            if (def?.MaterialGlows != null && def.MaterialGlows.TryGetValue(name, out Color glow))
            {
                m.mainTexture = null; // the colour comes from the glow, not the original (blue) texture
                m.color = glow;
                if (m.HasProperty("_EmColor")) m.SetColor("_EmColor", glow);
                if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 6f);
            }
            return m;
        }
    }

    /// <summary>
    /// Sits on the stand-in model (body and character-select display). When this player's Deadlock model is
    /// converted, the hero is put on the stand-in's feet facing its way, its renderers become the CharacterModel's
    /// renderers (hit flash, elite and item overlays, cloaking all work) and the stand-in's are hidden. The stand-in
    /// skeleton keeps animating underneath for item displays. Without Deadlock the stand-in simply stays.
    /// </summary>
    public class DeadlockModelSwap : MonoBehaviour
    {
        public string HeroKey = "";
        public bool IsDisplay;
        /// <summary>A display copy that must not say its select line (Paradox's intro keeps the last pick on the pedestal).</summary>
        public bool Silent;

        private DeadlockHeroDef? _def;
        private GlbModel? _template;
        private Material[] _baseMaterials = Array.Empty<Material>();
        private int _skin;
        private CharacterModel? _model;
        private CharacterBody? _body;
        private GameObject? _hero;

        /// <summary>The attached Deadlock model (null until it loads, or on the stand-in).</summary>
        public GameObject? HeroObject => _hero;
        private CharacterModel.RendererInfo[] _heroInfos = Array.Empty<CharacterModel.RendererInfo>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private float _nextTry;
        private int _tries;

        public DeadlockAnimator? Animator { get; private set; }

        private string Tag => "[" + (_def != null ? _def.DisplayName : HeroKey) + "]";

        private void Awake()
        {
            _model = GetComponent<CharacterModel>();
            var skins = GetComponent<ModelSkinController>();
            if (skins != null) skins.onSkinApplied += i => { _skin = i; ApplySkinMaterials(); Reassert(); };
        }

        private void Start()
        {
            _def = DeadlockHeroes.Get(HeroKey);
            _body = _model != null ? _model.body : null;
            if (_def == null) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] no hero registered as '{HeroKey}'"); enabled = false; return; }
            TryAttach();
            // A lobby intro says the line itself once the hero is on the pedestal (from the start position it was too far to hear).
            if (IsDisplay && !Silent && GetComponentInParent<DeadlockDisplayIntro>() == null) PlaySelectVoice(transform.position);
        }

        /// <summary>The hero's character-select line.</summary>
        public void PlaySelectVoice(Vector3 at)
        {
            if (_def != null) DeadlockSounds.Play(_def.Sound("vo_select"), at);
        }

        private void Update()
        {
            // The model may still be converting (first launch) or preloading. Keep trying for a few minutes, then give up.
            if (_hero == null && _tries < 3000 && Time.unscaledTime >= _nextTry) { _nextTry = Time.unscaledTime + 0.1f; TryAttach(); }
        }

        private void TryAttach()
        {
            if (_def == null) return;
            _tries++;
            DeadlockHeroDef def = _def;
            GlbModel? template = DeadlockInstall.Model(def.Hero);
            if (template == null)
            {
                // Say why the hero is the stand-in, to their own player only (or in the lobby).
                if (IsDisplay || (_body != null && _body.hasEffectiveAuthority && _body.isPlayerControlled))
                    DeadlockInstall.ShowModelNotice(def.Hero, def.DisplayName);
                return;
            }

            Transform frame = transform.parent != null ? transform.parent : transform; // ModelBase: turned by CharacterDirection
            _hero = Instantiate(template.Root, transform, false);
            _hero.name = "CursedApple_" + def.DisplayName;

            // Face the frame's forward: the hips run left to right along the frame's right.
            Quaternion yaw = Quaternion.identity;
            if (template.RestInRoot.TryGetValue("leg_upper_L", out Matrix4x4 ll) && template.RestInRoot.TryGetValue("leg_upper_R", out Matrix4x4 lr))
            {
                Vector3 right = (Vector3)lr.GetColumn(3) - (Vector3)ll.GetColumn(3);
                right.y = 0f;
                if (right.sqrMagnitude > 1e-6f) yaw = Quaternion.FromToRotation(right.normalized, Vector3.right);
            }
            float height = 1.9f * frame.lossyScale.y * def.ModelHeightScale / 1.08f; // lobby display: follow the display's own scale
            Vector3 feet = transform.position;
            if (_body != null)
            {
                var capsule = _body.GetComponent<CapsuleCollider>();
                if (capsule != null) height = capsule.height * _body.transform.lossyScale.y * def.ModelHeightScale;
                feet = _body.footPosition;
            }
            _hero.transform.SetPositionAndRotation(feet, frame.rotation * yaw);
            float scale = height / Mathf.Max(0.1f, template.Height);
            _hero.transform.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, transform.lossyScale.x));
            _hero.SetActive(true);

            // Animations first: nothing below can leave the hero frozen in the bind pose.
            Animator = _hero.AddComponent<DeadlockAnimator>();
            Animator.Init(def, _body, frame, IsDisplay);

            var infos = new List<CharacterModel.RendererInfo>();
            foreach (Renderer r in _hero.GetComponentsInChildren<Renderer>(true))
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
            _heroInfos = infos.ToArray();
            _template = template;
            _baseMaterials = new Material[_heroInfos.Length];
            for (int i = 0; i < _heroInfos.Length; i++) _baseMaterials[i] = _heroInfos[i].defaultMaterial;
            var msc = GetComponent<ModelSkinController>();
            if (msc != null) _skin = msc.currentSkinIndex;
            ApplySkinMaterials();

            // The muzzle on the hero's gun, findable by the gun state. ChildLocators are locked after load (AddChild
            // throws): the body reserves the slot at build time and it is pointed at the bone here.
            var locator = GetComponent<ChildLocator>();
            Transform? muzzle = def.MuzzleBone != null ? FindDeep(_hero.transform, def.MuzzleBone) : null;
            try { if (locator != null && muzzle != null) SetChild(locator, def.MuzzleName, muzzle); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} muzzle: " + e.Message); }

            // The stand-in's animator still drives its (hidden) skeleton: item displays hang off it.
            var standIn = GetComponent<UnityEngine.Animator>();
            if (standIn != null) standIn.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!IsDisplay)
            {
                try { MoveItemPoints(locator); }
                catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} item points: " + e); }
            }
            try { Reassert(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"{Tag} renderers: " + e); }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"{Tag} Deadlock model attached ({(IsDisplay ? "display" : "body")}, {_heroInfos.Length} renderers, scale {scale:0.00})");
        }

        /// <summary>The hero's materials for the current skin (the default skin, or an unknown index, gets the base ones).</summary>
        private void ApplySkinMaterials()
        {
            if (_hero == null || _template == null) return;
            Dictionary<Material, Material>? swap = _skin >= 1 && _skin - 1 < _template.SkinMaterials.Count ? _template.SkinMaterials[_skin - 1] : null;
            for (int i = 0; i < _heroInfos.Length; i++)
            {
                Material m = _baseMaterials[i];
                if (swap != null && swap.TryGetValue(m, out Material sm)) m = sm;
                _heroInfos[i].defaultMaterial = m;
                if (_heroInfos[i].renderer != null) _heroInfos[i].renderer.sharedMaterial = m;
            }
            if (_model != null) _model.forceUpdate = true;
        }

        /// <summary>After any skin is applied (it sets the stand-in's renderers), make the hero's renderers the model's again.</summary>
        private void Reassert()
        {
            if (_hero == null || _model == null) return;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(_hero.transform) || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (r.GetComponentInParent<ItemDisplay>() != null) continue; // items hang off the stand-in skeleton: keep them
                if (r.enabled) { r.enabled = false; _hidden.Add(r); }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _model.baseRendererInfos = _heroInfos;
            _model.forceUpdate = true;
        }

        private float _nextHide;

        private void LateUpdate()
        {
            if (_hero == null) return;
            // Skins, item displays and death effects can switch stand-in renderers back on.
            if (Time.unscaledTime >= _nextHide)
            {
                _nextHide = Time.unscaledTime + 0.5f;
                if (_model != null && _model.baseRendererInfos != _heroInfos) Reassert();
                AdoptItemDisplays();
            }
        }

        // ---------------- items on the hero ----------------

        /// <summary>Stand-in bone -> the anchor on the hero that replaced it in the ChildLocator.</summary>
        private readonly Dictionary<Transform, Transform> _itemAnchors = new Dictionary<Transform, Transform>();

        /// <summary>
        /// Item displays hang off ChildLocator points (Head, Chest, HandL...) authored for the stand-in skeleton,
        /// which is hidden. Each point is repointed (entry count can't change at runtime) to an anchor on the hero's
        /// matching bone, turned and scaled like the stand-in bone was, so items keep their authored offsets but
        /// ride on the hero.
        /// </summary>
        private void MoveItemPoints(ChildLocator? locator)
        {
            if (locator == null || _hero == null || _def == null) return;
            var heroBones = new Dictionary<string, Transform>();
            foreach (Transform t in _hero.GetComponentsInChildren<Transform>(true)) if (!heroBones.ContainsKey(t.name)) heroBones[t.name] = t;
            var pairs = (ChildLocator.NameTransformPair[])PairsField.GetValue(locator);
            int moved = 0;
            for (int i = 0; i < pairs.Length; i++)
            {
                Transform standIn = pairs[i].transform;
                if (standIn == null || pairs[i].name == _def.MuzzleName || standIn.IsChildOf(_hero.transform)) continue;
                if (_def.ItemAnchors != null && _def.ItemAnchors.TryGetValue(pairs[i].name, out var fit) && heroBones.TryGetValue(fit.Bone, out Transform fitBone))
                {
                    if (!_itemAnchors.TryGetValue(standIn, out Transform fitted))
                    {
                        fitted = new GameObject("HeroItemAnchor_" + pairs[i].name).transform;
                        fitted.SetParent(fitBone, false);
                        fitted.localPosition = fit.Pos;
                        fitted.localRotation = Quaternion.Euler(fit.Angles);
                        fitted.localScale = fit.Scale;
                        _itemAnchors[standIn] = fitted;
                    }
                    pairs[i].transform = fitted;
                    moved++;
                    continue;
                }
                string? heroName = HeroBoneFor(pairs[i].name);
                if (heroName == null || !heroBones.TryGetValue(heroName, out Transform bone)) continue;
                if (!_itemAnchors.TryGetValue(standIn, out Transform anchor))
                {
                    anchor = new GameObject("HeroItemAnchor_" + pairs[i].name).transform;
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
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"{Tag} {moved} item attach points moved onto the hero");
        }

        /// <summary>Item displays already hanging on the stand-in skeleton move to the hero's anchors (keeping their local offsets).</summary>
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

        /// <summary>The Deadlock bone for a stand-in ChildLocator name (Commando's: Head, Chest, ThighL, HandR, GunL...). Deadlock heroes share bone names.</summary>
        private static string? HeroBoneFor(string name)
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

        /// <summary>The hero's animator on a body, for entity states (null when this player has no Deadlock model).</summary>
        public static DeadlockAnimator? AnimatorOf(GameObject bodyObject)
        {
            var locator = bodyObject != null ? bodyObject.GetComponent<ModelLocator>() : null;
            Transform? model = locator != null ? locator.modelTransform : null;
            return model != null && model.TryGetComponent(out DeadlockModelSwap swap) ? swap.Animator : null;
        }

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => AnimatorOf(bodyObject)?.Play(state);
    }

    /// <summary>
    /// Fallback footsteps for a hero without an animated Deadlock model: the stand-in skeleton's footstep animation
    /// events (the same events RoR2's FootstepHandler uses for dust; its own Wwise footstep is blanked). With the
    /// model, DeadlockAnimator steps on the hero's own feet instead (the stand-in runs on Commando's stride, which
    /// never matched). Sound id "{hero}_footstep".
    /// </summary>
    public class DeadlockFootsteps : MonoBehaviour
    {
        public string HeroKey = "";
        private CharacterModel? _model;
        private DeadlockModelSwap? _swap;
        private EntityStateMachine? _machine;

        private void Awake() => _model = GetComponent<CharacterModel>();

        public void Footstep(AnimationEvent e) => Step(); // same signature as FootstepHandler's animation event

        private void Step()
        {
            if (_swap == null) _swap = GetComponent<DeadlockModelSwap>();
            if (_swap != null && _swap.Animator != null && _swap.Animator.Ready) return; // the hero's feet step instead
            CharacterBody? body = _model != null ? _model.body : null;
            if (body == null || body.characterMotor == null || !body.characterMotor.isGrounded) return;
            // The stand-in's animator keeps "running" under a slide, dash or ability: only real walking steps.
            if (_machine == null) _machine = EntityStateMachine.FindByCustomName(body.gameObject, "Body");
            if (_machine != null && !(_machine.state is EntityStates.CursedApple.Deadlock.DeadlockMain)) return;
            DeadlockSounds.Play(HeroKey + "_footstep", body.footPosition, body.gameObject);
        }
    }

    /// <summary>
    /// Plays a hero's own Deadlock clips on their Deadlock skeleton. Locomotion
    /// comes from how the body moves; one-shots are requested by entity states, which run on every client.
    /// </summary>
    public class DeadlockAnimator : MonoBehaviour
    {
        /// <summary>What a hero's LoopRule sees.</summary>
        public readonly struct Ctx
        {
            public readonly string State, Loco;
            public readonly bool Grounded;
            public readonly float GroundedFor, AirTime, StateTime;
            public Ctx(string state, string loco, bool grounded, float groundedFor, float airTime, float stateTime)
            { State = state; Loco = loco; Grounded = grounded; GroundedFor = groundedFor; AirTime = airTime; StateTime = stateTime; }
        }

        private class Row { public string State = "", Clip = "", Next = "", Layer = ""; public bool Loop; }
        private class Layer
        {
            public Row Row = null!; public AnimClip Clip = null!; public float T; public bool Valid;
            /// <summary>This frame: how far the pelvis lock pulled the pelvis back, in the cloth points' frame (see ClothLockShift).</summary>
            public Vector3 ClothShift;
        }

        private DeadlockHeroDef _def = null!;
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
        private Transform? _ikWeapon, _ikHand;
        private Quaternion _ikRot;
        private Vector3 _ikPos;
        private class Foot { public Transform Bone = null!; public float Lo, Hi; public bool Up, Tracking; }
        private Foot[] _feet = Array.Empty<Foot>();
        private float _legLength;

        public bool Ready => _clips != null && _cur.Valid;
        public string Current => _cur.Valid ? _cur.Row.State : "";

        public void Init(DeadlockHeroDef def, CharacterBody? body, Transform frame, bool display)
        {
            _def = def;
            _body = body;
            _frame = frame;
            _display = display;
            foreach (var a in def.Anims) _states[a.State] = new Row { State = a.State, Clip = a.Clip, Loop = a.Loop, Next = a.Next, Layer = a.Layer };
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
            _bones.TryGetValue("spine_1", out _spine1);
            _bones.TryGetValue("spine_2", out _spine2);
            _bones.TryGetValue("spine_3", out _spine3);
            if (def.HandIK is var (weapon, hand) && _bones.TryGetValue(weapon, out Transform w) && _bones.TryGetValue(hand, out Transform h))
                (_ikWeapon, _ikHand) = (w, h);
            var feet = new List<Foot>();
            foreach (string side in new[] { "_L", "_R" })
                if (_bones.TryGetValue("ankle" + side, out Transform ankle))
                {
                    feet.Add(new Foot { Bone = ankle });
                    if (_bones.TryGetValue("leg_upper" + side, out Transform hip)) _legLength = Mathf.Max(_legLength, Vector3.Distance(hip.position, ankle.position));
                }
            _feet = feet.ToArray();
            _lastPos = transform.position;
            TryClips();
        }

        private void TryClips()
        {
            if (_clips != null) return;
            _clips = DeadlockInstall.Clips(_def.Hero);
            if (_clips != null && _clips.Count == 0) _clips = null;
            if (_clips != null) Switch("idle", 0f);
        }

        // ---- states ----

        public void Play(string id) => Play(id, 0.08f);

        /// <summary>Play a state, crossfading a full-body one over <paramref name="blend"/> seconds (a lobby intro's slow settle).</summary>
        public void Play(string id, float blend)
        {
            if (id == "shoot") _lastShot = Time.time;
            if (_clips == null || !_states.TryGetValue(id, out Row row)) return;
            // An ability cast mid-slide plays over the slide pose (Deadlock masks casts to the upper body): as a full
            // pose it stood the hero up while the body kept sliding.
            bool overSlide = Sliding && !row.Loop && !IsMovement(id) && _states.ContainsKey("slide");
            if (row.Layer == "additive" || row.Layer == "delta" || row.Layer == "upper" || overSlide)
            {
                SetLayer(_over, overSlide && row.Layer == "full" ? new Row { State = row.State, Clip = row.Clip, Next = row.Next, Layer = "delta" } : row);
                _overWeight = Mathf.Max(_overWeight, 0.01f);
                if (overSlide && _cur.Row.State != "slide_start") Switch("slide", 0.1f);
            }
            else
            {
                // A looping overlay (Apollo's additive lunge aim) has no end of its own: a full-body state ends it.
                if (_over.Valid && _over.Row.Loop) _overEnding = true;
                Switch(id, blend);
            }
        }

        private EntityStateMachine? _bodyMachine;

        /// <summary>The body is in a Deadlock slide (the Body state machine runs on every client).</summary>
        private bool Sliding
        {
            get
            {
                if (_body == null) return false;
                if (_bodyMachine == null) _bodyMachine = EntityStateMachine.FindByCustomName(_body.gameObject, "Body");
                return _bodyMachine != null && _bodyMachine.state is EntityStates.CursedApple.Deadlock.DeadlockSlide;
            }
        }

        /// <summary>Movement states that take the whole body even mid-slide (the slide's own, jumps out of it, dashes, death).</summary>
        private static bool IsMovement(string id) =>
            id.StartsWith("slide") || id.StartsWith("dash") || id.EndsWith("jump") || id == "in_air" || id == "falling" || id == "land" || id == "hard_land" || id == "death";

        /// <summary>Only while just moving around (doesn't cut into an ability), e.g. a jump or landing.</summary>
        private void PlaySoft(string id)
        {
            if (_cur.Valid && !IsLocomotion(_cur.Row.State) && _cur.Row.State != "in_air" && _cur.Row.State != "falling") return;
            Play(id);
        }

        private bool _overEnding;

        private bool SetLayer(Layer l, Row row)
        {
            if (l == _over) _overEnding = false;
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

        /// <summary>Idle, runs and sprints, in any locomotion set ("siphon.run_n").</summary>
        private static bool IsLocomotion(string id)
        {
            int dot = id.LastIndexOf('.');
            string b = dot >= 0 ? id.Substring(dot + 1) : id;
            return b == "idle" || b.StartsWith("run_") || b.StartsWith("sprint_");
        }

        private bool Grounded => _body == null || _body.characterMotor == null || _body.characterMotor.isGrounded;
        private bool Dead => _body != null && _body.healthComponent != null && !_body.healthComponent.alive;

        /// <summary>Time.time of the last "shoot" (every client plays it): sprinting while shooting runs the gun-up run instead.</summary>
        private float _lastShot = -10f;
        private const float ShootRunHold = 0.4f;

        private string Locomotion(out float speedScale)
        {
            speedScale = 1f;
            if (!Grounded && _groundedFor <= 0f) return _vel.y > 0.5f ? "in_air" : "falling";
            string id;
            Vector3 v = _vel;
            v.y = 0f;
            if (v.magnitude < 0.6f) id = "idle";
            else
            {
                float move = _body != null ? Mathf.Max(1f, _body.moveSpeed) : _def.BaseSpeed;
                Vector3 fwd = _frame.forward;
                fwd.y = 0f;
                float angle = Vector3.SignedAngle(fwd, v, Vector3.up);
                string[] dirs = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };
                string dir = dirs[((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8];
                bool sprinting = _body != null && _body.isSprinting;
                // Sprinting while shooting (Seven's Power Surge, user): his gun-up run, sped up to the sprint's pace.
                bool shooting = sprinting && Time.time - _lastShot < ShootRunHold;
                if (shooting && _body!.sprintingSpeedMultiplier > 0f) move /= _body.sprintingSpeedMultiplier;
                speedScale = Mathf.Clamp(v.magnitude / move, 0.5f, 1.8f);
                bool sprint = sprinting && !shooting && dir != "s" && dir != "se" && dir != "sw";
                id = (sprint ? "sprint_" : "run_") + dir;
            }
            // A hero's locomotion set (Abrams runs differently while Siphon Life drains).
            string? set = _body != null && _def.LocoSet != null ? _def.LocoSet(_body) : null;
            if (set != null && _states.ContainsKey(set + "." + id)) return set + "." + id;
            return id;
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
            if (_wasGrounded && !grounded && _vel.y > 2f) { PlaySoft("jump"); if (_body != null) DeadlockSounds.Play(_def.Sound("jump"), transform.position, _body.gameObject); }
            if (grounded && !_wasGrounded && _airTime <= 0f && _lastAir > 0.35f)
            {
                PlaySoft(_lastFallSpeed < -18f && _states.ContainsKey("hard_land") ? "hard_land" : "land");
                if (_body != null) DeadlockSounds.Play(_def.Sound("land"), transform.position, _body.gameObject);
            }
            _wasGrounded = grounded;

            string loco = Locomotion(out float scale);
            // Whatever ends mid-slide (a full-body pose from before it, a loop rule) goes back to the slide, not a standing run.
            if (grounded && Sliding && _states.ContainsKey("slide") && id != "slide_start" && (IsLocomotion(id) || !_cur.Row.Loop && _cur.T >= _cur.Clip.Length))
            {
                Switch("slide", 0.15f);
                return 1f;
            }
            if (!_cur.Row.Loop && _cur.T >= _cur.Clip.Length)
            {
                string next = _cur.Row.Next;
                float holdFor = _def.HoldSeconds != null ? _def.HoldSeconds(id) : 3f; // held last frame
                if (next == "hold")
                {
                    // A held pose can end early by the hero's rule too (Grey Talon holds a jump pose until he lands).
                    string? held = _def.LoopRule?.Invoke(new Ctx(id, loco, grounded, _groundedFor, _airTime, _stateTime));
                    if (held != null) { if (!Switch(held, 0.1f)) Switch(loco, 0.1f); }
                    else if (_stateTime > holdFor) Switch(loco, 0.2f);
                }
                else if (!Switch(next == "locomotion" ? loco : next, 0.15f)) Switch(loco, 0.15f); // never freeze on a missing clip
                return 1f;
            }
            if (!_cur.Row.Loop) return 1f; // one-shot still playing
            string? rule = _def.LoopRule?.Invoke(new Ctx(id, loco, grounded, _groundedFor, _airTime, _stateTime));
            if (rule != null) { if (!Switch(rule, 0.1f)) Switch(loco, 0.1f); return 1f; }
            switch (id)
            {
                case "slide": if (!grounded && _airTime > 0.3f) Switch(loco, 0.15f); break; // the slide state asks for its getup
                case "in_air":
                case "falling":
                    if (grounded && _groundedFor > 0.05f) Switch(loco, 0.1f);
                    else if (id != loco && (loco == "in_air" || loco == "falling")) Switch(loco, 0.25f);
                    break;
                default:
                    if (IsLocomotion(id) && loco != id) Switch(loco, 0.15f);
                    break;
            }
            return IsLocomotion(_cur.Row.State) && !_cur.Row.State.EndsWith("idle") ? scale : 1f;
        }

        private float _lastAir, _lastFallSpeed;

        /// <summary>Lobby display (no body): a one-shot an intro plays runs on to its next state, and back to idle.</summary>
        private float DisplayStep()
        {
            if (_cur.Row.Loop || _cur.T < _cur.Clip.Length) return 1f;
            string next = _cur.Row.Next;
            if (next == "" || next == "hold" || next == "locomotion" || !Switch(next, 0.15f)) Switch("idle", 0.2f);
            return 1f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_clips == null)
            {
                if (Time.unscaledTime >= _nextClipTry) { _nextClipTry = Time.unscaledTime + 0.1f; TryClips(); }
                if (_clips == null) return;
            }
            // Velocity from how the model actually moved: right for remote players too.
            Vector3 v = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            if (v.magnitude < 60f) _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt * 12f));
            if (!Grounded) { _lastAir = _airTime + dt; _lastFallSpeed = Mathf.Min(_lastFallSpeed, _vel.y); }
            else if (_groundedFor > 0.2f) _lastFallSpeed = 0f;

            float scale = _display ? DisplayStep() : Decide(dt);
            Advance(_cur, dt * scale);
            if (_prev.Valid) Advance(_prev, dt);
            _fade = Mathf.Min(1f, _fade + dt / _fadeTime);
            if (_over.Valid)
            {
                Advance(_over, dt);
                bool done = _over.T >= _over.Clip.Length && !_over.Row.Loop || _overEnding;
                _overWeight = Mathf.MoveTowards(_overWeight, done ? 0f : 1f, dt / 0.08f);
                if (done && _overWeight <= 0f) _over.Valid = false;
            }
            Apply();
            GripCapture();
            AimPitch();
            HandIK();
            if (!_display) Footsteps(dt);
        }

        /// <summary>
        /// Footsteps on the hero's own feet: a step when an ankle comes back down to the bottom of its stride, so the
        /// sound lands with the visible foot at any clip speed. Each foot's stride range is learned as it runs
        /// (any hero size or gait); a foot must lift past 60% of it, then a plant is under 30%.
        /// </summary>
        private void Footsteps(float dt)
        {
            if (_body == null || _feet.Length == 0) return;
            string id = _cur.Row.State;
            bool walking = Grounded && !Dead && IsLocomotion(id) && !id.EndsWith("idle");
            float minRange = Mathf.Max(0.02f, _legLength * 0.08f);
            float relax = 1f - Mathf.Exp(-dt * 0.5f); // the range forgets slowly, for a gait change
            foreach (Foot f in _feet)
            {
                float h = f.Bone.position.y - transform.position.y;
                if (!walking || !f.Tracking) { f.Lo = f.Hi = h; f.Up = false; f.Tracking = walking; continue; }
                f.Lo = Mathf.Min(h, Mathf.Lerp(f.Lo, h, relax));
                f.Hi = Mathf.Max(h, Mathf.Lerp(f.Hi, h, relax));
                float range = f.Hi - f.Lo;
                if (range < minRange) continue;
                if (!f.Up) { if (h > f.Lo + range * 0.6f) f.Up = true; }
                else if (h < f.Lo + range * 0.3f)
                {
                    f.Up = false;
                    DeadlockSounds.Play(_def.Sound("footstep"), f.Bone.position, _body.gameObject);
                }
            }
        }

        /// <summary>The weapon in the hand's space as the clips posed it (before the aim bend moves the hand).</summary>
        private void GripCapture()
        {
            if (_ikWeapon == null || _ikHand == null) return;
            if (_def.HandSnap) { _ikPos = Vector3.zero; _ikRot = Quaternion.identity; return; }
            if (_def.HandGripFromBase && _overWeight > 0f) return; // the last base-pose grip stands
            Quaternion inv = Quaternion.Inverse(_ikHand.rotation);
            _ikRot = inv * _ikWeapon.rotation;
            _ikPos = inv * (_ikWeapon.position - _ikHand.position);
        }

        /// <summary>
        /// Carry the weapon with the hand: put it back at the clip's grip after the aim bend. (Not the hand's
        /// weapon_hand_R socket: that sits 19 cm and ~98 deg off the grip the clips use, which turned Abrams' gun sideways.)
        /// </summary>
        private void HandIK()
        {
            if (_ikWeapon == null || _ikHand == null) return;
            _ikWeapon.SetPositionAndRotation(_ikHand.position + _ikHand.rotation * _ikPos, _ikHand.rotation * _ikRot);
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
            _cur.ClothShift = ClothLockShift(_cur);
            if (_prev.Valid) _prev.ClothShift = ClothLockShift(_prev);
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
                if (_overWeight > 0f && _over.Valid && _over.Row.Layer == "upper" && Find(_over.Clip, tr.Bone) is AnimTrack mt)
                {
                    // An ability masked to the upper body (Victor's Pain Battery while running, user 2.5): its own pose from the
                    // spine up, blended in by the overlay weight; the hips and legs keep the run.
                    if (IsUpper(tr.Bone))
                    {
                        if (hasR && mt.SampleRot(_over.T, out Quaternion mr)) r = Quaternion.Slerp(r, mr, _overWeight);
                        if (hasP && mt.SamplePos(_over.T, out Vector3 mp)) p = Vector3.Lerp(p, mp, _overWeight);
                    }
                }
                else if (_overWeight > 0f && _over.Valid && Find(_over.Clip, tr.Bone) is AnimTrack ut)
                {
                    // Deadlock's additive clips store offsets on top of the current pose.
                    // Root-level bones carry the converter's axis turn instead of an offset, and Source 2 cloth points
                    // ($cloth_*, Abrams' coat) a reference pose: read those relative to the clip's first frame, or the
                    // pose is added on top and the coat stretches.
                    // A "delta" overlay is a full pose clip (Grey Talon's shoot_idle has no additive version): its motion
                    // relative to its own first frame goes on top, like the cloth.
                    bool cloth = tr.Bone.StartsWith("$cloth", StringComparison.Ordinal) || _over.Row.Layer == "delta";
                    if (hasR && ut.SampleRot(_over.T, out Quaternion ur))
                    {
                        if (cloth || Quaternion.Angle(Quaternion.identity, ut.Rot![0]) > 45f) ur = Quaternion.Inverse(ut.Rot![0]) * ur;
                        r = r * Quaternion.Slerp(Quaternion.identity, ur, _overWeight);
                    }
                    if (hasP && ut.SamplePos(_over.T, out Vector3 up))
                    {
                        // A true additive offset starts near zero. Some landing clips store a whole pose offset instead (Mina's
                        // and Ivy's pelvis sits 1.6-2 m off from frame one): read those relative to their first frame too, or
                        // the model jumps off the body on landing.
                        if (cloth || ut.Pos![0].sqrMagnitude > 0.25f) up -= ut.Pos![0];
                        p += up * _overWeight;
                    }
                }
                if (hasR) bone.localRotation = r;
                if (hasP) bone.localPosition = p;
            }
            if (w >= 1f) _prev.Valid = false;
        }

        /// <summary>
        /// A bone's position in a layer. Root-locked clips (Deadlock moves heroes with these animations; RoR2 moves
        /// the body itself) keep the pelvis at its first-frame horizontal spot, or the model runs ahead of the hero.
        /// </summary>
        private bool SamplePos(Layer l, AnimTrack t, out Vector3 p)
        {
            if (!t.SamplePos(l.T, out p)) return false;
            if (t.Bone == "pelvis" && t.Pos != null && t.Pos.Length > 0 && _def.IsRootLocked(l.Row.State))
            {
                // Pin the horizontal, keep the height. "Up" in the pelvis's parent frame: Apollo's pelvis hangs off a turned
                // root_motion, where height runs along local z (pinning x and z froze his height and let him slide: user 2.5).
                Vector3 up = PelvisUp();
                Vector3 d = p - t.Pos[0];
                p = t.Pos[0] + up * Vector3.Dot(d, up);
            }
            else if (l.ClothShift != Vector3.zero && t.Bone.StartsWith("$cloth", StringComparison.Ordinal)) p -= l.ClothShift;
            if (t.Bone == "pelvis" && _def.CenterIdle && l.Row.State == "idle") p -= IdleShift(t);
            return true;
        }

        private HashSet<string>? _upper;

        /// <summary>The bones from spine_1 up (the arms, the head, anything they carry): an "upper" overlay's half of the body.</summary>
        private bool IsUpper(string bone)
        {
            if (_upper == null)
            {
                _upper = new HashSet<string>();
                if (_bones.TryGetValue("spine_1", out Transform spine))
                    foreach (Transform t in spine.GetComponentsInChildren<Transform>(true)) _upper.Add(t.name);
            }
            return _upper.Contains(bone);
        }

        private Vector3? _idleShift;

        /// <summary>How far the idle's pelvis sits off run_n's, horizontally (see DeadlockHeroDef.CenterIdle).</summary>
        private Vector3 IdleShift(AnimTrack idlePelvis)
        {
            if (_idleShift.HasValue) return _idleShift.Value;
            Vector3 shift = Vector3.zero;
            if (_clips != null && _states.TryGetValue("run_n", out Row run) && _clips.TryGetValue(run.Clip, out AnimClip rc)
                && Find(rc, "pelvis") is AnimTrack rp && rp.Pos != null && rp.Pos.Length > 0 && idlePelvis.Pos != null && idlePelvis.Pos.Length > 0)
            {
                Vector3 up = PelvisUp();
                Vector3 d = idlePelvis.Pos[0] - rp.Pos[0];
                shift = d - up * Vector3.Dot(d, up);
            }
            _idleShift = shift;
            return shift;
        }

        private Vector3? _pelvisUp;

        /// <summary>World up in the pelvis's parent frame (the model's own frame, or a turned root_motion's).</summary>
        private Vector3 PelvisUp()
        {
            if (_pelvisUp.HasValue) return _pelvisUp.Value;
            Vector3 up = Vector3.up;
            if (_bones.TryGetValue("pelvis", out Transform pelvis) && pelvis.parent != null && pelvis.parent.name == "root_motion")
                up = (Quaternion.Inverse(pelvis.parent.localRotation) * Vector3.up).normalized;
            _pelvisUp = up;
            return up;
        }

        /// <summary>
        /// The pelvis lock pins the pelvis at its first-frame spot, but cloth points ($cloth_*, off the model root) carry
        /// the pelvis's lead over root_motion in a locked clip (Celeste's dash: 0.6 m mid-dash): they ran ahead of her
        /// body and snapped back, as Abrams' coat once did. They are pulled back by the same amount, turned into their
        /// frame (root_motion's resting turn: GlbLoader moved them into it).
        /// </summary>
        private Vector3 ClothLockShift(Layer l)
        {
            if (!l.Valid || !_def.IsRootLocked(l.Row.State)) return Vector3.zero;
            AnimTrack? pelvis = Find(l.Clip, "pelvis");
            if (pelvis == null || pelvis.Pos == null || pelvis.Pos.Length == 0 || !pelvis.SamplePos(l.T, out Vector3 pp)) return Vector3.zero;
            Vector3 d = new Vector3(pp.x - pelvis.Pos[0].x, 0f, pp.z - pelvis.Pos[0].z);
            if (d.sqrMagnitude < 1e-6f) return Vector3.zero;
            AnimTrack? root = Find(l.Clip, "root_motion");
            Quaternion turn = root != null && root.Rot != null && root.Rot.Length > 0 ? root.Rot[0]
                : _bones.TryGetValue("root_motion", out Transform rb) ? rb.localRotation : Quaternion.identity;
            return turn * d;
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
