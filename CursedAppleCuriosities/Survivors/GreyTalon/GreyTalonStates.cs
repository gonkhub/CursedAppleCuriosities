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
                        info.damage *= GreyTalonData.BowFalloff(hit.distance),
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
    /// Rain of Arrows (Deadlock ability_power_jump): after a 0.2 s crouch, leap up and forward (27.5 m/s at 60 deg), then
    /// glide for 4 s: falling no faster than Deadlock's FallSpeedMax and drifting slowly with movement (user). While it lasts
    /// the bow splits into 5 arrows (FireBow reads GreyTalonRain). Jump ends the glide early (Deadlock: Mantle cancels).
    /// </summary>
    public class RainOfArrows : BaseSkillState
    {
        private bool _launched;
        private float _glide;
        private DeadlockVoice? _hum;

        public override void OnEnter()
        {
            base.OnEnter();
            _glide = GreyTalonData.RainDuration * DurationExtender.Multiplier(characterBody);
            GreyTalonModel.Play(gameObject, "rain_leap");
            DeadlockSounds.Play("greytalon_rain_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("greytalon_vo_power2", gameObject);
            DeadlockFallGuard.Protect(gameObject, GreyTalonData.RainCastDelay + _glide + 4f);
            GreyTalonRain.Begin(gameObject, GreyTalonData.RainCastDelay + _glide);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_launched)
            {
                characterMotor.velocity = Vector3.zero;
                if (fixedAge < GreyTalonData.RainCastDelay) return;
                _launched = true;
                _hum = DeadlockSounds.Loop("greytalon_rain_loop", gameObject, _glide);
                Vector3 fwd = inputBank.aimDirection;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
                float pitch = GreyTalonData.RainPitchDeg * Mathf.Deg2Rad;
                characterMotor.Motor.ForceUnground();
                characterMotor.disableAirControlUntilCollision = false;
                characterMotor.velocity = fwd * (Mathf.Cos(pitch) * GreyTalonData.RainLaunchSpeed) + Vector3.up * (Mathf.Sin(pitch) * GreyTalonData.RainLaunchSpeed);
                return;
            }
            StartAimMode(0.5f);
            // Glide: slow drift with movement, and a capped fall.
            Vector3 v = characterMotor.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            Vector3 wish = inputBank.moveVector;
            wish.y = 0f;
            flat = Vector3.MoveTowards(flat, Vector3.ClampMagnitude(wish, 1f) * GreyTalonData.RainAirSpeed, GreyTalonData.RainAirAccel * Time.fixedDeltaTime);
            characterMotor.velocity = new Vector3(flat.x, Mathf.Max(v.y, -GreyTalonData.RainFallSpeed), flat.z);
            bool landed = fixedAge > GreyTalonData.RainCastDelay + 0.3f && characterMotor.isGrounded;
            bool cancel = inputBank.jump.justPressed;
            if (landed || cancel || fixedAge >= GreyTalonData.RainCastDelay + _glide) outer.SetNextStateToMain();
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
            bool boost = inputBank.rawMoveData.y > 0.5f;
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
