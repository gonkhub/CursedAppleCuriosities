using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// Shiv's rage meter: a red bar under the stamina pips (user 2.5), like Deadlock's rage bar under his crosshair.
    /// It reads the synced Rage buff, so it works on clients too.
    /// </summary>
    public class ShivRageHud : MonoBehaviour
    {
        private HUD _hud = null!;
        private RectTransform? _root;
        private Image? _fill;
        private const float W = 54f, H = 4f;

        public static void Hook()
        {
            On.RoR2.UI.HUD.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<ShivRageHud>()._hud = self; };
        }

        private void Build()
        {
            var go = new GameObject("ShivRage", typeof(RectTransform), typeof(Image));
            _root = (RectTransform)go.transform;
            _root.SetParent(_hud.mainContainer != null ? _hud.mainContainer.transform : _hud.transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = new Vector2(0f, -50f); // the stamina pips sit at -42
            _root.sizeDelta = new Vector2(W, H);
            var back = go.GetComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.55f);
            back.raycastTarget = false;
            var f = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)f.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, H);
            _fill = f.GetComponent<Image>();
            _fill.raycastTarget = false;
        }

        private void Update()
        {
            GameObject? o = _hud.targetBodyObject;
            CharacterBody? body = o != null && o.GetComponent<ShivRage>() != null ? o.GetComponent<CharacterBody>() : null;
            if (body == null || ShivSurvivor.RageBuff == null) { if (_root != null) _root.gameObject.SetActive(false); return; }
            if (_root == null) Build();
            _root!.gameObject.SetActive(true);
            float rage = Mathf.Clamp01(body.GetBuffCount(ShivSurvivor.RageBuff) / ShivData.RageMax);
            _fill!.rectTransform.sizeDelta = new Vector2(W * rage, H);
            // Full rage pulses brighter.
            _fill.color = rage >= 1f ? Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(1f, 0.55f, 0.45f), 0.5f + 0.5f * Mathf.Sin(Time.time * 10f))
                                     : new Color(0.85f, 0.1f, 0.08f, 0.95f);
        }
    }

    /// <summary>
    /// At full rage Shiv glows red (user 2.5): RoR2's red fresnel overlay (Predatory Instincts') on his Deadlock model, and a
    /// red light on him. Every machine, from the synced Rage buff.
    /// </summary>
    public class ShivRageGlow : MonoBehaviour
    {
        private static Material? _overlay;
        private CharacterBody _body = null!;
        private TemporaryOverlayInstance? _instance;
        private Light? _light;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            bool full = ShivRage.Full(_body) && _body.healthComponent != null && _body.healthComponent.alive;
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null
                ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            if (full && _instance == null && model != null)
            {
                _overlay ??= UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>("RoR2/Base/AttackSpeedOnCrit/matWolfhatOverlay.mat").WaitForCompletion();
                if (_overlay != null)
                {
                    _instance = TemporaryOverlayManager.AddOverlay(gameObject);
                    _instance.duration = float.PositiveInfinity;
                    _instance.animateShaderAlpha = false;
                    _instance.destroyComponentOnEnd = false;
                    _instance.originalMaterial = _overlay;
                    _instance.AddToCharacterModel(model);
                }
            }
            else if (!full && _instance != null)
            {
                _instance.Destroy();
                _instance = null;
            }
            if (full && _light == null)
            {
                var go = new GameObject("ShivRageLight");
                go.transform.SetParent(_body.coreTransform != null ? _body.coreTransform : transform, false);
                _light = go.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.12f, 0.08f);
                _light.range = 5f;
            }
            if (_light != null)
            {
                _light.enabled = full;
                if (full) _light.intensity = 2.2f + 0.8f * Mathf.Sin(Time.time * 6f);
            }
        }

        private void OnDestroy()
        {
            _instance?.Destroy();
            if (_light != null) Destroy(_light.gameObject);
        }
    }
}
