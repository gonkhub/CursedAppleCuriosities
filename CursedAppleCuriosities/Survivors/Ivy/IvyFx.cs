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
        private static Material? _material;
        private LineRenderer? _rim;
        private MeshFilter? _disc;
        private MeshRenderer? _discRenderer;
        private float _age, _builtRadius = -1f;

        public static Material Material => _material ??= new Material(Shader.Find("Sprites/Default")) { name = "matCursedAppleIvyGround" };

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

        private void Build()
        {
            _rim = gameObject.AddComponent<LineRenderer>();
            _rim.useWorldSpace = false;
            _rim.loop = true;
            _rim.positionCount = Points;
            _rim.widthMultiplier = 0.12f;
            _rim.sharedMaterial = Material;
            _rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var disc = new GameObject("Disc", typeof(MeshFilter), typeof(MeshRenderer));
            disc.transform.SetParent(transform, false);
            _disc = disc.GetComponent<MeshFilter>();
            _discRenderer = disc.GetComponent<MeshRenderer>();
            _discRenderer.sharedMaterial = Material;
            _discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Reshape()
        {
            _builtRadius = Radius;
            var rim = new Vector3[Points];
            for (int i = 0; i < Points; i++)
            {
                float a = i * Mathf.PI * 2f / Points;
                rim[i] = new Vector3(Mathf.Cos(a) * Radius, 0.08f, Mathf.Sin(a) * Radius);
            }
            _rim!.SetPositions(rim);
            // A fan: clear in the middle, tinted toward the edge (vertex colours; Sprites/Default reads them).
            var verts = new Vector3[Points + 1];
            var cols = new Color[Points + 1];
            var tris = new int[Points * 3];
            verts[0] = new Vector3(0f, 0.06f, 0f);
            for (int i = 0; i < Points; i++)
            {
                verts[i + 1] = new Vector3(rim[i].x, 0.06f, rim[i].z);
                tris[i * 3] = 0; tris[i * 3 + 1] = (i + 1) % Points + 1; tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "IvyDisc", vertices = verts, triangles = tris, colors = cols };
            mesh.RecalculateBounds();
            _disc!.sharedMesh = mesh;
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
            Mesh m = _disc!.sharedMesh;
            var cols = new Color[m.vertexCount];
            cols[0] = new Color(Color.r, Color.g, Color.b, 0.04f * fade);
            for (int i = 1; i < cols.Length; i++) cols[i] = new Color(Color.r, Color.g, Color.b, 0.3f * fade);
            m.colors = cols;
        }

        private void OnDestroy()
        {
            if (_disc != null && _disc.sharedMesh != null) Destroy(_disc.sharedMesh);
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

        public IvyThrowPreview(float radius, bool showArc)
        {
            _root = new GameObject("CursedAppleIvyPreview");
            if (showArc)
            {
                _arc = _root.AddComponent<LineRenderer>();
                _arc.useWorldSpace = true;
                _arc.widthMultiplier = 0.08f;
                _arc.sharedMaterial = IvyGroundRing.Material;
                _arc.startColor = new Color(0.55f, 1f, 0.45f, 0.9f);
                _arc.endColor = new Color(0.55f, 1f, 0.45f, 0.3f);
                _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _ring = IvyGroundRing.Spawn(Vector3.zero, radius, 0f, new Color(0.55f, 1f, 0.45f, 0.75f));
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
