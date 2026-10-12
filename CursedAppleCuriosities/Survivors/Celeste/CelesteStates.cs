using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Celeste;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Celeste
{
    /// <summary>Hangs a caster in the air: her fall is capped while the state runs (Deadlock's casting hover).</summary>
    internal static class CelesteHover
    {
        public static void Cap(CharacterMotor? motor, float fall)
        {
            if (motor == null || motor.isGrounded) return;
            Vector3 v = motor.velocity;
            if (v.y < -fall) motor.velocity = new Vector3(v.x, -fall, v.z);
        }
    }

    // ================= Primary: Carny Life =================

    /// <summary>Carny Life: a thrown blade every 0.58 s (Deadlock), arcing, homing, bouncing once (CelesteDagger). No reload.</summary>
    public class ThrowBlade : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = CelesteData.DaggerCycle / attackSpeedStat;
            StartAimMode(2f);
            CelesteModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("celeste_gun", transform.position, gameObject);
            if (isAuthority && CelesteSurvivor.DaggerProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = CelesteSurvivor.DaggerProjectile,
                    position = aim.origin + aim.direction * 0.5f,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = CelesteData.DaggerSpeed,
                });
            }
            characterBody.AddSpreadBloom(0.4f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Light Eater =================

    /// <summary>Light Eater: a 0.3 s wind-up (she hangs in the air), then a flare in a cone that damages and marks.</summary>
    public class LightEater : BaseSkillState
    {
        private bool _fired;

        public override void OnEnter()
        {
            base.OnEnter();
            StartAimMode(1f);
            CelesteModel.Play(gameObject, "flare");
            DeadlockSounds.Play("celeste_flare_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("celeste_vo_power1", gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority) CelesteHover.Cap(characterMotor, CelesteData.FlareHangFall);
            if (!_fired && fixedAge >= CelesteData.FlareDelay)
            {
                _fired = true;
                if (isAuthority)
                {
                    Ray aim = GetAimRay();
                    CelesteNet.ToServer(new CelesteFlareMessage { Attacker = gameObject, Origin = aim.origin, Dir = aim.direction, Crit = RollCrit() });
                }
            }
            if (fixedAge >= CelesteData.FlareDelay + 0.15f && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Secondary alt: Radiant Daggers =================

    /// <summary>Radiant Daggers: call a beam of light onto the spot under the crosshair (30 m); it strikes 1.4 s later.</summary>
    public class RadiantDaggers : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            CelesteModel.Play(gameObject, "beam");
            DeadlockSounds.Play("celeste_beam_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("celeste_vo_power3", gameObject);
            StartAimMode(1f);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                float range = CelesteData.BeamRange;
                Vector3 point = Physics.Raycast(aim, out RaycastHit hit, range, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore)
                    ? hit.point : aim.GetPoint(range);
                // Out of reach in the air: it lands on the ground below the aimed point (Deadlock: the beam spans every height).
                if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 60f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    point = ground.point;
                CelesteNet.ToServer(new CelesteBeamMessage { Attacker = gameObject, Point = point, Crit = RollCrit() });
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= 0.2f && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Dazzling Trick =================

    /// <summary>Dazzling Trick: a prism of barrier for 4 s; if it's broken, it bursts, damaging and silencing everything near.</summary>
    public class DazzlingTrick : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            CelesteModel.Play(gameObject, "prism");
            DeadlockHeroVoice.Power("celeste_vo_power2", gameObject);
            if (isAuthority)
            {
                CelesteNet.ToServer(new CelestePrismMessage { Attacker = gameObject });
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Shining Wonder =================

    /// <summary>
    /// Shining Wonder: hold to aim (she slows her fall in the air), release to launch the orb (CelesteOrb), which bounces
    /// between enemies. Holding past the limit launches it anyway.
    /// </summary>
    public class ShiningWonder : BaseSkillState
    {
        private DeadlockVoice? _loop;
        private bool _fired;

        public override void OnEnter()
        {
            base.OnEnter();
            CelesteModel.Play(gameObject, "orb_start");
            DeadlockSounds.Play("celeste_orb_cast", transform.position, gameObject);
            _loop = DeadlockSounds.Loop("celeste_orb_channel", gameObject, CelesteData.OrbMaxHold + 0.5f);
            DeadlockHeroVoice.Power("celeste_vo_power4", gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!isAuthority) return;
            CelesteHover.Cap(characterMotor, CelesteData.OrbAimFall);
            bool held = inputBank != null && (inputBank.skill4.down || inputBank.skill1.down && fixedAge < 0.1f);
            if (fixedAge >= CelesteData.OrbMaxHold || fixedAge >= CelesteData.OrbMinHold && !held) Fire();
        }

        private void Fire()
        {
            if (_fired) return;
            _fired = true;
            if (CelesteSurvivor.OrbProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = CelesteSurvivor.OrbProjectile,
                    position = aim.origin + aim.direction * 1.5f,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = CelesteData.OrbSpeed,
                });
            }
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            bool air = characterMotor != null && !characterMotor.isGrounded;
            CelesteModel.Play(gameObject, air ? "orb_end_air" : "orb_end");
            DeadlockSounds.Play("celeste_orb_fire", transform.position, gameObject);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
