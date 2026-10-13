using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Viscous;
using RoR2;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Viscous
{
    internal static class Shared
    {
        public static Vector3 Flat(Vector3 d, Vector3 fallback)
        {
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : fallback;
        }

        /// <summary>Enemies (one hurtbox each) in a sphere, seen from this client: the owner picks, the server hits.</summary>
        public static List<HurtBox> Enemies(Vector3 center, float radius, TeamIndex team)
        {
            return new List<HurtBox>(new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes());
        }
    }

    // ================= Primary: goo gun (placeholder) =================

    /// <summary>His gun, plain for now: one bullet every 0.21 s (Deadlock's cycle). Infinite ammo, like every hero here.</summary>
    public class FireGooGun : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = ViscousData.GunCycle / attackSpeedStat;
            string muzzle = "MuzzleRight";
            Transform? gun = FindModelChild(ViscousSurvivor.MuzzleName);
            if (gun != null && gun.name == ViscousData.MuzzleBone) muzzle = ViscousSurvivor.MuzzleName;
            StartAimMode(2f);
            ViscousModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("viscous_gun", transform.position, gameObject);
            if (ViscousSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(ViscousSurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-0.3f, -0.5f, -0.25f, 0.25f);
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
                    maxSpread = ViscousData.GunSpread,
                    damage = ViscousData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 80f,
                    tracerEffectPrefab = ViscousSurvivor.TracerEffect,
                    hitEffectPrefab = ViscousSurvivor.HitEffect,
                    muzzleName = muzzle,
                    isCrit = RollCrit(),
                    radius = ViscousData.GunRadius,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.DefaultBullet,
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.15f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Goo Ball =================

    /// <summary>
    /// Goo Ball's cast (Deadlock AbilityCastDelay 0.55 s): he curls up (rolling_goo), then becomes the ball. He keeps
    /// the way he was going, without steering, while he curls.
    /// </summary>
    public class GooBallCast : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            ViscousModel.Play(gameObject, "ball_cast");
            DeadlockSounds.Play("viscous_ball_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("viscous_vo_power4", gameObject);
            if (isAuthority) characterMotor.disableAirControlUntilCollision = false;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            characterMotor.moveDirection = Vector3.zero;
            if (fixedAge >= ViscousData.BallCastDelay) outer.SetNextState(new GooBall());
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    /// <summary>
    /// Goo Ball, as Deadlock rolls it: the ball always rolls on, turning toward where he looks (110 deg/s on the
    /// ground, 50 in the air). It cruises at 11.4 m/s; holding Primary ("Go Fast") builds to 20.3 m/s at 15 m/s2, and
    /// letting go sheds it slowly (1.3 m/s2). Walls bounce it off with 65% of its speed and 0.1 s without steering.
    /// Jump jumps, once more in the air. Enemies it touches (1.8 m from its centre) take damage, a short stun and a
    /// knock, each at most once every 1.2 s; bosses don't move and bounce the ball back instead. 50% damage resist
    /// while rolling. Special again (after 0.3 s) ends it early; otherwise it lasts 11 s. The owner moves the ball;
    /// damage happens on the server; ViscousBallLook draws it on every client.
    /// </summary>
    public class GooBall : BaseSkillState
    {
        private Vector3 _dir, _lastPos;
        private float _speed, _duration, _steerPause, _stalledFor, _freezeLeft;
        private bool _airJumped, _ending;
        private readonly Dictionary<HealthComponent, float> _nextHit = new Dictionary<HealthComponent, float>();
        private DeadlockVoice? _loop;
        private DeadlockMover? _mover;
        private CameraTargetParams.CameraParamsOverrideHandle _camera;

        private float R => ViscousData.Radius(characterBody); // Mystic Expansion grows the ball (user)
        private Vector3 Center => characterBody.footPosition + Vector3.up * R;

        public override void OnEnter()
        {
            base.OnEnter();
            ViscousModel.Play(gameObject, "ball");
            _duration = ViscousData.BallDuration * DurationExtender.Multiplier(characterBody);
            _mover = GetComponent<DeadlockMover>();
            _loop = DeadlockSounds.Loop("viscous_ball_loop", gameObject, _duration + 1f);
            if (ViscousSurvivor.SplashEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.SplashEffect, new EffectData { origin = characterBody.footPosition, scale = 4f }, false);
            if (!isAuthority) return;
            _dir = Shared.Flat(GetAimRay().direction, characterDirection.forward);
            Vector3 v = characterMotor.velocity;
            _speed = Mathf.Max(ViscousData.BallSpeed, new Vector3(v.x, 0f, v.z).magnitude); // never slower than its roll; a running start carries over
            _lastPos = transform.position;
            characterMotor.disableAirControlUntilCollision = false;
            ViscousNet.ToServer(new ViscousBallMessage { Body = gameObject, Rolling = true, Seconds = _duration });
            if (cameraTargetParams != null)
            {
                // Deadlock pulls the camera up and well back while rolling.
                CharacterCameraParamsData data = cameraTargetParams.currentCameraParamsData;
                Vector3 pos = data.idealLocalCameraPos.value;
                data.idealLocalCameraPos = new Vector3(pos.x, pos.y + 1.2f, pos.z * 1.5f);
                _camera = cameraTargetParams.AddParamsOverride(new CameraTargetParams.CameraParamsOverrideRequest { cameraParamsData = data, priority = 1f }, 0.5f);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (characterBody != null) characterBody.isSprinting = false;
            if (!isAuthority || _ending) return;
            float dt = Time.fixedDeltaTime;
            if (_freezeLeft > 0f)
            {
                // Boss impact (user): the ball stops dead for a moment, then rolls on with the speed and heading it had.
                // The roll's own time doesn't run out during the freeze.
                _freezeLeft -= dt;
                _duration += dt;
                characterMotor.velocity = Vector3.zero;
                characterMotor.moveDirection = Vector3.zero;
                _lastPos = transform.position;
                _stalledFor = 0f;
                return;
            }
            bool grounded = characterMotor.isGrounded;
            if (grounded) _airJumped = false;

            // Jump, and one double jump.
            if (inputBank.jump.justPressed && (grounded || !_airJumped))
            {
                if (!grounded)
                {
                    _airJumped = true;
                    if (ViscousSurvivor.JumpEffect != null)
                        EffectManager.SpawnEffect(ViscousSurvivor.JumpEffect, new EffectData { origin = characterBody.footPosition, scale = 2f }, true);
                }
                EntityStates.CursedApple.Deadlock.DeadlockJump.Fired(characterBody, characterMotor); // jump dust, on-jump items
                characterMotor.Motor.ForceUnground();
                Vector3 jv = characterMotor.velocity;
                characterMotor.velocity = new Vector3(jv.x, grounded ? ViscousData.BallJump : ViscousData.BallAirJump, jv.z);
            }

            // Down-dash: double-tap the slide key in the air (half a stamina pip, a push down), as any Deadlock hero.
            if (_mover != null && _mover.TryDownDash(characterMotor) && ViscousSurvivor.JumpEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.JumpEffect, new EffectData { origin = characterBody.footPosition, scale = 2f }, true);

            // Steer: toward where he looks, or with WASD the way the keys point relative to the camera (user: holding A
            // turns it to the left of where you look). Speed toward cruise, or fast while Primary is held. In the air,
            // a stamina move (the down-dash) steers it harder until it lands, as Deadlock movement does for every hero.
            _steerPause -= dt;
            if (_steerPause <= 0f)
            {
                float air = ViscousData.BallAirTurn * (_mover != null ? _mover.AirControlMult : 1f);
                float turn = (grounded ? ViscousData.BallTurn : air) * Mathf.Deg2Rad * dt;
                Vector3 keys = inputBank.moveVector;
                keys.y = 0f;
                Vector3 want = keys.sqrMagnitude > 0.01f ? keys.normalized : Shared.Flat(GetAimRay().direction, _dir);
                _dir = Shared.Flat(Vector3.RotateTowards(_dir, want, turn, 0f), _dir);
            }
            float target = inputBank.skill1.down ? ViscousData.BallFastSpeed : ViscousData.BallSpeed;
            _speed = _speed < target ? Mathf.Min(target, _speed + ViscousData.BallAccel * dt) : Mathf.Max(target, _speed - ViscousData.BallDecel * dt);

            Bounce(dt);
            characterDirection.forward = _dir;
            characterDirection.moveVector = _dir;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.rootMotion += _dir * _speed * dt;
            Vector3 v = characterMotor.velocity;
            characterMotor.velocity = new Vector3(0f, v.y, 0f); // horizontal is the roll above; gravity and jumps stay

            Contact();
            // Deadlock CastWhileRolling: his other abilities work from inside the ball (Primary stays "Go Fast").
            if (inputBank.skill2.justPressed && skillLocator.secondary != null) skillLocator.secondary.ExecuteIfReady();
            if (inputBank.skill3.justPressed && skillLocator.utility != null) skillLocator.utility.ExecuteIfReady();
            bool cancel =fixedAge >= ViscousData.BallCancelGrace && inputBank.skill4.justPressed;
            if (cancel || fixedAge >= _duration) { _ending = true; outer.SetNextStateToMain(); }
        }

        /// <summary>
        /// A wall just ahead: bounce off it. Checked with a sphere smaller than the ball by 0.45 m, centred on the ball,
        /// so anything under 0.45 m (curbs, steps, rocks) is rolled over instead (Deadlock checks walls 65 hu up).
        /// Stopped dead against something it didn't see counts as a wall straight ahead.
        /// </summary>
        private void Bounce(float dt)
        {
            const float Inset = 0.45f;
            Vector3 center = Center;
            float reach = _speed * dt + Inset + 0.05f;
            if (Physics.SphereCast(center, R - Inset, _dir, out RaycastHit hit, reach, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                && hit.normal.y < 0.6f)
            {
                Vector3 n = Shared.Flat(hit.normal, -_dir);
                if (Vector3.Dot(_dir, n) < 0f) { BounceOff(n, hit.point); _stalledFor = 0f; _lastPos = transform.position; return; }
            }
            Vector3 moved = transform.position - _lastPos;
            _lastPos = transform.position;
            moved.y = 0f;
            _stalledFor = moved.magnitude < _speed * dt * 0.25f ? _stalledFor + dt : 0f;
            if (fixedAge > 0.2f && _stalledFor > 0.12f)
            {
                _stalledFor = 0f;
                BounceOff(-_dir, center + _dir * R);
            }
        }

        private void BounceOff(Vector3 normal, Vector3 point)
        {
            _dir = Shared.Flat(Vector3.Reflect(_dir, normal), -_dir);
            _speed *= ViscousData.BallElasticity;
            _steerPause = ViscousData.BallWallPause;
            ViscousNet.ToServer(new ViscousFxMessage { Kind = ViscousFxKind.Wall, Point = point, Normal = normal });
        }

        /// <summary>
        /// Enemies the ball touches, each once per 1.2 s. Normal enemies: rolled through, stunned and knocked away.
        /// A boss (user): the ball stops dead for a moment (BossFreeze), the boss takes double damage and no stun,
        /// and the ball comes off it as off a wall.
        /// </summary>
        private void Contact()
        {
            Vector3 center = Center;
            float radius = ViscousData.BallHitRadius * MysticExpansion.RadiusMultiplier(characterBody);
            var normal = new List<GameObject>();
            var bosses = new List<GameObject>();
            Vector3 dir = _dir;
            foreach (HurtBox h in Shared.Enemies(center, radius, GetTeam()))
            {
                HealthComponent hc = h.healthComponent;
                if (hc == null || !hc.alive) continue;
                if (_nextHit.TryGetValue(hc, out float at) && fixedAge < at) continue;
                _nextHit[hc] = fixedAge + ViscousData.BallRehit;
                if (!ViscousNet.Immovable(hc.body)) { normal.Add(hc.gameObject); continue; }
                bosses.Add(hc.gameObject);
                Vector3 away = Shared.Flat(center - h.transform.position, -_dir);
                if (Vector3.Dot(_dir, away) < 0f) BounceOff(away, h.transform.position);
            }
            if (normal.Count > 0)
                ViscousNet.ToServer(new ViscousHitMessage
                {
                    Attacker = gameObject, Targets = normal.ToArray(), Damage = ViscousData.BallDamage * damageStat, Crit = RollCrit(), Dir = dir,
                });
            if (bosses.Count > 0)
            {
                _freezeLeft = ViscousData.BossFreeze;
                ViscousNet.ToServer(new ViscousHitMessage
                {
                    Attacker = gameObject, Targets = bosses.ToArray(), Damage = ViscousData.BallDamage * ViscousData.BossDamageMult * damageStat,
                    Crit = RollCrit(), Dir = dir, Boss = true,
                });
            }
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play("viscous_ball_end", transform.position, gameObject);
            if (ViscousSurvivor.SplashEffect != null && characterBody != null)
                EffectManager.SpawnEffect(ViscousSurvivor.SplashEffect, new EffectData { origin = characterBody.footPosition, scale = 4f }, false);
            ViscousModel.Play(gameObject, characterMotor != null && !characterMotor.isGrounded ? "in_air" : "idle");
            if (isAuthority)
            {
                ViscousNet.ToServer(new ViscousBallMessage { Body = gameObject, Rolling = false });
                if (cameraTargetParams != null && _camera.isValid) cameraTargetParams.RemoveParamsOverride(_camera, 0.6f);
                // Deadlock keeps the ball's momentum as he pops out of it.
                Vector3 v = characterMotor.velocity;
                characterMotor.velocity = _dir * Mathf.Min(_speed, characterBody.moveSpeed * 1.5f) + Vector3.up * v.y;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
