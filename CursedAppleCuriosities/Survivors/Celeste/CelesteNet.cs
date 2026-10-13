using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Server side of Celeste: Light Eater's flare and mark (bonus blade damage, lifesteal), Radiant Daggers' beam and
    /// Radiance stacks, Dazzling Trick's prism (barrier; breaks into a stunning burst), and the blade and orb projectiles.
    /// States run on the owning player and message the server.
    /// </summary>
    public static class CelesteNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<CelesteFlareMessage>();
            NetworkingAPI.RegisterMessageType<CelesteBeamMessage>();
            NetworkingAPI.RegisterMessageType<CelestePrismMessage>();

            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                float barrierBefore = self.barrier;
                bool prism = CelesteSurvivor.PrismBuff != null && self.body != null && self.body.HasBuff(CelesteSurvivor.PrismBuff);
                orig(self, info);
                if (!NetworkServer.active || info.rejected) return;
                // Dazzling Trick: the hit that takes the barrier down breaks the prism.
                if (prism && barrierBefore > 0f && self.barrier <= 0f)
                {
                    var p = self.GetComponent<CelestePrism>();
                    if (p != null) p.Shatter();
                }
                // Light Eater: Celeste heals a share of what she deals to a marked enemy (Deadlock's spirit lifesteal).
                if (CelesteSurvivor.MarkBuff == null || self.body == null || !self.body.HasBuff(CelesteSurvivor.MarkBuff)) return;
                var attacker = info.attacker != null ? info.attacker.GetComponent<CharacterBody>() : null;
                if (attacker == null || attacker.bodyIndex != CelesteSurvivor.BodyIndex || attacker.healthComponent == null) return;
                float heal = info.damage * CelesteData.MarkLifesteal;
                if (heal > 0f) attacker.healthComponent.Heal(heal, default);
            };
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (CelesteSurvivor.RadianceBuff != null)
                    args.damageMultAdd += CelesteData.RadianceDamage * body.GetBuffCount(CelesteSurvivor.RadianceBuff);
                if (CelesteSurvivor.OrbSlow != null && body.HasBuff(CelesteSurvivor.OrbSlow))
                    args.moveSpeedReductionMultAdd += CelesteData.OrbSlow;
            };
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Celeste, with procs.</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, DamageTypeCombo type, float proc = 1f, Vector3? at = null)
        {
            if (target == null || !target.alive || damage <= 0f) return;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = at ?? Core(target),
                force = Vector3.zero,
                procCoefficient = proc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            if (di.rejected) return;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            HurtBox[] boxes = new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
            foreach (HurtBox h in boxes) if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        public static bool Marked(HealthComponent hc) =>
            CelesteSurvivor.MarkBuff != null && hc.body != null && hc.body.HasBuff(CelesteSurvivor.MarkBuff);

        /// <summary>
        /// Server: Radiant Daggers hit this many enemies: that many more Radiance stacks, uncapped (user: it's taken in place of
        /// Light Eater, her only healing), and the whole pile's RadianceSeconds timer (Duration Extender lengthens it) starts over (CelesteRadiance).
        /// </summary>
        public static void AddRadiance(CharacterBody celeste, int stacks)
        {
            if (CelesteSurvivor.RadianceBuff == null || stacks <= 0) return;
            var r = celeste.GetComponent<CelesteRadiance>() ?? celeste.gameObject.AddComponent<CelesteRadiance>();
            r.Add(stacks, CelesteData.RadianceSeconds * DurationExtender.Multiplier(celeste));
            EquipmentFx.Sound("celeste_beam_buff", celeste.corePosition, celeste.gameObject);
        }

        /// <summary>Server: the nearest visible enemy for Shining Wonder's next bounce (user: just the nearest).</summary>
        public static HealthComponent? NextTarget(Vector3 from, float range, TeamIndex team, HealthComponent? skip)
        {
            HealthComponent? best = null;
            float bestD = float.MaxValue;
            foreach (HealthComponent hc in EnemiesIn(from, range, team))
            {
                if (hc == skip || hc.body == null) continue;
                float d = (Core(hc) - from).sqrMagnitude;
                if (d < bestD && Visible(from, Core(hc))) { bestD = d; best = hc; }
            }
            return best;
        }

        public static bool Visible(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
    }

    // ================= Light Eater =================

    /// <summary>The flare goes off: everything in the cone takes damage and is marked.</summary>
    public class CelesteFlareMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Origin, Dir;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Origin); w.Write(Dir); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Origin = r.ReadVector3(); Dir = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.teamComponent == null) return;
            float range = CelesteData.FlareRange * MysticExpansion.RadiusMultiplier(body);
            Vector3 dir = Dir.sqrMagnitude > 0.01f ? Dir.normalized : body.transform.forward;
            if (CelesteSurvivor.FlareEffect != null)
                EffectManager.SpawnEffect(CelesteSurvivor.FlareEffect, new EffectData { origin = Origin + dir * range * 0.45f, scale = range * 0.6f }, true);
            if (CelesteSurvivor.FlareCone != null)
                EffectManager.SpawnEffect(CelesteSurvivor.FlareCone, new EffectData { origin = Origin, rotation = Quaternion.LookRotation(dir), scale = range }, true);
            EquipmentFx.Sound("celeste_flare_blast", Origin, Attacker);
            int hits = 0;
            using (DurationExtender.From(body))
                foreach (HealthComponent hc in CelesteNet.EnemiesIn(Origin, range, body.teamComponent.teamIndex))
                {
                    Vector3 to = CelesteNet.Core(hc) - Origin;
                    // Deadlock's ExtraSweepRadius: anything right next to her counts, whatever the angle.
                    if (to.magnitude > 2f && Vector3.Angle(dir, to) > CelesteData.FlareConeDeg) continue;
                    if (!CelesteNet.Visible(Origin, CelesteNet.Core(hc))) continue;
                    CelesteNet.Hit(body, hc, CelesteData.FlareDamage * body.damage, Crit, DamageSource.Secondary);
                    if (hc.alive && hc.body != null && CelesteSurvivor.MarkBuff != null)
                    {
                        hc.body.ClearTimedBuffs(CelesteSurvivor.MarkBuff);
                        hc.body.AddTimedBuff(CelesteSurvivor.MarkBuff, CelesteData.MarkSeconds);
                    }
                    if (CelesteSurvivor.HitEffect != null) EffectManager.SimpleImpactEffect(CelesteSurvivor.HitEffect, CelesteNet.Core(hc), -dir, true);
                    hits++;
                }
            if (hits > 0) EquipmentFx.Sound("celeste_flare_hit", Origin + dir * 3f);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Light Eater: {hits} marked");
        }
    }

    // ================= Radiant Daggers =================

    /// <summary>A beam is called down on a spot: it forms for 1.4 s, then explodes.</summary>
    public class CelesteBeamMessage : INetMessage
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
            float radius = CelesteData.BeamRadius * MysticExpansion.RadiusMultiplier(body);
            if (CelesteSurvivor.BeamWarning != null)
                EffectManager.SpawnEffect(CelesteSurvivor.BeamWarning, new EffectData { origin = Point, scale = radius }, true);
            EquipmentFx.Sound("celeste_beam_warning", Point);
            var go = new GameObject("CursedAppleCelesteBeam");
            go.transform.position = Point;
            var beam = go.AddComponent<CelesteBeam>();
            beam.By = body;
            beam.Crit = Crit;
            beam.Radius = radius;
        }
    }

    /// <summary>Server: one forming beam.</summary>
    public class CelesteBeam : MonoBehaviour
    {
        public CharacterBody? By;
        public bool Crit;
        public float Radius;
        private float _age;

        private void FixedUpdate()
        {
            _age += Time.fixedDeltaTime;
            if (_age < CelesteData.BeamDelay) return;
            if (By != null && By.teamComponent != null)
            {
                Vector3 at = transform.position;
                if (CelesteSurvivor.BeamEffect != null) EffectManager.SpawnEffect(CelesteSurvivor.BeamEffect, new EffectData { origin = at, scale = Radius }, true);
                EquipmentFx.Sound("celeste_beam_explode", at);
                // Deadlock: the beam reaches from the sky to the ground, so every height in the circle is hit (a tall cylinder).
                int hits = 0;
                foreach (HealthComponent hc in CelesteNet.EnemiesIn(at, Radius * 4f, By.teamComponent.teamIndex))
                {
                    Vector3 d = CelesteNet.Core(hc) - at;
                    if (new Vector2(d.x, d.z).magnitude > Radius + (hc.body != null ? hc.body.radius : 0f) || Mathf.Abs(d.y) > Radius * 4f) continue;
                    CelesteNet.Hit(By, hc, CelesteData.BeamDamage * By.damage, Crit, DamageSource.Secondary);
                    hits++;
                }
                CelesteNet.AddRadiance(By, hits);
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Radiant Daggers struck: {hits} hit");
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Server, on Celeste: her Radiance stacks as one synced (untimed) buff count, all cleared together when the timer the
    /// last hit set runs out. Uncapped, so no per-stack timers (refreshing hundreds of timed stacks every hit was quadratic).
    /// </summary>
    public class CelesteRadiance : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _until;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Add(int stacks, float seconds)
        {
            if (CelesteSurvivor.RadianceBuff == null) return;
            _body.SetBuffCount(CelesteSurvivor.RadianceBuff.buffIndex, _body.GetBuffCount(CelesteSurvivor.RadianceBuff) + stacks);
            _until = Time.fixedTime + seconds;
        }

        private void FixedUpdate()
        {
            if (CelesteSurvivor.RadianceBuff == null || Time.fixedTime < _until || !_body.HasBuff(CelesteSurvivor.RadianceBuff)) return;
            _body.SetBuffCount(CelesteSurvivor.RadianceBuff.buffIndex, 0);
            EquipmentFx.Sound("celeste_beam_buff_expire", _body.corePosition, _body.gameObject);
        }
    }

    // ================= Dazzling Trick =================

    /// <summary>Celeste raises the prism: a barrier for 4 s. If something breaks it, it bursts.</summary>
    public class CelestePrismMessage : INetMessage
    {
        public GameObject? Attacker;

        public void Serialize(NetworkWriter w) => w.Write(Attacker);
        public void Deserialize(NetworkReader r) => Attacker = r.ReadGameObject();

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null) return;
            float seconds = CelesteData.PrismSeconds * DurationExtender.Multiplier(body);
            float barrier = body.healthComponent.fullCombinedHealth * CelesteData.PrismBarrier;
            body.healthComponent.AddBarrier(barrier);
            if (CelesteSurvivor.PrismBuff != null)
            {
                body.ClearTimedBuffs(CelesteSurvivor.PrismBuff);
                using (DurationExtender.Exact()) body.AddTimedBuff(CelesteSurvivor.PrismBuff, seconds);
            }
            var p = body.GetComponent<CelestePrism>() ?? body.gameObject.AddComponent<CelestePrism>();
            p.Raise(seconds, barrier);
            EquipmentFx.Sound("celeste_prism_cast", body.corePosition, body.gameObject);
        }
    }

    /// <summary>Server, on Celeste: the prism's window, and its burst when the barrier is broken.</summary>
    public class CelestePrism : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _until = -1f;
        private bool _up;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private float _barrier;

        public bool Up => _up;

        public void Raise(float seconds, float barrier) { _until = Time.fixedTime + seconds; _up = true; _barrier = barrier; }

        /// <summary>Shining Wonder bounced off the prism (Deadlock's T3, given free: user): its time starts over and its barrier is topped back up.</summary>
        public void Refresh()
        {
            if (!_up || _body == null || _body.healthComponent == null) return;
            float seconds = CelesteData.PrismSeconds * DurationExtender.Multiplier(_body);
            _until = Time.fixedTime + seconds;
            float missing = _barrier - _body.healthComponent.barrier;
            if (missing > 0f) _body.healthComponent.AddBarrier(missing);
            if (CelesteSurvivor.PrismBuff != null)
            {
                _body.ClearTimedBuffs(CelesteSurvivor.PrismBuff);
                using (DurationExtender.Exact()) _body.AddTimedBuff(CelesteSurvivor.PrismBuff, seconds);
            }
        }

        private void FixedUpdate()
        {
            if (!_up || Time.fixedTime < _until) return;
            _up = false;
            if (_body != null) EquipmentFx.Sound("celeste_prism_expire", _body.corePosition, _body.gameObject);
        }

        public void Shatter()
        {
            if (!_up || _body == null || _body.teamComponent == null) return;
            _up = false;
            if (CelesteSurvivor.PrismBuff != null) _body.ClearTimedBuffs(CelesteSurvivor.PrismBuff);
            Vector3 at = _body.corePosition;
            float radius = CelesteData.PrismRadius * MysticExpansion.RadiusMultiplier(_body);
            if (CelesteSurvivor.ShatterEffect != null) EffectManager.SpawnEffect(CelesteSurvivor.ShatterEffect, new EffectData { origin = at, scale = radius }, true);
            if (CelesteSurvivor.BreakEffect != null) EffectManager.SpawnEffect(CelesteSurvivor.BreakEffect, new EffectData { origin = at, scale = 2f }, true);
            EquipmentFx.Sound("celeste_prism_explode", at);
            bool crit = _body.RollCrit();
            List<HealthComponent> caught = CelesteNet.EnemiesIn(at, radius, _body.teamComponent.teamIndex);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Dazzling Trick broke: {caught.Count} caught in {radius:0.#} m");
            using (DurationExtender.From(_body))
                foreach (HealthComponent hc in caught)
                {
                    CelesteNet.Hit(_body, hc, CelesteData.PrismDamage * _body.damage, crit, DamageSource.Utility);
                    if (hc.alive) SetStateOnHurt.SetStunOnObject(hc.gameObject, CelesteData.PrismStun); // a stun (user), Deadlock's silence
                }
        }
    }

    // ================= Carny Life =================

    /// <summary>
    /// Server, on a thrown blade (Deadlock's Carny Life): a shallow arc that homes onto an enemy near its line, bounces
    /// once off the world. No ricochet between enemies (user).
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class CelesteDagger : MonoBehaviour, IProjectileImpactBehavior
    {
        private ProjectileController _pc = null!;
        private ProjectileDamage? _pd;
        private Rigidbody? _rb;
        private HealthComponent? _target;
        private bool _bounced, _done, _looked;
        private Vector3 _lastVel;
        private float _age, _flown, _launchFlat = -1f;

        private void Awake()
        {
            _pc = GetComponent<ProjectileController>();
            _pd = GetComponent<ProjectileDamage>();
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            _rb.angularVelocity = Vector3.zero;
            Vector3 v = _rb.velocity;
            if (NetworkServer.active && !_done)
            {
                _age += Time.fixedDeltaTime;
                if (_age > CelesteData.DaggerLifetime) { Destroy(gameObject); return; }
                if (!_looked && _age > 0.04f) { _looked = true; _target = FindTarget(v); }
                if (_target != null && _target.alive)
                {
                    // Home: turn toward the target (gently, so the arc still shows).
                    Vector3 to = CelesteNet.Core(_target) - transform.position;
                    v = Vector3.RotateTowards(v, to.normalized * v.magnitude, CelesteData.DaggerTurnDegPerSec * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                }
                v += Vector3.down * CelesteData.DaggerDrop * Time.fixedDeltaTime; // always falls: an arc (user)
                // Past 10 m it loses airspeed (user): its forward speed falls off evenly with distance to nothing at 90 m, so
                // it's already visibly slower by 20 m and creeps out gently near the stop, while gravity keeps pulling.
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                if (_launchFlat < 0f) _launchFlat = flat.magnitude;
                _flown += flat.magnitude * Time.fixedDeltaTime;
                float ease = Mathf.InverseLerp(CelesteData.DaggerDragFrom, CelesteData.DaggerStopAt, _flown);
                float cap = _launchFlat * (1f - ease);
                if (flat.magnitude > cap)
                {
                    flat = flat.sqrMagnitude > 1e-6f ? flat.normalized * cap : Vector3.zero;
                    v = new Vector3(flat.x, v.y, flat.z);
                }
            }
            _rb.velocity = v;
            if (v.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(v);
            _lastVel = v;
        }

        private HealthComponent? FindTarget(Vector3 v)
        {
            GameObject? owner = _pc.owner;
            if (owner == null || v.sqrMagnitude < 0.01f) return null;
            TeamIndex team = TeamComponent.GetObjectTeam(owner);
            HealthComponent? best = null;
            float bestAngle = CelesteData.DaggerHomeConeDeg;
            float range = CelesteData.DaggerHomeRange * MysticExpansion.RadiusMultiplier(owner.GetComponent<CharacterBody>());
            foreach (HealthComponent hc in CelesteNet.EnemiesIn(transform.position, range, team))
            {
                float a = Vector3.Angle(v, CelesteNet.Core(hc) - transform.position);
                if (a < bestAngle && CelesteNet.Visible(transform.position, CelesteNet.Core(hc))) { bestAngle = a; best = hc; }
            }
            return best;
        }

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !NetworkServer.active) return;
            GameObject? owner = _pc.owner;
            CharacterBody? body = owner != null ? owner.GetComponent<CharacterBody>() : null;
            HurtBox? box = info.collider != null ? info.collider.GetComponent<HurtBox>() : null;
            HealthComponent? hc = box != null ? box.healthComponent : null;
            if (body != null && hc != null && hc.body != null && box!.teamIndex != body.teamComponent.teamIndex)
            {
                _done = true;
                float dmg = _pd != null ? _pd.damage : body.damage;
                bool crit = _pd != null && _pd.crit;
                float coeff = CelesteData.DaggerDamage + (CelesteNet.Marked(hc) ? CelesteData.MarkDaggerBonus : 0f);
                CelesteNet.Hit(body, hc, coeff * dmg, crit, DamageSource.Primary, 1f, transform.position);
                EquipmentFx.Sound("celeste_gun_impact", transform.position);
                Destroy(gameObject);
                return;
            }
            if (hc != null) return; // an ally or herself: fly on
            // The world: one bounce (Deadlock), then it's spent.
            if (_bounced) { _done = true; Destroy(gameObject); return; }
            _bounced = true;
            Vector3 n = info.estimatedImpactNormal.sqrMagnitude > 0.01f ? info.estimatedImpactNormal.normalized : -_lastVel.normalized;
            Vector3 v = Vector3.Reflect(_lastVel, n) * CelesteData.DaggerBounceKeep;
            if (_rb != null) _rb.velocity = v;
            _lastVel = v;
            transform.position += n * 0.2f;
            _looked = false; // look for a new target along the bounce
            EquipmentFx.Sound("celeste_gun_impact", transform.position);
            // A small burst of her light where it bounced (user: show where and what happened).
            if (CelesteSurvivor.BounceEffect != null)
                EffectManager.SpawnEffect(CelesteSurvivor.BounceEffect, new EffectData
                {
                    origin = transform.position - n * 0.2f, rotation = Quaternion.LookRotation(n), scale = 1f,
                }, true);
        }
    }

    // ================= Shining Wonder =================

    /// <summary>
    /// Server, on the orb (Deadlock's Shining Wonder): homes onto an enemy near its line, then bounces to the nearest enemy
    /// (never the same one twice in a row), slowing each. Celeste's raised prism is a target too (Deadlock's T3, user): a
    /// bounce off it does no damage, restarts the prism and tops its barrier up. With nothing in reach it lingers, waiting.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class CelesteOrb : MonoBehaviour
    {
        private ProjectileController _pc = null!;
        private ProjectileDamage? _pd;
        private Rigidbody? _rb;
        private HealthComponent? _target, _last;
        private int _hits;
        private float _flight, _linger, _search, _orbit;
        private bool _lingering;
        // The current bounce's flight (_leg < 0: none, the first throw flies straight at OrbSpeed).
        private float _leg = -1f, _speed;
        private Vector3 _legAim;
        private DeadlockVoice? _hum;

        private void Awake()
        {
            _pc = GetComponent<ProjectileController>();
            _pd = GetComponent<ProjectileDamage>();
            _rb = GetComponent<Rigidbody>();
        }

        private void Start() => _hum = DeadlockSounds.Loop("celeste_orb_loop", gameObject, 30f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private CharacterBody? Owner => _pc.owner != null ? _pc.owner.GetComponent<CharacterBody>() : null;

        private void FixedUpdate()
        {
            if (_rb != null) _rb.angularVelocity = Vector3.zero;
            if (!NetworkServer.active || _rb == null) return;
            CharacterBody? owner = Owner;
            if (owner == null || owner.teamComponent == null) { Destroy(gameObject); return; }
            float dt = Time.fixedDeltaTime;

            if (_target != null && !_target.alive) _target = null;
            if (_target == null && !_lingering)
            {
                _flight += dt;
                _search -= dt;
                if (_search <= 0f)
                {
                    _search = 0.1f;
                    _target = Seek(owner, _hits == 0);
                    if (_target != null && _leg >= 0f) Retarget(); // its target died mid-flight: it turns onto the next
                }
                if (_target == null && (_flight > CelesteData.OrbFlightSeconds || HitsWorld(_rb.velocity * dt))) StartLinger();
            }
            if (_lingering)
            {
                Orbit(dt);
                _linger += dt;
                _search -= dt;
                if (_search <= 0f) { _search = 0.15f; _target = Next(owner); }
                if (_target != null) { _lingering = false; _linger = 0f; Launch(); }
                else if (_linger > CelesteData.OrbLinger) { Expire(); return; }
            }
            if (_target != null)
            {
                Vector3 to = CelesteNet.Core(_target) - transform.position;
                float reach = CelesteData.OrbRadius + (_target.body != null ? _target.body.radius : 0.5f);
                float speed = _leg >= 0f ? _speed : CelesteData.OrbSpeed;
                if (to.magnitude <= reach + speed * dt) { Strike(owner, _target); return; }
                // Committed: it flies through walls to its target (user).
                _rb.velocity = _leg >= 0f ? Fly(to, dt) : to.normalized * CelesteData.OrbSpeed;
            }
            if (_rb.velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(_rb.velocity);
        }

        /// <summary>Off an enemy (or the prism, or out of its orbit): slow and steep, toward where the target is now.</summary>
        private void Launch()
        {
            if (_target == null || _rb == null) return;
            _leg = 0f;
            _speed = CelesteData.OrbLaunchSpeed;
            _legAim = CelesteNet.Core(_target);
            Vector3 line = _legAim - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(line, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            float pitch = Mathf.Min(80f, Mathf.Atan2(line.y, flat.magnitude) * Mathf.Rad2Deg + CelesteData.OrbLaunchUpDeg);
            Vector3 dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat.normalized)) * flat.normalized;
            _rb.velocity = dir * _speed;
        }

        /// <summary>A new target mid-flight: it keeps its speed and tracks the new one from here.</summary>
        private void Retarget()
        {
            _legAim = CelesteNet.Core(_target!);
            _leg = Mathf.Max(_leg, CelesteData.OrbArcSeconds);
        }

        /// <summary>One step of a bounce: arc toward the target's old spot, then track it, faster all the while.</summary>
        private Vector3 Fly(Vector3 toTarget, float dt)
        {
            _leg += dt;
            _speed = Mathf.Min(CelesteData.OrbMaxSpeed, _speed + CelesteData.OrbAccel * dt);
            if (toTarget.magnitude < CelesteData.OrbFinalApproach) return toTarget.normalized * _speed;
            float track = Mathf.Clamp01((_leg - CelesteData.OrbArcSeconds) / CelesteData.OrbTrackSeconds);
            Vector3 aim = Vector3.Lerp(_legAim, transform.position + toTarget, track) - transform.position;
            Vector3 dir = _rb!.velocity.sqrMagnitude > 0.01f ? _rb.velocity.normalized : aim.normalized;
            float turn = Mathf.Lerp(CelesteData.OrbTurnStart, CelesteData.OrbTurnEnd, track) * Mathf.Deg2Rad * dt;
            return Vector3.RotateTowards(dir, aim.normalized, turn, 0f) * _speed;
        }

        private float BounceRange(CharacterBody owner) => CelesteData.OrbBounceRange * MysticExpansion.RadiusMultiplier(owner);

        /// <summary>The nearest enemy in bounce range, or Celeste's prism when it is nearer (never what it just hit).</summary>
        private HealthComponent? Next(CharacterBody owner)
        {
            float range = BounceRange(owner);
            HealthComponent? enemy = CelesteNet.NextTarget(transform.position, range, owner.teamComponent.teamIndex, _last);
            HealthComponent? self = owner.healthComponent;
            var prism = owner.GetComponent<CelestePrism>();
            if (self == null || self == _last || prism == null || !prism.Up) return enemy;
            float ds = (owner.corePosition - transform.position).sqrMagnitude;
            if (ds > range * range || !CelesteNet.Visible(transform.position, owner.corePosition)) return enemy;
            return enemy == null || ds < (CelesteNet.Core(enemy) - transform.position).sqrMagnitude ? self : enemy;
        }

        /// <summary>The first target: an enemy near the orb's line (or anything it is about to touch).</summary>
        private HealthComponent? Seek(CharacterBody owner, bool first)
        {
            if (!first) return Next(owner);
            Vector3 v = _rb != null ? _rb.velocity : transform.forward;
            HealthComponent? best = null;
            float bestAngle = CelesteData.OrbHomeConeDeg;
            foreach (HealthComponent hc in CelesteNet.EnemiesIn(transform.position, 60f, owner.teamComponent.teamIndex))
            {
                Vector3 to = CelesteNet.Core(hc) - transform.position;
                float a = to.magnitude < 3f ? 0f : Vector3.Angle(v, to);
                if (a < bestAngle && CelesteNet.Visible(transform.position, CelesteNet.Core(hc))) { bestAngle = a; best = hc; }
            }
            return best;
        }

        /// <summary>
        /// Waiting for a target: it circles whatever it last hit (an enemy, or Celeste after a prism bounce) at a little past
        /// its body (user); with nothing to circle (it stopped at a wall) it hovers in place.
        /// </summary>
        private void Orbit(float dt)
        {
            HealthComponent? around = _last != null && _last.alive && _last.body != null ? _last : null;
            if (around == null) { _rb!.velocity = Vector3.zero; return; }
            CharacterBody b = around.body;
            float radius = Mathf.Max(1.2f, b.bestFitRadius + 1f);
            _orbit += CelesteData.OrbOrbitDegPerSec * dt;
            float a = _orbit * Mathf.Deg2Rad;
            Vector3 point = b.corePosition + new Vector3(Mathf.Cos(a) * radius, 0.35f * radius * Mathf.Sin(a * 2f), Mathf.Sin(a) * radius);
            Vector3 to = point - transform.position;
            // Glide onto the circle, then ride it (velocity carries the position to every client).
            _rb!.velocity = Vector3.ClampMagnitude(to / dt, CelesteData.OrbSpeed * 2f);
        }

        private bool HitsWorld(Vector3 step) =>
            step.sqrMagnitude > 0.0001f && Physics.SphereCast(transform.position, 0.4f, step.normalized, out _, step.magnitude + 0.2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        private void StartLinger()
        {
            _lingering = true;
            _leg = -1f;
            _linger = 0f;
            if (_last != null && _last.body != null)
            {
                Vector3 off = transform.position - _last.body.corePosition;
                _orbit = Mathf.Atan2(off.z, off.x) * Mathf.Rad2Deg;
            }
            EquipmentFx.Sound("celeste_orb_linger", transform.position, gameObject);
        }

        private void Strike(CharacterBody owner, HealthComponent hc)
        {
            if (hc == owner.healthComponent) { OffPrism(owner); return; }
            float dmg = _pd != null ? _pd.damage : owner.damage;
            bool crit = _pd != null && _pd.crit;
            CelesteNet.Hit(owner, hc, CelesteData.OrbDamage * dmg, crit, DamageSource.Special);
            if (hc.alive && hc.body != null && CelesteSurvivor.OrbSlow != null)
                using (DurationExtender.From(owner)) hc.body.AddTimedBuff(CelesteSurvivor.OrbSlow, CelesteData.OrbSlowSeconds);
            if (CelesteSurvivor.OrbHitEffect != null) EffectManager.SpawnEffect(CelesteSurvivor.OrbHitEffect, new EffectData { origin = CelesteNet.Core(hc), scale = 2f }, true);
            _hits++;
            _last = hc;
            transform.position = CelesteNet.Core(hc);
            bool final = _hits > CelesteData.OrbBounces;
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Shining Wonder hit {hc.name} ({_hits}/{CelesteData.OrbBounces + 1})");
            EquipmentFx.Sound(final ? "celeste_orb_final" : "celeste_orb_bounce", transform.position);
            if (final) { Destroy(gameObject); return; }
            _target = Next(owner);
            if (_target == null) StartLinger();
            else Launch();
        }

        /// <summary>Off Celeste's prism: no damage, the prism restarts, and it counts as a bounce.</summary>
        private void OffPrism(CharacterBody owner)
        {
            owner.GetComponent<CelestePrism>()?.Refresh();
            _hits++;
            _last = owner.healthComponent;
            transform.position = owner.corePosition;
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Shining Wonder bounced off the prism ({_hits}/{CelesteData.OrbBounces + 1})");
            EquipmentFx.Sound("celeste_orb_bounce", transform.position);
            if (_hits > CelesteData.OrbBounces) { Destroy(gameObject); return; }
            _target = Next(owner);
            if (_target == null) StartLinger();
            else Launch();
        }

        private void Expire()
        {
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Celeste] Shining Wonder expired after {_hits} hits");
            EquipmentFx.Sound("celeste_orb_expire", transform.position);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Every client: Radiant Daggers' warning while the beam forms (an EffectManager effect, so everyone sees it): a pink
    /// ring on the ground at the blast's edge and a thin beam from the sky that thickens until it strikes.
    /// </summary>
    public class CelesteBeamWarning : MonoBehaviour
    {
        private LineRenderer? _ring, _beam;
        private float _age, _radius = 8f;

        private void Start()
        {
            var ec = GetComponent<EffectComponent>();
            if (ec != null && ec.effectData != null) _radius = Mathf.Max(1f, ec.effectData.scale);
            transform.localScale = Vector3.one; // the effect system scales by effectData; the lines are drawn in world units
            Material? mat = CelesteSurvivor.LineMaterial;
            _ring = Line("ring", 49, mat, 0.15f);
            _beam = Line("beam", 2, mat, 0.2f);
            for (int i = 0; i < 49; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                _ring.SetPosition(i, transform.position + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * _radius);
            }
            _beam.SetPosition(0, transform.position);
            _beam.SetPosition(1, transform.position + Vector3.up * 60f);
        }

        private LineRenderer Line(string name, int points, Material? mat, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.loop = false;
            lr.widthMultiplier = width;
            if (mat != null) lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = CelesteSurvivor.LightColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / CelesteData.BeamDelay);
            if (_beam != null) _beam.widthMultiplier = Mathf.Lerp(0.1f, _radius * 0.15f, t * t);
            if (_ring != null) _ring.widthMultiplier = 0.12f + 0.1f * Mathf.Sin(_age * 12f);
            if (_age >= CelesteData.BeamDelay) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Every client: Light Eater's real reach (user, 1.7), the exact cone the server hits (FlareConeDeg either side of the aim,
    /// out to the flare's range, Mystic Expansion included) as a see-through purple solid with a purple outline: it snaps
    /// out from her, holds a moment and fades.
    /// </summary>
    public class CelesteFlareCone : MonoBehaviour
    {
        private const float Grow = 0.08f, Life = 0.7f, Opacity = 0.7f; // 30% fainter than the materials (user, 1.7)
        private const int Around = 32, Rings = 6;
        private float _age, _range = 15f;
        private Transform? _shape;
        private Material? _fill, _edge;
        private Color _fillTint, _edgeTint;
        private string? _edgeProp;

        private void Start()
        {
            var ec = GetComponent<EffectComponent>();
            if (ec != null && ec.effectData != null)
            {
                _range = Mathf.Max(1f, ec.effectData.scale);
                transform.rotation = ec.effectData.rotation;
            }
            transform.localScale = Vector3.one;
            var shape = new GameObject("cone");
            shape.transform.SetParent(transform, false);
            _shape = shape.transform;
            float half = CelesteData.FlareConeDeg * Mathf.Deg2Rad;

            if (CelesteSurvivor.ConeFill != null)
            {
                _fill = new Material(CelesteSurvivor.ConeFill);
                _fillTint = _fill.HasProperty("_TintColor") ? _fill.GetColor("_TintColor") : Color.white;
                _fillTint.a *= Opacity;
                shape.AddComponent<MeshFilter>().sharedMesh = Mesh(half);
                var mr = shape.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _fill;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            if (CelesteSurvivor.ConeEdge != null)
            {
                _edge = new Material(CelesteSurvivor.ConeEdge);
                _edgeProp = _edge.HasProperty("_TintColor") ? "_TintColor" : _edge.HasProperty("_Color") ? "_Color" : null;
                _edgeTint = _edgeProp != null ? _edge.GetColor(_edgeProp) : CelesteSurvivor.ConeColor;
                _edgeTint.a *= Opacity;
                // The rim where the cone ends, and edges from her out to it.
                LineRenderer rim = Line(Around + 1);
                for (int i = 0; i <= Around; i++) rim.SetPosition(i, Point(half, i * Mathf.PI * 2f / Around));
                for (int k = 0; k < 8; k++)
                {
                    LineRenderer edge = Line(2);
                    edge.SetPosition(0, Vector3.zero);
                    edge.SetPosition(1, Point(half, k * Mathf.PI / 4f));
                }
            }
            _shape.localScale = Vector3.one * 0.2f;
        }

        /// <summary>A point on the cone's far end (a sphere of radius 1: the server's cone is a distance check), polar angle from the aim.</summary>
        private static Vector3 Point(float polar, float around) =>
            new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Sin(polar) * Mathf.Sin(around), Mathf.Cos(polar));

        private LineRenderer Line(int points)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(_shape, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.widthMultiplier = 0.08f;
            lr.sharedMaterial = _edge;
            lr.startColor = lr.endColor = new Color(CelesteSurvivor.ConeColor.r, CelesteSurvivor.ConeColor.g, CelesteSurvivor.ConeColor.b, Opacity);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        /// <summary>A unit cone (tip at the origin, along +Z): its side and its rounded end, both faces drawn.</summary>
        private static Mesh Mesh(float half)
        {
            var v = new List<Vector3> { Vector3.zero };
            var t = new List<int>();
            // Rounded end: Rings rings from the rim (polar = half) in to the centre.
            for (int r = 0; r <= Rings; r++)
                for (int i = 0; i < Around; i++)
                    v.Add(Point(half * (1f - r / (float)Rings), i * Mathf.PI * 2f / Around));
            int Ring(int r, int i) => 1 + r * Around + (i % Around);
            for (int i = 0; i < Around; i++) Tri(t, 0, Ring(0, i), Ring(0, i + 1)); // the side: tip to rim
            for (int r = 0; r < Rings; r++)
                for (int i = 0; i < Around; i++)
                {
                    Tri(t, Ring(r, i), Ring(r + 1, i), Ring(r, i + 1));
                    Tri(t, Ring(r, i + 1), Ring(r + 1, i), Ring(r + 1, i + 1));
                }
            var m = new Mesh { name = "CelesteFlareCone" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            return m;
        }

        private static void Tri(List<int> t, int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(b); }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_shape != null) _shape.localScale = Vector3.one * _range * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(_age / Grow));
            float fade = 1f - Mathf.Clamp01((_age - Grow) / (Life - Grow));
            fade *= fade;
            if (_fill != null && _fill.HasProperty("_TintColor")) _fill.SetColor("_TintColor", new Color(_fillTint.r, _fillTint.g, _fillTint.b, _fillTint.a * fade));
            if (_edge != null && _edgeProp != null) _edge.SetColor(_edgeProp, new Color(_edgeTint.r, _edgeTint.g, _edgeTint.b, _edgeTint.a * fade));
            if (_age >= Life) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_fill != null) Destroy(_fill);
            if (_edge != null) Destroy(_edge);
            var mf = GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }
}
