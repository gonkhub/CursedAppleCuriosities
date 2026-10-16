using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Abrams
{
    /// <summary>
    /// Entity states run on the player who owns Abrams; damage, healing, stuns and moving enemies happen on the
    /// server. These messages carry that over (on a host they simply run locally).
    /// </summary>
    public static class AbramsNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<AbramsHitMessage>();
            NetworkingAPI.RegisterMessageType<AbramsReleaseMessage>();
            NetworkingAPI.RegisterMessageType<AbramsSiphonMessage>();
            NetworkingAPI.RegisterMessageType<AbramsPendingMessage>();
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Abrams, with procs. Returns the health actually taken.</summary>
        public static float Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f)
        {
            if (target == null || !target.alive || damage <= 0f) return 0f;
            float before = target.combinedHealth;
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

        /// <summary>Bosses and the biggest monsters can't be scooped up: Shoulder Charge bounces off them.</summary>
        public static bool TooBigToMove(CharacterBody? b) =>
            b == null || b.isChampion || b.hullClassification == HullClassification.BeetleQueen;
    }

    /// <summary>What a hit is: sets its damage type and source on the server.</summary>
    public enum AbramsHitKind : byte { Charge, Melee, SlamGrab, SlamImpact }

    /// <summary>Damage (and a stun, and optionally carrying) for a list of targets.</summary>
    public class AbramsHitMessage : INetMessage
    {
        public AbramsHitKind Kind;
        public GameObject? Attacker;
        public GameObject[] Targets = System.Array.Empty<GameObject>();
        public float Damage, Stun;
        public bool Crit;
        public Vector3 Dir;

        public void Serialize(NetworkWriter w)
        {
            w.Write((byte)Kind);
            w.Write(Attacker);
            w.Write(Damage);
            w.Write(Stun);
            w.Write(Crit);
            w.Write(Dir);
            w.Write(Targets.Length);
            foreach (GameObject t in Targets) w.Write(t);
        }

        public void Deserialize(NetworkReader r)
        {
            Kind = (AbramsHitKind)r.ReadByte();
            Attacker = r.ReadGameObject();
            Damage = r.ReadSingle();
            Stun = r.ReadSingle();
            Crit = r.ReadBoolean();
            Dir = r.ReadVector3();
            Targets = new GameObject[r.ReadInt32()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            // Heavy Melee and Seismic Impact are both Special skills now; only Shoulder Charge is Utility.
            DamageTypeCombo type = Kind == AbramsHitKind.Charge ? DamageSource.Utility : DamageSource.Special;
            float dealt = 0f;
            for (int i = 0; i < Targets.Length; i++)
            {
                GameObject t = Targets[i];
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                Vector3 force = Kind == AbramsHitKind.SlamImpact ? Vector3.up * 1200f : Dir * 1500f;
                dealt += AbramsNet.Hit(body, hc, Damage, Crit, force, type);
                if (!hc.alive) continue;
                if (Stun > 0f) using (DurationExtender.From(body)) SetStateOnHurt.SetStunOnObject(t, Stun);
                if (Kind == AbramsHitKind.Charge || Kind == AbramsHitKind.SlamGrab)
                {
                    if (AbramsNet.TooBigToMove(hc.body)) continue;
                    var carry = t!.GetComponent<AbramsCarry>() ?? t.AddComponent<AbramsCarry>();
                    carry.Grab(body, Kind == AbramsHitKind.SlamGrab, Dir, AbramsCarry.CountBy(body));
                }
            }
            // Infernal Resilience's melee lifesteal (Deadlock's upgrade, given free).
            if (Kind == AbramsHitKind.Melee && dealt > 0f && AbramsData.MeleeLifesteal > 0f && body.healthComponent != null && body.healthComponent.alive)
                body.healthComponent.Heal(dealt * AbramsData.MeleeLifesteal, default(ProcChainMask));
        }
    }

    /// <summary>Lets go of everyone Abrams carries: stunned where they are (a wall), or flung on a little.</summary>
    public class AbramsReleaseMessage : INetMessage
    {
        public GameObject? Attacker;
        public bool Wall;
        public float Stun;
        public Vector3 Fling;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Wall); w.Write(Stun); w.Write(Fling); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Wall = r.ReadBoolean(); Stun = r.ReadSingle(); Fling = r.ReadVector3(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            bool crit = Wall && body.RollCrit();
            int slammed = 0;
            foreach (AbramsCarry c in AbramsCarry.All.ToArray())
            {
                if (c == null || c.By != body) continue;
                // Slammed into a wall: every carried enemy takes the wall damage (user 2026-10-12), then is pinned and stunned.
                var hc = c.GetComponent<HealthComponent>();
                if (Wall && hc != null && hc.alive) { slammed++; AbramsNet.Hit(body, hc, AbramsData.ChargeWallDamage * body.damage, crit, Vector3.zero, DamageSource.Utility); }
                c.Release(Stun, Fling);
            }
            if (slammed > 0) Unlocks.Challenges.AbramsFreightTrain.WallSlam(body, slammed);
        }
    }

    /// <summary>Siphon Life starts: the server puts the (synced) draining buff on Abrams; AbramsSiphon does the rest.</summary>
    public class AbramsSiphonMessage : INetMessage
    {
        public GameObject? Attacker;

        public void Serialize(NetworkWriter w) => w.Write(Attacker);
        public void Deserialize(NetworkReader r) => Attacker = r.ReadGameObject();

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null || AbramsSurvivor.SiphonBuff == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;
            body.ClearTimedBuffs(AbramsSurvivor.SiphonBuff);
            body.AddTimedBuff(AbramsSurvivor.SiphonBuff, AbramsData.SiphonDuration);
        }
    }

    /// <summary>
    /// Server only: one enemy carried by Abrams. Shoulder Charge holds it in front of him (Deadlock's drag modifier);
    /// Seismic Impact drags it down with him. Stunned while carried, so it can't fight back.
    /// </summary>
    public class AbramsCarry : MonoBehaviour
    {
        public static readonly List<AbramsCarry> All = new List<AbramsCarry>();

        public CharacterBody? By { get; private set; }
        private bool _slam;
        private int _slot;
        private float _until, _nextStun;
        private Vector3 _dir = Vector3.forward, _lastByPos;
        private CharacterBody? _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;

        public static int CountBy(CharacterBody by)
        {
            int n = 0;
            foreach (AbramsCarry c in All) if (c != null && c.By == by) n++;
            return n;
        }

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
            All.Add(this);
        }

        private void OnDestroy() => All.Remove(this);

        public void Grab(CharacterBody by, bool slam, Vector3 dir, int slot)
        {
            if (By != by) _slot = slot;
            By = by;
            _slam = slam;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) _dir = dir.normalized;
            _lastByPos = by.transform.position;
            _until = Time.time + (slam ? 5f : AbramsData.ChargeDuration + 1.5f); // safety: a release normally follows
            DeadlockFallGuard.Protect(gameObject, _until - Time.time + 1.5f); // being carried is never a fall
            Stun();
        }

        private void Stun()
        {
            _nextStun = Time.time + 0.25f;
            SetStateOnHurt.SetStunOnObject(gameObject, 0.5f);
        }

        public void Release(float stun, Vector3 fling)
        {
            if (stun > 0f) using (DurationExtender.From(By)) SetStateOnHurt.SetStunOnObject(gameObject, stun);
            DeadlockFallGuard.Protect(gameObject, 1.5f, landed: true);
            if (_motor != null) { _motor.velocity = fling; _motor.disableAirControlUntilCollision = fling.sqrMagnitude > 1f; }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = fling;
            Destroy(this);
        }

        /// <summary>Where the carried enemy rides: in front of Abrams (spread sideways by slot), or under him on the way down.</summary>
        private Vector3 Spot()
        {
            CharacterBody by = By!;
            float radius = _body != null ? _body.radius : 1f;
            Vector3 side = Vector3.Cross(Vector3.up, _dir);
            float offset = _slot == 0 ? 0f : (_slot % 2 == 1 ? 1f : -1f) * ((_slot + 1) / 2) * 1.2f;
            // Down with Abrams: a ring around his feet (a line of riders would trail out of the impact).
            if (_slam) return by.footPosition + Quaternion.Euler(0f, _slot * 47f, 0f) * _dir * (1.5f + radius + 0.4f * (_slot / 8)) - Vector3.up * 0.5f;
            return by.footPosition + _dir * (AbramsData.ChargeCarryAhead + radius) + side * offset + Vector3.up * 0.3f;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || By == null || _body == null || _body.healthComponent == null || !_body.healthComponent.alive
                || By.healthComponent == null || !By.healthComponent.alive || Time.time > _until) { Destroy(this); return; }
            // His heading, from how he actually moves (a client's Abrams is only a position here).
            Vector3 moved = By.transform.position - _lastByPos;
            _lastByPos = By.transform.position;
            moved.y = 0f;
            if (!_slam && moved.magnitude > 2f * Time.fixedDeltaTime) _dir = Vector3.Slerp(_dir, moved.normalized, 0.5f).normalized;
            if (Time.time >= _nextStun) Stun();
            Vector3 to = Spot() - _body.footPosition;
            float speed = (_slam ? AbramsData.SlamCrashSpeed : AbramsData.ChargeSpeedMax) + 10f;
            Vector3 v = Vector3.ClampMagnitude(to / Time.fixedDeltaTime, speed);
            if (_motor != null)
            {
                _motor.velocity = v;
                _motor.Motor.ForceUnground();
                _motor.disableAirControlUntilCollision = true;
            }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = v;
            else transform.position += v * Time.fixedDeltaTime;
        }
    }

    /// <summary>
    /// Siphon Life on an Abrams body. Server: while the draining buff lasts, every 0.25 s (Deadlock's tick) it hits
    /// every enemy within the radius for true damage and heals Abrams for a share. Every client: a blue ring at the
    /// drain's reach, and the drain loop.
    /// </summary>
    public class AbramsSiphon : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _nextTick;
        private int _ticks;
        private bool _active;
        private DeadlockVoice? _loop;
        private LineRenderer? _ring;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private float SiphonRadius => AbramsData.SiphonRadius * MysticExpansion.RadiusMultiplier(_body);

        private bool Draining => AbramsSurvivor.SiphonBuff != null && _body.HasBuff(AbramsSurvivor.SiphonBuff) && _body.healthComponent != null && _body.healthComponent.alive;

        private void FixedUpdate()
        {
            if (!NetworkServer.active || !Draining) { _nextTick = 0f; return; }
            if (_nextTick <= 0f) _nextTick = Time.time; // first tick at once
            if (Time.time < _nextTick) return;
            _nextTick += AbramsData.SiphonTick;
            float dealt = 0f;
            bool fx = _ticks++ % 2 == 0; // the blood effect twice a second is plenty
            DamageTypeCombo type = DamageSource.Secondary;
            type.damageType |= DamageType.BypassArmor; // Deadlock: spirit damage, here armor-piercing
            foreach (HealthComponent hc in AbramsNet.EnemiesIn(_body.corePosition, SiphonRadius, _body.teamComponent.teamIndex))
            {
                dealt += AbramsNet.Hit(_body, hc, AbramsData.SiphonDps * _body.damage * AbramsData.SiphonTick, _body.RollCrit(), Vector3.zero, type, 0.25f);
                if (fx && AbramsSurvivor.DrainEffect != null && hc.body != null)
                    EffectManager.SimpleImpactEffect(AbramsSurvivor.DrainEffect, hc.body.corePosition, Vector3.up, true);
            }
            if (dealt > 0f)
                CursedAppleCuriosities.Unlocks.Challenges.AbramsSeeingRed.Siphoned(_body, _body.healthComponent.Heal(dealt * AbramsData.SiphonHeal, default(ProcChainMask))); // Crimson's challenge
        }

        private void Update()
        {
            bool on = Draining;
            if (on != _active)
            {
                _active = on;
                if (on) _loop = DeadlockSounds.Loop("abrams_siphon_loop", gameObject, AbramsData.SiphonDuration + 0.5f);
                else { DeadlockSounds.Stop(_loop); DeadlockSounds.Play("abrams_siphon_end", transform.position, gameObject); Clear(); }
            }
            if (!on) return;
            Ring();
        }

        /// <summary>The drain's reach: a blue ring on the ground around Abrams, hugging the terrain.</summary>
        private void Ring()
        {
            const int n = 48;
            if (_ring == null) { _ring = AbramsFx.Line("CursedApple_SiphonRing", new Color(0.2f, 0.55f, 1f, 0.6f), 0.08f); _ring.positionCount = n; _ring.loop = true; }
            Vector3 c = _body.footPosition;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 pt = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * SiphonRadius;
                if (Physics.Raycast(pt + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) pt.y = hit.point.y;
                _ring.SetPosition(i, pt + Vector3.up * 0.15f);
            }
        }

        private void Clear()
        {
            if (_ring != null) Destroy(_ring.gameObject);
            _ring = null;
        }

        private void OnDisable()
        {
            DeadlockSounds.Stop(_loop);
            Clear();
            _active = false;
        }
    }

    /// <summary>
    /// Infernal Resilience on an Abrams body. Server: every hit taken is healed back, a share of it over 20 s, once a
    /// second (Deadlock's RegenIncomingDamage); each hit is its own pool, as in Deadlock. Every client: Pending (the
    /// health still to come back) for the health bar, sent over by the server whenever it changes.
    /// </summary>
    public class AbramsResilience : MonoBehaviour
    {
        private readonly List<(float perTick, int ticksLeft)> _pools = new List<(float, int)>();
        private float _nextTick;
        private CharacterBody _body = null!;

        /// <summary>Health Infernal Resilience has yet to heal back (every client).</summary>
        public float Pending { get; set; }

        private void Awake() => _body = GetComponent<CharacterBody>();

        public static void Hook() => GlobalEventManager.onServerDamageDealt += OnDamage;

        private static void OnDamage(DamageReport report)
        {
            if (report.victimBody == null || report.damageDealt <= 0f || !report.victimBody.TryGetComponent(out AbramsResilience r)) return;
            if (report.victimBody.healthComponent == null || !report.victimBody.healthComponent.alive) return;
            int ticks = Mathf.Max(1, Mathf.RoundToInt(AbramsData.ResilienceSeconds / AbramsData.ResilienceInterval));
            r._pools.Add((report.damageDealt * AbramsData.ResilienceFraction / ticks, ticks));
            r.Sync();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _nextTick) return;
            _nextTick = Time.time + AbramsData.ResilienceInterval;
            if (_pools.Count == 0) return;
            if (_body.healthComponent == null || !_body.healthComponent.alive) { _pools.Clear(); Sync(); return; }
            float heal = 0f;
            for (int i = 0; i < _pools.Count; i++)
            {
                heal += _pools[i].perTick;
                _pools[i] = (_pools[i].perTick, _pools[i].ticksLeft - 1);
            }
            _pools.RemoveAll(x => x.ticksLeft <= 0);
            if (heal > 0f) _body.healthComponent.Heal(heal, default(ProcChainMask), nonRegen: false);
            Sync();
        }

        /// <summary>Server: the pending total, here and on every client.</summary>
        private void Sync()
        {
            float pending = 0f;
            foreach (var x in _pools) pending += x.perTick * x.ticksLeft;
            if (Mathf.Abs(pending - Pending) < 0.01f) return;
            Pending = pending;
            new AbramsPendingMessage { Body = gameObject, Pending = pending }.Send(NetworkDestination.Clients);
        }
    }

    /// <summary>Server to every client: how much health Abrams' Infernal Resilience has yet to heal back.</summary>
    public class AbramsPendingMessage : INetMessage
    {
        public GameObject? Body;
        public float Pending;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Pending); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Pending = r.ReadSingle(); }

        public void OnReceived()
        {
            if (Body != null && Body.TryGetComponent(out AbramsResilience r)) r.Pending = Pending;
        }
    }

    /// <summary>
    /// On every health bar (the HUD's and allies' overhead ones): Infernal Resilience's pending heal, drawn right after
    /// the current health, so the player sees what they'll heal back up to.
    /// </summary>
    public class AbramsResilienceBar : MonoBehaviour
    {
        private static readonly Color Tint = new Color(1f, 0.4f, 0.75f, 0.6f); // pink, see-through: health to come (user 2.5: the old ember read as yellow, like Shiv's Bloodletting and barrier)

        private RoR2.UI.HealthBar _bar = null!;
        private RectTransform? _fill;
        private HealthComponent? _source;
        private AbramsResilience? _resilience;

        public static void Hook()
        {
            On.RoR2.UI.HealthBar.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<AbramsResilienceBar>()._bar = self; };
        }

        private void LateUpdate()
        {
            HealthComponent? src = _bar != null ? _bar.source : null;
            if (src != _source) { _source = src; _resilience = src != null ? src.GetComponent<AbramsResilience>() : null; }
            float pending = _resilience != null && src != null && src.alive ? Mathf.Min(_resilience.Pending, src.missingCombinedHealth) : 0f;
            if (pending <= 0.5f || _bar.barContainer == null) { if (_fill != null) _fill.gameObject.SetActive(false); return; }
            if (_fill == null) Build();
            HealthComponent.HealthBarValues v = src!.GetHealthBarValues();
            float per = (1f - v.curseFraction) / Mathf.Max(1f, src.fullCombinedHealth);
            float from = v.healthFraction;
            float to = Mathf.Min(1f - v.curseFraction, from + pending * per);
            _fill!.gameObject.SetActive(to > from);
            _fill.anchorMin = new Vector2(from, 0f);
            _fill.anchorMax = new Vector2(to, 1f);
            _fill.SetAsLastSibling();
        }

        private void Build()
        {
            var go = new GameObject("AbramsResiliencePending", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            _fill = (RectTransform)go.transform;
            _fill.SetParent(_bar.barContainer, false);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = Tint;
            img.raycastTarget = false;
        }
    }
}
