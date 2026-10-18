using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Shiv;
using EntityStates.CursedApple.Deadlock;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Shiv
{
    /// <summary>
    /// When Shiv's gun is next ready (owner). The knife shares the Weapon machine and can cut a shot's cycle short; it now waits out the
    /// rest of that cycle instead, so a throw never resets the gun (user 3.1: knife + alt fire chained into endless flight).
    /// </summary>
    public class ShivGunCycle : MonoBehaviour
    {
        public float ReadyAt;

        public static void Fired(GameObject body, float cycle) => (body.GetComponent<ShivGunCycle>() ?? body.AddComponent<ShivGunCycle>()).ReadyAt = Time.fixedTime + cycle;
        public static float Left(GameObject body) => body.TryGetComponent(out ShivGunCycle c) ? Mathf.Max(0f, c.ReadyAt - Time.fixedTime) : 0f;
    }

    internal static class ShivShared
    {
        /// <summary>Enemies (one hurtbox each) in a sphere, seen from this client: the owner picks, the server hits.</summary>
        public static List<HurtBox> Enemies(Vector3 center, float radius, TeamIndex team) =>
            new List<HurtBox>(new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes());

        public static string Muzzle(Transform? gun) =>
            gun != null && gun.name == ShivData.MuzzleBone ? ShivSurvivor.MuzzleName : "MuzzleRight"; // the gun once his model is on

        /// <summary>One shotgun blast: pellets with Deadlock's falloff curve (full damage, then smoothly down to 10%).</summary>
        public static void Blast(GameObject owner, Ray aim, string muzzle, bool crit, float damageStat, int pellets, float coeff, float spread, float start, float end, float force)
        {
            new BulletAttack
            {
                owner = owner,
                weapon = owner,
                origin = aim.origin,
                aimVector = aim.direction,
                bulletCount = (uint)Mathf.Max(1, pellets),
                minSpread = 0f,
                maxSpread = spread,
                spreadPitchScale = 0.7f,
                damage = coeff * damageStat,
                procCoefficient = ShivData.PelletProc,
                force = force,
                tracerEffectPrefab = ShivSurvivor.TracerEffect,
                hitEffectPrefab = ShivSurvivor.HitEffect,
                muzzleName = muzzle,
                isCrit = crit,
                radius = 0.3f,
                smartCollision = true,
                falloffModel = BulletAttack.FalloffModel.None,
                modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                    info.damage *= Kit.Falloff(hit.distance, start, end, ShivData.FalloffMin),
                damageType = DamageSource.Primary,
            }.Fire();
        }
    }

    // ================= Primary: the shotgun =================

    /// <summary>Shiv's shotgun (Deadlock citadel_weapon_shiv_set: 6 pellets every 0.55 s, falloff 20 -> 41 m). No reload, like Abrams.</summary>
    public class FireShotgun : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = ShivData.ShotCycle / attackSpeedStat;
            if (isAuthority) ShivGunCycle.Fired(gameObject, _duration);
            StartAimMode(2f);
            ShivModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("shiv_gun", transform.position, gameObject);
            if (ShivSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(ShivSurvivor.MuzzleEffect, gameObject, ShivShared.Muzzle(FindModelChild(ShivSurvivor.MuzzleName)), false);
            AddRecoil(-1f, -1.4f, -0.4f, 0.4f);
            if (isAuthority) ShivShared.Blast(gameObject, GetAimRay(), ShivShared.Muzzle(FindModelChild(ShivSurvivor.MuzzleName)), RollCrit(), damageStat, ShivData.Pellets, ShivData.PelletDamage, ShivData.SpreadDeg, ShivData.FalloffStart, ShivData.FalloffEnd, 150f);
            characterBody.AddSpreadBloom(0.6f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    /// <summary>
    /// The alt fire (Deadlock citadel_weapon_shiv_alt: 12 pellets). User: twice the damage at half the fire rate, a harsher
    /// falloff, and a strong knockback on Shiv himself as a movement tool: blast the ground to hop, blast behind a slide to
    /// keep it going.
    /// </summary>
    public class FireAltShotgun : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = ShivData.AltCycle / attackSpeedStat;
            if (isAuthority) ShivGunCycle.Fired(gameObject, _duration);
            StartAimMode(2f);
            ShivModel.Play(gameObject, "shoot_alt");
            DeadlockSounds.Play("shiv_gun_alt", transform.position, gameObject);
            if (ShivSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(ShivSurvivor.MuzzleEffect, gameObject, ShivShared.Muzzle(FindModelChild(ShivSurvivor.MuzzleName)), false);
            AddRecoil(-2.5f, -3f, -0.6f, 0.6f);
            if (isAuthority)
            {
                ShivShared.Blast(gameObject, GetAimRay(), ShivShared.Muzzle(FindModelChild(ShivSurvivor.MuzzleName)), RollCrit(), damageStat, ShivData.AltPellets, ShivData.AltPelletDamage, ShivData.AltSpreadDeg, ShivData.AltFalloffStart, ShivData.AltFalloffEnd, 400f);
                Knockback();
            }
            characterBody.AddSpreadBloom(1f);
        }

        /// <summary>Owner: push Shiv away from where he aims. A slide takes it as slide speed; otherwise it's air momentum.</summary>
        private void Knockback()
        {
            if (characterMotor == null) return;
            Vector3 push = -GetAimRay().direction * ShivData.AltKnockback;
            if (push.y > 0f) push.y *= ShivData.AltKnockbackUp;
            Vector3 flat = new Vector3(push.x, 0f, push.z);
            if (EntityStateMachine.FindByCustomName(gameObject, "Body")?.state is DeadlockSlide slide)
            {
                Vector3 v = slide.Dir * slide.Speed + flat * ShivData.AltSlideKnockback; // a slide takes it harder (user 3.1, for the lost lift)
                if (v.sqrMagnitude > 0.01f) { slide.Speed = v.magnitude; slide.Dir = v.normalized; }
                if (push.y > 2f) { characterMotor.Motor.ForceUnground(); characterMotor.velocity = new Vector3(characterMotor.velocity.x, push.y, characterMotor.velocity.z); }
                return;
            }
            Vector3 vel = characterMotor.velocity + push;
            // The lift never stacks on what he already has (user 3.1): shot after shot in the air can't climb, only slow his fall.
            if (push.y > 0f) vel.y = Mathf.Max(characterMotor.velocity.y, push.y);
            if (characterMotor.isGrounded)
            {
                // On the ground the run would eat it: lift off a touch so it carries as air momentum.
                vel.y = Mathf.Max(vel.y, 3f);
                characterMotor.Motor.ForceUnground();
            }
            characterMotor.velocity = vel;
            GetComponent<DeadlockMover>()?.SuspendAirDrag(0.6f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Serrated Knife =================

    /// <summary>Serrated Knife (Deadlock citadel_ability_shiv_dagger): throw a knife that bleeds what it hits (ShivKnife). 3 charges (user).</summary>
    public class ThrowKnife : BaseSkillState
    {
        private float _hold;

        public override void OnEnter()
        {
            base.OnEnter();
            ShivModel.Play(gameObject, "knife");
            DeadlockSounds.Play("shiv_knife_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("shiv_vo_power1", gameObject);
            StartAimMode(1f);
            if (isAuthority && ShivSurvivor.KnifeProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = ShivSurvivor.KnifeProjectile,
                    position = aim.origin,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = ShivData.KnifeSpeed,
                });
            }
            _hold = ShivGunCycle.Left(gameObject); // a shot it cut short: its cycle still runs out before the gun is back
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && fixedAge >= _hold) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Slice'n'Dice =================

    /// <summary>
    /// Slice'n'Dice (Deadlock citadel_ability_shiv_dash): a short wind-up, then a 12 m dash along the aim at Deadlock's 61 m/s,
    /// cutting every enemy in a 2.5 m path for 600% (user; x2 at full rage). Each enemy struck takes 1 s off its cooldown (user).
    /// </summary>
    public class SliceNDice : BaseSkillState
    {
        private Vector3 _dir;
        private float _dashTime, _travelled;
        private bool _dashing;
        private LayerMask? _collide;
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();

        public override void OnEnter()
        {
            base.OnEnter();
            ShivModel.Play(gameObject, "slice");
            DeadlockSounds.Play("shiv_dash_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("shiv_vo_power2", gameObject);
            Vector3 d = GetAimRay().direction;
            // Deadlock: the dash follows the camera, but not straight up or down. In the air it follows the camera's pitch much further
            // (user 2.5), so he can cut enemies above or below him.
            bool air = characterMotor != null && !characterMotor.isGrounded;
            float maxPitch = air ? ShivData.AirAimPitch : 35f;
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
            _dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            _dashTime = ShivData.DashMetres * MysticExpansion.RadiusMultiplier(characterBody) / ShivData.DashSpeed;
            if (isAuthority && characterMotor != null)
            {
                characterMotor.disableAirControlUntilCollision = false;
                // Straight through bodies, cutting them on the way (user 2.5): no collision with characters for the dash.
                _collide = characterMotor.Motor.CollidableLayers;
                characterMotor.Motor.CollidableLayers = characterMotor.Motor.CollidableLayers & ~(LayerIndex.enemyBody.mask | LayerIndex.playerBody.mask);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            float dt = Time.fixedDeltaTime;
            characterDirection.forward = new Vector3(_dir.x, 0f, _dir.z);
            if (fixedAge < ShivData.DashCastDelay)
            {
                characterMotor.velocity = Vector3.zero;
                return;
            }
            if (!_dashing)
            {
                _dashing = true;
                if (characterMotor.isGrounded && _dir.y > 0.05f) characterMotor.Motor.ForceUnground();
            }
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.velocity = Vector3.zero;
            characterMotor.rootMotion += _dir * ShivData.DashSpeed * dt;
            _travelled += dt;
            Cut();
            if (_travelled >= _dashTime) outer.SetNextStateToMain();
        }

        private void Cut()
        {
            var fresh = new List<GameObject>();
            float radius = ShivData.DashRadius * MysticExpansion.RadiusMultiplier(characterBody);
            foreach (HurtBox h in ShivShared.Enemies(characterBody.corePosition + _dir * 0.5f, radius, GetTeam()))
                if (h.healthComponent != null && _hit.Add(h.healthComponent)) fresh.Add(h.healthComponent.gameObject);
            if (fresh.Count == 0) return;
            ShivNet.ToServer(new ShivHitMessage { Kind = ShivHitKind.Slice, Attacker = gameObject, Targets = fresh.ToArray(), Crit = RollCrit(), Dir = _dir });
        }

        public override void OnExit()
        {
            if (characterMotor != null && _collide.HasValue) characterMotor.Motor.CollidableLayers = _collide.Value;
            if (isAuthority && characterMotor != null)
            {
                characterMotor.velocity = _dir * characterBody.moveSpeed * 1.5f; // a little of the dash carries on
                // User: each enemy struck takes a second off the cooldown, but at least DashMinWait is left (3.0: crowds of 8+
                // reset it outright, an endless chain).
                if (_hit.Count > 0 && activatorSkillSlot != null)
                {
                    float refund = Mathf.Min(_hit.Count * ShivData.DashCooldownPerHit, activatorSkillSlot.cooldownRemaining - ShivData.DashMinWait);
                    if (refund > 0f) activatorSkillSlot.RunRecharge(refund);
                }
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Special: Killing Blow =================

    /// <summary>
    /// Killing Blow (Deadlock citadel_ability_shiv_killing_blow), the user's version: a long lunge forward with his knife
    /// drawn, passing through enemies for 200%. The first enemy he touches that's under the execute threshold (10%, 35% at
    /// full rage; bosses half) stops the lunge and is executed: that cleanses his Bloodletting, fills a lot of rage and
    /// refunds the cooldown.
    /// </summary>
    public class KillingBlow : BaseSkillState
    {
        private Vector3 _dir;
        private bool _air;
        private float _time, _windup = 0.12f;
        private bool _ended;
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();

        public override void OnEnter()
        {
            base.OnEnter();
            ShivModel.Play(gameObject, "lunge");
            DeadlockSounds.Play("shiv_lunge_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("shiv_vo_power4", gameObject);
            Vector3 d = GetAimRay().direction;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
            // On the ground it runs along the ground with Deadlock's hop; in the air it goes where he aims, up or down (user 2.5).
            _air = characterMotor != null && !characterMotor.isGrounded;
            float pitch = _air ? Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, -ShivData.AirAimPitch, ShivData.AirAimPitch) : 0f;
            _dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            _time = ShivData.LungeMetres * MysticExpansion.RadiusMultiplier(characterBody) / ShivData.LungeSpeed;
            if (isAuthority && characterMotor != null)
            {
                characterMotor.disableAirControlUntilCollision = false;
                characterMotor.Motor.ForceUnground();
            }
            if (UnityEngine.Networking.NetworkServer.active) DeadlockFallGuard.ProtectNextLanding(gameObject, 3f); // no fall damage off the hop
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null || _ended) return;
            float dt = Time.fixedDeltaTime;
            characterDirection.forward = new Vector3(_dir.x, 0f, _dir.z);
            characterMotor.moveDirection = Vector3.zero;
            if (fixedAge < _windup) { characterMotor.velocity = Vector3.zero; return; }
            float t = fixedAge - _windup;
            // Along the ground with Deadlock's hop: up and back down over the lunge (a parabola's slope each tick).
            float u = Mathf.Clamp01(t / _time);
            float rise = _air ? 0f : ShivData.LungeHop * 4f * (1f - 2f * u) / Mathf.Max(0.05f, _time);
            characterMotor.velocity = Vector3.zero;
            characterMotor.rootMotion += (_dir * ShivData.LungeSpeed + Vector3.up * rise) * dt;
            if (Contact()) return;
            if (Blocked(dt) || t >= _time) Finish(false);
        }

        /// <summary>Enemies touched: an executable one ends the lunge on it, the rest are cut and passed through.</summary>
        private bool Contact()
        {
            var fresh = new List<GameObject>();
            foreach (HurtBox h in ShivShared.Enemies(characterBody.corePosition + _dir * 0.6f, ShivData.LungeRadius * MysticExpansion.RadiusMultiplier(characterBody), GetTeam()))
            {
                HealthComponent? hc = h.healthComponent;
                if (hc == null || !_hit.Add(hc)) continue;
                if (ShivNet.Executable(characterBody, hc))
                {
                    if (fresh.Count > 0) Send(fresh);
                    ShivNet.ToServer(new ShivExecuteMessage { Attacker = gameObject, Target = hc.gameObject });
                    activatorSkillSlot?.AddOneStock(); // user: an execute refunds the cooldown
                    DeadlockHeroVoice.Power("shiv_vo_power3", gameObject); // Bloodletting's line for the cleanse
                    Finish(true);
                    return true;
                }
                fresh.Add(hc.gameObject);
            }
            if (fresh.Count > 0) Send(fresh);
            return false;
        }

        private void Send(List<GameObject> targets) =>
            ShivNet.ToServer(new ShivHitMessage { Kind = ShivHitKind.Lunge, Attacker = gameObject, Targets = targets.ToArray(), Crit = RollCrit(), Dir = _dir });

        private bool Blocked(float dt)
        {
            Vector3 chest = characterBody.footPosition + Vector3.up * 1.2f;
            return fixedAge > _windup + 0.08f &&
                   Physics.SphereCast(chest, 0.4f, _dir, out RaycastHit hit, characterBody.radius + ShivData.LungeSpeed * dt, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) &&
                   hit.normal.y < 0.6f;
        }

        private void Finish(bool executed)
        {
            _ended = true;
            if (isAuthority) CursedAppleCuriosities.Unlocks.Challenges.ShivChainReaction.KillingBlowEnded(gameObject, executed);
            ShivModel.Play(gameObject, "slash");
            if (characterMotor != null) characterMotor.velocity = executed ? Vector3.up * 4f : _dir * characterBody.moveSpeed + Vector3.down * 2f;
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
