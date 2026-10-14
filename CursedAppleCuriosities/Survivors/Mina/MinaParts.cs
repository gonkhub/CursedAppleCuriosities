using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Mina's bats: Deadlock's own bat (bat_ui_a, converted as "mina_bat") when it's there, else a small dark shape. Used by
    /// the Nox Nostra projectiles, Sanguine Retreat's swarm and her lobby intro.
    /// </summary>
    public static class MinaBatVisual
    {
        private static Material? _fallback;

        /// <summary>One bat under <paramref name="parent"/>, about <paramref name="span"/> metres wide, facing the parent's forward.</summary>
        public static GameObject Build(Transform parent, float span)
        {
            GlbModel? model = DeadlockInstall.Model(MinaData.BatKey);
            GameObject bat;
            if (model != null)
            {
                bat = Object.Instantiate(model.Root, parent, false);
                bat.SetActive(true);
                var bounds = new Bounds(bat.transform.position, Vector3.zero);
                foreach (Renderer r in bat.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float size = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
                bat.transform.localScale *= span / size;
            }
            else
            {
                bat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(bat.GetComponent<Collider>());
                bat.transform.SetParent(parent, false);
                bat.transform.localScale = new Vector3(span, span * 0.25f, span * 0.4f); // a flat dark wing shape
                if (_fallback == null) { _fallback = DeadlockHeroes.MakeMaterial("Mina", "bat", null); _fallback.color = new Color(0.12f, 0.03f, 0.06f); }
                bat.GetComponent<Renderer>().sharedMaterial = _fallback;
            }
            bat.name = "MinaBat";
            bat.transform.localPosition = Vector3.zero;
            bat.transform.localRotation = Quaternion.identity;
            bat.AddComponent<MinaBatFlap>();
            return bat;
        }

        /// <summary>A little swarm circling a transform (every client): Sanguine Retreat's bats.</summary>
        public static GameObject Cloud(Transform follow, int count)
        {
            var go = new GameObject("MinaBatCloud");
            go.transform.position = follow.position + Vector3.up;
            var cloud = go.AddComponent<MinaBatCloud>();
            cloud.Follow = follow;
            for (int i = 0; i < count; i++)
            {
                var pivot = new GameObject("BatPivot").transform;
                pivot.SetParent(go.transform, false);
                Build(pivot, Random.Range(0.35f, 0.6f));
                cloud.Add(pivot);
            }
            return go;
        }
    }

    /// <summary>Wings beating: the bat squashes and stretches quickly (the UI bat has no flight clip to play).</summary>
    public class MinaBatFlap : MonoBehaviour
    {
        private Vector3 _base;
        private float _rate, _phase;

        private void Start()
        {
            _base = transform.localScale;
            _rate = Random.Range(12f, 18f);
            _phase = Random.value * 6.28f;
        }

        private void Update()
        {
            float s = Mathf.Sin(Time.time * _rate + _phase);
            transform.localScale = new Vector3(_base.x * (0.75f + 0.25f * s), _base.y * (1f - 0.2f * s), _base.z);
        }
    }

    /// <summary>Sanguine Retreat's swarm: bats circling the body on their own orbits.</summary>
    public class MinaBatCloud : MonoBehaviour
    {
        public Transform? Follow;
        private readonly System.Collections.Generic.List<(Transform t, float r, float speed, float phase, float tilt)> _bats =
            new System.Collections.Generic.List<(Transform, float, float, float, float)>();

        public void Add(Transform pivot) => _bats.Add((pivot, Random.Range(0.4f, 1.2f), Random.Range(5f, 9f) * (Random.value < 0.5f ? 1f : -1f), Random.value * 6.28f, Random.Range(-0.6f, 0.6f)));

        private void LateUpdate()
        {
            if (Follow == null) { Destroy(gameObject); return; }
            transform.position = Follow.position + Vector3.up * 1f;
            foreach (var b in _bats)
            {
                if (b.t == null) continue;
                float a = Time.time * b.speed + b.phase;
                Vector3 p = new Vector3(Mathf.Cos(a) * b.r, Mathf.Sin(a * 1.7f) * b.tilt + b.tilt, Mathf.Sin(a) * b.r);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(b.speed);
                b.t.localPosition = p;
                b.t.rotation = Quaternion.LookRotation(tangent);
            }
        }
    }

    /// <summary>On a Nox Nostra bat's ghost (every client): Deadlock's bat inside the crimson glow.</summary>
    public class MinaBatGhost : MonoBehaviour
    {
        private void Start()
        {
            // The ghost is scaled down for the glow; the bat is sized in world metres.
            var holder = new GameObject("MinaBatHolder").transform;
            holder.SetParent(transform, false);
            holder.localScale = Vector3.one / Mathf.Max(0.01f, transform.lossyScale.x);
            MinaBatVisual.Build(holder, 0.6f);
        }
    }

    /// <summary>
    /// Every client, on Mina: her pistol's layered sound, as Deadlock plays it (the first shot of a string, then main shots with
    /// the mechanism; the tail when she stops).
    /// </summary>
    public class MinaGunAudio : MonoBehaviour
    {
        private float _last = -10f;
        private bool _firing;

        public static void Shot(GameObject body)
        {
            var a = body.GetComponent<MinaGunAudio>();
            bool first = a == null || !a._firing;
            DeadlockSounds.Play(first ? "mina_gun_first" : "mina_gun", body.transform.position, body);
            DeadlockSounds.Play("mina_gun_mech", body.transform.position, body);
            if (a != null) { a._firing = true; a._last = Time.time; }
        }

        private void Update()
        {
            if (!_firing || Time.time - _last < MinaData.GunCycle * 2.5f) return;
            _firing = false;
            DeadlockSounds.Play("mina_gun_tail", transform.position, gameObject);
        }
    }

    /// <summary>
    /// Owner side, on Mina: Sanguine Retreat's one recast (user: within 5 s). The first cast gives its charge back and opens the
    /// window; casting again inside it spends that charge, so the cooldown starts from the recast. If the window closes unused,
    /// the charge is taken away and the cooldown starts then.
    /// </summary>
    public class MinaRetreatRecast : MonoBehaviour
    {
        private bool _pending;
        private float _until;
        private GenericSkill? _slot;

        public bool Open => _pending && Time.time < _until;

        public void Use(bool recast)
        {
            if (recast) { _pending = false; return; }
            _slot = GetComponent<SkillLocator>()?.utility;
            if (_slot == null) return;
            _slot.AddOneStock();
            _pending = true;
            _until = Time.time + MinaData.RetreatRecastWindow;
        }

        private void Update()
        {
            if (!_pending || Time.time < _until) return;
            _pending = false;
            if (_slot != null && _slot.stock > 0) _slot.DeductStock(1);
        }
    }
}
