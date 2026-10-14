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

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Server side of Paradox: Pulse Bomb's pulses and damage amp, the Time Wall (projectile deletion, the cut through it, the
    /// friendly damage bonus), Kinetic Carbine's speed and slow, and Paradoxical Swap (the shot, both travellers, the lifesteal).
    /// </summary>
    public static class ParadoxNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<ParadoxWallMessage>();
            NetworkingAPI.RegisterMessageType<ParadoxSlowMessage>();
            NetworkingAPI.RegisterMessageType<ParadoxCarbineMessage>();
            NetworkingAPI.RegisterMessageType<ParadoxSwapMessage>();
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (NetworkServer.active && info != null && info.attacker != null && !info.rejected) Amplify(self, info);
                orig(self, info);
            };
            GlobalEventManager.onServerDamageDealt += report =>
            {
                // Paradoxical Swap: 100% lifesteal while the swap lasts (user).
                CharacterBody? by = report.attackerBody;
                if (by == null || report.damageDealt <= 0f || !by.TryGetComponent(out ParadoxLifesteal steal) || !steal.Active) return;
                if (by.healthComponent != null && by.healthComponent.alive) by.healthComponent.Heal(report.damageDealt, default(ProcChainMask));
            };
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ParadoxSurvivor.SlowBuff != null)
                {
                    float s = Mathf.Clamp(body.GetBuffCount(ParadoxSurvivor.SlowBuff) / 100f, 0f, 0.95f);
                    if (s > 0f) args.moveSpeedReductionMultAdd += s / (1f - s); // a true s% slow
                }
                if (ParadoxSurvivor.CarbineSpeedBuff != null)
                    args.moveSpeedMultAdd += body.GetBuffCount(ParadoxSurvivor.CarbineSpeedBuff) / 100f;
            };
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;
        public static bool IsBoss(CharacterBody? b) => b != null && (b.isChampion || b.isBoss);
        public static bool IsParadox(CharacterBody? b) => b != null && BodyCatalog.GetBodyName(b.bodyIndex) == ParadoxSurvivor.BodyName;

        /// <summary>Server: one hit from Paradox, with procs.</summary>
        public static DamageInfo? Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f)
        {
            if (target == null || !target.alive || damage <= 0f) return null;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = Core(target),
                force = force,
                procCoefficient = proc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            if (di.rejected) return di;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
            return di;
        }

        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            foreach (HurtBox h in new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                         .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes())
                if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        /// <summary>
        /// Server, every hit: Pulse Bomb's debuff makes Paradox's damage to its target higher (Deadlock DamageAmplificationPerStack,
        /// per stack); an ally's attack that crossed a Time Wall on its way deals more (Deadlock FriendlyBulletDamageBonus).
        /// </summary>
        private static void Amplify(HealthComponent victim, DamageInfo info)
        {
            CharacterBody? by = info.attacker.GetComponent<CharacterBody>();
            if (by == null || victim.body == null) return;
            if (ParadoxSurvivor.AmpBuff != null && IsParadox(by))
            {
                int stacks = victim.body.GetBuffCount(ParadoxSurvivor.AmpBuff);
                if (stacks > 0) info.damage *= 1f + ParadoxData.PulseAmpPerStack * stacks;
            }
            if (ParadoxWall.Live.Count > 0 && by.teamComponent != null && victim.body.teamComponent != null && by.teamComponent.teamIndex != victim.body.teamComponent.teamIndex)
            {
                Vector3 from = by.corePosition, to = info.position != Vector3.zero ? info.position : victim.body.corePosition;
                foreach (ParadoxWall w in ParadoxWall.Live)
                    if (w.Team == by.teamComponent.teamIndex && w.Formed && w.Crosses(from, to))
                    {
                        info.damage *= 1f + ParadoxData.WallFriendlyBonus;
                        break;
                    }
            }
        }

        /// <summary>Server: a slow that starts at <paramref name="amount"/> and fades to nothing over <paramref name="seconds"/>.</summary>
        public static void FadingSlow(HealthComponent? hc, float amount, float seconds)
        {
            if (hc == null || !hc.alive || hc.body == null) return;
            var s = hc.GetComponent<ParadoxFadingSlow>() ?? hc.gameObject.AddComponent<ParadoxFadingSlow>();
            s.Add(amount, seconds * DurationExtender.Current);
        }
    }

    // ================= slows =================

    /// <summary>Server, on a slowed body: the strongest of its fading slows, shown as the slow buff's stacks (percent).</summary>
    public class ParadoxFadingSlow : MonoBehaviour
    {
        private readonly List<(float amount, float start, float seconds)> _slows = new List<(float, float, float)>();
        private CharacterBody _body = null!;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Add(float amount, float seconds) => _slows.Add((amount, Time.time, Mathf.Max(0.1f, seconds)));

        private void FixedUpdate()
        {
            if (!NetworkServer.active || ParadoxSurvivor.SlowBuff == null) return;
            float most = 0f;
            _slows.RemoveAll(s => Time.time - s.start >= s.seconds);
            foreach (var s in _slows) most = Mathf.Max(most, s.amount * (1f - (Time.time - s.start) / s.seconds));
            int want = Mathf.RoundToInt(most * 100f);
            if (_body.GetBuffCount(ParadoxSurvivor.SlowBuff) != want) _body.SetBuffCount(ParadoxSurvivor.SlowBuff.buffIndex, want);
            if (_slows.Count == 0) Destroy(this);
        }
    }

    /// <summary>Owner to server: Kinetic Carbine hit this (a fading slow) or Time Wall-like slow requests.</summary>
    public class ParadoxSlowMessage : INetMessage
    {
        public GameObject? Attacker, Target;
        public float Amount, Seconds;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Amount); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Amount = r.ReadSingle(); Seconds = r.ReadSingle(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Target == null) return;
            var hc = Target.GetComponent<HealthComponent>();
            using (DurationExtender.From(Attacker)) ParadoxNet.FadingSlow(hc, Amount, Seconds);
            if (hc != null) EquipmentFx.Sound("paradox_carbine_slow", ParadoxNet.Core(hc));
        }
    }

    // ================= Kinetic Carbine's speed =================

    /// <summary>Owner to server: Kinetic Carbine's charge (0-1) while charging; Fired holds the speed for Deadlock's 3.5 s after the shot.</summary>
    public class ParadoxCarbineMessage : INetMessage
    {
        public GameObject? Body;
        public float Charge;
        public bool Fired, Cancelled;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Charge); w.Write(Fired); w.Write(Cancelled); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Charge = r.ReadSingle(); Fired = r.ReadBoolean(); Cancelled = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null || !Body.TryGetComponent(out CharacterBody b) || ParadoxSurvivor.CarbineSpeedBuff == null) return;
            var speed = b.GetComponent<ParadoxCarbineSpeed>() ?? b.gameObject.AddComponent<ParadoxCarbineSpeed>();
            speed.Set(Cancelled ? 0f : Charge, Fired ? ParadoxData.CarbineSpeedSeconds * DurationExtender.Multiplier(b) : float.MaxValue);
        }
    }

    /// <summary>Server, on Paradox: Kinetic Carbine's move speed (Deadlock SpeedChange), as buff stacks (percent).</summary>
    public class ParadoxCarbineSpeed : MonoBehaviour
    {
        private float _charge, _until = float.MaxValue;
        private CharacterBody _body = null!;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Set(float charge, float hold)
        {
            _charge = charge;
            _until = hold == float.MaxValue ? float.MaxValue : Time.time + hold;
            Show();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (Time.time >= _until) { _charge = 0f; _until = float.MaxValue; Show(); }
        }

        private void Show()
        {
            if (ParadoxSurvivor.CarbineSpeedBuff == null) return;
            int want = Mathf.RoundToInt(Mathf.Clamp01(_charge) * ParadoxData.CarbineSpeed * 100f);
            if (_body.GetBuffCount(ParadoxSurvivor.CarbineSpeedBuff) != want) _body.SetBuffCount(ParadoxSurvivor.CarbineSpeedBuff.buffIndex, want);
        }
    }

    // ================= Secondary: Pulse Bomb =================

    /// <summary>
    /// Server, on the Pulse Bomb (Deadlock citadel_ability_chrono_pulse_grenade): thrown in an arc, it settles where it lands and
    /// pulses (faster with Paradox's attack speed: user) for its duration, each blast 1 m wider than the last. Every enemy a pulse
    /// hits takes damage (bosses half), a stack of Paradox's damage amp and a short slow.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class ParadoxPulseBomb : MonoBehaviour, IProjectileImpactBehavior
    {
        private Rigidbody? _rb;
        private bool _armed;
        private float _age, _armedAt, _nextPulse, _duration, _interval;
        private int _pulses;
        private DeadlockVoice? _hum;
        private CharacterBody? _owner;

        private void Awake() => _rb = GetComponent<Rigidbody>();
        private void Start() => _hum = DeadlockSounds.Loop("paradox_pulse_loop", gameObject, 12f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (!NetworkServer.active || _armed) return;
            Arm();
        }

        private void Arm()
        {
            _armed = true;
            _armedAt = _age;
            if (_rb != null) { _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; _rb.isKinematic = true; }
            var simple = GetComponent<ProjectileSimple>();
            if (simple != null) { simple.desiredForwardSpeed = 0f; simple.enabled = false; }
            var pc = GetComponent<ProjectileController>();
            _owner = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            _duration = ParadoxData.PulseDuration * DurationExtender.Multiplier(_owner);
            _interval = ParadoxData.PulseInterval / Mathf.Max(0.1f, _owner != null ? _owner.attackSpeed : 1f);
            _nextPulse = _age + 0.15f;
            EquipmentFx.Sound("paradox_pulse_land", transform.position);
            EquipmentFx.Sound("paradox_pulse_armed", transform.position);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (!_armed) { if (_age > 2.5f) Arm(); return; }
            if (_age >= _nextPulse && _age - _armedAt <= _duration + 0.01f) { _nextPulse += _interval; Pulse(); }
            if (_age - _armedAt > _duration)
            {
                EquipmentFx.Sound("paradox_pulse_expire", transform.position);
                Destroy(gameObject);
            }
        }

        private void Pulse()
        {
            if (_owner == null || _owner.teamComponent == null) return;
            var pd = GetComponent<ProjectileDamage>();
            float radius = ParadoxData.PulseRadiusAt(_pulses++) * MysticExpansion.RadiusMultiplier(_owner);
            if (ParadoxSurvivor.PulseEffect != null) EffectManager.SpawnEffect(ParadoxSurvivor.PulseEffect, new EffectData { origin = transform.position, scale = radius }, true);
            foreach (HealthComponent hc in ParadoxNet.EnemiesIn(transform.position, radius, _owner.teamComponent.teamIndex))
            {
                float damage = ParadoxData.PulseDamage * (pd != null ? pd.damage : _owner.damage) * (ParadoxNet.IsBoss(hc.body) ? ParadoxData.PulseBossScale : 1f);
                ParadoxNet.Hit(_owner, hc, damage, pd != null && pd.crit, Vector3.zero, DamageSource.Secondary, 0.5f);
                if (!hc.alive || hc.body == null) continue;
                EquipmentFx.Sound("paradox_pulse_hit", ParadoxNet.Core(hc));
                using (DurationExtender.From(_owner))
                {
                    if (ParadoxSurvivor.AmpBuff != null) hc.body.AddTimedBuff(ParadoxSurvivor.AmpBuff, ParadoxData.PulseAmpSeconds);
                    ParadoxNet.FadingSlow(hc, ParadoxData.PulseSlow, ParadoxData.PulseSlowSeconds);
                }
            }
        }
    }

    // ================= Secondary alt: Time Wall =================

    /// <summary>Owner to everyone: a Time Wall stands here (each machine draws it; the server runs it).</summary>
    public class ParadoxWallMessage : INetMessage
    {
        public GameObject? Owner;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool FromServer;

        public void Serialize(NetworkWriter w) { w.Write(Owner); w.Write(Position); w.Write(Rotation); w.Write(FromServer); }
        public void Deserialize(NetworkReader r) { Owner = r.ReadGameObject(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); FromServer = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!FromServer)
            {
                // The owner asked: the server relays it to every client (itself included, if it is one).
                if (!NetworkServer.active) return;
                FromServer = true;
                if (NetworkServer.active && !NetworkClient.active) ParadoxWall.Spawn(this); // dedicated server: no client copy of its own
                this.Send(NetworkDestination.Clients);
                return;
            }
            ParadoxWall.Spawn(this);
        }
    }

    /// <summary>
    /// A Time Wall (Deadlock citadel_ability_chrono_time_wall: 8 m x 4 m x 0.5 m for 5.5 s), the user's version. Every machine
    /// draws it. Server: it deletes enemy projectiles that touch it, cuts enemies that pass through for 500% (once each) with a slow
    /// and a moment's stop, and allies' attacks that cross it hit harder (ParadoxNet.Amplify asks Crosses).
    /// </summary>
    public class ParadoxWall : MonoBehaviour
    {
        public static readonly List<ParadoxWall> Live = new List<ParadoxWall>();

        public CharacterBody? Owner;
        public TeamIndex Team;
        private float _age, _duration;
        private Vector3 _half;
        private readonly HashSet<HealthComponent> _cut = new HashSet<HealthComponent>();
        private Renderer? _fill;
        private Color _tint;

        public bool Formed => _age >= ParadoxData.WallFormation;

        public static void Spawn(ParadoxWallMessage m)
        {
            var go = new GameObject("CursedAppleParadoxTimeWall");
            go.transform.SetPositionAndRotation(m.Position, m.Rotation);
            var w = go.AddComponent<ParadoxWall>();
            w.Owner = m.Owner != null ? m.Owner.GetComponent<CharacterBody>() : null;
            w.Team = w.Owner != null && w.Owner.teamComponent != null ? w.Owner.teamComponent.teamIndex : TeamIndex.Player;
            float scale = MysticExpansion.RadiusMultiplier(w.Owner);
            w._half = new Vector3(ParadoxData.WallWidth * scale, ParadoxData.WallHeight * scale, ParadoxData.WallDepth) * 0.5f;
            w._duration = ParadoxData.WallDuration * DurationExtender.Multiplier(w.Owner);
            w.Draw();
            Live.Add(w);
            EquipmentFx.Sound("paradox_wall_form", m.Position);
        }

        private void OnDestroy() => Live.Remove(this);

        private void Draw()
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(transform, false);
            box.transform.localPosition = new Vector3(0f, _half.y, 0f);
            box.transform.localScale = _half * 2f;
            _fill = box.GetComponent<Renderer>();
            if (ParadoxSurvivor.WallMaterial != null) _fill.sharedMaterial = ParadoxSurvivor.WallMaterial;
            _fill.material = new Material(_fill.sharedMaterial); // this wall fades on its own
            _tint = _fill.material.HasProperty("_TintColor") ? _fill.material.GetColor("_TintColor") : ParadoxSurvivor.Light;
            _fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Does a line cross the wall (Liang-Barsky against the box, in the wall's frame)?</summary>
        public bool Crosses(Vector3 from, Vector3 to)
        {
            Vector3 center = transform.position + transform.up * _half.y;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            Vector3 a = inv * (from - center), b = inv * (to - center), d = b - a;
            float t0 = 0f, t1 = 1f;
            for (int i = 0; i < 3; i++)
            {
                float p = a[i], q = d[i], h = _half[i];
                if (Mathf.Abs(q) < 1e-6f) { if (p < -h || p > h) return false; continue; }
                float u = (-h - p) / q, v = (h - p) / q;
                if (u > v) (u, v) = (v, u);
                t0 = Mathf.Max(t0, u); t1 = Mathf.Min(t1, v);
                if (t0 > t1) return false;
            }
            return true;
        }

        private void Update()
        {
            // Fade in while it forms, out in its last half second.
            if (_fill == null) return;
            float a = Mathf.Clamp01(_age / ParadoxData.WallFormation) * Mathf.Clamp01((_duration - _age) / 0.5f);
            if (_fill.material.HasProperty("_TintColor")) _fill.material.SetColor("_TintColor", new Color(_tint.r, _tint.g, _tint.b, _tint.a * a));
        }

        private void FixedUpdate()
        {
            _age += Time.fixedDeltaTime;
            if (_age >= _duration)
            {
                if (NetworkServer.active) EquipmentFx.Sound("paradox_wall_expire", transform.position);
                Destroy(gameObject);
                return;
            }
            if (!NetworkServer.active || !Formed) return;
            Vector3 center = transform.position + transform.up * _half.y;
            // Enemy projectiles that touch it are deleted (user).
            foreach (Collider c in Physics.OverlapBox(center, _half + Vector3.one * 0.3f, transform.rotation, LayerIndex.projectile.mask, QueryTriggerInteraction.Collide))
            {
                var pc = c.GetComponentInParent<ProjectileController>();
                if (pc == null || pc.teamFilter == null || pc.teamFilter.teamIndex == Team) continue;
                if (ParadoxSurvivor.BlockEffect != null) EffectManager.SpawnEffect(ParadoxSurvivor.BlockEffect, new EffectData { origin = pc.transform.position, scale = 1f }, true);
                EquipmentFx.Sound("paradox_wall_block", pc.transform.position);
                Destroy(pc.gameObject);
            }
            // Enemies passing through: cut once each (user), slowed and stopped for a moment (Deadlock: time nearly stops in the wall).
            if (Owner == null) return;
            foreach (Collider c in Physics.OverlapBox(center, _half, transform.rotation, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                var box = c.GetComponent<HurtBox>();
                HealthComponent? hc = box != null ? box.healthComponent : null;
                if (hc == null || !hc.alive || box!.teamIndex == Team || !_cut.Add(hc)) continue;
                ParadoxNet.Hit(Owner, hc, ParadoxData.WallDamage * Owner.damage, Owner.RollCrit(), Vector3.zero, DamageSource.Secondary);
                EquipmentFx.Sound("paradox_wall_hit", ParadoxNet.Core(hc));
                if (!hc.alive) continue;
                using (DurationExtender.From(Owner))
                {
                    ParadoxNet.FadingSlow(hc, ParadoxData.WallSlow, ParadoxData.WallSlowSeconds);
                    SetStateOnHurt.SetStunOnObject(hc.gameObject, ParadoxData.WallFormation); // Deadlock TimeScaleDuration 0.5
                }
            }
        }
    }

    // ================= Special: Paradoxical Swap =================

    /// <summary>
    /// Server, on the swap shot: like Grey Talon's Charged Shot it sweeps the path it flew with a wide hitbox, but stops at the
    /// first enemy (or terrain, or its range: user, limited). The enemy it reaches swaps places with Paradox.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class ParadoxSwapShot : MonoBehaviour
    {
        private Vector3 _last;
        private bool _started, _done;
        private DeadlockVoice? _hum;

        private void Start() => _hum = DeadlockSounds.Loop("paradox_swap_loop", gameObject, 2f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _done) return;
            Vector3 now = transform.position;
            var pc = GetComponent<ProjectileController>();
            CharacterBody? by = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            if (by == null || by.teamComponent == null) { Destroy(gameObject); return; }
            if (!_started)
            {
                _started = true;
                _last = now;
                var simple = GetComponent<ProjectileSimple>();
                if (simple != null) simple.lifetime = ParadoxData.SwapRange * MysticExpansion.RadiusMultiplier(by) / ParadoxData.SwapSpeed;
                return;
            }
            Vector3 d = now - _last;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            bool wall = Physics.SphereCast(_last, 0.2f, d / len, out RaycastHit wallHit, len, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            float reach = wall ? wallHit.distance : len;
            RaycastHit[] hits = Physics.SphereCastAll(_last, ParadoxData.SwapRadius, d / len, reach, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                HurtBox? box = h.collider.GetComponent<HurtBox>();
                HealthComponent? hc = box != null ? box.healthComponent : null;
                if (hc == null || !hc.alive || hc.body == null || box!.teamIndex == by.teamComponent.teamIndex) continue;
                _done = true;
                var pd = GetComponent<ProjectileDamage>();
                ParadoxSwap.Begin(by, hc, pd != null ? pd.damage : by.damage * ParadoxData.SwapDamage, pd != null && pd.crit);
                Destroy(gameObject);
                return;
            }
            if (wall)
            {
                _done = true;
                if (ParadoxSurvivor.SwapHitEffect != null) EffectManager.SpawnEffect(ParadoxSurvivor.SwapHitEffect, new EffectData { origin = wallHit.point, scale = 1.5f }, true);
                Destroy(gameObject);
                return;
            }
            _last = now;
        }
    }

    /// <summary>The swap itself: both travel at once on mirrored arcs, passing each other (user: a smooth swap).</summary>
    public static class ParadoxSwap
    {
        /// <summary>Where a traveller is at time t of a swap from <paramref name="from"/> to <paramref name="to"/> (side: +1 / -1).</summary>
        public static Vector3 PathAt(Vector3 from, Vector3 to, float t, float duration, float side)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.05f, duration)));
            Vector3 flat = to - from;
            flat.y = 0f;
            Vector3 right = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            float bump = Mathf.Sin(s * Mathf.PI);
            return Vector3.Lerp(from, to, s) + Vector3.up * ParadoxData.SwapArc * bump + right * side * ParadoxData.SwapPass * bump;
        }

        /// <summary>Server: the shot reached an enemy. It takes the swap damage (healing her: the lifesteal is already on), then both go.</summary>
        public static void Begin(CharacterBody paradox, HealthComponent enemy, float damage, bool crit)
        {
            Vector3 a = paradox.footPosition;
            Vector3 b = enemy.body != null ? enemy.body.footPosition : enemy.transform.position;
            float duration = ParadoxData.SwapTime(Vector3.Distance(a, b));
            var steal = paradox.GetComponent<ParadoxLifesteal>() ?? paradox.gameObject.AddComponent<ParadoxLifesteal>();
            steal.Until = Time.time + duration + 0.3f;
            paradox.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, duration + 0.1f); // she's out of time while she travels

            EquipmentFx.Sound("paradox_swap_hit", ParadoxNet.Core(enemy));
            EquipmentFx.Sound("paradox_swap_go", a);
            if (ParadoxSurvivor.SwapHitEffect != null)
            {
                EffectManager.SpawnEffect(ParadoxSurvivor.SwapHitEffect, new EffectData { origin = ParadoxNet.Core(enemy), scale = 3f }, true);
                EffectManager.SpawnEffect(ParadoxSurvivor.SwapHitEffect, new EffectData { origin = paradox.corePosition, scale = 3f }, true);
            }
            ParadoxNet.Hit(paradox, enemy, damage, crit, Vector3.zero, DamageSource.Special);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Paradox] swap with {enemy.name}: {Vector3.Distance(a, b):0.#} m in {duration:0.##} s");
            if (enemy.alive)
            {
                var carry = enemy.GetComponent<ParadoxSwapCarry>() ?? enemy.gameObject.AddComponent<ParadoxSwapCarry>();
                carry.Go(paradox, b, a, duration);
            }
            // Her own trip runs on her owner's machine (movement is owner-authoritative).
            new ParadoxSwapMessage { Paradox = paradox.gameObject, From = a, To = b, Duration = duration }.Send(NetworkDestination.Clients); // a host's own client too
        }
    }

    /// <summary>Server to every client: Paradox's half of a swap. Her owner moves her (ParadoxSwapping); others just watch.</summary>
    public class ParadoxSwapMessage : INetMessage
    {
        public GameObject? Paradox;
        public Vector3 From, To;
        public float Duration;

        public void Serialize(NetworkWriter w) { w.Write(Paradox); w.Write(From); w.Write(To); w.Write(Duration); }
        public void Deserialize(NetworkReader r) { Paradox = r.ReadGameObject(); From = r.ReadVector3(); To = r.ReadVector3(); Duration = r.ReadSingle(); }

        public void OnReceived()
        {
            if (Paradox == null || !Paradox.TryGetComponent(out CharacterBody b) || !b.hasEffectiveAuthority) return;
            EntityStateMachine? body = EntityStateMachine.FindByCustomName(Paradox, "Body");
            body?.SetInterruptState(new EntityStates.CursedApple.Paradox.ParadoxSwapping { From = From, To = To, Duration = Duration }, EntityStates.InterruptPriority.Frozen);
        }
    }

    /// <summary>Server, on the swapped enemy: carried along its arc to where Paradox stood, stunned on the way, slowed at the end (user).</summary>
    public class ParadoxSwapCarry : MonoBehaviour
    {
        private CharacterBody? _by, _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;
        private Vector3 _from, _to;
        private float _start, _duration;
        private bool _going;

        public void Go(CharacterBody by, Vector3 from, Vector3 to, float duration)
        {
            _by = by;
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
            _from = from; _to = to; _duration = duration;
            _start = Time.time;
            _going = true;
            SetStateOnHurt.SetStunOnObject(gameObject, duration + 0.1f);
            DeadlockFallGuard.Protect(gameObject, duration + 1.5f);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || !_going) return;
            if (_body == null || _body.healthComponent == null || !_body.healthComponent.alive) { Destroy(this); return; }
            float t = Time.time - _start + Time.fixedDeltaTime;
            Vector3 want = ParadoxSwap.PathAt(_from, _to, t, _duration, -1f);
            Vector3 v = (want - _body.footPosition) / Time.fixedDeltaTime;
            if (_motor != null)
            {
                _motor.Motor.ForceUnground();
                _motor.velocity = v;
                _motor.disableAirControlUntilCollision = true;
            }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = v;
            else transform.position += v * Time.fixedDeltaTime;
            if (t >= _duration) End();
        }

        private void End()
        {
            _going = false;
            if (_motor != null) { _motor.velocity = Vector3.zero; _motor.disableAirControlUntilCollision = false; }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = Vector3.zero;
            EquipmentFx.Sound("paradox_swap_end", transform.position);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Paradox] swapped {name} ended {Vector3.Distance(transform.position, _to):0.#} m from her old spot");
            using (DurationExtender.From(_by)) ParadoxNet.FadingSlow(_body!.healthComponent, ParadoxData.SwapSlow, ParadoxData.SwapSlowSeconds);
            Destroy(this);
        }
    }

    /// <summary>Server, on Paradox: the swap's 100% lifesteal window (user).</summary>
    public class ParadoxLifesteal : MonoBehaviour
    {
        public float Until;
        public bool Active => Time.time < Until;
    }
}
