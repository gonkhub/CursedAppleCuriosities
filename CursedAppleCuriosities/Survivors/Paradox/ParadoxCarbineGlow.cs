using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Kinetic Carbine's blue glow, every machine, while the Carbine is active (the KineticCarbine state, which runs everywhere).
    /// 3.1 (user): it glows from her gun, not over her whole body (the 3.0 overlay just turned her white): a blue orb and a blue light
    /// at the muzzle, both growing as the charge builds.
    /// </summary>
    public class ParadoxCarbineGlow : MonoBehaviour
    {
        private static readonly Color Blue = new Color(0.2f, 0.55f, 1f, 1f);
        private static Material? _blue;
        private Transform? _orb;
        private Light? _light;

        /// <summary>How far the charge has built (0-1), set by the state: the glow grows with it.</summary>
        public float Charge;

        /// <summary>The orb's look: the Huntress's bright flash (a fresnel glow), tinted blue.</summary>
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
            var body = GetComponent<CharacterBody>();
            Transform? model = body.modelLocator != null ? body.modelLocator.modelTransform : null;
            Transform? muzzle = model != null && model.TryGetComponent(out ChildLocator children) ? children.FindChild(ParadoxSurvivor.MuzzleName) : null;
            Transform at = muzzle != null ? muzzle : body.coreTransform != null ? body.coreTransform : transform;

            Material? blue = BlueMaterial();
            if (blue != null)
            {
                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "ParadoxCarbineOrb";
                Destroy(orb.GetComponent<Collider>());
                var r = orb.GetComponent<MeshRenderer>();
                r.sharedMaterial = blue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                orb.transform.SetParent(at, false);
                _orb = orb.transform;
            }
            var go = new GameObject("ParadoxCarbineLight");
            go.transform.SetParent(at, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Blue;
            _light.range = 3f;
        }

        private void Update()
        {
            float flicker = Mathf.Sin(Time.time * 6f);
            if (_light != null)
            {
                _light.intensity = 1.5f + 3f * Charge + 0.5f * flicker;
                _light.range = 2.5f + 2.5f * Charge;
            }
            // Sized in world metres whatever the bone's own scale.
            if (_orb != null && _orb.parent != null)
            {
                float metres = (0.12f + 0.25f * Charge) * (1f + 0.08f * flicker);
                Vector3 s = _orb.parent.lossyScale;
                _orb.localScale = new Vector3(metres / Mathf.Max(1e-4f, s.x), metres / Mathf.Max(1e-4f, s.y), metres / Mathf.Max(1e-4f, s.z));
            }
        }

        private void OnDestroy()
        {
            if (_orb != null) Destroy(_orb.gameObject);
            if (_light != null) Destroy(_light.gameObject);
        }
    }
}
