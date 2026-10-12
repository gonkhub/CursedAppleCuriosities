using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Vyper;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Vyper
{
    // ================= Primary: The Rattler =================

    /// <summary>The Rattler: full-auto, one bullet every 0.07 s (Deadlock), proc coefficient 1 (user). No clip or reload.</summary>
    public class FireRattler : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            // Slither: she fires faster while sliding (user).
            bool sliding = EntityStateMachine.FindByCustomName(gameObject, "Body")?.state is EntityStates.CursedApple.Deadlock.DeadlockSlide;
            _duration = VyperData.GunCycle / (attackSpeedStat * (sliding ? 1f + VyperData.GunSlideFireRate : 1f));
            string muzzle = "MuzzleRight";
            Transform? gun = FindModelChild(VyperSurvivor.MuzzleName);
            if (gun != null && gun.name == VyperData.MuzzleBone) muzzle = VyperSurvivor.MuzzleName;
            StartAimMode(2f);
            VyperModel.Play(gameObject, "shoot");
            VyperGunAudio.Shot(gameObject); // first shot, then mid shots; tail + release when the trigger lets go
            if (VyperSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(VyperSurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-0.25f, -0.45f, -0.25f, 0.25f);
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
                    maxSpread = VyperGunAudio.Spread(gameObject),
                    spreadPitchScale = 0.8f,
                    damage = VyperData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 40f,
                    tracerEffectPrefab = VyperSurvivor.TracerEffect,
                    hitEffectPrefab = VyperSurvivor.HitEffect,
                    muzzleName = muzzle,
                    isCrit = RollCrit(),
                    radius = 0.12f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.DefaultBullet, // Deadlock: full to 15 m, falling off to 33 m
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(VyperData.GunBloomPerShot);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Screwjab Dagger =================

    /// <summary>Screwjab Dagger: throw a dagger (2 charges). Each dagger on the same target hits and slows harder (VyperDagger).</summary>
    public class ThrowDagger : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            VyperModel.Play(gameObject, "dagger");
            DeadlockSounds.Play("vyper_dagger_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("vyper_vo_power1", gameObject);
            StartAimMode(1f);
            if (isAuthority && VyperSurvivor.DaggerProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = VyperSurvivor.DaggerProjectile,
                    position = aim.origin,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = VyperData.DaggerSpeed,
                });
            }
            if (isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Lethal Venom =================

    /// <summary>
    /// Lethal Venom: inject the enemy nearest the crosshair within range (Deadlock: 10 m, a 70 deg cone). After 3 s it
    /// detonates, harder the more health they're missing; it goes through Petrify. No target: no cast, no cooldown.
    /// </summary>
    public class LethalVenom : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (!isAuthority) return;
            float range = VyperData.VenomRange * MysticExpansion.RadiusMultiplier(characterBody);
            CharacterBody? target = EquipmentTargeting.FindEnemy(characterBody, range, VyperData.VenomConeDeg, _ => true);
            if (target == null)
            {
                activatorSkillSlot?.AddOneStock(); // refund
                outer.SetNextStateToMain();
                return;
            }
            VyperModel.Play(gameObject, "venom");
            DeadlockSounds.Play("vyper_venom_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("vyper_vo_power2", gameObject);
            if (VyperSurvivor.VenomEffect != null) EffectManager.SimpleImpactEffect(VyperSurvivor.VenomEffect, target.corePosition, -GetAimRay().direction, true);
            VyperNet.ToServer(new VyperVenomMessage { Attacker = gameObject, Target = target.gameObject, Crit = RollCrit() });
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Petrifying Bola =================

    /// <summary>Petrifying Bola: a fast, straight bola that bursts on contact (VyperBola).</summary>
    public class ThrowBola : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            VyperModel.Play(gameObject, "bola");
            DeadlockSounds.Play("vyper_bola_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("vyper_vo_power4", gameObject);
            StartAimMode(1f);
            if (isAuthority && VyperSurvivor.BolaProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = VyperSurvivor.BolaProjectile,
                    position = aim.origin + aim.direction * 1f,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = VyperData.BolaSpeed,
                });
            }
            if (isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
