using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Kinetic Carbine's blue glow (user 3.0), every machine: a blue overlay and a blue light on Paradox while the Carbine is active
    /// (the KineticCarbine state, which runs everywhere). Brighter as the charge builds. Apollo's red glow, in blue.
    /// </summary>
    public class ParadoxCarbineGlow : MonoBehaviour
    {
        private static readonly Color Blue = new Color(0.2f, 0.55f, 1f, 1f);
        private static Material? _blue;
        private CharacterBody _body = null!;
        private TemporaryOverlayInstance? _overlay;
        private Light? _light;

        /// <summary>How far the charge has built (0-1), set by the state: the light grows with it.</summary>
        public float Charge;

        /// <summary>The blue overlay: the Huntress's bright flash, tinted blue.</summary>
        private static Material? BlueMaterial()
        {
            if (_blue != null) return _blue;
            Material? flash = Kit.TryLoad<Material>("Paradox", "RoR2/Base/Huntress/matHuntressFlashBright.mat", "RoR2/Base/Characters/Huntress/matHuntressFlashBright.mat");
            if (flash == null) return null;
            _blue = new Material(flash) { name = "matCursedAppleParadoxCarbineGlow" };
            if (_blue.HasProperty("_TintColor")) _blue.SetColor("_TintColor", Blue);
            if (_blue.HasProperty("_Color")) _blue.SetColor("_Color", Blue);
            return _blue;
        }

        private void Start()
        {
            _body = GetComponent<CharacterBody>();
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            Material? blue = BlueMaterial();
            if (model != null && blue != null)
            {
                _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
                _overlay.duration = float.PositiveInfinity;
                _overlay.animateShaderAlpha = false;
                _overlay.destroyComponentOnEnd = false;
                _overlay.originalMaterial = blue;
                _overlay.AddToCharacterModel(model);
            }
            var go = new GameObject("ParadoxCarbineLight");
            go.transform.SetParent(_body.coreTransform != null ? _body.coreTransform : transform, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Blue;
            _light.range = 4f + _body.radius * 2f;
        }

        private void Update()
        {
            if (_light != null) _light.intensity = (1f + 1.5f * Charge) + 0.5f * Mathf.Sin(Time.time * 6f);
        }

        private void OnDestroy()
        {
            _overlay?.Destroy();
            if (_light != null) Destroy(_light.gameObject);
        }
    }
}
