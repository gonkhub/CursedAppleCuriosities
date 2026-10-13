using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Seven;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Seven
{
    // ================= Primary: Cold Calculus =================

    /// <summary>
    /// Seven's gun: 3-round bursts (Deadlock 0.084 s between rounds, 0.2625 s between bursts). No clip or reload. A mid-range
    /// electric gun (user): lightning tracers, recoil that climbs through the burst, a gentle falloff past 20 m. While
    /// Power Surge is up, the first bullet of each burst that hits an enemy sends a shock jumping from it (Deadlock:
    /// "occurs once per burst shot").
    /// </summary>
    public class FireBurst : BaseSkillState
    {
        private float _interval, _duration;
        private int _fired;
        private bool _chained;
        private string _muzzle = "MuzzleRight";

        public override void OnEnter()
        {
            base.OnEnter();
            _interval = SevenData.BurstInterval / attackSpeedStat;
            _duration = (SevenData.BurstInterval * (SevenData.BurstCount - 1) + SevenData.BurstCycle) / attackSpeedStat;
            Transform? gun = FindModelChild(SevenSurvivor.MuzzleName);
            if (gun != null && gun.name == SevenData.MuzzleBone) _muzzle = SevenSurvivor.MuzzleName; // pointed at the gun once Seven is on
            StartAimMode(2f);
            Fire();
        }

        private void Fire()
        {
            _fired++;
            SevenModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("seven_gun_fire", transform.position, gameObject); // falls back to Commando's shot without Deadlock
            if (SevenSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(SevenSurvivor.MuzzleEffect, gameObject, _muzzle, false);
            // Climbing recoil (user, Deadlock's vertical recoil): each round of the burst kicks harder than the last
            float kick = SevenData.GunRecoilKick * _fired;
            AddRecoil(-kick, -kick * 1.3f, -0.15f, 0.15f);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                bool crit = RollCrit();
                bool surging = SevenSurvivor.SurgeBuff != null && characterBody.HasBuff(SevenSurvivor.SurgeBuff);
                var bullet = new BulletAttack
                {
                    owner = gameObject,
                    weapon = gameObject,
                    origin = aim.origin,
                    aimVector = aim.direction,
                    minSpread = 0f,
                    maxSpread = characterBody.spreadBloomAngle * SevenData.GunSpread,
                    damage = SevenData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 120f,
                    tracerEffectPrefab = SevenSurvivor.TracerEffect,
                    hitEffectPrefab = SevenSurvivor.HitEffect,
                    muzzleName = _muzzle,
                    isCrit = crit,
                    radius = 0.1f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.None,
                    // Mid range (user): full damage to 20 m, easing to 60% by 57.5 m
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) => info.damage *= SevenData.GunFalloff(hit.distance),
                    damageType = DamageSource.Primary,
                };
                if (surging && !_chained)
                    bullet.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
                    {
                        bool result = BulletAttack.defaultHitCallback(attack, ref hit);
                        if (!_chained && hit.hitHurtBox != null && hit.hitHurtBox.healthComponent != null && hit.hitHurtBox.teamIndex != GetTeam())
                        {
                            _chained = true;
                            SevenNet.ToServer(new SevenChainMessage { Attacker = gameObject, Target = hit.hitHurtBox.healthComponent.gameObject, Crit = crit });
                        }
                        return result;
                    };
                bullet.Fire();
            }
            characterBody.AddSpreadBloom(SevenData.GunBloomPerShot);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            while (_fired < SevenData.BurstCount && fixedAge >= _fired * _interval) Fire();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Lightning Ball =================

    /// <summary>Lightning Ball: a slow orb of lightning down the crosshair that shocks everything near it (SevenBall).</summary>
    public class LightningBall : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            SevenModel.Play(gameObject, characterMotor != null && !characterMotor.isGrounded ? "ball_air" : "ball");
            DeadlockSounds.Play("seven_ball_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("seven_vo_power1", gameObject);
            StartAimMode(1f);
            if (isAuthority && SevenSurvivor.BallProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = SevenSurvivor.BallProjectile,
                    position = aim.origin + aim.direction * 1.5f,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = SevenData.BallSpeed,
                });
            }
            if (isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary (alt): Static Charge =================

    /// <summary>
    /// Static Charge: lock onto the enemy nearest the crosshair (the reticle shows it while the skill is ready) and charge
    /// them; the charge follows them, and 3.5 s later stuns and damages everything around them. It must attach to an
    /// enemy (user): no target, no cast and no cooldown.
    /// </summary>
    public class StaticCharge : BaseSkillState
    {
        /// <summary>The enemy Static Charge would lock onto right now (the reticle and the cast use the same pick).</summary>
        public static CharacterBody? Target(CharacterBody body) =>
            EquipmentTargeting.FindEnemy(body, SevenData.StaticRange * MysticExpansion.RadiusMultiplier(body), SevenData.StaticConeDeg, _ => true);

        public override void OnEnter()
        {
            base.OnEnter();
            if (!isAuthority) return;
            CharacterBody? target = Target(characterBody);
            if (target == null)
            {
                activatorSkillSlot?.AddOneStock(); // refund
                outer.SetNextStateToMain();
                return;
            }
            SevenModel.Play(gameObject, "static");
            DeadlockSounds.Play("seven_static_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("seven_vo_power2", gameObject);
            StartAimMode(1f);
            if (SevenSurvivor.StrikeEffect != null) EffectManager.SimpleImpactEffect(SevenSurvivor.StrikeEffect, target.corePosition, -GetAimRay().direction, true);
            SevenNet.ToServer(new SevenStaticMessage { Attacker = gameObject, Target = target.gameObject, Point = target.corePosition, Crit = RollCrit() });
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Power Surge =================

    /// <summary>Power Surge: 10 s of chain-lightning bursts and extra move speed (Deadlock's T2, given free). Runs alongside everything.</summary>
    public class PowerSurge : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            SevenModel.Play(gameObject, "surge");
            DeadlockSounds.Play("seven_surge_start", transform.position, gameObject);
            DeadlockHeroVoice.Power("seven_vo_power3", gameObject);
            if (isAuthority)
            {
                SevenNet.ToServer(new SevenBuffMessage { Attacker = gameObject, Kind = SevenBuffKind.Surge });
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // ================= Special: Storm Cloud =================

    /// <summary>
    /// Storm Cloud, as Deadlock plays it now: Seven rises a few metres and hangs there for 7 s, drifting slowly with
    /// movement, while the storm around him grows (here: to the whole map, user) and shocks every enemy he can see.
    /// Primary once marks a Lightning Strike; special again ends it early. No gun while channeling.
    /// </summary>
    public class StormCloud : BaseSkillState
    {
        private bool _struck;
        private float _riseFrom;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockFallGuard.Protect(gameObject, SevenData.StormChannel + 4f);
            SevenModel.Play(gameObject, "storm_start");
            DeadlockSounds.Play("seven_storm_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("seven_vo_power4", gameObject);
            if (!isAuthority) return;
            _riseFrom = transform.position.y;
            characterMotor.Motor.ForceUnground();
            characterMotor.disableAirControlUntilCollision = false;
            SevenNet.ToServer(new SevenBuffMessage { Attacker = gameObject, Kind = SevenBuffKind.StormStart });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            StartAimMode(0.5f);
            characterMotor.moveDirection = Vector3.zero;
            // Rise to cloud height, then keep climbing slowly (user): more of the growing storm stays in sight.
            float up = fixedAge < SevenData.StormRiseTime && transform.position.y < _riseFrom + SevenData.StormRise
                ? SevenData.StormRise / SevenData.StormRiseTime : SevenData.StormClimb;
            // Deadlock m_flAirDrag: leftover momentum dies fast; movement drifts at FlightControlEnabled.
            Vector3 v = characterMotor.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z) * Mathf.Pow(0.1f, Time.fixedDeltaTime * 3f);
            Vector3 wish = inputBank.moveVector;
            wish.y = 0f;
            if (wish.sqrMagnitude > 0.01f) flat = Vector3.MoveTowards(flat, Vector3.ClampMagnitude(wish, 1f) * SevenData.StormDrift, 10f * Time.fixedDeltaTime);
            characterMotor.velocity = flat + Vector3.up * up;

            if (!_struck && inputBank.skill1.justPressed)
            {
                _struck = true;
                Ray aim = GetAimRay();
                Vector3 point = Physics.Raycast(aim, out RaycastHit hit, SevenData.StrikeRange, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore)
                    ? hit.point : aim.GetPoint(SevenData.StrikeRange);
                SevenNet.ToServer(new SevenStrikeMessage { Attacker = gameObject, Point = point, Crit = RollCrit() });
            }
            bool endEarly = fixedAge > 0.5f && inputBank.skill4.justPressed;
            if (endEarly || fixedAge >= SevenData.StormRiseTime + SevenData.StormChannel * CursedAppleCuriosities.Items.DurationExtender.Multiplier(characterBody)) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            SevenModel.Play(gameObject, "storm_end");
            DeadlockSounds.Play("seven_storm_end", transform.position, gameObject);
            if (isAuthority) SevenNet.ToServer(new SevenBuffMessage { Attacker = gameObject, Kind = SevenBuffKind.StormStop });
            DeadlockFallGuard.ProtectNextLanding(gameObject, 6f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
