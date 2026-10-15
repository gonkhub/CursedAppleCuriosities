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

    // ================= Secondary: Kudzu Bomb =================

    /// <summary>
    /// Kudzu Bomb (Deadlock citadel_ability_tengu_urn): hold to aim, with the arc and the patch it will grow previewed (Deadlock's
    /// generic_arc and stone squall preview; user 2.5); release to throw. The bomb flies at 1800 hu/s with a little loft (10 deg)
    /// and grows a kudzu patch where it lands (IvySeed).
    /// </summary>
    public class ThrowThorns : BaseSkillState
    {
        private IvyThrowPreview? _preview;
        private DeadlockVoice? _aimLoop;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockSounds.Play("ivy_bomb_select", transform.position, gameObject);
            _aimLoop = DeadlockSounds.Loop("ivy_bomb_aim_loop", gameObject, 30f);
            if (isAuthority && characterBody.isPlayerControlled)
                _preview = new IvyThrowPreview(IvyData.ThornsRadius * MysticExpansion.RadiusMultiplier(characterBody), showArc: true);
        }

        private Ray Throw()
        {
            Ray aim = GetAimRay();
            Vector3 right = Vector3.Cross(Vector3.up, aim.direction);
            Vector3 dir = right.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(-IvyData.ThornsThrowUp, right.normalized) * aim.direction : aim.direction;
            return new Ray(aim.origin + aim.direction, dir);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!isAuthority) return;
            Ray t = Throw();
            _preview?.Update(t.origin, t.direction * IvyData.ThornsThrowSpeed, IvyData.ThornsRadius * MysticExpansion.RadiusMultiplier(characterBody));
            if (inputBank != null && inputBank.skill2.down && fixedAge < IvyData.ThornsMaxAim) return;
            IvyModel.Play(gameObject, "cast");
            DeadlockSounds.Play("ivy_bomb_throw", transform.position, gameObject);
            DeadlockHeroVoice.Power("ivy_vo_power1", gameObject);
            if (IvySurvivor.SeedProjectile != null)
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = IvySurvivor.SeedProjectile, position = t.origin, rotation = Quaternion.LookRotation(t.direction),
                    owner = gameObject, damage = damageStat, crit = RollCrit(), force = 0f, speedOverride = IvyData.ThornsThrowSpeed,
                });
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            _preview?.Destroy();
            DeadlockSounds.Stop(_aimLoop);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Stone Form =================

    /// <summary>
    /// Stone Form (Deadlock citadel_ability_tengu_stone_form; reworked, user 2.5). She turns to stone (her statue: IvyStoneLook),
    /// invulnerable and healing, leaping 4.6 m up as she transforms (Deadlock LiftHeight; from the ground or the air, user 2.5),
    /// then drops like a statue, accelerating hard, steerable at up to 8 m/s, keeping only a
    /// quarter of her momentum (DampingFactor). The slam hurts and stuns in 5.75 m; she breaks out half a second later (user 2.5).
    /// </summary>
    public class StoneForm : BaseSkillState
    {
        private bool _lifting, _landed;
        private float _landedAt;
        private Vector3 _flat;

        public override void OnEnter()
        {
            base.OnEnter();
            _lifting = characterMotor != null && characterMotor.isGrounded;
            IvyModel.Play(gameObject, _lifting ? "stone" : "stone_air");
            DeadlockSounds.Play("ivy_stone_cast", transform.position, gameObject);
            DeadlockSounds.Play("ivy_stone_cast_layer", transform.position, gameObject);
            DeadlockHeroVoice.Power("ivy_vo_power3", gameObject);
            if (!isAuthority) return;
            IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.Start });
            if (characterMotor != null)
            {
                Vector3 v = characterMotor.velocity;
                _flat = new Vector3(v.x, 0f, v.z) * IvyData.StoneDamping;
                characterMotor.Motor.ForceUnground();
                characterMotor.disableAirControlUntilCollision = false;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            float dt = Time.fixedDeltaTime;
            if (_landed)
            {
                characterMotor.velocity = Vector3.zero;
                characterMotor.moveDirection = Vector3.zero;
                if (fixedAge - _landedAt >= IvyData.StoneHoldAfterLanding) outer.SetNextStateToMain();
                return;
            }
            // She leaps up as she turns to stone (user 2.5: from the ground or the air), slowing to the top: h(s) = Lift (1 - (1 - s)^2).
            if (fixedAge < IvyData.StoneLiftTime)
            {
                float s = fixedAge / IvyData.StoneLiftTime;
                characterMotor.velocity = new Vector3(_flat.x, 2f * IvyData.StoneLift * (1f - s) / IvyData.StoneLiftTime, _flat.z);
                return;
            }
            Vector3 steer = inputBank != null ? inputBank.moveVector * IvyData.StoneSteer : Vector3.zero;
            _flat = Vector3.MoveTowards(_flat, new Vector3(steer.x, 0f, steer.z), 20f * dt);
            float vy = Mathf.Max(Mathf.Min(characterMotor.velocity.y, 0f) - IvyData.StoneFallAccel * dt, -IvyData.StoneFallMax);
            characterMotor.velocity = new Vector3(_flat.x, vy, _flat.z);
            bool falling = fixedAge > IvyData.StoneLiftTime + 0.05f;
            if ((characterMotor.isGrounded && falling) || fixedAge >= 6f) Land();
        }

        private void Land()
        {
            _landed = true;
            _landedAt = fixedAge;
            IvyModel.Play(gameObject, "stone_hold");
            IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.Slam, At = characterBody.footPosition, Crit = RollCrit() });
        }

        public override void OnExit()
        {
            if (isAuthority) IvyNet.ToServer(new IvyStoneMessage { Ivy = gameObject, Kind = IvyStoneKind.End });
            IvyModel.Play(gameObject, "idle");
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
        private IvyThrowPreview? _preview;

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
            // Where the bomb would land, and how wide it blows (user 2.5).
            if (isAuthority && characterBody.isPlayerControlled) _preview = new IvyThrowPreview(Radius, showArc: true);
        }

        private float Radius => IvyData.AirDropRadius * MysticExpansion.RadiusMultiplier(characterBody);

        private Vector3 ReleasePoint => characterBody.footPosition + Vector3.down * 0.5f;

        /// <summary>The toss: along her aim (much farther, user 2.5), on top of the way she's flying.</summary>
        private Vector3 TossVelocity() => _vel + (inputBank != null ? inputBank.aimDirection : characterDirection.forward) * IvyData.BombToss;

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
            _preview?.Update(ReleasePoint, TossVelocity(), Radius);
            bool press = fixedAge > 0.3f && (inputBank.skill4.justPressed || inputBank.skill1.justPressed);
            if (!_dropped && (press || fixedAge >= _limit)) Drop();
            if (_dropped && fixedAge > 0.1f) outer.SetNextStateToMain();
        }

        private void Drop()
        {
            _dropped = true;
            IvyModel.Play(gameObject, "fly_drop");
            _preview?.Destroy();
            _preview = null;
            IvyNet.ToServer(new IvyBombMessage { Ivy = gameObject, At = ReleasePoint, Velocity = TossVelocity(), Crit = RollCrit() });
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play("ivy_airdrop_end", transform.position, gameObject);
            if (isAuthority && !_dropped) Drop(); // interrupted: the bomb still falls
            _preview?.Destroy();
            // She keeps her momentum after the throw (user 2.5), and air control.
            if (isAuthority && characterMotor != null)
            {
                characterMotor.velocity = _vel;
                characterMotor.disableAirControlUntilCollision = false;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
