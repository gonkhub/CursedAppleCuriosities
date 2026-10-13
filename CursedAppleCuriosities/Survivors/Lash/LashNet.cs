using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
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
            NetworkingAPI.RegisterMessageType<SlamMessage>();
            NetworkingAPI.RegisterMessageType<SoundMessage>();
            NetworkingAPI.RegisterMessageType<DiveGrabMessage>();
            DeadlockFallGuard.Hook();
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

    /// <summary>
    /// Ground Strike's dive: Release false = Lash's dive touched this enemy (the server carries it down only if it's
    /// airborne); Release true = Lash hit terrain, let go of everything he carries.
    /// </summary>
    public class DiveGrabMessage : INetMessage
    {
        public GameObject? Attacker;
        public GameObject? Target;
        public bool Release;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Release); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Release = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var lash = Attacker.GetComponent<CharacterBody>();
            if (lash == null) return;
            if (Release)
            {
                foreach (LashDiveCarry c in Object.FindObjectsOfType<LashDiveCarry>()) if (c.Lash == lash) c.Drop();
                return;
            }
            if (Target == null || Target.GetComponent<LashDiveCarry>() != null || Target.GetComponent<LashSlamCarry>() != null) return;
            var body = Target.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;
            // Airborne only: a flyer, or anything off the ground (knocked up, mid-jump). Grounded enemies just take the strike.
            if (body.characterMotor != null && body.characterMotor.isGrounded) return;
            int slot = 0;
            foreach (LashDiveCarry c in Object.FindObjectsOfType<LashDiveCarry>()) if (c.Lash == lash) slot++;
            Target.AddComponent<LashDiveCarry>().Grab(lash, slot);
        }
    }

    /// <summary>
    /// Server only: an airborne enemy Lash's Ground Strike dive ran into, dragged down beside him (stunned, no fall
    /// damage) until he hits terrain; there it's let go inside his strike. Its own motor still collides, so it stops on
    /// the ground rather than being pushed through it.
    /// </summary>
    public class LashDiveCarry : MonoBehaviour
    {
        public CharacterBody? Lash;
        private int _slot;
        private float _until;
        private CharacterBody? _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;

        public void Grab(CharacterBody lash, int slot)
        {
            Lash = lash;
            _slot = slot;
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
            _until = Time.time + LashData.GroundStrikeCarryMax;
            SetStateOnHurt.SetStunOnObject(gameObject, LashData.GroundStrikeCarryMax);
            DeadlockFallGuard.Protect(gameObject, LashData.GroundStrikeCarryMax + 1.5f);
            SoundMessage.Everyone("lash_gs_hit", transform.position);
        }

        /// <summary>Beside Lash rather than under him, so he never lands on what he carries.</summary>
        private Vector3 HoldSpot()
        {
            Vector3 v = Lash!.characterMotor != null ? Lash.characterMotor.velocity : Vector3.down;
            Vector3 fwd = new Vector3(v.x, 0f, v.z);
            fwd = fwd.sqrMagnitude > 0.5f ? fwd.normalized : Lash.transform.forward;
            float reach = 1.2f + (_body != null ? _body.radius : 1f) + Lash.radius;
            Vector3 side = Quaternion.Euler(0f, _slot * 70f, 0f) * fwd * reach;
            return Lash.transform.position + side;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Lash == null || _body == null || _body.healthComponent == null || !_body.healthComponent.alive || Time.time > _until) { Drop(); return; }
            float dt = Time.fixedDeltaTime;
            Vector3 v = (HoldSpot() - _body.transform.position) / dt;
            if (v.magnitude > LashData.GroundStrikeCarrySpeed) v = v.normalized * LashData.GroundStrikeCarrySpeed;
            if (_motor != null)
            {
                _motor.velocity = v;
                _motor.Motor.ForceUnground();
                _motor.disableAirControlUntilCollision = true;
            }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = v;
            else transform.position += v * dt;
        }

        public void Drop()
        {
            if (_motor != null) _motor.velocity = Vector3.zero;
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = Vector3.zero;
            if (_body != null && _body.healthComponent != null && _body.healthComponent.alive)
            {
                DeadlockFallGuard.Protect(gameObject, 1.5f, landed: true);
                SetStateOnHurt.SetStunOnObject(gameObject, 0.6f);
            }
            Destroy(this);
        }
    }

    /// <summary>Server to every client: play a Deadlock sound there (impacts only the server sees happen).</summary>
    public class SoundMessage : INetMessage
    {
        public string Id = "";
        public Vector3 Point;
        public GameObject? Follow; // optional: the sound tracks it, and its vanilla fallback can play on it

        public void Serialize(NetworkWriter w) { w.Write(Id); w.Write(Point); w.Write(Follow); }
        public void Deserialize(NetworkReader r) { Id = r.ReadString(); Point = r.ReadVector3(); Follow = r.ReadGameObject(); }
        public void OnReceived() => DeadlockSounds.Play(Id, Follow != null ? Follow.transform.position : Point, Follow);

        public static void Everyone(string id, Vector3 point, GameObject? follow = null) => new SoundMessage { Id = id, Point = point, Follow = follow }.Send(NetworkDestination.Clients);
    }

    /// <summary>
    /// Server only: moves one enemy for Death Slam. Lift pulls it up beside Lash and holds it; Throw flings it along
    /// the line to the landing point and keeps it on that line until it actually hits terrain (playtest: stopping at
    /// the aim point left enemies standing on an invisible floor). There it (and everyone nearby not yet hit by this
    /// throw) takes the impact once. Carried enemies take no fall damage (playtest: bosses lost huge chunks of health
    /// to it); one thrown off the map is killed, credited to Lash.
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
        private Vector3 _dir;
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
            DeadlockFallGuard.Protect(gameObject, _limit);
        }

        private ThrowGroup? _pendingGroup;
        private Vector3 _pendingLand;

        /// <summary>Still on its way up to Lash (server).</summary>
        private bool Rising => _phase == Phase.Lift && _lash != null && (HoldSpot() - transform.position).magnitude > LashData.DeathSlamArrived
            && Time.time - _started < LashData.DeathSlamWaitMax;

        public void Throw(ThrowGroup group, Vector3 land)
        {
            // Not up to Lash yet (user, 1.7): keep pulling, and throw the moment it arrives.
            if (Rising)
            {
                _pendingGroup = group;
                _pendingLand = land;
                _limit = LashData.DeathSlamWaitMax + 1f;
                SetStateOnHurt.SetStunOnObject(gameObject, _limit - (Time.time - _started));
                DeadlockFallGuard.Protect(gameObject, _limit);
                return;
            }
            _pendingGroup = null;
            _phase = Phase.Throw;
            _group = group;
            _lash = group.Attacker;
            _started = Time.time;
            Vector3 to = land - Feet();
            _dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.down;
            _limit = LashData.DeathSlamFlightMax;
            DeadlockFallGuard.Protect(gameObject, _limit + 1.5f);
        }

        private Vector3 Feet() => _body != null ? _body.footPosition : transform.position;

        private Vector3 HoldSpot()
        {
            Transform l = _lash!.transform;
            Vector3 fwd = _lash.inputBank != null ? _lash.inputBank.aimDirection : l.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 side = _slot == 0 ? fwd * 1.5f : Quaternion.Euler(0f, _slot * 60f, 0f) * fwd * 2f;
            // 3.5 m over Lash, lower under a ceiling: a big body held up there would be pushed through it.
            float rise = 3.5f, height = _body != null ? Mathf.Max(1f, _body.radius * 2f) : 2f;
            if (Physics.Raycast(l.position + side, Vector3.up, out RaycastHit ceiling, rise + height + 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                rise = Mathf.Max(0f, ceiling.distance - height - 1f);
            return l.position + side + Vector3.up * rise;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null || _lash == null || _body.healthComponent == null || !_body.healthComponent.alive) { Destroy(this); return; }
            float t = Time.time - _started;
            if (_phase == Phase.Lift)
            {
                // The pull takes DeathSlamPull whatever the distance (user, 1.7: from the skybox too): speed is what's left
                // over the time left, uncapped. Whatever terrain held back is put in place when the time is up.
                Vector3 spot = HoldSpot();
                Vector3 to = spot - _body.transform.position;
                float left = LashData.DeathSlamPull - t;
                if (left > Time.fixedDeltaTime) Move(to, Mathf.Max(4f, to.magnitude / left));
                else if (to.magnitude > LashData.DeathSlamArrived) Snap(spot);
                else Move(to, LashData.DeathSlamHoldSpeed);
                if (_pendingGroup != null && !Rising) { Throw(_pendingGroup, _pendingLand); return; }
                if (t > _limit) Destroy(this);
                return;
            }
            // Throw: straight along the line until terrain is in the way (cast ahead, so nothing tunnels through).
            float step = LashData.DeathSlamSpeed * Time.fixedDeltaTime;
            Vector3 from = Feet() + Vector3.up * 0.5f;
            bool blocked = Physics.SphereCast(from, 0.3f, _dir, out RaycastHit hit, step + 0.5f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (blocked || (t > 0.2f && _motor != null && _motor.isGrounded)) { Impact(blocked ? hit.point : Feet()); return; }
            if (t > _limit) { if (OverVoid()) KillInVoid(); else Impact(Feet()); return; }
            Move(_dir * step, LashData.DeathSlamSpeed);
        }

        /// <summary>No terrain anywhere below: off the map, or under its floor.</summary>
        private bool OverVoid() => !Physics.Raycast(Feet() + Vector3.up, Vector3.down, 4000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        private void KillInVoid()
        {
            HealthComponent hc = _body!.healthComponent;
            GameObject? killer = _group?.Attacker != null ? _group.Attacker.gameObject : null;
            var oob = new DamageTypeCombo(DamageType.OutOfBounds, DamageTypeExtended.OutOfBounds, DamageSource.Special);
            if (_body.master != null) _body.master.TrueKill(killer, killer, oob);
            else hc.Suicide(killer, killer, oob);
            Destroy(this);
        }

        /// <summary>Straight to a spot, through anything in the way (the hold spot itself is kept clear of ceilings).</summary>
        private void Snap(Vector3 spot)
        {
            if (_motor != null) { _motor.Motor.SetPosition(spot); _motor.velocity = Vector3.zero; }
            else if (_rb != null && !_rb.isKinematic) { _rb.position = spot; _rb.velocity = Vector3.zero; }
            else transform.position = spot;
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

        private void Impact(Vector3 at)
        {
            ThrowGroup g = _group!;
            if (_motor != null) _motor.velocity = Vector3.zero;
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = Vector3.zero;
            DeadlockFallGuard.Protect(gameObject, 1.5f, landed: true);
            float radius = LashData.DeathSlamRadius * MysticExpansion.RadiusMultiplier(g.Attacker);
            if (LashSurvivor.SlamEffect != null)
                EffectManager.SpawnEffect(LashSurvivor.SlamEffect, new EffectData { origin = at, scale = radius }, transmit: true);
            SoundMessage.Everyone("lash_ds_impact", at);
            using var extend = DurationExtender.From(g.Attacker); // Duration Extender: Lash's, for the slow and stun
            if (g.Attacker != null)
            {
                var victims = LashNet.EnemiesIn(at, radius, g.Attacker.teamComponent.teamIndex);
                if (_body!.healthComponent != null && !victims.Contains(_body.healthComponent)) victims.Insert(0, _body.healthComponent);
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
