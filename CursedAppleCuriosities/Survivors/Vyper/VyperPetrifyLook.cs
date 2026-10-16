using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Every machine, on a petrified body (user 3.0: petrify needed a strong visual): the whole model turns to stone (Ivy's stone,
    /// the Stone Golem's rock) and freezes mid-pose until the synced Petrify buff wears off. The Jade Elephant shell stays on top.
    /// </summary>
    public class VyperPetrifyLook : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private bool _on;
        private TemporaryOverlayInstance? _overlay;
        private Animator? _animator;
        private float _speed = 1f;

        private static bool _hooked;

        /// <summary>Adds the look to any body that gets petrified, on every client (the game refreshes buff visuals there).</summary>
        public static void Register()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.CharacterBody.UpdateAllTemporaryVisualEffects += (orig, self) =>
            {
                orig(self);
                if (VyperSurvivor.PetrifyBuff != null && self.HasBuff(VyperSurvivor.PetrifyBuff) && self.GetComponent<VyperPetrifyLook>() == null)
                    self.gameObject.AddComponent<VyperPetrifyLook>();
            };
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            bool want = VyperSurvivor.PetrifyBuff != null && _body.HasBuff(VyperSurvivor.PetrifyBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
        }

        private void Begin()
        {
            _on = true;
            Transform? mt = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
            if (mt == null) return;
            Material? stone = Ivy.IvyStoneLook.Stone();
            CharacterModel? model = mt.GetComponent<CharacterModel>();
            if (model != null && stone != null)
            {
                _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
                _overlay.duration = float.PositiveInfinity;
                _overlay.animateShaderAlpha = false;
                _overlay.destroyComponentOnEnd = false;
                _overlay.originalMaterial = stone;
                _overlay.AddToCharacterModel(model);
            }
            _animator = mt.GetComponent<Animator>();
            if (_animator != null) { _speed = _animator.speed; _animator.speed = 0f; } // a statue: held in whatever pose it was caught in
        }

        private void End()
        {
            _on = false;
            _overlay?.Destroy();
            _overlay = null;
            if (_animator != null) _animator.speed = _speed;
            _animator = null;
        }

        private void OnDestroy() => End();
    }
}
