using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Mina;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Mina
{
    // ================= Primary: the pistol =================

    /// <summary>Mina's pistol (Deadlock citadel_weapon_vampirebat_set: one bullet every 0.252 s, falloff 20 -> 46 m). Low damage: Love Bites does the rest.</summary>
    public class FirePistol : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = MinaData.GunCycle / attackSpeedStat;
            Transform? gun = FindModelChild(MinaSurvivor.MuzzleName);
            string muzzle = gun != null && gun.name == MinaData.MuzzleBone ? MinaSurvivor.MuzzleName : "MuzzleRight";
            StartAimMode(2f);
            MinaModel.Play(gameObject, "shoot");
            MinaGunAudio.Shot(gameObject);
            if (MinaSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(MinaSurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-0.4f, -0.7f, -0.2f, 0.2f);
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
                    maxSpread = MinaData.GunSpread,
                    damage = MinaData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 60f,
                    tracerEffectPrefab = MinaSurvivor.TracerEffect,
                    hitEffectPrefab = MinaSurvivor.HitEffect,
                    muzzleName = muzzle,
                    isCrit = RollCrit(),
                    radius = 0.15f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.None,
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                        info.damage *= Kit.Falloff(hit.distance, MinaData.GunFalloffStart, MinaData.GunFalloffEnd, MinaData.GunFalloffMin),
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.25f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Rake =================

    /// <summary>
    /// Rake (Deadlock ability_vampirebat_steallife): after a short wind-up, swipe the umbrella across a 60 deg cone, 10 m deep.
    /// Every enemy caught takes 300% plus a share of its missing health; kills heal her. In the air she hops (Deadlock MiniJumpVelocity).
    /// </summary>
    public class Rake : BaseSkillState
    {
        private bool _swiped;

        public override void OnEnter()
        {
            base.OnEnter();
            MinaModel.Play(gameObject, "rake");
            DeadlockSounds.Play("mina_rake_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("mina_vo_power1", gameObject);
            StartAimMode(1f);
            if (isAuthority && characterMotor != null && !characterMotor.isGrounded)
                characterMotor.velocity = new Vector3(characterMotor.velocity.x, Mathf.Max(characterMotor.velocity.y, MinaData.RakeHop), characterMotor.velocity.z);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_swiped && fixedAge >= MinaData.RakeCastDelay) Swipe();
            if (fixedAge >= MinaData.RakeCastDelay + 0.25f) outer.SetNextStateToMain();
        }

        private void Swipe()
        {
            _swiped = true;
            Ray aim = GetAimRay();
            float range = MinaData.RakeRange * MysticExpansion.RadiusMultiplier(characterBody);
            var caught = new List<GameObject>();
            foreach (HurtBox h in new SphereSearch { origin = characterBody.corePosition, radius = range, mask = LayerIndex.entityPrecise.mask }
                         .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(GetTeam())).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes())
            {
                if (h.healthComponent == null) continue;
                Vector3 to = h.transform.position - characterBody.corePosition;
                if (to.sqrMagnitude > 1f && Vector3.Angle(aim.direction, to) > MinaData.RakeConeDeg * 0.5f) continue;
                if (!caught.Contains(h.healthComponent.gameObject)) caught.Add(h.healthComponent.gameObject);
            }
            if (MinaSurvivor.SwipeEffect != null)
                EffectManager.SpawnEffect(MinaSurvivor.SwipeEffect, new EffectData { origin = characterBody.corePosition + aim.direction * 2f, rotation = Util.QuaternionSafeLookRotation(aim.direction), scale = range * 0.5f }, true);
            if (caught.Count > 0) MinaNet.ToServer(new MinaRakeMessage { Attacker = gameObject, Targets = caught.ToArray(), Crit = RollCrit() });
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Sanguine Retreat =================

    /// <summary>
    /// Sanguine Retreat (Deadlock ability_vampirebat_batblink): she bursts into a swarm of bats and flies along the aim, unseen,
    /// untargetable and invulnerable (user), then reforms with a little of the speed. She can recast it once within 5 s (user):
    /// the first cast hands the charge back (MinaRetreatRecast), and the cooldown starts after the recast or when the window closes.
    /// </summary>
    public class SanguineRetreat : BaseSkillState
    {
        private Vector3 _dir;
        private bool _recast;
        private CharacterModel? _model;
        private GameObject? _cloud;
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            var recast = GetComponent<MinaRetreatRecast>();
            _recast = recast != null && recast.Open;
            if (isAuthority && recast != null) recast.Use(_recast);
            Ray aim = GetAimRay();
            _dir = aim.direction;
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(_dir.y, -1f, 1f)) * Mathf.Rad2Deg, -45f, 60f);
            Vector3 flat = new Vector3(_dir.x, 0f, _dir.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
            _dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;

            DeadlockSounds.Play("mina_retreat_start", transform.position, gameObject);
            _loop = DeadlockSounds.Loop("mina_retreat_loop", gameObject, MinaData.RetreatSeconds + 0.3f);
            if (!_recast) DeadlockHeroVoice.Power("mina_vo_power2", gameObject);
            if (MinaSurvivor.BlinkEffect != null) EffectManager.SimpleImpactEffect(MinaSurvivor.BlinkEffect, characterBody.corePosition, _dir, false);
            // She is the bats: her model goes away on every machine, a little swarm follows the body.
            _model = GetModelTransform() != null ? GetModelTransform().GetComponent<CharacterModel>() : null;
            if (_model != null) _model.invisibilityCount++;
            _cloud = MinaBatVisual.Cloud(transform, 10);
            if (isAuthority)
            {
                MinaNet.ToServer(new MinaRetreatMessage { Body = gameObject, Seconds = MinaData.RetreatSeconds + 0.15f });
                if (characterMotor != null)
                {
                    characterMotor.disableAirControlUntilCollision = false;
                    if (_dir.y > 0.05f) characterMotor.Motor.ForceUnground();
                }
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            float speed = MinaData.RetreatMetres * MysticExpansion.RadiusMultiplier(characterBody) / MinaData.RetreatSeconds;
            characterMotor.velocity = Vector3.zero;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.rootMotion += _dir * speed * Time.fixedDeltaTime;
            characterDirection.forward = new Vector3(_dir.x, 0f, _dir.z);
            if (fixedAge >= MinaData.RetreatSeconds) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (_model != null) _model.invisibilityCount--;
            if (_cloud != null) Object.Destroy(_cloud);
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play(_recast ? "mina_retreat_end_final" : "mina_retreat_end", transform.position, gameObject);
            if (MinaSurvivor.BlinkEffect != null) EffectManager.SimpleImpactEffect(MinaSurvivor.BlinkEffect, characterBody.corePosition, -_dir, false);
            MinaModel.Play(gameObject, "retreat_end");
            if (isAuthority && characterMotor != null) characterMotor.velocity = _dir * MinaData.RetreatExitSpeed + Vector3.up * 2f; // Deadlock ExitVelocity
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Special: Nox Nostra =================

    /// <summary>
    /// Nox Nostra (Deadlock ability_vampirebat_batswarm): she leaps up (17 m/s at 60 deg), opens her umbrella and hovers, and after
    /// the cast delay unleashes bats ahead at 30 a second: 75, plus one per Love Bites stack this stage (user; at most 50 more,
    /// Deadlock's BonusBatsMax). The bats home in on enemies (MinaBat).
    /// </summary>
    public class NoxNostra : BaseSkillState
    {
        private int _toLaunch, _launched;
        private float _owed;
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            MinaModel.Play(gameObject, "nox");
            DeadlockSounds.Play("mina_nox_cast", transform.position, gameObject);
            DeadlockSounds.Play("mina_umbrella_open", transform.position, gameObject);
            DeadlockHeroVoice.Power("mina_vo_power4", gameObject);
            // Love Bites stacks, read from the synced buff (the stack count itself lives on the server's master).
            int stacks = characterBody != null && MinaSurvivor.LoveStacksBuff != null ? characterBody.GetBuffCount(MinaSurvivor.LoveStacksBuff) : 0;
            _toLaunch = MinaData.Bats + Mathf.Min(MinaData.BonusBatsMax, stacks);
            if (isAuthority && characterMotor != null)
            {
                Vector3 d = GetAimRay().direction;
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = flat * MinaData.NoxJumpSpeed * Mathf.Cos(MinaData.NoxJumpPitch * Mathf.Deg2Rad) + Vector3.up * MinaData.NoxJumpSpeed * Mathf.Sin(MinaData.NoxJumpPitch * Mathf.Deg2Rad);
                characterMotor.disableAirControlUntilCollision = false;
            }
            if (UnityEngine.Networking.NetworkServer.active) DeadlockFallGuard.ProtectNextLanding(gameObject, 8f); // floats down on the umbrella
        }

        private float Duration => MinaData.NoxCastDelay + _toLaunch / MinaData.BatsPerSecond;

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (fixedAge >= MinaData.NoxCastDelay && _loop == null) _loop = DeadlockSounds.Loop("mina_nox_loop", gameObject, Duration);
            if (!isAuthority || characterMotor == null) return;
            float dt = Time.fixedDeltaTime;
            // Hovering on the umbrella: the leap's rise carries her up, then she falls no faster than Deadlock's FallSpeedMax,
            // and her drift dies away quickly (Deadlock AirDrag 12).
            Vector3 v = characterMotor.velocity;
            float fall = fixedAge > 0.4f ? Mathf.Max(v.y, -MinaData.NoxFallMax) : v.y;
            Vector3 flat = new Vector3(v.x, 0f, v.z) * Mathf.Max(0f, 1f - (fixedAge > 0.4f ? 3f : 0.5f) * dt);
            characterMotor.velocity = new Vector3(flat.x, fall, flat.z);
            if (fixedAge >= MinaData.NoxCastDelay && _launched < _toLaunch)
            {
                _owed += MinaData.BatsPerSecond * dt;
                while (_owed >= 1f && _launched < _toLaunch) { _owed -= 1f; Launch(); }
            }
            if (fixedAge >= Duration) outer.SetNextStateToMain();
        }

        private void Launch()
        {
            _launched++;
            if (MinaSurvivor.BatProjectile == null) return;
            Ray aim = GetAimRay();
            Vector3 from = characterBody.corePosition + Vector3.up * 0.8f + Random.insideUnitSphere * 1.5f; // Deadlock BatSpawnRadius
            Vector3 dir = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(-18f, 18f), 0f) * Quaternion.LookRotation(aim.direction) * Vector3.forward;
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = MinaSurvivor.BatProjectile,
                position = from,
                rotation = Quaternion.LookRotation(dir),
                owner = gameObject,
                damage = damageStat,
                crit = false,
                force = 0f,
                speedOverride = MinaData.BatSpeed,
            });
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play("mina_nox_end", transform.position, gameObject);
            DeadlockSounds.Play("mina_umbrella_close", transform.position, gameObject);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
