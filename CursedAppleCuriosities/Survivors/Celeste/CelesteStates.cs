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
            CelesteModel.Play(gameObject, characterMotor != null && !characterMotor.isGrounded ? "flare_air" : "flare");
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

    /// <summary>
    /// Radiant Daggers (user: Puddle Punch's controls): hold to preview where the beam will land (a pink ring the size of
    /// the blast and a thin beam from the sky), release to call it down; it strikes 1.4 s later.
    /// </summary>
    public class RadiantDaggers : BaseSkillState
    {
        private Vector3 _point;
        private bool _released;
        private float _releasedAt;
        private BeamPreview? _preview;

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority && characterBody.isPlayerControlled) _preview = new BeamPreview();
        }

        /// <summary>The spot under the crosshair within range; out of reach in the air, the ground below it (the beam spans every height).</summary>
        public static Vector3 Target(Ray aim)
        {
            float range = CelesteData.BeamRange;
            Vector3 point = Physics.Raycast(aim, out RaycastHit hit, range, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore)
                ? hit.point : aim.GetPoint(range);
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 60f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                point = ground.point;
            return point;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_released)
            {
                StartAimMode(0.5f);
                _point = Target(GetAimRay());
                _preview?.Show(_point, CelesteData.BeamRadius * MysticExpansion.RadiusMultiplier(characterBody));
                if (inputBank.skill2.down) return;
                Release();
            }
            if (fixedAge - _releasedAt >= 0.2f) outer.SetNextStateToMain();
        }

        private void Release()
        {
            _released = true;
            _releasedAt = fixedAge;
            _preview?.Destroy();
            _preview = null;
            CelesteModel.Play(gameObject, "beam");
            DeadlockSounds.Play("celeste_beam_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("celeste_vo_power4", gameObject); // Deadlock files Radiant Daggers as power4
            CelesteNet.ToServer(new CelesteBeamMessage { Attacker = gameObject, Point = _point, Crit = RollCrit() });
        }

        public override void OnExit()
        {
            _preview?.Destroy();
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => _released ? InterruptPriority.PrioritySkill : InterruptPriority.Skill;
    }

    /// <summary>The local player's aiming preview for Radiant Daggers: a pink ring on the ground at the blast's edge and a thin beam above its centre.</summary>
    internal class BeamPreview
    {
        private const int Segments = 48;
        private readonly LineRenderer? _ring, _beam;

        public BeamPreview()
        {
            _ring = Line("CursedApple_BeamPreviewRing", Segments, true, 0.12f);
            _beam = Line("CursedApple_BeamPreviewBeam", 2, false, 0.08f);
        }

        private static LineRenderer? Line(string name, int points, bool loop, float width)
        {
            Material? mat = CelesteSurvivor.LineMaterial;
            if (mat == null) return null;
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = CelesteSurvivor.LightColor;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = points;
            lr.loop = loop;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        public void Show(Vector3 at, float radius)
        {
            if (_ring != null)
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    _ring.SetPosition(i, at + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * radius);
                }
            if (_beam != null)
            {
                _beam.SetPosition(0, at + Vector3.up * 0.15f);
                _beam.SetPosition(1, at + Vector3.up * 25f);
            }
        }

        public void Destroy()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            if (_beam != null) Object.Destroy(_beam.gameObject);
        }
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
            DeadlockHeroVoice.Power("celeste_vo_power3", gameObject); // her ult lines are power3 ("_ult" in Deadlock's captions)
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
