using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Mina's bats: a small dark-crimson bat built in code (two scalloped wings on a body, both sides drawn, flapping). Deadlock's
    /// own bat_ui_a is a flat menu silhouette with a see-through texture, which drew as black cards. Used by the Nox Nostra
    /// projectiles, Sanguine Retreat's swarm and her lobby intro.
    /// </summary>
    public static class MinaBatVisual
    {
        private static Material? _material;
        private static Mesh? _wing, _body;

        /// <summary>One bat under <paramref name="parent"/>, about <paramref name="span"/> metres wingtip to wingtip, facing the parent's forward.</summary>
        public static GameObject Build(Transform parent, float span)
        {
            if (_material == null)
            {
                _material = DeadlockHeroes.MakeMaterial("Mina", "bat", null);
                _material.color = new Color(0.16f, 0.03f, 0.07f);
                if (_material.HasProperty("_EmColor")) { _material.SetColor("_EmColor", new Color(0.5f, 0.05f, 0.15f)); _material.SetFloat("_EmPower", 0.6f); }
            }
            _wing ??= WingMesh();
            _body ??= BodyMesh();
            var bat = new GameObject("MinaBat");
            bat.transform.SetParent(parent, false);
            bat.transform.localScale = Vector3.one * span;
            Part(bat.transform, "Body", _body, Vector3.one);
            Transform right = Part(bat.transform, "WingR", _wing, Vector3.one);
            Transform left = Part(bat.transform, "WingL", _wing, new Vector3(-1f, 1f, 1f)); // mirrored (both faces are drawn)
            var flap = bat.AddComponent<MinaBatFlap>();
            flap.Right = right;
            flap.Left = left;
            return bat;
        }

        private static Transform Part(Transform parent, string name, Mesh mesh, Vector3 scale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>The right wing, half a unit long: a fan from the shoulder out to a scalloped trailing edge (x out, z forward).</summary>
        private static Mesh WingMesh()
        {
            Vector3[] rim =
            {
                new Vector3(0.02f, 0f, 0.07f), new Vector3(0.2f, 0.03f, 0.1f), new Vector3(0.38f, 0.02f, 0.08f), new Vector3(0.5f, 0f, 0.02f),
                new Vector3(0.42f, 0f, -0.1f), new Vector3(0.33f, 0f, -0.05f), new Vector3(0.24f, 0f, -0.13f), new Vector3(0.14f, 0f, -0.07f),
                new Vector3(0.03f, 0f, -0.1f),
            };
            return Fan(new Vector3(0.02f, 0f, 0f), rim);
        }

        private static Mesh BodyMesh() => Fan(Vector3.zero, new[]
        {
            new Vector3(0f, 0.02f, 0.14f), new Vector3(0.04f, 0f, 0.05f), new Vector3(0.03f, 0f, -0.1f), new Vector3(0f, 0.01f, -0.14f),
            new Vector3(-0.03f, 0f, -0.1f), new Vector3(-0.04f, 0f, 0.05f), new Vector3(0f, 0.02f, 0.14f),
        });

        /// <summary>A triangle fan around a centre, with both faces (the wings are paper thin).</summary>
        private static Mesh Fan(Vector3 centre, Vector3[] rim)
        {
            var v = new System.Collections.Generic.List<Vector3> { centre };
            v.AddRange(rim);
            var t = new System.Collections.Generic.List<int>();
            for (int i = 1; i < v.Count - 1; i++)
            {
                t.AddRange(new[] { 0, i, i + 1 });
                t.AddRange(new[] { 0, i + 1, i });
            }
            var m = new Mesh { name = "MinaBat", vertices = v.ToArray(), triangles = t.ToArray() };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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

    /// <summary>Wings beating: each wing swings up and down about the body.</summary>
    public class MinaBatFlap : MonoBehaviour
    {
        public Transform? Right, Left;
        private float _rate, _phase;

        private void Start()
        {
            _rate = Random.Range(16f, 24f);
            _phase = Random.value * 6.28f;
        }

        private void Update()
        {
            float a = Mathf.Sin(Time.time * _rate + _phase) * 40f;
            if (Right != null) Right.localRotation = Quaternion.Euler(0f, 0f, a);
            if (Left != null) Left.localRotation = Quaternion.Euler(0f, 0f, -a);
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

    /// <summary>
    /// Her umbrella hover (Deadlock: aiming down sights in the air; user: holding jump in the air). Owner: once she's falling with
    /// jump held, her fall is capped at Deadlock's FallSpeedMax, for up to MaxFloatTime per time in the air. Every client: the
    /// umbrella's flight sounds from the synced buff (the glide pose comes from MinaModel.LocoSet).
    /// </summary>
    public class MinaHover : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _held, _used;
        private bool _on, _shown;
        private DeadlockVoice? _loop;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (_body.hasEffectiveAuthority) Steer();
            Show();
        }

        private void Steer()
        {
            CharacterMotor? motor = _body.characterMotor;
            InputBankTest? input = _body.inputBank;
            if (motor == null || input == null) return;
            if (motor.isGrounded) { _used = 0f; _held = 0f; Set(false); return; }
            _held = input.jump.down ? _held + Time.fixedDeltaTime : 0f;
            // Only in her normal movement: an ability that moves her (Retreat, Nox Nostra) takes precedence.
            bool free = EntityStateMachine.FindByCustomName(gameObject, "Body")?.state is EntityStates.CursedApple.Deadlock.DeadlockMain;
            bool want = free && _held >= MinaData.HoverHoldDelay && motor.velocity.y <= 0f && _used < MinaData.HoverSeconds;
            if (want)
            {
                _used += Time.fixedDeltaTime;
                Vector3 v = motor.velocity;
                if (v.y < -MinaData.HoverFallSpeed) motor.velocity = new Vector3(v.x, -MinaData.HoverFallSpeed, v.z);
            }
            Set(want);
        }

        private void Set(bool on)
        {
            if (on == _on) return;
            _on = on;
            MinaNet.ToServer(new MinaHoverMessage { Body = gameObject, On = on });
        }

        private void Show()
        {
            bool on = MinaSurvivor.HoverBuff != null && _body.HasBuff(MinaSurvivor.HoverBuff);
            if (on == _shown) return;
            _shown = on;
            if (on)
            {
                DeadlockSounds.Play("mina_hover_start", transform.position, gameObject);
                _loop = DeadlockSounds.Loop("mina_hover_loop", gameObject, MinaData.HoverSeconds + 1f);
            }
            else
            {
                DeadlockSounds.Stop(_loop);
                DeadlockSounds.Play("mina_hover_end", transform.position, gameObject);
            }
        }

        private void OnDestroy() => DeadlockSounds.Stop(_loop);
    }
}
