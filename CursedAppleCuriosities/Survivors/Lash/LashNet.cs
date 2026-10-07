using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Entity states run on the player who owns Lash; damage, healing and moving enemies must happen on the
    /// server. These messages carry that over (on a host they simply run locally).
    /// </summary>
    public static class LashNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<FlogMessage>();
            NetworkingAPI.RegisterMessageType<SlamMessage>();
            NetworkingAPI.RegisterMessageType<SoundMessage>();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Lash, with procs. Returns the health actually taken.</summary>
        public static float Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type)
        {
            if (target == null || !target.alive) return 0f;
            float before = target.combinedHealth;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = target.body != null ? target.body.corePosition : target.transform.position,
                force = force,
                procCoefficient = 1f,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
            return Mathf.Max(0f, before - target.combinedHealth);
        }

        /// <summary>Enemies of a team whose hurtboxes touch a sphere (one per body).</summary>
        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var mask = TeamMask.GetEnemyTeams(team);
            var list = new List<HealthComponent>();
            HurtBox[] boxes = new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(mask).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
            foreach (HurtBox h in boxes) if (h.healthComponent != null && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }
    }

    /// <summary>Flog: whip every target, heal Lash for a share of what it took.</summary>
    public class FlogMessage : INetMessage
    {
        public GameObject? Attacker;
        public GameObject[] Targets = System.Array.Empty<GameObject>();
        public float Damage, HealFraction;
        public bool Crit;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Attacker);
            w.Write(Damage);
            w.Write(HealFraction);
            w.Write(Crit);
            w.Write(Targets.Length);
            foreach (GameObject t in Targets) w.Write(t);
        }

        public void Deserialize(NetworkReader r)
        {
            Attacker = r.ReadGameObject();
            Damage = r.ReadSingle();
            HealFraction = r.ReadSingle();
            Crit = r.ReadBoolean();
            Targets = new GameObject[r.ReadInt32()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;
            float dealt = 0f;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null) continue;
                Vector3 push = (t!.transform.position - Attacker.transform.position).normalized * -400f; // pulled toward Lash
                dealt += LashNet.Hit(body, hc, Damage, Crit, push, DamageSource.Secondary);
            }
            if (dealt > 0f && HealFraction > 0f) body.healthComponent.Heal(dealt * HealFraction, default(ProcChainMask));
        }
    }

    /// <summary>Death Slam: phase 0 lifts the locked enemies up to Lash, phase 1 throws them at Point.</summary>
    public class SlamMessage : INetMessage
    {
        public byte Phase;
        public GameObject? Attacker;
        public GameObject[] Targets = System.Array.Empty<GameObject>();
        public Vector3 Point;
        public float Damage;
        public bool Crit;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Phase);
            w.Write(Attacker);
            w.Write(Point);
            w.Write(Damage);
            w.Write(Crit);
            w.Write(Targets.Length);
            foreach (GameObject t in Targets) w.Write(t);
        }

        public void Deserialize(NetworkReader r)
        {
            Phase = r.ReadByte();
            Attacker = r.ReadGameObject();
            Point = r.ReadVector3();
            Damage = r.ReadSingle();
            Crit = r.ReadBoolean();
            Targets = new GameObject[r.ReadInt32()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            if (Phase == 0)
            {
                for (int i = 0; i < Targets.Length; i++)
                {
                    GameObject t = Targets[i];
                    if (t == null) continue;
                    var carry = t.GetComponent<LashSlamCarry>() ?? t.AddComponent<LashSlamCarry>();
                    carry.Lift(body, i);
                }
                return;
            }
            var group = new LashSlamCarry.ThrowGroup { Attacker = body, Damage = Damage, Crit = Crit };
            foreach (GameObject t in Targets)
            {
                if (t == null) continue;
                group.Thrown.Add(t);
                var carry = t.GetComponent<LashSlamCarry>() ?? t.AddComponent<LashSlamCarry>();
                carry.Throw(group, Point + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)));
            }
        }
    }

    /// <summary>Server to every client: play a Deadlock sound there (impacts only the server sees happen).</summary>
    public class SoundMessage : INetMessage
    {
        public string Id = "";
        public Vector3 Point;

        public void Serialize(NetworkWriter w) { w.Write(Id); w.Write(Point); }
        public void Deserialize(NetworkReader r) { Id = r.ReadString(); Point = r.ReadVector3(); }
        public void OnReceived() => DeadlockSounds.Play(Id, Point);

        public static void Everyone(string id, Vector3 point) => new SoundMessage { Id = id, Point = point }.Send(NetworkDestination.Clients);
    }

    /// <summary>
    /// Server only: moves one enemy for Death Slam. Lift pulls it up beside Lash and holds it; Throw flings it at
    /// the landing point, where it (and everyone nearby not yet hit by this throw) takes the impact once.
    /// </summary>
    public class LashSlamCarry : MonoBehaviour
    {
        public class ThrowGroup
        {
            public CharacterBody Attacker = null!;
            public float Damage;
            public bool Crit;
            public readonly HashSet<HealthComponent> Damaged = new HashSet<HealthComponent>();
            public readonly HashSet<GameObject> Thrown = new HashSet<GameObject>();
        }

        private enum Phase { Lift, Throw }
        private Phase _phase;
        private CharacterBody? _lash;
        private int _slot;
        private float _started, _limit;
        private Vector3 _land;
        private ThrowGroup? _group;
        private CharacterBody? _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
        }

        public void Lift(CharacterBody lash, int slot)
        {
            _phase = Phase.Lift;
            _lash = lash;
            _slot = slot;
            _started = Time.time;
            _limit = LashData.DeathSlamPull + LashData.DeathSlamHang + 2f; // safety: a throw normally follows
            SetStateOnHurt.SetStunOnObject(gameObject, _limit);
        }

        public void Throw(ThrowGroup group, Vector3 land)
        {
            _phase = Phase.Throw;
            _group = group;
            _lash = group.Attacker;
            _land = land;
            _started = Time.time;
            _limit = Vector3.Distance(transform.position, land) / LashData.DeathSlamSpeed + 0.6f;
        }

        private Vector3 HoldSpot()
        {
            Transform l = _lash!.transform;
            Vector3 up = l.position + Vector3.up * 3.5f;
            Vector3 fwd = _lash.inputBank != null ? _lash.inputBank.aimDirection : l.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            return _slot == 0 ? up + fwd * 1.5f : up + Quaternion.Euler(0f, _slot * 60f, 0f) * fwd * 2f;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null || _lash == null || _body.healthComponent == null || !_body.healthComponent.alive) { Destroy(this); return; }
            float t = Time.time - _started;
            if (_phase == Phase.Lift)
            {
                Vector3 to = HoldSpot() - _body.transform.position;
                float speed = Mathf.Max(4f, to.magnitude / Mathf.Max(0.05f, LashData.DeathSlamPull - t));
                Move(to, speed);
                if (t > _limit) Destroy(this);
                return;
            }
            Vector3 toLand = _land - _body.footPosition;
            if (toLand.magnitude < 1.2f || t > _limit || (_motor != null && _motor.isGrounded && t > 0.25f)) { Impact(); return; }
            Move(toLand, LashData.DeathSlamSpeed);
        }

        private void Move(Vector3 to, float maxSpeed)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 v = to / dt;
            if (v.magnitude > maxSpeed) v = v.normalized * maxSpeed;
            if (_motor != null)
            {
                _motor.velocity = v;
                _motor.Motor.ForceUnground();
                _motor.disableAirControlUntilCollision = true;
            }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = v;
            else transform.position += v * dt;
        }

        private void Impact()
        {
            ThrowGroup g = _group!;
            Vector3 at = _body!.footPosition;
            if (_motor != null) _motor.velocity = Vector3.zero;
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = Vector3.zero;
            if (LashSurvivor.SlamEffect != null)
                EffectManager.SpawnEffect(LashSurvivor.SlamEffect, new EffectData { origin = at, scale = LashData.DeathSlamRadius }, transmit: true);
            SoundMessage.Everyone("lash_ds_impact", at);
            if (g.Attacker != null)
            {
                var victims = LashNet.EnemiesIn(at, LashData.DeathSlamRadius, g.Attacker.teamComponent.teamIndex);
                if (_body.healthComponent != null && !victims.Contains(_body.healthComponent)) victims.Insert(0, _body.healthComponent);
                foreach (HealthComponent hc in victims)
                {
                    if (hc != _body.healthComponent && g.Thrown.Contains(hc.gameObject)) continue; // takes it where it lands
                    if (!g.Damaged.Add(hc)) continue;
                    LashNet.Hit(g.Attacker, hc, g.Damage, g.Crit, Vector3.down * 1000f, DamageSource.Special);
                    if (hc.body != null) hc.body.AddTimedBuff(RoR2Content.Buffs.Slow50, LashData.DeathSlamSlowSeconds);
                }
            }
            SetStateOnHurt.SetStunOnObject(gameObject, 0.6f);
            Destroy(this);
        }
    }
}
