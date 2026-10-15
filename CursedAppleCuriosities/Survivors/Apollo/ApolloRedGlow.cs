using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Itani Lo Sahn's red glow (user 2.5), every machine: a bright red overlay and a red light on Apollo for the whole special (the
    /// mask buff) and on every enemy carrying its mark (the synced UltMarked debuff). Added to a body while it needs it; it removes
    /// itself when the buff goes.
    /// </summary>
    public class ApolloRedGlow : MonoBehaviour
    {
        public BuffDef? Buff;
        private static Material? _red;
        private CharacterBody _body = null!;
        private TemporaryOverlayInstance? _overlay;
        private Light? _light;

        /// <summary>The red overlay: the Huntress's bright flash, tinted red (the red fresnel alone barely showed).</summary>
        public static Material? Red()
        {
            if (_red != null) return _red;
            Material? flash = Kit.TryLoad<Material>("Apollo", "RoR2/Base/Huntress/matHuntressFlashBright.mat", "RoR2/Base/Characters/Huntress/matHuntressFlashBright.mat");
            if (flash == null) return null;
            _red = new Material(flash) { name = "matCursedAppleApolloRedGlow" };
            if (_red.HasProperty("_TintColor")) _red.SetColor("_TintColor", new Color(1f, 0.08f, 0.05f, 1f));
            if (_red.HasProperty("_Color")) _red.SetColor("_Color", new Color(1f, 0.08f, 0.05f, 1f));
            return _red;
        }

        public static void Ensure(CharacterBody body, BuffDef? buff)
        {
            if (buff == null || body == null || !body.HasBuff(buff)) return;
            foreach (ApolloRedGlow g in body.GetComponents<ApolloRedGlow>()) if (g.Buff == buff) return;
            body.gameObject.AddComponent<ApolloRedGlow>().Buff = buff;
        }

        private void Start()
        {
            _body = GetComponent<CharacterBody>();
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            Material? red = Red();
            if (model != null && red != null)
            {
                _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
                _overlay.duration = float.PositiveInfinity;
                _overlay.animateShaderAlpha = false;
                _overlay.destroyComponentOnEnd = false;
                _overlay.originalMaterial = red;
                _overlay.AddToCharacterModel(model);
            }
            var go = new GameObject("ApolloRedLight");
            go.transform.SetParent(_body.coreTransform != null ? _body.coreTransform : transform, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.1f, 0.08f);
            _light.range = 5f + _body.radius * 2f;
        }

        private void Update()
        {
            if (_body == null) return;
            if (Buff == null || !_body.HasBuff(Buff) || _body.healthComponent == null || !_body.healthComponent.alive) { Destroy(this); return; }
            if (_light != null) _light.intensity = 2.5f + 0.8f * Mathf.Sin(Time.time * 7f);
        }

        private void OnDestroy()
        {
            _overlay?.Destroy();
            if (_light != null) Destroy(_light.gameObject);
        }
    }
}
