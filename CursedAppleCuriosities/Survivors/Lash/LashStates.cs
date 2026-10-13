using System.Collections.Generic;
using CursedAppleCuriosities;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Lash;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Lash
{
    internal static class Fx
    {
        public static void Line(Vector3 from, Vector3 to, Color color, float width, float seconds)
        {
            var go = new GameObject("CursedApple_LashWhip");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LashSurvivor.LineMaterial;
            lr.startWidth = width;
            lr.endWidth = width * 0.4f;
            lr.startColor = lr.endColor = color;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(go, seconds);
        }

        public static readonly Color Whip = new Color(0.55f, 0.35f, 0.2f);
        public static readonly Color Slam = new Color(0.71f, 0.55f, 1f, 0.9f); // Lash purple

        /// <summary>A looping outline drawn with RoR2's laser-pointer beam (local player only: an aiming aid).</summary>
        public static LineRenderer Outline(string name)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LashSurvivor.IndicatorMaterial;
            if (lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", Slam);
            lr.startColor = lr.endColor = Slam;
            lr.startWidth = lr.endWidth = 0.12f;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>A flat ring (Death Slam's impact radius where the enemies will land).</summary>
        public static void Ring(LineRenderer lr, Vector3 center, float radius)
        {
            const int n = 40;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.25f, Mathf.Sin(a) * radius));
            }
        }

        /// <summary>Height above the ground under the body (200 when there is none).</summary>
        public static float Height(CharacterBody body) =>
            Physics.Raycast(body.footPosition + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 200f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                ? hit.distance - 0.2f : 200f;
    }

    // ================= Primary: Lash's gun =================

    /// <summary>
    /// Lash's gun: a three-round burst (Deadlock: 0.084 s between bullets, 0.2625 s between bursts). A close-range gun (user):
    /// tight, no recoil, falls off hard past 16 m.
    /// </summary>
    public class FireBurst : BaseSkillState
    {
        private float _interval, _duration;
        private int _fired;
        private string _muzzle = "MuzzleRight";

        public override void OnEnter()
        {
            base.OnEnter();
            _interval = LashData.BurstInterval / attackSpeedStat;
            _duration = (LashData.BurstInterval * (LashData.BurstCount - 1) + LashData.BurstCycle) / attackSpeedStat;
            Transform? glove = FindModelChild(LashSurvivor.MuzzleName);
            if (glove != null && glove.name == LashData.MuzzleBone) _muzzle = LashSurvivor.MuzzleName; // pointed at the glove once Lash is on
            StartAimMode(2f);
            Fire();
        }

        private void Fire()
        {
            _fired++;
            LashModelSwap.Play(gameObject, "shoot");
            DeadlockSounds.Play("lash_gun_fire", transform.position, gameObject);
            if (LashSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(LashSurvivor.MuzzleEffect, gameObject, _muzzle, false);
            // No recoil: Deadlock's Lash gun stays dead flat (user: a tight close-range gun, unlike Seven's climbing one)
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                new BulletAttack
                {
                    owner = gameObject,
                    weapon = gameObject,
                    origin = aim.origin,
                    aimVector = aim.direction,
                    minSpread = 0f,
                    maxSpread = characterBody.spreadBloomAngle * LashData.GunSpread,
                    damage = LashData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 120f,
                    tracerEffectPrefab = LashSurvivor.TracerEffect,
                    hitEffectPrefab = LashSurvivor.HitEffect,
                    muzzleName = _muzzle,
                    isCrit = RollCrit(),
                    radius = 0.1f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.None,
                    // Short range (user): full damage to 16 m, easing to 35% by 48 m
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) => info.damage *= LashData.GunFalloff(hit.distance),
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(LashData.GunBloomPerShot);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            while (_fired < LashData.BurstCount && fixedAge >= _fired * _interval) Fire();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Ground Strike =================

    /// <summary>
    /// Ground Strike. In the air, Lash dives where you aim (at most 60 degrees from straight down) and crashes down,
    /// harder the further he fell. On the ground he strikes right where he stands (no hop, by playtest).
    /// </summary>
    public class GroundStrike : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, 2f);
            DeadlockSounds.Play("lash_gs_whoosh", transform.position, gameObject);
            DeadlockHeroVoice.Power("lash_vo_power2", gameObject);
            if (!isAuthority) return;
            if (characterMotor.isGrounded)
            {
                Vector3 fwd = GetAimRay().direction;
                fwd.y = 0f;
                outer.SetNextState(new GroundStrikeLand { Fallen = 0f, Forward = fwd.sqrMagnitude > 0.01f ? fwd.normalized : characterDirection.forward });
            }
            else outer.SetNextState(new GroundStrikeDive());
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    public class GroundStrikeDive : BaseSkillState
    {
        private DeadlockVoice? _loop;

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            if (isAuthority && _grabbed.Count > 0) LashNet.ToServer(new DiveGrabMessage { Attacker = gameObject, Release = true });
            base.OnExit();
        }

        private readonly HashSet<HealthComponent> _grabbed = new HashSet<HealthComponent>();
        private Vector3 _dir;
        private float _startY;
        private Vector3 _lastPos;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, 15f); // the dive lasts until he lands
            LashModelSwap.Play(gameObject, "gs_drop");
            _loop = DeadlockSounds.Loop("lash_gs_loop", gameObject, 4f);
            if (!isAuthority) return;
            _dir = GetAimRay().direction;
            if (Vector3.Angle(_dir, Vector3.down) > LashData.GroundStrikeMinAimDeg)
            {
                Vector3 flat = new Vector3(_dir.x, 0f, _dir.z);
                flat = flat.sqrMagnitude < 0.01f ? characterDirection.forward : flat.normalized;
                _dir = Vector3.RotateTowards(Vector3.down, flat, LashData.GroundStrikeMinAimDeg * Mathf.Deg2Rad, 0f);
            }
            _startY = characterBody.footPosition.y;
            _lastPos = transform.position;
            characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            characterMotor.velocity = _dir * LashData.GroundStrikeSpeed;
            float moved = (transform.position - _lastPos).magnitude;
            _lastPos = transform.position;
            bool stuck = fixedAge > 0.15f && moved < LashData.GroundStrikeSpeed * Time.fixedDeltaTime * 0.2f; // a wall, roof or a big body
            // Enemies the dive runs into don't stop it (user): airborne ones are dragged down with him (the server
            // decides who's airborne), grounded ones are just in the strike. It lands only on terrain.
            float step = LashData.GroundStrikeSpeed * Time.fixedDeltaTime + 0.6f;
            foreach (RaycastHit hit in Physics.SphereCastAll(characterBody.corePosition, 0.9f, _dir, step, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox hb = hit.collider.GetComponent<HurtBox>();
                if (hb == null || hb.healthComponent == null || hb.healthComponent == healthComponent || !FriendlyFireManager.ShouldDirectHitProceed(hb.healthComponent, GetTeam())) continue;
                if (_grabbed.Add(hb.healthComponent)) LashNet.ToServer(new DiveGrabMessage { Attacker = gameObject, Target = hb.healthComponent.gameObject });
            }
            // Terrain reached this tick (walls and slopes too: a sphere at his feet, cast along the dive): he's put on it and
            // strikes there. Or standing on the world (not on an enemy's back).
            bool terrain = Physics.SphereCast(characterBody.footPosition + Vector3.up * 0.6f, 0.5f, _dir, out RaycastHit wall, LashData.GroundStrikeSpeed * Time.fixedDeltaTime, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (terrain && fixedAge > 0.05f) characterMotor.Motor.SetPosition(transform.position + _dir * wall.distance);
            Collider ground = characterMotor.Motor.GroundingStatus.GroundCollider;
            bool onWorld = characterMotor.isGrounded && ground != null && (LayerIndex.world.mask.value & (1 << ground.gameObject.layer)) != 0;
            if (onWorld && fixedAge > 0.05f || terrain && fixedAge > 0.05f || stuck || fixedAge > 4f)
            {
                Vector3 fwd = new Vector3(_dir.x, 0f, _dir.z);
                if (fwd.sqrMagnitude < 0.04f) { fwd = GetAimRay().direction; fwd.y = 0f; } // a straight-down dive waves where you look
                outer.SetNextState(new GroundStrikeLand { Fallen = Mathf.Max(0f, _startY - characterBody.footPosition.y), Forward = fwd.normalized });
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    public class GroundStrikeLand : BaseSkillState
    {
        public float Fallen;
        public Vector3 Forward = Vector3.forward;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, 1.5f, landed: true); // covers the landing report reaching the server
            LashModelSwap.Play(gameObject, "gs_land");
            Vector3 at = characterBody.footPosition;
            DeadlockSounds.Play("lash_gs_impact", at);
            if (LashSurvivor.GroundSlamEffect != null)
                EffectManager.SpawnEffect(LashSurvivor.GroundSlamEffect, new EffectData { origin = at, scale = LashData.GroundStrikeRadius * MysticExpansion.RadiusMultiplier(characterBody) }, false);
            if (!isAuthority) return;
            characterMotor.velocity = Vector3.zero;
            float coeff = LashData.GroundStrikeDamage
                + LashData.GroundStrikeDamagePerMetre * Mathf.Min(Fallen, LashData.FallPrimaryM)
                + LashData.GroundStrikeDamagePerMetreBeyond * Mathf.Max(0f, Fallen - LashData.FallPrimaryM);
            // The wave goes forward: centre the blast ahead of Lash; anything right beside him is still inside it.
            BlastAttack.Result r = new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                baseDamage = coeff * damageStat,
                baseForce = 1500f,
                bonusForce = Vector3.up * 800f,
                position = at + Forward * (LashData.GroundStrikeRadius * 0.3f),
                radius = LashData.GroundStrikeRadius * 0.8f,
                falloffModel = BlastAttack.FalloffModel.None,
                crit = RollCrit(),
                procCoefficient = 1f,
                damageType = DamageSource.Utility,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
            }.Fire();
            CursedAppleCuriositiesPlugin.Logger?.LogDebug($"[Lash] Ground Strike: fell {Fallen:0.0} m, {coeff * 100f:0}% damage, hit {r.hitCount}");
            if (r.hitCount > 0)
            {
                DeadlockSounds.Play("lash_gs_hit", at);
                if (Fallen >= 20f) DeadlockSounds.Play("lash_vo_ground_pound", at, gameObject);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && fixedAge >= 0.35f) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Secondary: Grapple =================

    /// <summary>
    /// Grapple: lash onto a target (enemy or friendly player) and get launched up steeply (GrappleLaunchAngle) toward it;
    /// no steering until you jump or dash. Gift from Deadlock's upgrade: air jumps, air dashes and a stamina pip back.
    /// </summary>
    public class Grapple : BaseSkillState
    {
        private HurtBox? _target;

        public override void OnEnter()
        {
            base.OnEnter();
            LashModelSwap.Play(gameObject, "grapple_start");
            DeadlockSounds.Play("lash_grapple_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("lash_vo_power1", gameObject);
            var tracker = GetComponent<LashGrappleTracker>();
            _target = tracker != null ? tracker.Target : null;
            if (_target != null)
            {
                DeadlockSounds.Play("lash_grapple_target", _target.transform.position);
                DeadlockSounds.Play("lash_grapple_impact", _target.transform.position);
                DeadlockSounds.Loop("lash_grapple_loop", gameObject, 0.9f);
                Transform? hand = FindModelChild(LashSurvivor.MuzzleName);
                Fx.Line(hand != null && hand.name == LashData.MuzzleBone ? hand.position : characterBody.corePosition, _target.transform.position, Fx.Whip, 0.05f, 0.3f);
            }
            if (!isAuthority) return;
            if (_target == null) { outer.SetNextStateToMain(); return; }
            // A speedy, steep launch up toward the target (playtest: no arc onto it).
            Vector3 flat = _target.transform.position - characterBody.footPosition;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.01f ? flat.normalized : characterDirection.forward;
            float up = LashData.GrappleLaunchAngle * Mathf.Deg2Rad;
            Vector3 v = (flat * Mathf.Cos(up) + Vector3.up * Mathf.Sin(up)) * LashData.GrappleLaunchSpeed;
            characterMotor.Motor.ForceUnground();
            characterMotor.velocity = v;
            characterMotor.disableAirControlUntilCollision = true;
            // Deadlock's Grapple upgrade, given free: air jumps and air dashes come back, plus a stamina pip.
            characterMotor.jumpCount = 1;
            var stamina = GetComponent<DeadlockMover>();
            if (stamina != null) { stamina.AirDashes = 0; stamina.Refund(1); }
            characterDirection.forward = new Vector3(v.x, 0f, v.z).sqrMagnitude > 0.01f ? new Vector3(v.x, 0f, v.z) : characterDirection.forward;
            // Just the launch: Lash is free at once (other skills, jumps, dashes) while the arc carries him (playtest).
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // ================= Special: Death Slam =================

    /// <summary>
    /// Death Slam, as Deadlock plays it: channel while hovering; enemies that stay in the cone for 0.7 s are locked
    /// on (max 6, a "ding" each; a lock holds however far Lash drifts). When the channel ends (or you press it again), the
    /// locked enemies are pulled up to Lash, held, then thrown where you aim. Bosses included.
    /// </summary>
    public class DeathSlamChannel : BaseSkillState
    {
        private readonly Dictionary<CharacterBody, float> _inCone = new Dictionary<CharacterBody, float>();
        private readonly List<CharacterBody> _locked = new List<CharacterBody>();
        private readonly Dictionary<CharacterBody, Indicator> _markers = new Dictionary<CharacterBody, Indicator>();
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            LashModelSwap.Play(gameObject, characterMotor != null && characterMotor.isGrounded ? "ds_channel_ground" : "ds_channel");
            DeadlockSounds.Play("lash_ds_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("lash_vo_power4", gameObject);
            _loop = DeadlockSounds.Loop("lash_ds_channel", gameObject, LashData.DeathSlamChannel * DurationExtender.Multiplier(characterBody) + 0.2f);
            if (isAuthority)
            {
                _ring = Fx.Outline("CursedApple_DeathSlamArea");
                _groundRing = Fx.Outline("CursedApple_DeathSlamAreaGround");
                characterMotor.disableAirControlUntilCollision = false; // out of a grapple launch: steering returns once Death Slam ends
            }
        }

        private LineRenderer? _ring, _groundRing;

        /// <summary>The lock-on cylinder: a ring at Lash's height, and one on the ground below when there is ground in reach.</summary>
        public override void Update()
        {
            base.Update();
            if (_ring == null || characterBody == null) return;
            Vector3 me = characterBody.footPosition;
            Fx.Ring(_ring, me, LashData.DeathSlamRange * MysticExpansion.RadiusMultiplier(characterBody));
            bool ground = Physics.Raycast(me + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && hit.distance > 2f;
            if (_groundRing != null)
            {
                _groundRing.enabled = ground;
                if (ground) Fx.Ring(_groundRing, hit.point, LashData.DeathSlamRange * MysticExpansion.RadiusMultiplier(characterBody));
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;
            // Hover; horizontal momentum carries on, no steering (playtest): grapple over a group, drift above it,
            // but no faster than DeathSlamChannelMaxSpeed (playtest: a grapple launch carried him far off).
            Vector3 v = characterMotor.velocity;
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(v.x, 0f, v.z), LashData.DeathSlamChannelMaxSpeed);
            characterMotor.velocity = new Vector3(flat.x, Mathf.Max(0f, v.y * 0.5f), flat.z);
            StartAimMode(0.5f);

            Ray aim = GetAimRay();
            Vector3 me = characterBody.footPosition;
            var inRange = new HashSet<CharacterBody>();
            float reach = LashData.DeathSlamRange * MysticExpansion.RadiusMultiplier(characterBody);
            TeamMask enemies = TeamMask.GetEnemyTeams(GetTeam());
            for (TeamIndex t = 0; t < TeamIndex.Count; t++)
            {
                if (!enemies.HasTeam(t)) continue;
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(t))
                {
                    CharacterBody b = tc.body;
                    if (b == null || b.healthComponent == null || !b.healthComponent.alive) continue; // bosses too (playtest)
                    Vector3 d = b.corePosition - me;
                    if (d.y <= LashData.DeathSlamHeight && new Vector2(d.x, d.z).magnitude <= reach) inRange.Add(b);
                }
            }
            foreach (CharacterBody b in inRange)
            {
                // In the cylinder, the crosshair picks: lock onto what you keep it near (seen from the camera).
                if (Vector3.Angle(aim.direction, b.corePosition - aim.origin) <= LashData.DeathSlamAimDeg)
                {
                    _inCone[b] = (_inCone.TryGetValue(b, out float s) ? s : 0f) + dt;
                    if (!_locked.Contains(b) && _inCone[b] >= LashData.DeathSlamLockOn && _locked.Count < LashData.DeathSlamMaxTargets) Lock(b);
                }
                else _inCone.Remove(b);
            }
            // A lock holds however far Lash drifts (playtest); only death breaks it.
            foreach (CharacterBody b in _locked.ToArray())
                if (b == null || b.healthComponent == null || !b.healthComponent.alive) Unlock(b);

            // Release early with primary (Deadlock: left mouse) or special again, once something is locked (playtest:
            // a reflex click with nothing locked cancelled it). The gun doesn't fire: only the main state reads it.
            bool early = fixedAge > 0.2f && _locked.Count > 0 && (inputBank.skill1.justPressed || inputBank.skill4.justPressed);
            if (fixedAge < LashData.DeathSlamChannel * DurationExtender.Multiplier(characterBody) && !early) return;
            if (_locked.Count == 0) { DeadlockSounds.Play("lash_ds_miss", transform.position, gameObject); outer.SetNextStateToMain(); return; }
            // Every locked enemy is thrown (playtest: a line-of-sight check here silently cancelled throws whenever
            // terrain was between Lash and his targets; the lock itself already needed them under the crosshair).
            var targets = new List<GameObject>();
            foreach (CharacterBody b in _locked) targets.Add(b.gameObject);
            LashNet.ToServer(new SlamMessage { Phase = 0, Attacker = gameObject, Targets = targets.ToArray() });
            outer.SetNextState(new DeathSlamHold { Targets = targets.ToArray() });
        }

        private void Lock(CharacterBody b)
        {
            _locked.Add(b);
            DeadlockSounds.Play("lash_ds_lockon", transform.position, gameObject);
            DeadlockSounds.Play("lash_ds_lockon_victim", b.corePosition);
            var marker = new Indicator(gameObject, LashSurvivor.TrackingIndicator) { targetTransform = b.mainHurtBox != null ? b.mainHurtBox.transform : b.transform };
            marker.active = true;
            _markers[b] = marker;
        }

        private void Unlock(CharacterBody b)
        {
            _locked.Remove(b);
            if (b != null && _markers.TryGetValue(b, out Indicator m)) { m.active = false; _markers.Remove(b); }
        }

        public override void OnExit()
        {
            foreach (Indicator m in _markers.Values) m.active = false;
            _markers.Clear();
            DeadlockSounds.Stop(_loop);
            if (_ring != null) Object.Destroy(_ring.gameObject);
            if (_groundRing != null) Object.Destroy(_groundRing.gameObject);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>
    /// From the end of the channel through the throw, Lash doesn't move at all (playtest): no drift, no falling, no
    /// movement input (the Body machine runs these states, so the motor would keep the last input's direction).
    /// </summary>
    internal static class DeathSlamFreeze
    {
        public static void Apply(CharacterMotor motor, CharacterDirection? direction)
        {
            if (motor == null) return;
            motor.velocity = Vector3.zero;
            motor.rootMotion = Vector3.zero;
            motor.moveDirection = Vector3.zero;
            if (direction != null) direction.moveVector = Vector3.zero;
        }
    }

    /// <summary>
    /// The locked enemies are pulled up and held; Lash stops dead in the air. Ground Strike or Grapple can cut in
    /// (playtest: waiting felt far too long): the throw then happens at once, so Lash can dive after them.
    /// </summary>
    public class DeathSlamHold : BaseSkillState
    {
        public GameObject[] Targets = System.Array.Empty<GameObject>();
        private bool _thrown;

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority) DeathSlamFreeze.Apply(characterMotor, characterDirection);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            DeathSlamFreeze.Apply(characterMotor, characterDirection);
            StartAimMode(0.5f);
            if (fixedAge < LashData.DeathSlamPull + LashData.DeathSlamHang) return;
            ThrowNow();
            outer.SetNextState(new DeathSlamThrow());
        }

        private LineRenderer? _ring;

        public override void Update()
        {
            base.Update();
            if (!isAuthority || _thrown || characterBody == null) return;
            if (_ring == null) _ring = Fx.Outline("CursedApple_DeathSlamImpact");
            Fx.Ring(_ring, ThrowPoint(), LashData.DeathSlamRadius * MysticExpansion.RadiusMultiplier(characterBody));
        }

        /// <summary>Where the crosshair points, at most throw_m away, brought down to the ground.</summary>
        private Vector3 ThrowPoint()
        {
            Ray aim = GetAimRay();
            Vector3 me = characterBody.footPosition;
            float far = LashData.DeathSlamThrow * MysticExpansion.RadiusMultiplier(characterBody);
            Vector3 target = Physics.Raycast(aim, out RaycastHit hit, far + 20f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                ? hit.point : aim.origin + aim.direction * (far + 6f);
            Vector3 flat = target - me;
            flat.y = 0f;
            if (flat.magnitude > far) target = me + flat.normalized * far + Vector3.up * (target.y - me.y);
            if (Physics.Raycast(target + Vector3.up * 20f, Vector3.down, out RaycastHit ground, 80f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                target = ground.point;
            return target;
        }

        private void ThrowNow()
        {
            if (_thrown) return;
            _thrown = true;
            characterMotor.velocity = Vector3.zero; // the throw stops Lash dead (playtest)
            if (_ring != null) Object.Destroy(_ring.gameObject);
            Vector3 target = ThrowPoint();
            LashNet.ToServer(new SlamMessage { Phase = 1, Attacker = gameObject, Targets = Targets, Point = target, Damage = LashData.DeathSlamDamage * damageStat, Crit = RollCrit() });
        }

        public override void OnExit()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            if (isAuthority && !_thrown)
            {
                ThrowNow(); // interrupted: throw now rather than drop them
                DeadlockSounds.Play("lash_ds_launch", transform.position, gameObject);
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    public class DeathSlamThrow : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            LashModelSwap.Play(gameObject, characterMotor != null && characterMotor.isGrounded ? "ds_throw_ground" : "ds_throw");
            DeadlockSounds.Play("lash_ds_launch", transform.position, gameObject);
            if (isAuthority) DeathSlamFreeze.Apply(characterMotor, characterDirection);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            DeathSlamFreeze.Apply(characterMotor, characterDirection); // dead still while they fly
            if (fixedAge >= 0.4f) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }
}
