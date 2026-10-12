using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Entity states run on the player who owns Seven; damage, stuns and buffs happen on the server. These messages
    /// carry that over (on a host they simply run locally). Visual-only messages go back out to every client.
    /// </summary>
    public static class SevenNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<SevenBuffMessage>();
            NetworkingAPI.RegisterMessageType<SevenChainMessage>();
            NetworkingAPI.RegisterMessageType<SevenStaticMessage>();
            NetworkingAPI.RegisterMessageType<SevenStaticFxMessage>();
            NetworkingAPI.RegisterMessageType<SevenStrikeMessage>();
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>
        /// Server: a visual-only message to every client. Receivers skip it on the host (NetworkServer.active), so the
        /// caller shows it there itself.
        /// </summary>
        public static void ToClients(INetMessage m)
        {
            if (NetworkServer.active) m.Send(NetworkDestination.Clients);
        }

        /// <summary>Server: one hit from Seven, with procs.</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc)
        {
            if (target == null || !target.alive || damage <= 0f) return;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = target.body != null ? target.body.corePosition : target.transform.position,
                force = force,
                procCoefficient = proc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
        }

        /// <summary>Enemies of a team whose hurtboxes touch a sphere (one per body).</summary>
        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            HurtBox[] boxes = new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
            foreach (HurtBox h in boxes) if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        public static void Effect(GameObject? prefab, Vector3 at, float scale)
        {
            if (prefab != null && NetworkServer.active) EffectManager.SpawnEffect(prefab, new EffectData { origin = at, scale = scale }, true);
        }
    }

    public enum SevenBuffKind : byte { Surge, StormStart, StormStop }

    /// <summary>Power Surge or Storm Cloud starts/ends: the server sets the synced buff (and Storm Cloud's ticking).</summary>
    public class SevenBuffMessage : INetMessage
    {
        public GameObject? Attacker;
        public SevenBuffKind Kind;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write((byte)Kind); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Kind = (SevenBuffKind)r.ReadByte(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;
            switch (Kind)
            {
                case SevenBuffKind.Surge:
                    if (SevenSurvivor.SurgeBuff == null) return;
                    body.ClearTimedBuffs(SevenSurvivor.SurgeBuff);
                    body.AddTimedBuff(SevenSurvivor.SurgeBuff, SevenData.SurgeDuration);
                    break;
                case SevenBuffKind.StormStart:
                    if (SevenSurvivor.StormBuff == null) return;
                    body.ClearTimedBuffs(SevenSurvivor.StormBuff);
                    body.AddTimedBuff(SevenSurvivor.StormBuff, SevenData.StormChannel + SevenData.StormRiseTime + 0.5f);
                    (body.GetComponent<SevenStorm>() ?? body.gameObject.AddComponent<SevenStorm>()).Begin();
                    break;
                case SevenBuffKind.StormStop:
                    if (SevenSurvivor.StormBuff != null) body.ClearTimedBuffs(SevenSurvivor.StormBuff);
                    body.GetComponent<SevenStorm>()?.End();
                    break;
            }
        }
    }

    /// <summary>
    /// Power Surge: a burst hit an enemy, so a shock jumps from it to up to ChainCount enemies near it in turn
    /// (each new one within ChainRadius of the last), stronger with every jump (Deadlock BonusPerChain).
    /// </summary>
    public class SevenChainMessage : INetMessage
    {
        public GameObject? Attacker, Target;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null || Target == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            var first = Target.GetComponent<HealthComponent>();
            if (body == null || first == null || SevenSurvivor.SurgeBuff == null || !body.HasBuff(SevenSurvivor.SurgeBuff)) return;
            float radius = SevenData.SurgeChainRadius * MysticExpansion.RadiusMultiplier(body);
            EquipmentFx.Sound("seven_surge_fire", SevenNet.Core(first));
            var hit = new List<HealthComponent>();
            HealthComponent current = first;
            Vector3 from = SevenNet.Core(first);
            for (int i = 0; i < SevenData.SurgeChainCount && current != null; i++)
            {
                hit.Add(current);
                HurtBox? box = current.body != null && current.body.mainHurtBox != null ? current.body.mainHurtBox : null;
                if (box == null) break;
                OrbManager.instance.AddOrb(new LightningOrb
                {
                    origin = from,
                    damageValue = (SevenData.SurgeChainDamage + SevenData.SurgeChainGrowth * i) * body.damage,
                    isCrit = Crit,
                    bouncesRemaining = 0,
                    teamIndex = body.teamComponent.teamIndex,
                    attacker = body.gameObject,
                    procCoefficient = 0.5f,
                    lightningType = LightningOrb.LightningType.Ukulele,
                    damageColorIndex = DamageColorIndex.Item,
                    range = radius,
                    target = box,
                    damageType = DamageSource.Utility,
                });
                from = SevenNet.Core(current);
                HealthComponent? next = null;
                float best = float.MaxValue;
                foreach (HealthComponent hc in SevenNet.EnemiesIn(from, radius, body.teamComponent.teamIndex))
                {
                    if (hit.Contains(hc)) continue;
                    float d = (SevenNet.Core(hc) - from).sqrMagnitude;
                    if (d < best) { best = d; next = hc; }
                }
                current = next!;
            }
        }
    }

    /// <summary>Static Charge: a charge on an enemy (it follows them) or on a spot; it goes off after the delay.</summary>
    public class SevenStaticMessage : INetMessage
    {
        public GameObject? Attacker, Target;
        public Vector3 Point;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Point); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Point = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            var go = new GameObject("CursedAppleSevenStaticCharge");
            go.transform.position = Point;
            var charge = go.AddComponent<SevenStaticCharge>();
            charge.By = body;
            charge.Target = Target;
            charge.Crit = Crit;
            charge.Radius = SevenData.StaticRadius * MysticExpansion.RadiusMultiplier(body);
            var fx = new SevenStaticFxMessage { Target = Target, Point = Point, Radius = charge.Radius };
            SevenNet.ToClients(fx);
            if (NetworkClient.active) fx.Show(); // the host's own view
        }
    }

    /// <summary>Every client: the ring that counts down a Static Charge (follows the marked enemy).</summary>
    public class SevenStaticFxMessage : INetMessage
    {
        public GameObject? Target;
        public Vector3 Point;
        public float Radius;

        public void Serialize(NetworkWriter w) { w.Write(Target); w.Write(Point); w.Write(Radius); }
        public void Deserialize(NetworkReader r) { Target = r.ReadGameObject(); Point = r.ReadVector3(); Radius = r.ReadSingle(); }

        public void OnReceived() { if (!NetworkServer.active) Show(); }

        public void Show()
        {
            var go = new GameObject("CursedAppleSevenStaticRing");
            go.transform.position = Point;
            var ring = go.AddComponent<SevenRing>();
            ring.Follow = Target;
            ring.Radius = Radius;
            ring.Seconds = SevenData.StaticDelay;
            ring.Color = new Color(SevenSurvivor.Orange.r, SevenSurvivor.Orange.g, SevenSurvivor.Orange.b, 0.9f);
        }
    }

    /// <summary>Server: one pending Static Charge.</summary>
    public class SevenStaticCharge : MonoBehaviour
    {
        public CharacterBody? By;
        public GameObject? Target;
        public bool Crit;
        public float Radius;
        private float _age;

        private void FixedUpdate()
        {
            if (Target != null)
            {
                var hc = Target.GetComponent<HealthComponent>();
                if (hc != null && hc.alive) transform.position = SevenNet.Core(hc);
            }
            _age += Time.fixedDeltaTime;
            if (_age < SevenData.StaticDelay) return;
            if (By != null && By.teamComponent != null)
            {
                Vector3 at = transform.position;
                SevenNet.Effect(SevenSurvivor.StrikeEffect, at, Radius);
                foreach (HealthComponent hc in SevenNet.EnemiesIn(at, Radius, By.teamComponent.teamIndex))
                {
                    SevenNet.Hit(By, hc, SevenData.StaticDamage * By.damage, Crit, Vector3.zero, DamageSource.Secondary, 1f);
                    if (hc.alive) using (DurationExtender.From(By)) StunEvenBosses(hc.gameObject, SevenData.StaticStun);
                }
            }
            Destroy(gameObject);
        }

        /// <summary>
        /// Static Charge stuns bosses too (user). Bosses usually have stuns switched off (SetStateOnHurt.canBeStunned): it is
        /// switched on just for this stun, then put back, so nothing else gains the ability to stun them.
        /// </summary>
        private static void StunEvenBosses(GameObject target, float seconds)
        {
            var ssoh = target.GetComponent<SetStateOnHurt>();
            if (ssoh == null) return; // no hurt states at all (a few scripted bodies): leave them be
            bool was = ssoh.canBeStunned;
            ssoh.canBeStunned = true;
            try { SetStateOnHurt.SetStunOnObject(target, seconds); }
            finally { ssoh.canBeStunned = was; }
        }
    }

    /// <summary>Storm Cloud's Lightning Strike: a marked spot, struck after a short delay, knocking enemies away.</summary>
    public class SevenStrikeMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Point;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Point); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Point = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            EquipmentFx.Sound("seven_strike_cast", Point);
            var go = new GameObject("CursedAppleSevenStrike");
            go.transform.position = Point;
            var s = go.AddComponent<SevenStrike>();
            s.By = body;
            s.Crit = Crit;
        }
    }

    public class SevenStrike : MonoBehaviour
    {
        public CharacterBody? By;
        public bool Crit;
        private float _age;

        private void FixedUpdate()
        {
            _age += Time.fixedDeltaTime;
            if (_age < SevenData.StrikeDelay) return;
            if (By != null && By.teamComponent != null)
            {
                Vector3 at = transform.position;
                float radius = SevenData.StrikeRadius * MysticExpansion.RadiusMultiplier(By);
                SevenNet.Effect(SevenSurvivor.StrikeEffect, at, radius);
                EquipmentFx.Sound("seven_strike_explode", at);
                foreach (HealthComponent hc in SevenNet.EnemiesIn(at, radius, By.teamComponent.teamIndex))
                {
                    Vector3 away = SevenNet.Core(hc) - at;
                    away.y = 0f;
                    away = (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) + Vector3.up * 0.6f;
                    float mass = hc.body != null && hc.body.rigidbody != null ? hc.body.rigidbody.mass : 100f;
                    SevenNet.Hit(By, hc, SevenData.StrikeDamage * By.damage, Crit, away.normalized * SevenData.StrikeKnockback * Mathf.Max(1f, mass / 100f), DamageSource.Special, 1f);
                }
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Server, on Seven while Storm Cloud channels: every 0.3 s, every enemy inside the cloud that Seven can see takes
    /// damage. The cloud starts at Deadlock's 10 m and grows (slowly, then fast) to the whole map by the end (user).
    /// </summary>
    public class SevenStorm : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _age, _tick;
        private bool _on;
        private int _ticks;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin() { _on = true; _age = 0f; _tick = 0f; _ticks = 0; }
        public void End() => _on = false;

        public float Radius => SevenData.StormRadius(_age, _body);

        private void FixedUpdate()
        {
            if (!_on || !NetworkServer.active) return;
            if (_body == null || _body.healthComponent == null || !_body.healthComponent.alive || SevenSurvivor.StormBuff == null || !_body.HasBuff(SevenSurvivor.StormBuff)) { _on = false; return; }
            _age += Time.fixedDeltaTime;
            _tick += Time.fixedDeltaTime;
            if (_tick < SevenData.StormTick) return;
            _tick -= SevenData.StormTick;
            _ticks++;
            Vector3 eye = _body.corePosition + Vector3.up * 1f;
            bool crit = _body.RollCrit();
            foreach (HealthComponent hc in SevenNet.EnemiesIn(_body.corePosition, Radius, _body.teamComponent.teamIndex))
            {
                Vector3 to = SevenNet.Core(hc);
                // Deadlock: enemies out of Seven's line of sight take no damage.
                if (Physics.Linecast(eye, to, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                float dmg = SevenData.StormDps(Mathf.Clamp01(_age / (SevenData.StormChannel * DurationExtender.Multiplier(_body)))) * SevenData.StormTick * _body.damage;
                if (hc.body != null && hc.body.isChampion) dmg *= SevenData.StormBossScale;
                SevenNet.Hit(_body, hc, dmg, crit, Vector3.zero, DamageSource.Special, 0.3f);
                if ((_ticks + hc.GetInstanceID()) % 3 == 0) SevenNet.Effect(SevenSurvivor.BoltEffect, to, 1f);
            }
        }
    }

    /// <summary>Server, on the Lightning Ball projectile: shocks what's inside, crawls while it does, sticks to walls.</summary>
    [RequireComponent(typeof(ProjectileController))]
    public class SevenBall : MonoBehaviour, IProjectileImpactBehavior
    {
        private ProjectileController _pc = null!;
        private ProjectileDamage? _pd;
        private ProjectileSimple? _ps;
        private Rigidbody? _rb;
        private float _tick, _age;
        private bool _stuck;

        private void Awake()
        {
            _pc = GetComponent<ProjectileController>();
            _pd = GetComponent<ProjectileDamage>();
            _ps = GetComponent<ProjectileSimple>();
            _rb = GetComponent<Rigidbody>();
        }

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            // Deadlock PBF_StickToWorld: the ball stops at a wall and keeps shocking until it fades.
            if (info.collider != null && info.collider.GetComponent<HurtBox>() != null) return;
            Stick();
        }

        private void Stick()
        {
            _stuck = true;
            if (_ps != null) { _ps.desiredForwardSpeed = 0f; _ps.enabled = false; }
            if (_rb != null) { _rb.velocity = Vector3.zero; _rb.isKinematic = true; }
        }

        private DeadlockVoice? _hum;

        /// <summary>
        /// Every client: the hum, and a see-through sphere exactly the size of the shock radius (Mystic Expansion included),
        /// so the ball shows what it reaches.
        /// </summary>
        private void Start()
        {
            _hum = DeadlockSounds.Loop("seven_ball_loop", gameObject, SevenData.BallLifetime + 0.5f);
            if (SevenSurvivor.RangeMaterial == null) return;
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(sphere.GetComponent<Collider>()); // visual only: no physics on the projectile
            sphere.name = "RangeSphere";
            sphere.transform.SetParent(transform, false);
            _sphere = sphere.transform;
            Update();
            var r = sphere.GetComponent<MeshRenderer>();
            r.sharedMaterial = SevenSurvivor.RangeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private Transform? _sphere;

        /// <summary>Sized every frame: a client may only learn the ball's owner (and their Mystic Expansion) after it spawns.</summary>
        private void Update()
        {
            if (_sphere == null) return;
            GameObject? owner = _pc != null ? _pc.owner : null;
            float radius = SevenData.BallRadius * MysticExpansion.RadiusMultiplier(owner != null ? owner.GetComponent<CharacterBody>() : null);
            _sphere.localScale = Vector3.one * radius * 2f / Mathf.Max(0.01f, transform.lossyScale.x);
        }

        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            float dt = Time.fixedDeltaTime;
            _age += dt;
            if (_age >= SevenData.BallLifetime) { EquipmentFx.Sound("seven_ball_explode", transform.position); Destroy(gameObject); return; }
            // Walls: look ahead, since enemies pass through.
            if (!_stuck && _rb != null && _rb.velocity.sqrMagnitude > 0.01f &&
                Physics.SphereCast(transform.position, 0.3f, _rb.velocity.normalized, out _, _rb.velocity.magnitude * dt + 0.2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                Stick();
            _tick += dt;
            if (_tick < SevenData.BallTickRoR2) return;
            _tick -= SevenData.BallTickRoR2;
            GameObject? owner = _pc.owner;
            var body = owner != null ? owner.GetComponent<CharacterBody>() : null;
            if (body == null || body.teamComponent == null) return;
            float radius = SevenData.BallRadius * MysticExpansion.RadiusMultiplier(body);
            List<HealthComponent> inside = SevenNet.EnemiesIn(transform.position, radius, body.teamComponent.teamIndex);
            float baseDamage = _pd != null ? _pd.damage : body.damage;
            if (inside.Count > 0) EquipmentFx.Sound("seven_ball_zap", transform.position);
            foreach (HealthComponent hc in inside)
            {
                SevenNet.Hit(body, hc, SevenData.BallDps * SevenData.BallTickRoR2 * baseDamage, _pd != null && _pd.crit, Vector3.zero, DamageSource.Secondary, SevenData.BallProc);
                SevenNet.Effect(SevenSurvivor.BoltEffect, SevenNet.Core(hc), 0.6f);
            }
            if (_ps != null && !_stuck) _ps.desiredForwardSpeed = inside.Count > 0 ? SevenData.BallHitSpeed : SevenData.BallSpeed;
        }
    }

    /// <summary>A ground ring that shrinks to nothing over its lifetime (Static Charge's countdown), following a body.</summary>
    public class SevenRing : MonoBehaviour
    {
        public GameObject? Follow;
        public float Radius = 5f, Seconds = 3.5f;
        public Color Color = Color.cyan;
        private LineRenderer _outer = null!, _inner = null!;
        private float _age;

        private void Start()
        {
            _outer = SevenFx.Line("ring", Color, 0.12f, transform);
            _inner = SevenFx.Line("count", Color, 0.08f, transform);
            _hum = DeadlockSounds.Loop("seven_static_loop", gameObject, Seconds);
        }

        private DeadlockVoice? _hum;

        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Seconds) { Destroy(gameObject); return; }
            if (Follow != null)
            {
                var hc = Follow.GetComponent<HealthComponent>();
                if (hc != null) transform.position = hc.body != null ? hc.body.footPosition : hc.transform.position;
            }
            SevenFx.Circle(_outer, transform.position, Radius);
            SevenFx.Circle(_inner, transform.position, Radius * (1f - _age / Seconds));
        }
    }

    internal static class SevenFx
    {
        public static LineRenderer Line(string name, Color color, float width, Transform? parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = SevenSurvivor.IndicatorMaterial;
            if (lr.material != null && lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", color);
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        public static void Circle(LineRenderer lr, Vector3 at, float r)
        {
            const int n = 40;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, at + new Vector3(Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r));
            }
        }
    }
}

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>Every client: Power Surge's stop sound when the buff runs out.</summary>
    public class SevenSurgeEnd : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private bool _had;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            bool has = SevenSurvivor.SurgeBuff != null && _body.HasBuff(SevenSurvivor.SurgeBuff);
            if (_had && !has && _body.healthComponent != null && _body.healthComponent.alive) DeadlockSounds.Play("seven_surge_stop", transform.position, gameObject);
            _had = has;
        }
    }
}

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Every client, on Seven: while Storm Cloud channels, an orange ring on the ground at the storm's damage edge and a
    /// see-through dome over it, growing with the same radius the server damages with (SevenData.StormRadius).
    /// </summary>
    public class SevenStormFx : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private LineRenderer? _ring;
        private Transform? _dome;
        private float _age = -1f;
        private Vector3 _ground;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            bool storming = SevenSurvivor.StormBuff != null && _body.HasBuff(SevenSurvivor.StormBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (!storming) { if (_age >= 0f) Clear(); return; }
            if (_age < 0f) Begin();
            _age += Time.deltaTime;
            float r = SevenData.StormRadius(_age, _body);
            // The ground under him (he floats): a raycast down, kept from the last hit when he's over a drop.
            if (Physics.Raycast(_body.corePosition, Vector3.down, out RaycastHit hit, 200f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) _ground = hit.point;
            else _ground = new Vector3(_body.corePosition.x, _ground.y, _body.corePosition.z);
            if (_ring != null)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt(r * 2f), 48, 256);
                _ring.positionCount = n;
                _ring.startWidth = _ring.endWidth = Mathf.Clamp(r * 0.01f, 0.15f, 4f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    _ring.SetPosition(i, _ground + new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r));
                }
            }
            if (_dome != null)
            {
                _dome.position = _ground;
                _dome.localScale = Vector3.one * r * 2f;
            }
        }

        private void Begin()
        {
            _age = 0f;
            _ground = _body.footPosition;
            _ring = SevenFx.Line("CursedAppleSevenStormRing", new Color(SevenSurvivor.Orange.r, SevenSurvivor.Orange.g, SevenSurvivor.Orange.b, 0.9f), 0.2f);
            if (SevenSurvivor.RangeMaterial != null)
            {
                GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(dome.GetComponent<Collider>());
                dome.name = "CursedAppleSevenStormDome";
                var r = dome.GetComponent<MeshRenderer>();
                r.sharedMaterial = SevenSurvivor.RangeMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _dome = dome.transform;
            }
        }

        private void Clear()
        {
            _age = -1f;
            if (_ring != null) Destroy(_ring.gameObject);
            if (_dome != null) Destroy(_dome.gameObject);
            _ring = null;
            _dome = null;
        }

        private void OnDestroy() => Clear();
    }
}

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// The local player's Seven: while Static Charge is equipped and ready, Trophy Hunter's Tricorn reticle marks the enemy
    /// it would lock onto (same pick as the cast). Its own component, so equipment targeting can't hide it.
    /// </summary>
    public class SevenStaticReticle : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private Indicator? _indicator;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            Transform? at = null;
            GenericSkill? slot = _body.skillLocator != null ? _body.skillLocator.secondary : null;
            if (_body.hasEffectiveAuthority && _body.isPlayerControlled && slot != null && slot.skillDef == SevenSurvivor.StaticSkill && slot.stock > 0
                && _body.healthComponent != null && _body.healthComponent.alive)
            {
                CharacterBody? target = EntityStates.CursedApple.Seven.StaticCharge.Target(_body);
                if (target != null) at = target.mainHurtBox != null ? target.mainHurtBox.transform : target.coreTransform;
            }
            if (_indicator == null)
            {
                if (at == null) return;
                _indicator = new Indicator(gameObject, CursedAppleCuriosities.Items.EquipmentTargeting.IndicatorPrefab);
            }
            _indicator.targetTransform = at;
            _indicator.active = at != null;
        }

        private void OnDestroy() { if (_indicator != null) _indicator.active = false; }
    }
}

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Every client, on Seven: Storm Cloud's look, as Deadlock's animgraph does it ("Weapon_Vis_OFF", "Skeleton_Vis_Storm_Cloud").
    /// While channeling only his glowing skeleton shows: his body, clothes, headgear and gun are hidden (user); otherwise the
    /// skeleton is hidden. Each is its own mesh in his model (renderer objects "&lt;mesh&gt;_N"), found once the Deadlock model is on.
    /// </summary>
    public class SevenStormLook : MonoBehaviour
    {
        private CharacterBody? _body; // none on the lobby display: never storming there
        private readonly System.Collections.Generic.List<GameObject> _regular = new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<GameObject> _skeleton = new System.Collections.Generic.List<GameObject>();
        private float _nextSearch;
        private bool? _storming;
        /// <summary>The lobby intro shows the storm look on the display (no body, no buff there).</summary>
        public bool ForceStorm;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            bool storming = ForceStorm || _body != null && SevenSurvivor.StormBuff != null && _body.HasBuff(SevenSurvivor.StormBuff);
            if (_regular.Count + _skeleton.Count == 0 || _regular.Exists(g => g == null) || _skeleton.Exists(g => g == null))
            {
                if (Time.time < _nextSearch) return;
                _nextSearch = Time.time + 0.1f; // the Deadlock model arrives a little after spawn (and the lobby intro starts at once)
                _regular.Clear();
                _skeleton.Clear();
                Transform? model = _body != null ? (_body.modelLocator != null ? _body.modelLocator.modelTransform : null) : transform;
                if (model == null) return;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.gameObject.name;
                    // Only his own meshes (never the hidden stand-in's renderers).
                    if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^(gun|outer_costume|shirt_backfaces|headgear)_\d+$")) _regular.Add(r.gameObject);
                    else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^skeleton_\d+$")) _skeleton.Add(r.gameObject);
                }
                _storming = null; // apply to what was just found
            }
            if (_storming == storming) return;
            _storming = storming;
            foreach (GameObject g in _regular) if (g != null) g.SetActive(!storming);
            foreach (GameObject g in _skeleton) if (g != null) g.SetActive(storming);
        }
    }
}
