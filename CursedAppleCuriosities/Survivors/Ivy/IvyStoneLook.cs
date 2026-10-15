using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Every machine, on Ivy: how Stone Form looks (user 2.5), driven by the synced Stone buff. Deadlock's statue is its own model
    /// (models/particle/tengu_stone_form_model_fx, Ivy's mesh on its own skeleton) carrying its own animation, ability_stone_form: the
    /// gargoyle pose. Here that statue, posed by that clip and dressed in stone (the Stone Golem's dead rock), stands where she is and
    /// she is hidden. Until the statue and its clip are converted, her own model wears the stone instead. Breaking out leaves rubble.
    /// </summary>
    public class IvyStoneLook : MonoBehaviour
    {
        private static Material? _stone;
        private static bool _tried;
        private CharacterBody _body = null!;
        private bool _on;
        private TemporaryOverlayInstance? _overlay;
        private GameObject? _statue, _hero;
        private AnimClip? _pose;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private float _t, _nextAsk;
        private static bool _logged;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private static Material? Stone()
        {
            if (_tried) return _stone;
            _tried = true;
            // The material her own mesh renders with (Commando's opaque character material), with the Stone Golem's rock texture.
            // Not a golem material itself: matGolemDead dithers out (it's the dying golem's), so the statue and the overlay drew
            // nothing (user 2.5: "not visible at all").
            Material? rock = Kit.TryLoad<Material>("Ivy", "RoR2/Base/Golem/matGolem.mat", "RoR2/Base/Characters/Golem/matGolem.mat");
            _stone = DeadlockHeroes.MakeMaterial("Ivy", "stone", rock != null ? rock.mainTexture as Texture2D : null);
            _stone.color = rock != null ? new Color(0.85f, 0.85f, 0.82f) : new Color(0.55f, 0.55f, 0.52f);
            return _stone;
        }

        private void LateUpdate()
        {
            // Ask for the statue's clips early (they load on request), so they're ready the first time she turns to stone.
            if (_pose == null && Time.time >= _nextAsk)
            {
                _nextAsk = Time.time + 2f;
                var clips = DeadlockInstall.Clips(IvyData.StatueKey);
                if (clips != null) clips.TryGetValue(IvyData.StatueClip, out _pose);
            }
            bool want = IvySurvivor.StoneBuff != null && _body.HasBuff(IvySurvivor.StoneBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
            if (_statue != null && _hero != null)
            {
                _statue.transform.SetPositionAndRotation(_hero.transform.position, _hero.transform.rotation);
                _t += Time.deltaTime;
                Pose(_t);
            }
        }

        private void Begin()
        {
            _on = true;
            _hero = Hero();
            GlbModel? statue = DeadlockInstall.Model(IvyData.StatueKey);
            Material? stone = Stone();
            if (_hero != null && statue != null && _pose != null && stone != null)
            {
                _statue = Instantiate(statue.Root);
                _statue.name = "CursedAppleIvyStatue";
                _statue.transform.SetPositionAndRotation(_hero.transform.position, _hero.transform.rotation);
                _statue.transform.localScale = _hero.transform.lossyScale;
                _statue.SetActive(true);
                _bones.Clear();
                foreach (Transform t in _statue.GetComponentsInChildren<Transform>(true)) if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
                foreach (Renderer r in _statue.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = stone;
                    r.sharedMaterials = mats;
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                }
                _t = 0f;
                Pose(0f);
                SetHidden(_hero, true);
                if (!_logged) { _logged = true; CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Ivy] Stone Form: statue shown ({_statue.GetComponentsInChildren<Renderer>(true).Length} renderers, {_bones.Count} bones)"); }
                return;
            }
            if (!_logged) { _logged = true; CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Ivy] Stone Form: stone overlay (statue model {(statue != null ? "ready" : "not loaded")}, pose {(_pose != null ? "ready" : "not loaded")}, hero {(_hero != null ? "ready" : "missing")})"); }
            // No statue yet: her own model, turned to stone.
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            if (model == null || stone == null) return;
            _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
            _overlay.duration = float.PositiveInfinity;
            _overlay.animateShaderAlpha = false;
            _overlay.destroyComponentOnEnd = false;
            _overlay.originalMaterial = stone;
            _overlay.AddToCharacterModel(model);
        }

        /// <summary>The statue in ability_stone_form's pose (it loops; it barely moves).</summary>
        private void Pose(float t)
        {
            if (_pose == null) return;
            if (_pose.Length > 0f) t %= _pose.Length;
            foreach (AnimTrack tr in _pose.Tracks)
            {
                if (!_bones.TryGetValue(tr.Bone, out Transform bone)) continue;
                if (tr.SampleRot(t, out Quaternion r)) bone.localRotation = r;
                if (tr.SamplePos(t, out Vector3 p)) bone.localPosition = p;
            }
        }

        private GameObject? Hero()
        {
            Transform? mt = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
            var swap = mt != null ? mt.GetComponent<DeadlockModelSwap>() : null;
            return swap != null ? swap.HeroObject : null;
        }

        private static void SetHidden(GameObject hero, bool hidden)
        {
            foreach (Renderer r in hero.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hidden;
        }

        private void End()
        {
            _on = false;
            if (_statue != null) Destroy(_statue);
            _statue = null;
            if (_hero != null) SetHidden(_hero, false);
            _overlay?.Destroy();
            _overlay = null;
            // Breaking out of the stone.
            DeadlockSounds.Play("ivy_stone_hit", _body.corePosition, gameObject);
            if (IvySurvivor.SlamEffect != null) EffectManager.SimpleEffect(IvySurvivor.SlamEffect, _body.corePosition, Quaternion.identity, false);
        }

        private void OnDestroy()
        {
            if (_statue != null) Destroy(_statue);
            if (_hero != null) SetHidden(_hero, false);
            _overlay?.Destroy();
        }
    }
}
