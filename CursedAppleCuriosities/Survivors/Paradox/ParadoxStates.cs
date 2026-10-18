using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Paradox;
using RoR2;
using RoR2.HudOverlay;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Paradox
{
    internal static class ParadoxShared
    {
        public static string Muzzle(Transform? gun) =>
            gun != null && gun.name == ParadoxData.MuzzleBone ? ParadoxSurvivor.MuzzleName : "MuzzleRight";
    }

    // ================= Primary: the burst rifle =================

    /// <summary>Paradox's rifle (Deadlock citadel_weapon_chrono_set): 5-round bursts, 0.0735 s apart, 0.294 s between bursts.</summary>
    public class FireBurst : BaseSkillState
    {
        private float _interval, _duration;
        private int _fired;
        private string _muzzle = "MuzzleRight";

        public override void OnEnter()
        {
            base.OnEnter();
            _interval = ParadoxData.BurstInterval / attackSpeedStat;
            _duration = (ParadoxData.BurstInterval * (ParadoxData.BurstCount - 1) + ParadoxData.BurstCycle) / attackSpeedStat;
            _muzzle = ParadoxShared.Muzzle(FindModelChild(ParadoxSurvivor.MuzzleName));
            StartAimMode(2f);
            Fire();
        }

        private void Fire()
        {
            _fired++;
            ParadoxModel.Play(gameObject, "shoot");
            DeadlockSounds.Play("paradox_gun", transform.position, gameObject);
            if (ParadoxSurvivor.MuzzleEffect != null) EffectManager.SimpleMuzzleFlash(ParadoxSurvivor.MuzzleEffect, gameObject, _muzzle, false);
            AddRecoil(-0.3f * _fired, -0.4f * _fired, -0.1f, 0.1f);
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
                    maxSpread = ParadoxData.GunSpread + characterBody.spreadBloomAngle * 0.3f,
                    damage = ParadoxData.GunDamage * damageStat,
                    procCoefficient = ParadoxData.GunProc,
                    force = 80f,
                    tracerEffectPrefab = ParadoxSurvivor.TracerEffect,
                    hitEffectPrefab = ParadoxSurvivor.HitEffect,
                    muzzleName = _muzzle,
                    isCrit = RollCrit(),
                    radius = 0.1f,
                    smartCollision = true,
                    falloffModel = BulletAttack.FalloffModel.None,
                    modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                        info.damage *= Kit.Falloff(hit.distance, ParadoxData.GunFalloffStart, ParadoxData.GunFalloffEnd, ParadoxData.GunFalloffMin),
                    damageType = DamageSource.Primary,
                }.Fire();
            }
            characterBody.AddSpreadBloom(0.2f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (_fired < ParadoxData.BurstCount && fixedAge >= _interval * _fired) Fire();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill; // Any would let a held trigger restart the burst during its cycle time
    }

    // ================= Secondary: Pulse Bomb =================

    /// <summary>Pulse Bomb (Deadlock citadel_ability_chrono_pulse_grenade): an arcing throw, a little above the crosshair (Deadlock m_flVerticalAimBias).</summary>
    public class ThrowPulseBomb : BaseSkillState
    {
        // Hold to aim with the arc and the first pulse's ring shown, release to throw (user 2.5, as Ivy's Kudzu Bomb).
        private CursedAppleCuriosities.Survivors.Ivy.IvyThrowPreview? _preview;
        private static readonly Color Amber = new Color(1f, 0.72f, 0.3f, 0.9f);

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority && characterBody.isPlayerControlled)
                _preview = new CursedAppleCuriosities.Survivors.Ivy.IvyThrowPreview(ParadoxData.PulseRadius * MysticExpansion.RadiusMultiplier(characterBody), showArc: true, Amber);
        }

        private Ray Throw()
        {
            Ray aim = GetAimRay();
            Vector3 right = Vector3.Cross(Vector3.up, aim.direction);
            Vector3 dir = right.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(-ParadoxData.PulseThrowUp, right.normalized) * aim.direction : aim.direction;
            return new Ray(aim.origin + aim.direction * 0.5f, dir);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!isAuthority) return;
            Ray t = Throw();
            _preview?.Update(t.origin, t.direction * ParadoxData.PulseThrowSpeed, ParadoxData.PulseRadius * MysticExpansion.RadiusMultiplier(characterBody));
            if (inputBank != null && inputBank.skill2.down && fixedAge < 10f) return;
            ParadoxModel.Play(gameObject, "pulse");
            DeadlockSounds.Play("paradox_pulse_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("paradox_vo_power1", gameObject);
            if (ParadoxSurvivor.PulseProjectile != null)
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = ParadoxSurvivor.PulseProjectile,
                    position = t.origin,
                    rotation = Quaternion.LookRotation(t.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = ParadoxData.PulseThrowSpeed,
                });
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            _preview?.Destroy();
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary alt: Time Wall =================

    /// <summary>Time Wall (Deadlock citadel_ability_chrono_time_wall): an 8 m wall stood up 5 m in front of her, across her view.</summary>
    public class PlaceTimeWall : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            ParadoxModel.Play(gameObject, "wall");
            DeadlockSounds.Play("paradox_wall_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("paradox_vo_power2", gameObject);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                Vector3 flat = new Vector3(aim.direction.x, 0f, aim.direction.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
                Vector3 spot = characterBody.footPosition + flat * ParadoxData.WallDistance;
                // Stand it on the ground under that spot (or where the view hits it first).
                if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 12f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) spot = ground.point;
                // Leave the state before the wall is built: an error building it (on a host it builds right here) once left her
                // weapon machine stuck in this state, her primary dead (user 2.5).
                outer.SetNextStateToMain();
                ParadoxNet.ToServer(new ParadoxWallMessage { Owner = gameObject, Position = spot, Rotation = Quaternion.LookRotation(flat) });
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Utility: Kinetic Carbine =================

    /// <summary>
    /// Kinetic Carbine (Deadlock citadel_ability_chrono_kinetic_carbine): a tap of the utility starts the charge (up to 2.5 s,
    /// growing faster and the shot stronger); her next primary click fires one big shot (user 2.5). Full charge holds for 5 s
    /// (Duration Extender lengthens it); if she never fires, the shot is wasted, never forced. Fully charged it can hit weak points
    /// and shows them (Railgunner's scope view); whatever it hits is slowed hard, the slow fading fast (user).
    /// </summary>
    public class KineticCarbine : BaseSkillState
    {
        private float _charge, _fullFor, _nextSync, _hold;
        private bool _fired, _primaryReleased;
        private DeadlockVoice? _hum;
        private OverlayController? _scope;
        private ParadoxCarbineGlow? _glow;

        private float Fraction => Mathf.Clamp01(_charge / ParadoxData.CarbineChargeSeconds);

        public override void OnEnter()
        {
            base.OnEnter();
            ParadoxModel.Play(gameObject, "carbine_charge");
            DeadlockHeroVoice.Power("paradox_vo_power3", gameObject);
            _hold = ParadoxData.CarbineHoldSeconds * DurationExtender.Multiplier(characterBody);
            _hum = DeadlockSounds.Loop("paradox_carbine_loop", gameObject, ParadoxData.CarbineChargeSeconds + _hold + 0.5f);
            _glow = gameObject.AddComponent<ParadoxCarbineGlow>(); // she glows blue while the Carbine is active (user 3.0)
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (_glow != null) _glow.Charge = Mathf.Clamp01(fixedAge / ParadoxData.CarbineChargeSeconds); // every machine: the state's own clock
            if (!isAuthority || _fired) return;
            bool wasFull = Fraction >= 1f;
            _charge += Time.fixedDeltaTime;
            if (Fraction >= 1f)
            {
                _fullFor += Time.fixedDeltaTime;
                if (!wasFull) Full();
            }
            if (Time.time >= _nextSync)
            {
                _nextSync = Time.time + 0.2f;
                ParadoxNet.ToServer(new ParadoxCarbineMessage { Body = gameObject, Charge = Fraction });
            }
            // A fresh click fires (a trigger already held when she tapped the utility has to be let go first).
            if (!inputBank.skill1.down) _primaryReleased = true;
            else if (_primaryReleased) { Fire(); return; }
            if (_fullFor >= _hold) outer.SetNextStateToMain(); // never fired: the charge is wasted (user)
        }

        private void Full()
        {
            DeadlockSounds.Play("paradox_carbine_full", transform.position, gameObject);
            // Railgunner's scope view, which marks every weak point in sight (its scope frame stays hidden).
            if (ParadoxSurvivor.ScopeOverlay != null && characterBody.isPlayerControlled)
            {
                _scope = HudOverlayManager.AddOverlay(gameObject, new OverlayCreationParams { prefab = ParadoxSurvivor.ScopeOverlay, childLocatorEntry = "ScopeContainer" });
                _scope.onInstanceAdded += (_, instance) =>
                {
                    foreach (UnityEngine.UI.Graphic g in instance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.enabled = false; // just the weak-point markers, spawned later
                };
            }
        }

        private void Fire()
        {
            _fired = true;
            float f = Fraction;
            bool full = f >= 1f;
            ParadoxModel.Play(gameObject, "carbine_shoot");
            DeadlockSounds.Play("paradox_carbine_fire", transform.position, gameObject);
            string muzzle = ParadoxShared.Muzzle(FindModelChild(ParadoxSurvivor.MuzzleName));
            if (ParadoxSurvivor.CarbineMuzzle != null) EffectManager.SimpleMuzzleFlash(ParadoxSurvivor.CarbineMuzzle, gameObject, muzzle, false);
            AddRecoil(-3f * f, -4f * f, -0.5f, 0.5f);
            Ray aim = GetAimRay();
            GameObject self = gameObject;
            new BulletAttack
            {
                owner = gameObject,
                weapon = gameObject,
                origin = aim.origin,
                aimVector = aim.direction,
                minSpread = 0f,
                maxSpread = 0f,
                damage = Mathf.Lerp(ParadoxData.CarbineMinDamage, ParadoxData.CarbineMaxDamage, f) * damageStat,
                procCoefficient = 1f, // procs (user 3.0: her rifle and this shot are her only procs; a big charged hit should set off Bands, Tankbuster...)
                force = 1500f * f,
                tracerEffectPrefab = ParadoxSurvivor.CarbineTracer,
                hitEffectPrefab = ParadoxSurvivor.CarbineHit,
                muzzleName = muzzle,
                isCrit = RollCrit(),
                radius = ParadoxData.CarbineRadius,
                maxDistance = 1000f,
                smartCollision = true,
                sniper = full, // fully charged: weak points (user)
                falloffModel = BulletAttack.FalloffModel.None,
                stopperMask = LayerIndex.world.mask,
                hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
                {
                    bool result = BulletAttack.defaultHitCallback(attack, ref hit);
                    HealthComponent? hc = hit.hitHurtBox != null ? hit.hitHurtBox.healthComponent : null;
                    if (hc != null && hit.hitHurtBox!.teamIndex != TeamComponent.GetObjectTeam(self))
                        ParadoxNet.ToServer(new ParadoxSlowMessage { Attacker = self, Target = hc.gameObject, Amount = ParadoxData.CarbineSlow, Seconds = ParadoxData.CarbineSlowSeconds * Mathf.Max(0.4f, f) });
                    return result;
                },
                damageType = DamageSource.Utility,
            }.Fire();
            ParadoxNet.ToServer(new ParadoxCarbineMessage { Body = gameObject, Charge = f, Fired = true });
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_hum);
            if (_glow != null) Object.Destroy(_glow);
            if (_scope != null) HudOverlayManager.RemoveOverlay(_scope);
            if (isAuthority && !_fired) ParadoxNet.ToServer(new ParadoxCarbineMessage { Body = gameObject, Cancelled = true });
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Paradoxical Swap =================

    /// <summary>
    /// Paradoxical Swap (Deadlock citadel_ability_chrono_swap): a short cast, then a big, fast shot (like Grey Talon's Charged Shot,
    /// with a limited range: user). Whatever enemy it reaches swaps places with her (ParadoxSwap).
    /// </summary>
    public class SwapCast : BaseSkillState
    {
        private bool _fired;

        public override void OnEnter()
        {
            base.OnEnter();
            ParadoxModel.Play(gameObject, "swap_cast");
            DeadlockSounds.Play("paradox_swap_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("paradox_vo_power4", gameObject);
            StartAimMode(1f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_fired && fixedAge >= ParadoxData.SwapCastDelay)
            {
                _fired = true;
                if (ParadoxSurvivor.SwapProjectile != null)
                {
                    Ray aim = GetAimRay();
                    ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                    {
                        projectilePrefab = ParadoxSurvivor.SwapProjectile,
                        position = aim.origin,
                        rotation = Quaternion.LookRotation(aim.direction),
                        owner = gameObject,
                        damage = damageStat * ParadoxData.SwapDamage,
                        crit = RollCrit(),
                        force = 0f,
                        speedOverride = ParadoxData.SwapSpeed,
                        damageTypeOverride = DamageSource.Special,
                    });
                }
            }
            if (fixedAge >= ParadoxData.SwapCastDelay + 0.15f) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    /// <summary>
    /// Her half of the swap (Body machine, entered from the server's ParadoxSwapMessage on her owner): along a mirrored arc to where
    /// the enemy stood, passing it on the way.
    /// </summary>
    public class ParadoxSwapping : BaseState
    {
        public Vector3 From, To;
        public float Duration = 1f;
        /// <summary>The enemy she swaps with: her camera stays on it the whole way (user 2.5).</summary>
        public GameObject? Enemy;
        private Vector3 _start;
        private CameraRigController? _rig;
        private static System.Reflection.MethodInfo? _setLook;

        public override void OnEnter()
        {
            base.OnEnter();
            ParadoxModel.Play(gameObject, "swap");
            _start = characterBody != null ? characterBody.footPosition : transform.position;
            if (isAuthority && characterMotor != null) characterMotor.Motor.ForceUnground();
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
                if (rig.target == gameObject && rig.localUserViewer != null) _rig = rig;
        }

        public override void Update()
        {
            base.Update();
            // Facing the enemy as they pass each other. Aimed from her own body, not the camera (the camera chases her, and aiming
            // from it fed back into itself: jittery, user 2.5), and eased toward it rather than snapped, with no roll of the pitch.
            if (_rig == null || Enemy == null || !Enemy.TryGetComponent(out CharacterBody e) || characterBody == null) return;
            Vector3 want = e.corePosition - characterBody.corePosition;
            if (want.sqrMagnitude < 0.25f || _rig.cameraMode is not RoR2.CameraModes.CameraModeBase mode) return;
            want.y = Mathf.Clamp(want.y, -want.magnitude * 0.6f, want.magnitude * 0.6f);
            Vector3 look = Vector3.Slerp(_rig.transform.forward, want.normalized, 1f - Mathf.Exp(-8f * Time.deltaTime));
            // The player camera's own pitch/yaw setter (CameraModePlayerBasic.InstanceData, internal: reached by reflection).
            object? cam = mode.DebugGetInstanceData(_rig);
            if (cam == null) return;
            _setLook ??= cam.GetType().GetMethod("SetPitchYawFromLookVector", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _setLook?.Invoke(cam, new object[] { look });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            float t = fixedAge + Time.fixedDeltaTime;
            Vector3 want = ParadoxSwap.PathAt(_start, To, t, Duration, 1f);
            characterMotor.velocity = Vector3.zero;
            characterMotor.moveDirection = Vector3.zero;
            characterMotor.rootMotion += want - characterBody.footPosition;
            Vector3 flat = To - _start;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) characterDirection.forward = flat.normalized;
            if (fixedAge >= Duration) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            ParadoxModel.Play(gameObject, "swap_end");
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(From); writer.Write(To); writer.Write(Duration); writer.Write(Enemy); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); From = reader.ReadVector3(); To = reader.ReadVector3(); Duration = reader.ReadSingle(); Enemy = reader.ReadGameObject(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
