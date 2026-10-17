using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Flawless Advance's release cue (user 3.1, in place of a HUD bar): for the perfect window his blade flashes bright red, a glowing
    /// glow down the blade (its blade_0-6 bones) with a bigger flare and a light at the tip (weaponTip), flaring at once and easing off
    /// as the window closes. Every machine (AdvanceReady
    /// runs everywhere), so other players see the glint too.
    /// </summary>
    public class ApolloBladeFlash : MonoBehaviour
    {
        private static readonly Color Red = new Color(1f, 0.12f, 0.08f, 1f);
        private static Material? _red;
        private readonly System.Collections.Generic.List<(Transform Orb, float Size)> _orbs = new System.Collections.Generic.List<(Transform, float)>();
        private Light? _light;
        private float _age, _length;

        public static void Flash(GameObject body, float length)
        {
            Stop(body);
            body.AddComponent<ApolloBladeFlash>()._length = length;
        }

        public static void Stop(GameObject body)
        {
            if (body.TryGetComponent(out ApolloBladeFlash f)) Destroy(f);
        }

        /// <summary>The Huntress's bright flash (a fresnel glow), tinted red.</summary>
        private static Material? RedMaterial()
        {
            if (_red != null) return _red;
            Material? flash = Kit.TryLoad<Material>("Apollo", "RoR2/Base/Huntress/matHuntressFlashBright.mat", "RoR2/Base/Characters/Huntress/matHuntressFlashBright.mat");
            if (flash == null) return null;
            _red = new Material(flash) { name = "matCursedAppleApolloBladeFlashRed" };
            if (_red.HasProperty("_TintColor")) _red.SetColor("_TintColor", Red);
            if (_red.HasProperty("_Color")) _red.SetColor("_Color", Red);
            return _red;
        }

        private static Transform? Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                Transform? f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        private void Orb(Transform at, float size, Material material)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "ApolloBladeFlash";
            Destroy(orb.GetComponent<Collider>());
            var r = orb.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            orb.transform.SetParent(at, false);
            _orbs.Add((orb.transform, size));
        }

        private void Start()
        {
            var body = GetComponent<CharacterBody>();
            Transform? model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
            Transform fallback = body != null && body.coreTransform != null ? body.coreTransform : transform;
            Transform tip = (model != null ? Find(model, "weaponTip") : null) ?? fallback;

            Material? red = RedMaterial();
            if (red != null)
            {
                if (model != null)
                    for (int i = 0; i <= 6; i++)
                    {
                        Transform? b = Find(model, "blade_" + i);
                        if (b != null) Orb(b, 0.3f, red);
                    }
                Orb(tip, 0.7f, red);
            }
            var go = new GameObject("ApolloBladeFlashLight");
            go.transform.SetParent(tip, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Red;
            _light.range = 6f;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _length) { Destroy(this); return; }
            // A sharp flare that eases off over the window, with a quick shimmer.
            float t = _age / Mathf.Max(0.01f, _length);
            float flare = Mathf.Lerp(1f, 0.55f, t) * (1f + 0.15f * Mathf.Sin(Time.time * 50f));
            if (_light != null) _light.intensity = 12f * flare;
            foreach ((Transform orb, float size) in _orbs)
            {
                if (orb == null || orb.parent == null) continue;
                float metres = size * flare; // sized in world metres whatever the bone's own scale
                Vector3 sc = orb.parent.lossyScale;
                orb.localScale = new Vector3(metres / Mathf.Max(1e-4f, sc.x), metres / Mathf.Max(1e-4f, sc.y), metres / Mathf.Max(1e-4f, sc.z));
            }
        }

        private void OnDestroy()
        {
            foreach ((Transform orb, float _) in _orbs) if (orb != null) Destroy(orb.gameObject);
            if (_light != null) Destroy(_light.gameObject);
        }
    }
}
