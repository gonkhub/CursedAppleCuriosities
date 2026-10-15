using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Ivy's own ground marker (user 2.5: her Kudzu Bomb used Seven's ring and its sound): a vine-green rim over a soft filled disc,
    /// both drawn in code. A timed one fades out over its last half second; a preview (Seconds 0) stays until it's destroyed.
    /// </summary>
    public class IvyGroundRing : MonoBehaviour
    {
        public float Radius = 6f, Seconds;
        public Color Color = new Color(0.45f, 0.9f, 0.35f, 0.9f);
        public GameObject? Follow;

        private const int Points = 64;
        private LineRenderer? _rim, _inner;
        private Transform? _disc;
        private Material? _discMaterial;
        private float _age, _builtRadius = -1f;

        public static Material Material => CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.LineMaterial();

        /// <summary>A ring on the ground at <paramref name="at"/> (every client draws its own).</summary>
        public static IvyGroundRing Spawn(Vector3 at, float radius, float seconds, Color? color = null)
        {
            var go = new GameObject("CursedAppleIvyRing");
            go.transform.position = at;
            var r = go.AddComponent<IvyGroundRing>();
            r.Radius = radius;
            r.Seconds = seconds;
            if (color.HasValue) r.Color = color.Value;
            return r;
        }

        private LineRenderer Rim(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = Points;
            lr.widthMultiplier = width;
            lr.sharedMaterial = Material;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        private void Build()
        {
            _rim = Rim("Rim", 0.16f);
            _inner = Rim("Inner", 0.06f);
            // The filled area: RoR2's own range indicator on a flat cylinder.
            _discMaterial = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.AreaMaterial(new Color(Color.r, Color.g, Color.b, 0.35f));
            if (_discMaterial != null)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(disc.GetComponent<Collider>());
                disc.transform.SetParent(transform, false);
                var r = disc.GetComponent<MeshRenderer>();
                r.sharedMaterial = _discMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _disc = disc.transform;
            }
        }

        private void Reshape()
        {
            _builtRadius = Radius;
            var rim = new Vector3[Points];
            var inner = new Vector3[Points];
            for (int i = 0; i < Points; i++)
            {
                float a = i * Mathf.PI * 2f / Points;
                rim[i] = new Vector3(Mathf.Cos(a) * Radius, 0.1f, Mathf.Sin(a) * Radius);
                inner[i] = new Vector3(rim[i].x * 0.92f, 0.1f, rim[i].z * 0.92f);
            }
            _rim!.SetPositions(rim);
            _inner!.SetPositions(inner);
            if (_disc != null)
            {
                _disc.localScale = new Vector3(Radius * 2f, 0.05f, Radius * 2f);
                _disc.localPosition = new Vector3(0f, 0.05f, 0f);
            }
        }

        private void LateUpdate()
        {
            if (_rim == null) Build();
            if (!Mathf.Approximately(_builtRadius, Radius)) Reshape();
            if (Follow != null) transform.position = Follow.transform.position;
            _age += Time.deltaTime;
            if (Seconds > 0f && _age >= Seconds) { Destroy(gameObject); return; }
            float fade = Seconds > 0f ? Mathf.Clamp01((Seconds - _age) / 0.5f) * Mathf.Clamp01(_age / 0.15f) : 1f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
            Color rim = new Color(Color.r, Color.g, Color.b, Color.a * fade * pulse);
            _rim!.startColor = _rim.endColor = rim;
            _inner!.startColor = _inner.endColor = new Color(rim.r, rim.g, rim.b, rim.a * 0.6f);
            if (_discMaterial != null && _discMaterial.HasProperty("_TintColor"))
                _discMaterial.SetColor("_TintColor", new Color(Color.r, Color.g, Color.b, 0.35f * fade));
        }

        private void OnDestroy()
        {
            if (_discMaterial != null) Destroy(_discMaterial);
        }
    }

    /// <summary>
    /// The owner's aim preview for a thrown or dropped bomb (user 2.5): the arc it will fly (Deadlock's generic_arc) and the ring it
    /// will cover where it lands. Physics.gravity, as the projectile's own rigidbody falls.
    /// </summary>
    public class IvyThrowPreview
    {
        private readonly GameObject _root;
        private readonly LineRenderer? _arc;
        private readonly IvyGroundRing _ring;
        private readonly List<Vector3> _points = new List<Vector3>();

        public IvyThrowPreview(float radius, bool showArc, Color? color = null)
        {
            Color c = color ?? new Color(0.55f, 1f, 0.45f, 0.9f);
            _root = new GameObject("CursedAppleIvyPreview");
            if (showArc)
            {
                _arc = _root.AddComponent<LineRenderer>();
                _arc.useWorldSpace = true;
                _arc.widthMultiplier = 0.08f;
                _arc.sharedMaterial = IvyGroundRing.Material;
                _arc.startColor = c;
                _arc.endColor = new Color(c.r, c.g, c.b, 0.3f);
                _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _ring = IvyGroundRing.Spawn(Vector3.zero, radius, 0f, new Color(c.r, c.g, c.b, 0.75f));
        }

        /// <summary>Where a projectile from <paramref name="from"/> at <paramref name="velocity"/> comes down (and the arc on the way).</summary>
        public static Vector3 Land(Vector3 from, Vector3 velocity, List<Vector3>? points, float maxSeconds = 6f)
        {
            points?.Clear();
            points?.Add(from);
            Vector3 p = from, v = velocity;
            const float step = 0.04f;
            for (float t = 0f; t < maxSeconds; t += step)
            {
                Vector3 next = p + v * step + 0.5f * Physics.gravity * step * step;
                v += Physics.gravity * step;
                Vector3 d = next - p;
                if (Physics.Raycast(p, d, out RaycastHit hit, d.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    points?.Add(hit.point);
                    return hit.point;
                }
                p = next;
                points?.Add(p);
            }
            return p;
        }

        public void Update(Vector3 from, Vector3 velocity, float radius)
        {
            Vector3 at = Land(from, velocity, _points);
            // A blast lands on the ground under where it hits (a wall or a ledge: the patch grows below).
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit ground, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) at = ground.point;
            _ring.transform.position = at;
            _ring.Radius = radius;
            if (_arc != null)
            {
                _arc.positionCount = _points.Count;
                for (int i = 0; i < _points.Count; i++) _arc.SetPosition(i, _points[i]);
            }
        }

        public void Destroy()
        {
            Object.Destroy(_root);
            if (_ring != null) Object.Destroy(_ring.gameObject);
        }
    }
}
