using CursedAppleCuriosities.Deadlock;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Every machine, on Ivy: how Stone Form looks (user 2.5), driven by the synced Stone buff. Deadlock's statue
    /// (models/particle/tengu_stone_form_model_fx) is her own mesh posed by the ability and dressed in a stone material, so here her
    /// own model, in Stone Form's pose, wears stone: the Stone Golem's dead-rock material over her. Breaking out leaves a puff of rubble.
    /// </summary>
    public class IvyStoneLook : MonoBehaviour
    {
        private static Material? _stone;
        private static bool _tried;
        private CharacterBody _body = null!;
        private bool _on;
        private TemporaryOverlayInstance? _overlay;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private static Material? Stone()
        {
            if (_tried) return _stone;
            _tried = true;
            Material? rock = Kit.TryLoad<Material>("Ivy", "RoR2/Base/Golem/matGolemDead.mat", "RoR2/Base/Characters/Golem/matGolemDead.mat",
                "RoR2/Base/Golem/matGolem.mat", "RoR2/Base/Characters/Golem/matGolem.mat");
            if (rock != null) _stone = new Material(rock) { name = "matCursedAppleIvyStone" };
            else
            {
                _stone = DeadlockHeroes.MakeMaterial("Ivy", "stone", null);
                _stone.color = new Color(0.55f, 0.55f, 0.52f);
            }
            return _stone;
        }

        private void LateUpdate()
        {
            bool want = IvySurvivor.StoneBuff != null && _body.HasBuff(IvySurvivor.StoneBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
        }

        private void Begin()
        {
            _on = true;
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            Material? stone = Stone();
            if (model == null || stone == null) return;
            _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
            _overlay.duration = float.PositiveInfinity;
            _overlay.animateShaderAlpha = false;
            _overlay.destroyComponentOnEnd = false;
            _overlay.originalMaterial = stone;
            _overlay.AddToCharacterModel(model);
        }

        private void End()
        {
            _on = false;
            _overlay?.Destroy();
            _overlay = null;
            // Breaking out of the stone.
            DeadlockSounds.Play("ivy_stone_hit", _body.corePosition, gameObject);
            if (IvySurvivor.SlamEffect != null) EffectManager.SimpleEffect(IvySurvivor.SlamEffect, _body.corePosition, Quaternion.identity, false);
        }

        private void OnDestroy() => _overlay?.Destroy();
    }
}
