using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Ivy;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Ivy
{
    // ================= Primary: Protector's Hand =================

    /// <summary>Protector's Hand (Deadlock citadel_weapon_tengu_set): full-auto, a bullet every 0.0735 s, falloff 20 -> 57.5 m.</summary>
    public class FireIvyGun : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = IvyData.GunCycle / attackSpeedStat;
            Transform? gun = FindModelChild(IvySurvivor.MuzzleName);
            string muzzle = gun != null && gun.name == IvyData.MuzzleBone ? IvySurvivor.MuzzleName : "MuzzleRight";
            StartAimMode(2f);
            IvyModel.Play(gameObject, "shoot");
            IvyGunAudio.Shot(gameObject);
            if (IvySurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(IvySurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-0.2f, -0.4f, -0.2f, 0.2f);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                new BulletAttack
                {
                    owner = gameObject, weapon = gameObject, origin = aim.origin, aimVector = aim.direction,
                    minSpread = 0f, maxSpread = IvyData.GunSpread, damage = IvyData.GunDamage * damageStat, procCoefficient = 1f, force = 30f,
                    tracerEffectPrefab = IvySurvivor.TracerEffect, hitEffectPrefab = IvySurvivor.HitEffect, muzzleName = muzzle, isCrit = RollCrit(),
                    radius = 0.12f, smartCollision = true, falloffModel = BulletAttack.FalloffModel.None,
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                        info.damage *= Kit.Falloff(hit.distance, IvyData.GunFalloffStart, IvyData.GunFalloffEnd, IvyData.GunFalloffMin),
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.1f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Entangling Thorns =================

    /// <summary>Entangling Thorns (Deadlock citadel_ability_tengu_urn): toss a seed in an arc; a thorn patch grows where it lands (IvySeed).</summary>
    public class ThrowThorns : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            IvyModel.Play(gameObject, "cast");
            DeadlockHeroVoice.Power("ivy_vo_power1", gameObject);
            StartAimMode(1f);
            if (isAuthority && IvySurvivor.SeedProjectile != null)
            {
                Ray aim = GetAimRay();
                Vector3 right = Vector3.Cross(Vector3.up, aim.direction);
                Vector3 dir = right.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(-IvyData.ThornsThrowUp, right.normalized) * aim.direction : aim.direction;
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = IvySurvivor.SeedProjectile, position = aim.origin + aim.direction, rotation = Quaternion.LookRotation(dir),
                    owner = gameObject, damage = damageStat, crit = RollCrit(), force = 0f, speedOverride = IvyData.ThornsThrowSpeed,
                });
            }
            if (isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Stone Form =================

    /// <summary>
    /// Stone Form (Deadlock citadel_ability_tengu_stone_form): she turns to stone (invulnerable, healing), rises a little if she's on the
    /// ground, then slams straight down, steering a bit; the impact hurts and stuns everything around (5.75 m). She stays stone a moment
    /// after landing, then breaks out.
    /// </summary>
    public class StoneForm : BaseSkillState
    {
        private bool _lifting, _landed;
        private float _landedAt;

        public override void OnEnter()
        {
            base.OnEnter();
            _lifting = characterMotor != null && characterMotor.isGrounded;
            IvyModel.Play(gameObject, _lifting ? "stone" : "stone_air");
            DeadlockSounds.Play("ivy_stone_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("ivy_vo_power3", gameObject);
            if (!isAuthority) return;
            IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.Start });
            if (_lifting && characterMotor != null) characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            if (_landed)
            {
                characterMotor.velocity = Vector3.zero;
                if (fixedAge - _landedAt >= IvyData.StoneHoldAfterLanding) outer.SetNextStateToMain();
                return;
            }
            if (_lifting && fixedAge < IvyData.StoneLiftTime)
            {
                characterMotor.velocity = Vector3.up * (IvyData.StoneLift / IvyData.StoneLiftTime);
                return;
            }
            Vector3 steer = inputBank != null ? inputBank.moveVector * IvyData.StoneSteer : Vector3.zero;
            characterMotor.velocity = new Vector3(steer.x, -IvyData.StoneSlamSpeed, steer.z);
            if ((characterMotor.isGrounded && fixedAge > 0.1f) || fixedAge >= IvyData.StoneMaxSeconds) Land();
        }

        private void Land()
        {
            _landed = true;
            _landedAt = fixedAge;
            IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.Slam, At = characterBody.footPosition, Crit = RollCrit() });
        }

        public override void OnExit()
        {
            if (isAuthority) IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.End });
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    // ================= Special: Air Drop =================

    /// <summary>
    /// Air Drop (Deadlock citadel_ability_tengu_airlift), alone: she takes off carrying a bomb and flies where she looks (10 m/s, 22 m/s
    /// holding forward), for up to 8 s. Special or primary drops it (the timer does too): it explodes where it lands (16 m) and she
    /// deals 20% more damage for 8 s. Then she floats down on her wings.
    /// </summary>
    public class AirDrop : BaseSkillState
    {
        private Vector3 _vel;
        private float _limit;
        private bool _dropped;
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            IvyModel.Play(gameObject, "fly_start");
            DeadlockSounds.Play("ivy_airdrop_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("ivy_vo_power4", gameObject);
            _loop = DeadlockSounds.Loop("ivy_airdrop_loop", gameObject, IvyData.AirDropSeconds + 1f);
            _limit = IvyData.AirDropSeconds * DurationExtender.Multiplier(characterBody);
            if (isAuthority && characterMotor != null)
            {
                characterMotor.Motor.ForceUnground();
                _vel = characterMotor.velocity + Vector3.up * IvyData.FlyClimb;
                characterMotor.disableAirControlUntilCollision = false;
            }
            if (UnityEngine.Networking.NetworkServer.active) DeadlockFallGuard.ProtectNextLanding(gameObject, _limit + 6f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!isAuthority || characterMotor == null || inputBank == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 look = inputBank.aimDirection;
            // Level-ish flight: she climbs or dives with the camera, but not straight up or down.
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 30f);
            Vector3 flat = new Vector3(look.x, 0f, look.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
            Vector3 dir = flat * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
            bool fast = inputBank.moveVector.sqrMagnitude > 0.1f && Vector3.Dot(inputBank.moveVector.normalized, flat) > 0.5f;
            Vector3 want = dir * ((fast ? IvyData.FlyFastSpeed : IvyData.FlySpeed) * Mathf.Max(1f, characterBody.moveSpeed / characterBody.baseMoveSpeed));
            _vel = Vector3.MoveTowards(_vel, want, IvyData.FlyAccel * 4f * dt);
            characterMotor.velocity = _vel;
            characterDirection.forward = flat;
            bool press = fixedAge > 0.3f && (inputBank.skill4.justPressed || inputBank.skill1.justPressed);
            if (!_dropped && (press || fixedAge >= _limit)) Drop();
            if (_dropped && fixedAge > 0.1f) outer.SetNextStateToMain();
        }

        private void Drop()
        {
            _dropped = true;
            IvyModel.Play(gameObject, "fly_drop");
            Vector3 v = new Vector3(_vel.x, 0f, _vel.z) * 0.6f + Vector3.down * IvyData.BombSpeed * 0.3f;
            IvyNet.ToServer(new IvyBombMessage { Ivy = gameObject, At = characterBody.footPosition + Vector3.down * 0.5f, Velocity = v, Crit = RollCrit() });
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play("ivy_airdrop_end", transform.position, gameObject);
            if (isAuthority && !_dropped) Drop(); // interrupted: the bomb still falls
            if (isAuthority && characterMotor != null) characterMotor.velocity = new Vector3(_vel.x * 0.5f, Mathf.Min(_vel.y, 2f), _vel.z * 0.5f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
