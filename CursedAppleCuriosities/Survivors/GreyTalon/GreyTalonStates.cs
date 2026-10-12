using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.GreyTalon;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.GreyTalon
{
    // ================= Primary: the bow =================

    /// <summary>
    /// Grey Talon's bow: one slow, very long range hitscan arrow (user). Damage eases off past the falloff start (user: the
    /// primary only). During Rain of Arrows it splits into a fan of 5 (Deadlock BulletSplitShot), each a little stronger.
    /// </summary>
    public class FireBow : BaseSkillState
    {
        private float _duration;
        private string _muzzle = "MuzzleLeft";

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = GreyTalonData.BowCycle / attackSpeedStat;
            Transform? bow = FindModelChild(GreyTalonSurvivor.MuzzleName);
            if (bow != null && bow.name == GreyTalonData.MuzzleBone) _muzzle = GreyTalonSurvivor.MuzzleName; // the bow once his model is on
            bool rain = GreyTalonRain.Active(gameObject);
            GreyTalonModel.Play(gameObject, rain ? "rain_fire" : "shoot");
            DeadlockSounds.Play(rain ? "greytalon_rain_fire" : "greytalon_bow_fire", transform.position, gameObject);
            if (GreyTalonSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(GreyTalonSurvivor.MuzzleEffect, gameObject, _muzzle, false);
            AddRecoil(-0.6f, -1f, -0.2f, 0.2f);
            StartAimMode(2f);
            if (!isAuthority) return;

            Ray aim = GetAimRay();
            bool crit = RollCrit();
            int arrows = rain ? GreyTalonData.RainArrows : 1;
            float damage = GreyTalonData.BowDamage * damageStat * (rain ? 1f + GreyTalonData.RainDamageBonus : 1f);
            // The fan spreads sideways across the view.
            Vector3 right = Vector3.Cross(Vector3.up, aim.direction);
            Vector3 fanAxis = right.sqrMagnitude > 1e-4f ? Vector3.Cross(aim.direction, right.normalized).normalized : Vector3.up;
            // One shot's arrows: the first to hit an enemy deals full damage, every further one on it half (user).
            var struck = new System.Collections.Generic.HashSet<HealthComponent>();
            for (int i = 0; i < arrows; i++)
            {
                float angle = (i - (arrows - 1) * 0.5f) * GreyTalonData.RainFanDeg;
                new BulletAttack
                {
                    owner = gameObject,
                    weapon = gameObject,
                    origin = aim.origin,
                    aimVector = Quaternion.AngleAxis(angle, fanAxis) * aim.direction,
                    minSpread = 0f,
                    maxSpread = 0f,
                    damage = damage,
                    procCoefficient = 1f,
                    force = GreyTalonData.BowForce,
                    tracerEffectPrefab = GreyTalonSurvivor.TracerEffect,
                    hitEffectPrefab = GreyTalonSurvivor.HitEffect,
                    muzzleName = _muzzle,
                    isCrit = crit,
                    radius = GreyTalonData.BowRadius,
                    maxDistance = GreyTalonData.BowRange,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.None,
                    // Falloff by distance (user): full damage across about half a map, easing to half.
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                    {
                        info.damage *= GreyTalonData.BowFalloff(hit.distance);
                        HealthComponent? hc = hit.hitHurtBox != null ? hit.hitHurtBox.healthComponent : null;
                        if (arrows > 1 && hc != null && !struck.Add(hc)) info.damage *= GreyTalonData.RainRepeatHit;
                    },
                    damageType = DamageSource.Primary,
                }.Fire();
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Charged Shot =================

    /// <summary>
    /// Charged Shot (Deadlock ability_charged_shot): draw for 0.5 s, then hold it as long as Secondary or Primary is held
    /// (Deadlock: "Hold Ability1 or Attack to hold the shot"), walking slowly with the camera zoomed over his shoulder.
    /// Letting go looses a large, fast arrow that pierces every enemy on its line (GreyTalonChargedArrow) for 800% (user).
    /// </summary>
    public class ChargedShot : BaseSkillState
    {
        private float _drawTime;
        private bool _drawn, _fired;
        private CameraTargetParams.CameraParamsOverrideHandle _zoom;
        private DeadlockVoice? _hum;

        public override void OnEnter()
        {
            base.OnEnter();
            _drawTime = GreyTalonData.ChargeTime / attackSpeedStat;
            GreyTalonModel.Play(gameObject, characterMotor != null && !characterMotor.isGrounded ? "charge_air" : "charge_draw");
            DeadlockSounds.Play("greytalon_charge_start", transform.position, gameObject);
            DeadlockSounds.Play("greytalon_zoom_in", transform.position, gameObject);
            _hum = DeadlockSounds.Loop("greytalon_charge_loop", gameObject, 60f);
            DeadlockHeroVoice.Power("greytalon_vo_power1", gameObject);
            if (!isAuthority) return;
            GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.ChargeStart });
            if (cameraTargetParams != null)
            {
                CharacterCameraParamsData data = cameraTargetParams.currentCameraParamsData;
                data.fov = GreyTalonData.ChargedFov;
                Vector3 pos = data.idealLocalCameraPos.value;
                data.idealLocalCameraPos = new Vector3(pos.x + GreyTalonData.ChargedCamRight, pos.y + GreyTalonData.ChargedCamUp, pos.z * 0.6f);
                _zoom = cameraTargetParams.AddParamsOverride(new CameraTargetParams.CameraParamsOverrideRequest { cameraParamsData = data, priority = 1f }, 0.2f);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (characterBody != null) characterBody.isSprinting = false;
            if (!isAuthority || _fired) return;
            if (!_drawn && fixedAge >= _drawTime) _drawn = true;
            bool holding = inputBank.skill2.down || inputBank.skill1.down;
            if (_drawn && !holding) Fire();
        }

        private void Fire()
        {
            _fired = true;
            GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.ChargeFire });
            if (GreyTalonSurvivor.ChargedProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = GreyTalonSurvivor.ChargedProjectile,
                    position = aim.origin,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat * GreyTalonData.ChargedDamage,
                    crit = RollCrit(),
                    force = 1500f,
                    speedOverride = GreyTalonData.ChargedSpeed,
                    damageTypeOverride = DamageSource.Secondary,
                });
            }
            AddRecoil(-2f, -3f, -0.5f, 0.5f);
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_hum);
            if (_fired)
            {
                GreyTalonModel.Play(gameObject, "charge_release");
                DeadlockSounds.Play("greytalon_charge_fire", transform.position, gameObject);
                if (GreyTalonSurvivor.ChargedMuzzleEffect != null) EffectManager.SimpleMuzzleFlash(GreyTalonSurvivor.ChargedMuzzleEffect, gameObject, GreyTalonSurvivor.MuzzleName, false);
            }
            DeadlockSounds.Play("greytalon_zoom_out", transform.position, gameObject);
            if (isAuthority)
            {
                if (cameraTargetParams != null && _zoom.isValid) cameraTargetParams.RemoveParamsOverride(_zoom, 0.25f);
                if (!_fired) GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.ChargeFire }); // interrupted: the buff goes
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Rain of Arrows =================

    /// <summary>
    /// Rain of Arrows (Deadlock ability_power_jump): after a 0.2 s crouch, a fast leap up Deadlock's 60 deg line that stops
    /// dead at the glide height (user), then a 4 s glide: falling no faster than Deadlock's FallSpeedMax and drifting slowly
    /// with movement. While it lasts the bow splits into 5 arrows (FireBow reads GreyTalonRain). Jump ends it early
    /// (Deadlock: Mantle cancels); cancelled on the way up he keeps the leap's speed, so it doubles as a movement skill.
    /// </summary>
    public class RainOfArrows : BaseSkillState
    {
        private enum Phase { Crouch, Rise, Glide }
        private Phase _phase;
        private float _glide, _riseFrom, _riseAge, _glideAt;
        private Vector3 _launch;
        private DeadlockVoice? _hum;

        public override void OnEnter()
        {
            base.OnEnter();
            _glide = GreyTalonData.RainDuration * DurationExtender.Multiplier(characterBody);
            GreyTalonModel.Play(gameObject, "rain_leap");
            DeadlockSounds.Play("greytalon_rain_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("greytalon_vo_power2", gameObject);
            DeadlockFallGuard.Protect(gameObject, GreyTalonData.RainCastDelay + 1f + _glide + 4f);
            GreyTalonRain.Begin(gameObject, GreyTalonData.RainCastDelay + 1f + _glide);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;
            switch (_phase)
            {
                case Phase.Crouch:
                    characterMotor.velocity = Vector3.zero;
                    if (fixedAge < GreyTalonData.RainCastDelay) return;
                    _phase = Phase.Rise;
                    _riseFrom = transform.position.y;
                    _hum = DeadlockSounds.Loop("greytalon_rain_loop", gameObject, 1f + _glide);
                    Vector3 fwd = inputBank.aimDirection;
                    fwd.y = 0f;
                    fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
                    float pitch = GreyTalonData.RainPitchDeg * Mathf.Deg2Rad;
                    _launch = fwd * (Mathf.Cos(pitch) * GreyTalonData.RainLaunchSpeed) + Vector3.up * (Mathf.Sin(pitch) * GreyTalonData.RainLaunchSpeed);
                    characterMotor.Motor.ForceUnground();
                    characterMotor.disableAirControlUntilCollision = false;
                    characterMotor.velocity = _launch;
                    return;

                case Phase.Rise:
                    _riseAge += dt;
                    if (Cancelled()) return; // keeps the leap's speed
                    // Held on the leap's line at full speed (no gravity) until the glide height; a ceiling ends it early.
                    bool high = transform.position.y - _riseFrom >= GreyTalonData.RainHeight;
                    bool blocked = _riseAge > 0.1f && characterMotor.velocity.y < _launch.y * 0.5f;
                    if (high || blocked || _riseAge > 1f)
                    {
                        _phase = Phase.Glide;
                        _glideAt = fixedAge;
                        characterMotor.velocity = Vector3.zero; // stopped dead at the top (user)
                        GreyTalonRain.Begin(gameObject, _glide);
                        return;
                    }
                    characterMotor.velocity = _launch;
                    break;

                case Phase.Glide:
                    // The bow (5 arrows now) and Charged Shot: this runs on the Body machine in place of the main state,
                    // which is what normally reads skill buttons, so they are passed through by hand.
                    if (inputBank.skill1.down) skillLocator.primary?.ExecuteIfReady();
                    if (inputBank.skill2.justPressed) skillLocator.secondary?.ExecuteIfReady();
                    // Slow drift with movement, and a capped fall.
                    Vector3 v = characterMotor.velocity;
                    Vector3 flat = new Vector3(v.x, 0f, v.z);
                    Vector3 wish = inputBank.moveVector;
                    wish.y = 0f;
                    flat = Vector3.MoveTowards(flat, Vector3.ClampMagnitude(wish, 1f) * GreyTalonData.RainAirSpeed, GreyTalonData.RainAirAccel * dt);
                    characterMotor.velocity = new Vector3(flat.x, Mathf.Max(v.y, -GreyTalonData.RainFallSpeed), flat.z);
                    if (Cancelled()) return;
                    if (characterMotor.isGrounded || fixedAge - _glideAt >= _glide) outer.SetNextStateToMain();
                    break;
            }
            StartAimMode(0.5f);
        }

        /// <summary>Jump ends Rain of Arrows; the press is used up, so it doesn't also air-jump (which would reset his rise).</summary>
        private bool Cancelled()
        {
            if (!inputBank.jump.justPressed) return false;
            var mover = GetComponent<DeadlockMover>();
            if (mover != null) mover.SuppressJumpUntil = Time.time + 0.25f;
            outer.SetNextStateToMain();
            return true;
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_hum);
            DeadlockSounds.Play("greytalon_rain_end", transform.position, gameObject);
            GreyTalonRain.End(gameObject);
            DeadlockFallGuard.ProtectNextLanding(gameObject, 6f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Guided Owl =================

    /// <summary>
    /// Guided Owl (Deadlock ability_guided_arrow): a 1.5 s cast, standing still, then his owl flies and he steers it for up
    /// to 20 s: it turns toward where he looks, holding forward speeds it up, it bursts on whatever it touches (12 m, 1200%,
    /// a short stun). The camera rides behind the owl. Jump releases control (the owl flies on straight). He can't be
    /// hurt or targeted from the cast until the owl is gone or released (user). Each boss the owl kills makes it stronger.
    /// </summary>
    public class GuidedOwl : BaseSkillState
    {
        private GreyTalonOwl? _owl;
        private bool _launched, _released, _ended;

        public override void OnEnter()
        {
            base.OnEnter();
            GreyTalonModel.Play(gameObject, "owl_intro");
            DeadlockSounds.Play("greytalon_owl_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("greytalon_vo_power4", gameObject);
            DeadlockFallGuard.Protect(gameObject, GreyTalonData.OwlCastDelay + GreyTalonData.OwlFlight + 2f);
            if (isAuthority) GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.OwlStart });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            // Deadlock ChannelMoveSpeed 0: he stands (or hangs) where he is.
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.velocity = new Vector3(0f, Mathf.Max(characterMotor.velocity.y, -2f), 0f);
            if (!_launched)
            {
                if (fixedAge < GreyTalonData.OwlCastDelay) return;
                _launched = true;
                GreyTalonModel.Play(gameObject, "owl_fire");
                GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.OwlLaunch });
                Ray aim = GetAimRay();
                _owl = GreyTalonOwl.Launch(characterBody, aim.origin + aim.direction * 1.5f + Vector3.up * 0.3f, aim.direction, RollCrit());
                return;
            }
            if (_owl == null || _owl.Done) { outer.SetNextStateToMain(); return; }
            if (!_released && inputBank.jump.justPressed)
            {
                _released = true;
                _owl.Release(); // flies on straight; he gets control back
                outer.SetNextStateToMain();
                return;
            }
            // Steering input: where he looks, and forward held to speed it up.
            Vector3 look = _owl.LookDirection(characterBody);
            bool boost = inputBank.rawMoveData.y > 0.5f || Vector3.Dot(inputBank.moveVector, look) > 0.5f;
            _owl.Steer(look, boost);
        }

        public override void OnExit()
        {
            if (!_ended)
            {
                _ended = true;
                GreyTalonModel.Play(gameObject, "owl_outro");
                if (isAuthority)
                {
                    if (_owl != null && !_owl.Done && !_released) _owl.Release(); // interrupted (frozen, killed): the owl flies on
                    GreyTalonNet.ToServer(new GreyTalonBuffMessage { Attacker = gameObject, Kind = GreyTalonBuffKind.OwlStop });
                }
            }
            DeadlockFallGuard.ProtectNextLanding(gameObject, 4f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
