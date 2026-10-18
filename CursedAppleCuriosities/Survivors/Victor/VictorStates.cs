using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Victor;
using RoR2;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Victor
{
    // ================= Primary: Memento Mori =================

    /// <summary>
    /// Memento Mori: one fast bullet per shot, spinning up from 0.198 s to 0.171 s between shots the longer he holds the
    /// trigger (Deadlock). Infinite ammo, like every hero here.
    /// </summary>
    public class FireMementoMori : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = VictorGun.Shot(gameObject) / attackSpeedStat;
            string muzzle = "MuzzleRight";
            Transform? gun = FindModelChild(VictorSurvivor.MuzzleName);
            if (gun != null && gun.name == VictorData.MuzzleBone) muzzle = VictorSurvivor.MuzzleName;
            StartAimMode(2f);
            VictorModel.Play(gameObject, "shoot");
            if (VictorSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(VictorSurvivor.MuzzleEffect, gameObject, muzzle, false);
            AddRecoil(-0.4f, -0.6f, -0.3f, 0.3f);
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
                    maxSpread = VictorData.GunSpread,
                    damage = VictorData.GunDamage * damageStat,
                    procCoefficient = 1f,
                    force = 80f,
                    tracerEffectPrefab = VictorSurvivor.TracerEffect,
                    hitEffectPrefab = VictorSurvivor.HitEffect,
                    muzzleName = muzzle,
                    isCrit = RollCrit(),
                    radius = VictorData.GunRadius,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.DefaultBullet, // Deadlock: full to 20 m, falling off to 57 m
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.2f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Pain Battery =================

    /// <summary>
    /// Pain Battery: only castable once the battery is full (it fills from damage Victor takes, nothing else). A 0.35 s
    /// wind-up, then 7 shocking bolts in a 40 deg fan; each enemy is hit once and heals him for a share of his missing health.
    /// </summary>
    public class FirePainBattery : BaseSkillState
    {
        private bool _fired;

        public override void OnEnter()
        {
            base.OnEnter();
            // Running on the ground: the cast on his upper body over the run (Deadlock masks it so); standing or in the air: the full clip.
            bool running = characterMotor != null && characterMotor.isGrounded && new Vector3(characterMotor.velocity.x, 0f, characterMotor.velocity.z).sqrMagnitude > 1f;
            VictorModel.Play(gameObject, running ? "battery_run" : "battery");
            DeadlockHeroVoice.Power("victor_vo_power1", gameObject);
            StartAimMode(1f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!_fired && fixedAge >= VictorData.BatteryCastDelay)
            {
                _fired = true;
                DeadlockSounds.Play("victor_battery_cast", transform.position, gameObject);
                if (VictorSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(VictorSurvivor.MuzzleEffect, gameObject, "MuzzleRight", false);
                if (isAuthority)
                {
                    Ray aim = GetAimRay();
                    VictorNet.ToServer(new VictorBatteryMessage { Body = gameObject, Origin = aim.origin, Direction = aim.direction, Crit = RollCrit() });
                }
            }
            if (isAuthority && fixedAge >= VictorData.BatteryCastDelay + 0.2f) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Utility: Jumpstart =================

    /// <summary>
    /// Jumpstart: Victor zaps himself for a share of his current health, then heals more than that back over 4.5 s and runs
    /// faster, the bonus fading as it goes (VictorJumpstart on the server).
    /// </summary>
    public class Jumpstart : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            VictorModel.Play(gameObject, "jumpstart");
            DeadlockSounds.Play("victor_jumpstart_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("victor_vo_power2", gameObject);
            if (VictorSurvivor.ZapEffect != null) EffectManager.SimpleEffect(VictorSurvivor.ZapEffect, characterBody.corePosition, Quaternion.identity, false);
            if (isAuthority) VictorNet.ToServer(new VictorJumpstartMessage { Body = gameObject });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && fixedAge >= VictorData.JumpstartCast) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Aura of Suffering =================

    /// <summary>
    /// Aura of Suffering, on its own "Aura" machine so Victor keeps moving and shooting. The server deals the damage
    /// (VictorAura): ramping up the longer it runs, to enemies around him and to himself. It stays on until Special is
    /// pressed again (Deadlock's "End Aura") or he dies (user); going down to Reanimation counts. The cooldown starts when
    /// it ends, as in Deadlock.
    /// </summary>
    public class AuraOfSuffering : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            VictorModel.Play(gameObject, "aura");
            DeadlockSounds.Play("victor_aura_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("victor_vo_power3", gameObject);
            if (isAuthority) VictorNet.ToServer(new VictorAuraMessage { Body = gameObject, On = true });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            bool endPressed = fixedAge >= VictorData.AuraEndGrace && inputBank != null && inputBank.skill4.justPressed;
            // The server ends it on its own when he goes down (the buff disappears).
            bool serverEnded = fixedAge > 1f && VictorSurvivor.AuraBuff != null && !characterBody.HasBuff(VictorSurvivor.AuraBuff);
            if (endPressed || serverEnded) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            VictorModel.Play(gameObject, "aura_end");
            if (isAuthority)
            {
                VictorNet.ToServer(new VictorAuraMessage { Body = gameObject, On = false });
                // The cooldown runs from now, not from the cast (it was full again while the aura ran).
                skillLocator?.special?.RemoveAllStocks();
            }
            base.OnExit();
        }

        // A second special press while it runs must end it, not restart it.
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Pain;
    }

    // ================= Passive: Shocking Reanimation =================

    /// <summary>
    /// Shocking Reanimation, the down half: Victor took lethal damage with it ready (VictorRevive on the server). He lies
    /// still, untouchable, for the 3 s channel; when the server lets him up he stands, and the blast goes off around him.
    /// </summary>
    public class Reanimate : BaseState
    {
        private bool _up;
        private float _upAt;

        public override void OnEnter()
        {
            base.OnEnter();
            VictorModel.Play(gameObject, "revive_down");
            if (isAuthority)
            {
                // No shooting or casting while down (SkillSilence holds the skills; this stops anything mid-swing).
                EntityStateMachine.FindByCustomName(gameObject, "Weapon")?.SetNextStateToMain();
                EntityStateMachine.FindByCustomName(gameObject, "Aura")?.SetNextStateToMain();
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (characterMotor != null && isAuthority)
                characterMotor.velocity = new Vector3(0f, Mathf.Min(0f, characterMotor.velocity.y), 0f);
            bool down = VictorSurvivor.ReanimatingBuff != null && characterBody.HasBuff(VictorSurvivor.ReanimatingBuff);
            if (!_up && !down && fixedAge > 0.2f)
            {
                _up = true;
                _upAt = fixedAge;
                VictorModel.Play(gameObject, "revive_up");
                DeadlockHeroVoice.Power("victor_vo_power4", gameObject);
            }
            if (isAuthority && _up && fixedAge - _upAt >= VictorData.RevivePost) outer.SetNextStateToMain();
            if (isAuthority && fixedAge > VictorData.ReviveChannel + 5f) outer.SetNextStateToMain(); // never stuck down
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Death;
    }
}
