using System.Collections.Generic;
using CursedAppleCuriosities;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Abrams;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Abrams
{
    internal static class Shared
    {
        public static Vector3 FlatAim(Ray aim, Vector3 fallback)
        {
            Vector3 d = aim.direction;
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : fallback;
        }

        /// <summary>Stand still: no drift, no movement input (Body machine states would keep the last input's direction).</summary>
        public static void Plant(CharacterMotor motor, CharacterDirection? direction)
        {
            if (motor == null) return;
            Vector3 v = motor.velocity;
            motor.velocity = new Vector3(0f, Mathf.Min(v.y, 0f), 0f);
            motor.moveDirection = Vector3.zero;
            if (direction != null) direction.moveVector = Vector3.zero;
        }

        /// <summary>Enemies (one hurtbox each) in a sphere, seen from this client: the owner picks, the server hits.</summary>
        public static List<HurtBox> Enemies(Vector3 center, float radius, TeamIndex team)
        {
            return new List<HurtBox>(new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes());
        }

        public static GameObject[] Objects(IEnumerable<HurtBox> boxes)
        {
            var l = new List<GameObject>();
            foreach (HurtBox h in boxes) if (h != null && h.healthComponent != null && !l.Contains(h.healthComponent.gameObject)) l.Add(h.healthComponent.gameObject);
            return l.ToArray();
        }
    }

    // ================= Primary: the shotgun =================

    /// <summary>
    /// Abrams' shotgun (Deadlock: 9 pellets, 0.63 s between shots). No clip or reload: infinite ammo, like Lash.
    /// </summary>
    public class FireShotgun : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = AbramsData.ShotCycle / attackSpeedStat;
            string muzzle = "MuzzleRight";
            Transform? gun = FindModelChild(AbramsSurvivor.MuzzleName);
            if (gun != null && gun.name == AbramsData.MuzzleBone) muzzle = AbramsSurvivor.MuzzleName; // pointed at the gun once Abrams is on
            StartAimMode(2f);
            AbramsModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("abrams_gun_fire", transform.position, gameObject);
            if (AbramsSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(AbramsSurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-1.2f, -1.6f, -0.4f, 0.4f);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                new BulletAttack
                {
                    owner = gameObject,
                    weapon = gameObject,
                    origin = aim.origin,
                    aimVector = aim.direction,
                    bulletCount = (uint)Mathf.Max(1, AbramsData.Pellets),
                    minSpread = 0f,
                    maxSpread = AbramsData.SpreadDeg,
                    spreadPitchScale = 0.7f,
                    damage = AbramsData.PelletDamage * damageStat,
                    procCoefficient = 0.6f,
                    force = 150f,
                    tracerEffectPrefab = AbramsSurvivor.TracerEffect,
                    hitEffectPrefab = AbramsSurvivor.HitEffect,
                    muzzleName = muzzle,
                    isCrit = RollCrit(),
                    radius = 0.3f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.Buckshot, // Deadlock: full to 17 m, 10% by 40 m
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.6f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Siphon Life =================

    /// <summary>
    /// Siphon Life: drain every enemy near Abrams for 4 s and heal for part of it. It runs on its own state machine,
    /// so Abrams keeps moving, shooting, charging and leaping meanwhile (AbramsSiphon does the draining).
    /// </summary>
    public class SiphonLife : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockSounds.Play("abrams_siphon_cast", transform.position, gameObject);
            DeadlockSounds.Play("abrams_vo_power1", transform.position, gameObject);
            if (isAuthority)
            {
                AbramsNet.ToServer(new AbramsSiphonMessage { Attacker = gameObject });
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // ================= Secondary (alt): Heavy Melee =================

    /// <summary>Abrams' heavy melee: wind up while lunging forward, then a big punch in a cone in front of him.</summary>
    public class HeavyMelee : BaseSkillState
    {
        private float _windup;
        private Vector3 _dir;

        public override void OnEnter()
        {
            base.OnEnter();
            _windup = AbramsData.MeleeWindup / attackSpeedStat;
            AbramsModel.Play(gameObject, "melee_start");
            DeadlockSounds.Play("abrams_melee_swing", transform.position, gameObject);
            DeadlockSounds.Play("abrams_vo_melee", transform.position, gameObject);
            _dir = Shared.FlatAim(GetAimRay(), characterDirection.forward);
            StartAimMode(1f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            _dir = Shared.FlatAim(GetAimRay(), _dir);
            characterDirection.forward = _dir;
            characterDirection.moveVector = _dir;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.rootMotion += _dir * AbramsData.MeleeLunge * Time.fixedDeltaTime;
            if (fixedAge >= _windup) outer.SetNextState(new HeavyMeleeHit { Dir = LungeDir() });
        }

        /// <summary>Flat on the ground; in the air the punch can dive or rise a little toward the crosshair.</summary>
        private Vector3 LungeDir()
        {
            if (characterMotor.isGrounded) return _dir;
            Vector3 aim = GetAimRay().direction;
            aim.y = Mathf.Clamp(aim.y, -0.5f, 0.5f);
            return aim.sqrMagnitude > 0.01f ? aim.normalized : _dir;
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    /// <summary>
    /// The punch: Abrams lunges forward and the fist stays live for the whole lunge, so anything he reaches is hit
    /// (each enemy once). The lunge stops at the first enemy it connects with, so he doesn't sail past.
    /// </summary>
    public class HeavyMeleeHit : BaseSkillState
    {
        public Vector3 Dir = Vector3.forward;
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();
        private bool _crit, _connected;

        public override void OnEnter()
        {
            base.OnEnter();
            AbramsModel.Play(gameObject, "melee_hit");
            _crit = RollCrit();
            Swing();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            Swing();
            if (!isAuthority) return;
            if (!_connected && fixedAge < AbramsData.MeleeLungeTime)
            {
                Vector3 flat = new Vector3(Dir.x, 0f, Dir.z);
                if (flat.sqrMagnitude > 0.01f) characterDirection.forward = flat.normalized;
                characterDirection.moveVector = Vector3.zero;
                characterMotor.moveDirection = Vector3.zero;
                characterMotor.velocity = Vector3.zero;
                characterMotor.rootMotion += Dir * AbramsData.MeleeLungeSpeed * Time.fixedDeltaTime;
            }
            else Shared.Plant(characterMotor, characterDirection);
            if (fixedAge >= AbramsData.MeleeLungeTime + AbramsData.MeleeRecover / attackSpeedStat) outer.SetNextStateToMain();
        }

        /// <summary>
        /// Hits enemies whose nearest surface is within reach and roughly in front (along the lunge or the crosshair),
        /// or right up against Abrams. Every client runs it for the effects; only the owner sends the damage.
        /// </summary>
        private void Swing()
        {
            if (fixedAge > AbramsData.MeleeLungeTime + 0.05f) return;
            Vector3 origin = characterBody.corePosition;
            Vector3 aim = GetAimRay().direction;
            float reach = AbramsData.MeleeRange + characterBody.radius;
            var fresh = new List<HurtBox>();
            foreach (HurtBox h in Shared.Enemies(origin, reach, GetTeam()))
            {
                if (h.healthComponent == null || _hit.Contains(h.healthComponent)) continue;
                Vector3 point = Nearest(h, origin);
                Vector3 to = point - origin;
                bool inFront = Vector3.Angle(Dir, to) <= AbramsData.MeleeHalfAngle || Vector3.Angle(aim, to) <= AbramsData.MeleeHalfAngle;
                if (!inFront && to.magnitude > AbramsData.MeleePointBlank + characterBody.radius) continue;
                if (Physics.Linecast(origin, point, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                _hit.Add(h.healthComponent);
                fresh.Add(h);
            }
            if (fresh.Count == 0) return;
            _connected = true;
            foreach (HurtBox h in fresh)
            {
                if (AbramsSurvivor.PunchEffect != null) EffectManager.SimpleImpactEffect(AbramsSurvivor.PunchEffect, h.transform.position, -Dir, false);
                DeadlockSounds.Play("abrams_melee_impact", h.transform.position);
            }
            if (isAuthority)
                AbramsNet.ToServer(new AbramsHitMessage { Kind = AbramsHitKind.Melee, Attacker = gameObject, Targets = Shared.Objects(fresh), Damage = AbramsData.MeleeDamage * damageStat, Crit = _crit, Dir = Dir });
        }

        /// <summary>The closest point of the hurtbox to Abrams (its centre for colliders ClosestPoint can't handle).</summary>
        private static Vector3 Nearest(HurtBox h, Vector3 from)
        {
            Collider c = h.collider;
            if (c == null || (c is MeshCollider mesh && !mesh.convex)) return h.transform.position;
            return c.ClosestPoint(from);
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Dir); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Dir = reader.ReadVector3(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Utility: Shoulder Charge =================

    /// <summary>
    /// Shoulder Charge, as Deadlock plays it: charge for 1.4 s, building from 18.75 to 30 m/s and turning toward the
    /// crosshair at up to 140 deg/s. Enemies touched take damage and are scooped up and carried in front of Abrams; a
    /// wall ends the charge and stuns them. Bosses take the hit and stop the charge. No shooting while charging.
    /// </summary>
    public class ShoulderCharge : BaseSkillState
    {
        private Vector3 _dir;
        private float _speed, _stalledFor;
        private Vector3 _lastPos;
        private bool _wall, _ended;
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();

        public override void OnEnter()
        {
            base.OnEnter();
            AbramsModel.Play(gameObject, "charge_start");
            DeadlockSounds.Play("abrams_charge_cast", transform.position, gameObject);
            DeadlockSounds.Play("abrams_vo_power2", transform.position, gameObject);
            _dir = Shared.FlatAim(GetAimRay(), characterDirection.forward);
            _speed = AbramsData.ChargeSpeed;
            _lastPos = transform.position;
            if (isAuthority) characterMotor.disableAirControlUntilCollision = false;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge % 0.3f < Time.fixedDeltaTime) DeadlockSounds.Play("abrams_charge_step", transform.position, gameObject);
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;

            // Steer toward the crosshair; speed builds up over the charge.
            _dir = Vector3.RotateTowards(_dir, Shared.FlatAim(GetAimRay(), _dir), AbramsData.ChargeTurnDegPerSec * Mathf.Deg2Rad * dt, 0f);
            _dir.y = 0f;
            _dir.Normalize();
            _speed = Mathf.Lerp(AbramsData.ChargeSpeed, AbramsData.ChargeSpeedMax, Mathf.Clamp01(fixedAge / AbramsData.ChargeDuration));
            characterDirection.forward = _dir;
            characterDirection.moveVector = _dir;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.rootMotion += _dir * _speed * dt;
            Vector3 v = characterMotor.velocity;
            characterMotor.velocity = new Vector3(0f, characterMotor.isGrounded ? Mathf.Min(v.y, 0f) : v.y, 0f); // gravity still pulls off ledges

            Contact();
            if (!_ended && HitWall(dt)) End(wall: true);
            if (!_ended && fixedAge >= AbramsData.ChargeDuration) End(wall: false);
            // No shooting mid-charge (playtest); Siphon Life and cutting into Seismic Impact still work.
            if (inputBank.skill2.justPressed && skillLocator.secondary != null) skillLocator.secondary.ExecuteIfReady();
            if (inputBank.skill4.justPressed && skillLocator.special != null) skillLocator.special.ExecuteIfReady();
        }

        /// <summary>Enemies just ahead: scooped up (the server carries them), or a boss that stops the charge.</summary>
        private void Contact()
        {
            var fresh = new List<HurtBox>();
            bool bounced = false;
            foreach (HurtBox h in Shared.Enemies(characterBody.corePosition + _dir * 0.8f, AbramsData.ChargeRadius, GetTeam()))
            {
                if (h.healthComponent == null || !_hit.Add(h.healthComponent)) continue;
                fresh.Add(h);
                if (AbramsNet.TooBigToMove(h.healthComponent.body)) bounced = true;
                DeadlockSounds.Play("abrams_charge_impact", h.transform.position);
                if (AbramsSurvivor.PunchEffect != null) EffectManager.SimpleImpactEffect(AbramsSurvivor.PunchEffect, h.transform.position, -_dir, true);
            }
            if (fresh.Count > 0)
                AbramsNet.ToServer(new AbramsHitMessage
                {
                    Kind = AbramsHitKind.Charge, Attacker = gameObject, Targets = Shared.Objects(fresh), Damage = AbramsData.ChargeDamage * damageStat,
                    Crit = RollCrit(), Dir = _dir, Stun = bounced ? AbramsData.ChargeBumpStun : 0f,
                });
            if (bounced) End(wall: false);
        }

        /// <summary>A steep surface just ahead at chest height, or stopped dead against something for a moment.</summary>
        private bool HitWall(float dt)
        {
            Vector3 chest = characterBody.footPosition + Vector3.up * 1.4f;
            float ahead = characterBody.radius + AbramsData.ChargeWallLookAhead * 0.5f + (_hit.Count > 0 ? AbramsData.ChargeCarryAhead : 0f);
            if (fixedAge > 0.15f && Physics.SphereCast(chest, 0.4f, _dir, out RaycastHit hit, ahead, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && hit.normal.y < 0.6f)
                return true;
            Vector3 moved = transform.position - _lastPos;
            _lastPos = transform.position;
            moved.y = 0f;
            _stalledFor = moved.magnitude < _speed * dt * 0.3f ? _stalledFor + dt : 0f;
            return fixedAge > 0.3f && _stalledFor > 0.2f;
        }

        private void End(bool wall)
        {
            _ended = true;
            _wall = wall;
            outer.SetNextState(new ShoulderChargeEnd { Wall = wall, Dir = _dir });
        }

        public override void OnExit()
        {
            // Whatever ended it (a wall, time, a boss, Seismic Impact), the carried enemies are let go: pinned and
            // stunned against a wall, otherwise flung on a little.
            if (isAuthority)
                AbramsNet.ToServer(new AbramsReleaseMessage
                {
                    Attacker = gameObject,
                    Stun = _wall ? AbramsData.ChargeWallStun : AbramsData.ChargeBumpStun,
                    Fling = _wall ? Vector3.zero : _dir * _speed * 0.5f + Vector3.up * 4f,
                });
            if (isAuthority && !_wall)
            {
                Vector3 v = characterMotor.velocity;
                characterMotor.velocity = _dir * Mathf.Min(_speed, characterBody.moveSpeed * 1.5f) + Vector3.up * v.y; // Deadlock keeps the charge's momentum
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    public class ShoulderChargeEnd : BaseSkillState
    {
        public bool Wall;
        public Vector3 Dir = Vector3.forward;

        public override void OnEnter()
        {
            base.OnEnter();
            AbramsModel.Play(gameObject, "charge_hit");
            if (Wall)
            {
                AbramsModel.Play(gameObject, "charge_wall"); // the impact jolt, layered over the recovery
                Vector3 front = characterBody.corePosition + Dir * (characterBody.radius + 1f);
                DeadlockSounds.Play("abrams_charge_wall", front);
                if (AbramsSurvivor.WallEffect != null) EffectManager.SpawnEffect(AbramsSurvivor.WallEffect, new EffectData { origin = front, scale = 4f }, false);
            }
            if (isAuthority && Wall) Shared.Plant(characterMotor, characterDirection);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (Wall) Shared.Plant(characterMotor, characterDirection);
            if (fixedAge >= (Wall ? 0.4f : 0.2f)) outer.SetNextStateToMain();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Wall); writer.Write(Dir); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Wall = reader.ReadBoolean(); Dir = reader.ReadVector3(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Seismic Impact =================

    /// <summary>
    /// Seismic Impact, as Deadlock plays it: leap up and forward, then hover for up to 3 s (drifting a little with
    /// movement) while a ring marks where the crash will land: the crosshair, at least 30 deg below flat. Press
    /// special or primary again (after 0.5 s), or wait, to crash down.
    /// </summary>
    public class SeismicLeap : BaseSkillState
    {
        private bool _hovering;
        private float _hoverStart;
        private DeadlockVoice? _float;
        private LineRenderer? _ring;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, AbramsData.SlamHover + 6f); // the whole leap + hover + crash
            AbramsModel.Play(gameObject, "leap_launch");
            DeadlockSounds.Play("abrams_slam_cast", transform.position, gameObject);
            DeadlockSounds.Play("abrams_vo_power4", transform.position, gameObject);
            if (!isAuthority) return;
            characterMotor.Motor.ForceUnground();
            characterMotor.disableAirControlUntilCollision = false;
            characterMotor.velocity = Shared.FlatAim(GetAimRay(), characterDirection.forward) * AbramsData.SlamForward + Vector3.up * AbramsData.SlamUp;
            _ring = AbramsFx.Line("CursedApple_SeismicImpact", new Color(1f, 0.45f, 0.2f, 0.85f), 0.12f);
            _ring.loop = true;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!_hovering && (fixedAge > 0.25f && (characterMotor == null || characterMotor.velocity.y <= 0.5f) || fixedAge > AbramsData.SlamBoostTime))
            {
                _hovering = true;
                _hoverStart = fixedAge;
                _float = DeadlockSounds.Loop("abrams_slam_float", gameObject, AbramsData.SlamHover + 0.3f);
            }
            if (!isAuthority) return;
            StartAimMode(0.5f);
            characterMotor.moveDirection = Vector3.zero;
            if (_hovering)
            {
                // Hang in the air; leftover drift dies away, movement input nudges (Deadlock HoverInputSpeedMax).
                float dt = Time.fixedDeltaTime;
                Vector3 v = characterMotor.velocity;
                Vector3 flat = new Vector3(v.x, 0f, v.z) * Mathf.Pow(0.1f, dt); // Deadlock m_flHoverSpeedDecay
                Vector3 wish = inputBank.moveVector;
                wish.y = 0f;
                if (wish.sqrMagnitude > 0.01f) flat = Vector3.MoveTowards(flat, Vector3.ClampMagnitude(wish, 1f) * AbramsData.SlamHoverDrift, AbramsData.SlamHoverAccel * dt);
                characterMotor.velocity = flat;
            }
            if (inputBank.skill2.justPressed && skillLocator.secondary != null) skillLocator.secondary.ExecuteIfReady(); // Siphon Life mid-air (its own machine)
            bool crash = fixedAge >= AbramsData.SlamCrashBuffer && (inputBank.skill4.justPressed || inputBank.skill1.justPressed);
            if (crash || _hovering && fixedAge - _hoverStart >= AbramsData.SlamHover || characterMotor.isGrounded && fixedAge > 0.4f)
                outer.SetNextState(new SeismicCrash { Dir = CrashDir() });
        }

        public override void Update()
        {
            base.Update();
            if (_ring == null || characterBody == null) return;
            Vector3 at = LandingSpot(CrashDir());
            float r = AbramsData.SlamRadius * MysticExpansion.RadiusMultiplier(characterBody);
            const int n = 40;
            _ring.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                _ring.SetPosition(i, at + new Vector3(Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>The crash line: the crosshair, at least SlamMinAimDeg below flat (Deadlock m_flMinAimAngle).</summary>
        private Vector3 CrashDir()
        {
            Vector3 dir = GetAimRay().direction;
            float maxFromDown = 90f - AbramsData.SlamMinAimDeg;
            if (Vector3.Angle(dir, Vector3.down) > maxFromDown)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                flat = flat.sqrMagnitude < 0.01f ? characterDirection.forward : flat.normalized;
                dir = Vector3.RotateTowards(Vector3.down, flat, maxFromDown * Mathf.Deg2Rad, 0f);
            }
            return dir.normalized;
        }

        private Vector3 LandingSpot(Vector3 dir) =>
            Physics.Raycast(characterBody.footPosition + Vector3.up * 0.2f, dir, out RaycastHit hit, 300f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                ? hit.point : characterBody.footPosition + dir * 40f;

        public override void OnExit()
        {
            DeadlockSounds.Stop(_float);
            if (_ring != null) Object.Destroy(_ring.gameObject);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>The crash: straight down the aimed line. Enemies touched on the way ride down with Abrams; a boss stops him on top of it.</summary>
    public class SeismicCrash : BaseSkillState
    {
        public Vector3 Dir = Vector3.down;
        private float _startY;
        private Vector3 _lastPos;
        private readonly HashSet<HealthComponent> _grabbed = new HashSet<HealthComponent>();

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, 5f);
            AbramsModel.Play(gameObject, "leap_dive");
            DeadlockSounds.Play("abrams_slam_descend", transform.position, gameObject);
            if (!isAuthority) return;
            _startY = characterBody.footPosition.y;
            _lastPos = transform.position;
            characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.velocity = Dir * AbramsData.SlamCrashSpeed;
            float moved = (transform.position - _lastPos).magnitude;
            _lastPos = transform.position;
            bool stuck = fixedAge > 0.15f && moved < AbramsData.SlamCrashSpeed * dt * 0.2f; // a wall or roof

            bool onBoss = false;
            var grab = new List<HurtBox>();
            foreach (HurtBox h in Shared.Enemies(characterBody.corePosition + Dir * 0.5f, AbramsData.SlamCollide, GetTeam()))
            {
                if (h.healthComponent == null) continue;
                if (AbramsNet.TooBigToMove(h.healthComponent.body))
                {
                    // Landing on something huge ends the crash on contact instead of glancing off it.
                    if ((h.transform.position - characterBody.corePosition).magnitude < AbramsData.SlamCollide * 0.6f + h.healthComponent.body.radius) onBoss = true;
                    continue;
                }
                if (_grabbed.Add(h.healthComponent)) grab.Add(h);
            }
            if (grab.Count > 0) AbramsNet.ToServer(new AbramsHitMessage { Kind = AbramsHitKind.SlamGrab, Attacker = gameObject, Targets = Shared.Objects(grab), Dir = Dir });

            if (characterMotor.isGrounded && fixedAge > 0.05f || onBoss || stuck || fixedAge > 4f)
                outer.SetNextState(new SeismicLand());
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Dir); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Dir = reader.ReadVector3(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>The impact: everything within the radius takes the damage and is stunned; Abrams stays planted for 0.7 s.</summary>
    public class SeismicLand : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, 1.5f, landed: true); // covers the landing report reaching the server
            AbramsModel.Play(gameObject, "leap_smash");
            Vector3 at = characterBody.footPosition;
            float radius = AbramsData.SlamRadius * MysticExpansion.RadiusMultiplier(characterBody);
            DeadlockSounds.Play("abrams_slam_impact", at);
            if (AbramsSurvivor.SlamEffect != null)
                EffectManager.SpawnEffect(AbramsSurvivor.SlamEffect, new EffectData { origin = at, scale = radius }, false);
            if (!isAuthority) return;
            Shared.Plant(characterMotor, characterDirection);
            AbramsNet.ToServer(new AbramsReleaseMessage { Attacker = gameObject });
            // Deadlock's impact is a cylinder: ImpactRadius wide, ImpactHeight tall.
            var hits = new List<HurtBox>();
            foreach (HurtBox h in Shared.Enemies(at, radius + AbramsData.SlamHeight, GetTeam()))
            {
                if (h.healthComponent == null) continue;
                Vector3 d = h.transform.position - at;
                if (Mathf.Abs(d.y) <= AbramsData.SlamHeight && new Vector2(d.x, d.z).magnitude <= radius + (h.healthComponent.body != null ? h.healthComponent.body.radius : 0f)) hits.Add(h);
            }
            CursedAppleCuriositiesPlugin.Logger?.LogDebug($"[Abrams] Seismic Impact: hit {hits.Count}");
            if (hits.Count > 0)
                AbramsNet.ToServer(new AbramsHitMessage
                {
                    Kind = AbramsHitKind.SlamImpact, Attacker = gameObject, Targets = Shared.Objects(hits), Damage = AbramsData.SlamDamage * damageStat,
                    Crit = RollCrit(), Stun = AbramsData.SlamStun, Dir = Vector3.up,
                });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            Shared.Plant(characterMotor, characterDirection);
            if (fixedAge >= AbramsData.SlamLanding) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}

namespace CursedAppleCuriosities.Survivors.Abrams
{
    internal static class AbramsFx
    {
        /// <summary>A world-space line (tethers, rings) in RoR2's laser-pointer material.</summary>
        public static LineRenderer Line(string name, Color color, float width)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = AbramsSurvivor.IndicatorMaterial;
            if (lr.material != null && lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", color);
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
    }
}
