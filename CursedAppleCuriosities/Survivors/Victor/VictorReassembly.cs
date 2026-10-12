using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Shocking Reanimation's charge-up, as Deadlock shows it (user): Victor's body parts float back together. Deadlock
    /// plays this on a separate model, frank_hover_death_anim_fx_model: its clip _02 (2.47 s) flies the scattered parts into
    /// his stance, and its first clip holds that stance hovering. While it plays, his own model is hidden; when it ends he
    /// comes back for ability_reanimation (the get-up). Runs on every client (and in the lobby intro); visual only.
    /// </summary>
    public class VictorReassembly : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private AnimClip? _together, _hover;
        private float _age, _holdUntil;
        private GameObject? _hero;

        /// <summary>
        /// Start the reassembly where the hero model stands, ending (parts together, then a short hover) after
        /// <paramref name="seconds"/>. Null when the model or its clips haven't been converted: the caller keeps his own clips.
        /// </summary>
        public static VictorReassembly? Spawn(GameObject modelRoot, float seconds)
        {
            var swap = modelRoot.GetComponentInChildren<DeadlockModelSwap>(true);
            GameObject? hero = swap != null ? swap.HeroObject : null;
            if (swap == null || hero == null) return null;
            GlbModel? model = DeadlockInstall.Model(VictorData.FxKey, (name, tex) => DeadlockHeroes.MakeMaterial(VictorData.FxKey, name, tex));
            Dictionary<string, AnimClip>? clips = DeadlockInstall.Clips(VictorData.FxKey);
            if (model == null || clips == null || !clips.TryGetValue(VictorData.FxTogether, out AnimClip together)) return null;
            clips.TryGetValue(VictorData.FxHover, out AnimClip hover);

            // The parts model is Victor's own mesh cut up: placed and scaled exactly as his model is.
            GameObject fx = Instantiate(model.Root, hero.transform.parent, false);
            fx.name = "VictorReassembly";
            fx.transform.localPosition = hero.transform.localPosition;
            fx.transform.localRotation = hero.transform.localRotation;
            fx.transform.localScale = hero.transform.localScale;
            var r = fx.AddComponent<VictorReassembly>();
            foreach (Transform t in fx.GetComponentsInChildren<Transform>(true)) if (!r._bones.ContainsKey(t.name)) r._bones[t.name] = t;
            r._together = together;
            r._hover = hover;
            r._holdUntil = Mathf.Max(together.Length, seconds);
            r._hero = hero;
            hero.SetActive(false);
            fx.SetActive(true);
            r.Pose(together, 0f);
            return r;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_together == null) return;
            if (_age < _together.Length) Pose(_together, _age);
            else if (_hover != null && _hover.Length > 0f) Pose(_hover, (_age - _together.Length) % _hover.Length);
            if (_age > _holdUntil + 2f) Finish(); // never left behind if the end call got lost
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

        /// <summary>Remove the parts and show Victor again (he then plays his get-up clip).</summary>
        public void Finish()
        {
            if (_hero != null) _hero.SetActive(true);
            _hero = null;
            Destroy(gameObject);
        }

        private void OnDestroy() { if (_hero != null) _hero.SetActive(true); }
    }
}
