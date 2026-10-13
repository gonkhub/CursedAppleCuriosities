using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Shocking Reanimation's charge-up, as Deadlock shows it (user): Victor's body parts float back together. Deadlock
    /// plays this on a separate model, frank_hover_death_anim_fx_model: its clip _02 (2.47 s) flies the scattered parts into
    /// his stance, and its first clip holds that stance hovering. While it plays, his own model is hidden; when it ends he
    /// comes back for ability_reanimation (the get-up). The parts glow green with a green light on them (user), which
    /// lingers through the get-up. Runs on every client (and in the lobby intro); visual only.
    /// </summary>
    public class VictorReassembly : MonoBehaviour
    {
        public static readonly Color Glow = new Color(0.45f, 1f, 0.35f);
        private const float Shrunk = 1e-4f;      // his model's scale while hidden (kept active so his animator runs on)
        private const float Reveal = 0.12f;      // the get-up plays this long under the parts before he shows: no stale-pose pop

        private static readonly HashSet<GameObject> HiddenHeroes = new HashSet<GameObject>();

        /// <summary>True while this hero model is hidden under the parts (e.g. the wire glow stays off).</summary>
        public static bool IsHidden(GameObject hero) => HiddenHeroes.Contains(hero);

        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly List<Material> _glowMats = new List<Material>();
        private AnimClip? _together, _hover;
        private float _age, _holdUntil, _revealAt = -1f;
        private GameObject? _hero;
        private DeadlockModelSwap? _swap;
        private Transform? _follow;
        private Vector3 _heroScale;
        private Light? _light;

        /// <summary>Sit exactly where his (hidden) model is: the body may still move or turn while he's down.</summary>
        private void Follow()
        {
            if (_follow == null) return;
            transform.SetPositionAndRotation(_follow.position, _follow.rotation);
        }

        /// <summary>
        /// Start the reassembly where the hero model stands, ending (parts together, then a short hover) after
        /// <paramref name="seconds"/>. Null when the model or its clips haven't been converted: the caller keeps his own clips.
        /// </summary>
        public static VictorReassembly? Spawn(GameObject modelRoot, float seconds)
        {
            var swap = modelRoot.GetComponentInChildren<DeadlockModelSwap>(true);
            GameObject? hero = swap != null ? swap.HeroObject : null;
            if (swap == null || hero == null) return null;
            GlbModel? model = DeadlockInstall.Model(VictorData.FxKey);
            Dictionary<string, AnimClip>? clips = DeadlockInstall.Clips(VictorData.FxKey);
            if (model == null || clips == null || !clips.TryGetValue(VictorData.FxTogether, out AnimClip together)) return null;
            clips.TryGetValue(VictorData.FxHover, out AnimClip hover);

            // The parts model is Victor's own mesh cut up: placed and scaled exactly as his model is. Not under his model
            // transform: DeadlockModelSwap switches off every renderer there that isn't his model (the stand-in's), which
            // hid it (playtest). It follows his model from the scene root instead, on his layer.
            GameObject fx = Instantiate(model.Root);
            fx.name = "VictorReassembly";
            var r = fx.AddComponent<VictorReassembly>();
            foreach (Renderer rend in fx.GetComponentsInChildren<Renderer>(true))
            {
                rend.gameObject.layer = hero.layer;
                rend.enabled = true;
                foreach (Material m in rend.materials) // instances: the glow is this copy's alone
                    if (m.HasProperty("_EmColor")) { m.SetColor("_EmColor", Glow); r._glowMats.Add(m); }
            }
            foreach (Transform t in fx.GetComponentsInChildren<Transform>(true)) if (!r._bones.ContainsKey(t.name)) r._bones[t.name] = t;
            r._together = together;
            r._hover = hover;
            r._holdUntil = Mathf.Max(together.Length, seconds);
            r._hero = hero;
            r._swap = swap;
            r._follow = hero.transform;
            fx.transform.localScale = hero.transform.lossyScale;
            r.Follow();

            var lightGo = new GameObject("VictorReassemblyLight");
            lightGo.transform.SetParent(fx.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.3f, 0f); // his chest, in model units
            r._light = lightGo.AddComponent<Light>();
            r._light.type = LightType.Point;
            r._light.color = Glow;
            r._light.range = 6f * hero.transform.lossyScale.y / 0.75f;
            r._light.intensity = 2.5f;

            // Hidden by scale, not deactivated: his animator keeps running, so the get-up starts from a live pose.
            r._heroScale = hero.transform.localScale;
            hero.transform.localScale = Vector3.one * Shrunk;
            HiddenHeroes.Add(hero);
            fx.SetActive(true);
            r.Pose(together, 0f);
            return r;
        }

        private void Update()
        {
            if (_follow == null) { Destroy(gameObject); return; } // his body is gone
            Follow();
            _age += Time.deltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_age * 7f);
            foreach (Material m in _glowMats) if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 0.8f + 1.2f * pulse);
            if (_light != null) _light.intensity = 1.8f + 1.4f * pulse;
            if (_together != null)
            {
                if (_age < _together.Length) Pose(_together, _age);
                else if (_hover != null && _hover.Length > 0f) Pose(_hover, (_age - _together.Length) % _hover.Length);
            }
            if (_revealAt >= 0f && Time.time >= _revealAt) { Show(); return; }
            if (_revealAt < 0f && _age > _holdUntil + 2f) Finish(); // never left behind if the end call got lost
        }

        private void Pose(AnimClip clip, float t)
        {
            foreach (AnimTrack track in clip.Tracks)
            {
                if (!_bones.TryGetValue(track.Bone, out Transform bone)) continue;
                if (track.SampleRot(t, out Quaternion q)) bone.localRotation = q;
                if (track.SamplePos(t, out Vector3 p)) bone.localPosition = p;
            }
        }

        /// <summary>
        /// The parts are together: start his get-up (ability_reanimation) under them, and show him a moment later, once his
        /// animator has left the pose he went down in (that blend was the stutter, playtest).
        /// </summary>
        public void Finish()
        {
            if (_revealAt >= 0f) return;
            _swap?.Animator?.Play("revive_up");
            _revealAt = Time.time + Reveal;
        }

        private void Show()
        {
            Restore();
            if (_light != null)
            {
                // The glow lingers on him and fades through the get-up.
                _light.transform.SetParent(null, true);
                _light.gameObject.AddComponent<VictorGlowFade>().Duration = VictorData.RevivePost + 0.3f;
                _light = null;
            }
            Destroy(gameObject);
        }

        private void Restore()
        {
            if (_hero == null) return;
            _hero.transform.localScale = _heroScale;
            HiddenHeroes.Remove(_hero);
            _hero = null;
        }

        private void OnDestroy() => Restore();
    }

    /// <summary>A light that fades out and removes itself (Reanimation's glow after the parts are gone).</summary>
    public class VictorGlowFade : MonoBehaviour
    {
        public float Duration = 1f;
        private Light? _light;
        private float _from, _age;

        private void Start() { _light = GetComponent<Light>(); _from = _light != null ? _light.intensity : 0f; }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_light != null) _light.intensity = Mathf.Lerp(_from, 0f, _age / Duration);
            if (_age >= Duration) Destroy(gameObject);
        }
    }
}
