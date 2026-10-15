using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Apollo;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Apollo
{
    internal static class ApolloShared
    {
        public static Vector3 Flat(Vector3 d, Vector3 fallback)
        {
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : fallback;
        }

        /// <summary>Enemies a sphere touches along a line (one per body), terrain ending it.</summary>
        public static List<GameObject> Along(Vector3 from, Vector3 dir, float length, float radius, TeamIndex team)
        {
            if (Physics.Raycast(from, dir, out RaycastHit wall, length, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) length = wall.distance;
            var list = new List<GameObject>();
            foreach (RaycastHit h in Physics.SphereCastAll(from, radius, dir, length, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
            {
                var box = h.collider.GetComponent<HurtBox>();
                if (box == null || box.healthComponent == null || !box.healthComponent.alive || box.teamIndex == team) continue;
                if (!list.Contains(box.healthComponent.gameObject)) list.Add(box.healthComponent.gameObject);
            }
            return list;
        }

        public static List<GameObject> Around(Vector3 at, float radius, TeamIndex team)
        {
            var list = new List<GameObject>();
            foreach (HurtBox h in new SphereSearch { origin = at, radius = radius, mask = LayerIndex.entityPrecise.mask }
                         .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes())
                if (h.healthComponent != null && !list.Contains(h.healthComponent.gameObject)) list.Add(h.healthComponent.gameObject);
            return list;
        }

        /// <summary>Owner: hang in the air like Deadlock's casts do (FallSpeedMax 1).</summary>
        public static void Float(CharacterMotor? motor, float fall = 1f)
        {
            if (motor == null || motor.isGrounded) return;
            Vector3 v = motor.velocity;
            motor.velocity = new Vector3(v.x * 0.9f, Mathf.Max(v.y, -fall), v.z * 0.9f);
        }
    }

    // ================= Primary: Pride of Ixia =================

    /// <summary>Pride of Ixia (Deadlock citadel_weapon_fencer_set): a sword wave every 0.38 s that cuts through everything in a 25 m line.</summary>
    public class FireWave : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = ApolloData.WaveCycle / attackSpeedStat;
            var swing = GetComponent<ApolloSwing>();
            bool left = swing != null && (swing.Left = !swing.Left);
            ApolloModel.Play(gameObject, left ? "shoot_left" : "shoot");
            DeadlockSounds.Play("apollo_wave", transform.position, gameObject);
            DeadlockSounds.Play("apollo_wave_mech", transform.position, gameObject);
            StartAimMode(2f);
            if (!isAuthority) return;
            Ray aim = GetAimRay();
            if (ApolloSurvivor.WaveProjectile != null)
            {
                // A flying wave (ApolloWaveShot), from the camera ray like his old shot, so it goes where the crosshair is.
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = ApolloSurvivor.WaveProjectile, position = aim.origin, rotation = Util.QuaternionSafeLookRotation(aim.direction),
                    owner = gameObject, damage = ApolloData.WaveDamage * damageStat, crit = RollCrit(), force = 200f, speedOverride = ApolloData.WaveSpeed,
                });
                return;
            }
            new BulletAttack
            {
                owner = gameObject, weapon = gameObject, origin = aim.origin, aimVector = aim.direction, minSpread = 0f, maxSpread = 0f,
                damage = ApolloData.WaveDamage * damageStat, procCoefficient = 1f, force = 200f,
                tracerEffectPrefab = ApolloSurvivor.WaveTracer, hitEffectPrefab = ApolloSurvivor.SlashEffect, muzzleName = ApolloSurvivor.MuzzleName,
                isCrit = RollCrit(), radius = ApolloData.WaveRadius, maxDistance = ApolloData.WaveRange, smartCollision = true,
                stopperMask = LayerIndex.world.mask, // the wave passes through enemies
                falloffModel = BulletAttack.FalloffModel.None,
                damageType = DamageSource.Primary,
            }.Fire();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Disengaging Sigil =================

    /// <summary>
    /// Disengaging Sigil (Deadlock ability_fencer_throwblade): he draws a sigil 7.6 m ahead (half a second, hanging in the air), then it
    /// bursts, hurting and slowing everything in it, as he leaps back away from it.
    /// </summary>
    public class DrawSigil : BaseSkillState
    {
        private Vector3 _at, _back;
        private bool _burst;

        public override void OnEnter()
        {
            base.OnEnter();
            ApolloModel.Play(gameObject, "sigil");
            DeadlockSounds.Play("apollo_sigil_cast", transform.position, gameObject);
            DeadlockSounds.Play("apollo_sigil_charge", transform.position, gameObject);
            DeadlockHeroVoice.Power("apollo_vo_power1", gameObject);
            // Along his aim, pitch and all (user 2.5): looking down puts it under him or diagonally below; terrain stops it short.
            Ray aim = GetAimRay();
            float ahead = ApolloData.SigilAhead;
            if (Physics.Raycast(aim.origin, aim.direction, out RaycastHit wall, ahead + 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                ahead = Mathf.Max(0f, wall.distance - 0.3f);
            _at = aim.origin + aim.direction * ahead;
            if (isAuthority) ApolloNet.ToServer(new ApolloUltMessage { Apollo = gameObject, Kind = ApolloUltKind.SigilHaste });
            ApolloRingMessage.Show(_at, ApolloData.SigilRadius * MysticExpansion.RadiusMultiplier(characterBody), ApolloData.SigilCast); // every machine draws its own
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_burst) ApolloShared.Float(characterMotor);
            if (!_burst && fixedAge >= ApolloData.SigilCast) Burst();
            if (_burst && fixedAge >= ApolloData.SigilCast + 0.2f) outer.SetNextStateToMain();
        }

        private void Burst()
        {
            _burst = true;
            float radius = ApolloData.SigilRadius * MysticExpansion.RadiusMultiplier(characterBody);
            DeadlockSounds.Play("apollo_sigil_blast", _at);
            var hit = ApolloShared.Around(_at, radius, GetTeam());
            // The flip goes straight away from the sigil (user 2.5), plus a little lift.
            Vector3 away = characterBody.corePosition - _at;
            _back = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.up;
            if (hit.Count > 0)
            {
                ApolloNet.ToServer(new ApolloHitMessage { Kind = ApolloHitKind.Sigil, Attacker = gameObject, Targets = hit.ToArray(), Coefficient = ApolloData.SigilDamage, Dir = -_back, Crit = RollCrit() });
                // A hit gives back a stamina pip and his air jumps and dashes (user 2.5).
                if (TryGetComponent(out DeadlockMover mover))
                {
                    mover.Refund(1);
                    mover.AirDashes = 0;
                }
                if (characterMotor != null) characterMotor.jumpCount = Mathf.Min(characterMotor.jumpCount, 1);
            }
            if (ApolloSurvivor.SigilEffect != null) EffectManager.SpawnEffect(ApolloSurvivor.SigilEffect, new EffectData { origin = _at, scale = radius }, true);
            ApolloModel.Play(gameObject, "sigil_jump");
            if (characterMotor != null)
            {
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = _back * ApolloData.SigilLeap + Vector3.up * ApolloData.SigilLift;
                characterMotor.disableAirControlUntilCollision = false;
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Utility: Flawless Advance =================

    /// <summary>
    /// Flawless Advance (Deadlock ability_fencer_lunge): hold to ready a stab, release to lunge and run a 13 m stab through everything
    /// ahead. The longer the hold the harder it hits (250% -> 400%); released in the perfect window (0.525-0.775 s, a glint marks it) it
    /// hits for 650%. Up to three stabs in a row (ApolloAdvanceRecast).
    /// </summary>
    public class AdvanceReady : BaseSkillState
    {
        private bool _glinted;
        private Vector3 _hop, _facing;

        public override void OnEnter()
        {
            base.OnEnter();
            // A short hop the way he's moving (or straight ahead), with Deadlock's start clip for that direction (user 2.5).
            _facing = ApolloShared.Flat(GetAimRay().direction, characterDirection.forward);
            Vector3 move = inputBank != null ? new Vector3(inputBank.moveVector.x, 0f, inputBank.moveVector.z) : Vector3.zero;
            _hop = move.sqrMagnitude > 0.01f ? move.normalized : _facing;
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(_facing)) * _hop;
            string dir = Mathf.Abs(local.z) >= Mathf.Abs(local.x) ? (local.z >= 0f ? "n" : "s") : (local.x > 0f ? "e" : "w");
            ApolloModel.Play(gameObject, "lunge_start_" + dir);
            DeadlockSounds.Play("apollo_lunge_charge", transform.position, gameObject);
            var recast = GetComponent<ApolloAdvanceRecast>();
            if (recast == null || recast.Used == 0) DeadlockHeroVoice.Power("apollo_vo_power2", gameObject); // the lunge's lines are use_power2 (Deadlock's captions)
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!_glinted && fixedAge >= ApolloData.PerfectStart) { _glinted = true; DeadlockSounds.Play("apollo_lunge_glint", transform.position, gameObject); }
            if (!isAuthority) return;
            // The hop, then he hangs there while he readies it: gravity doesn't touch him (user 2.5).
            if (characterMotor != null)
            {
                characterDirection.forward = _facing;
                characterMotor.disableAirControlUntilCollision = false;
                if (fixedAge < ApolloData.ReadyDashTime)
                {
                    characterMotor.velocity = Vector3.zero;
                    characterMotor.rootMotion += _hop * (ApolloData.ReadyDash / ApolloData.ReadyDashTime) * Time.fixedDeltaTime;
                }
                else
                {
                    Vector3 v = characterMotor.velocity;
                    characterMotor.velocity = new Vector3(v.x, 0f, v.z);
                }
            }
            bool released = inputBank == null || !inputBank.skill3.down;
            if ((released && fixedAge >= ApolloData.HoldMin) || fixedAge >= ApolloData.HoldMax) outer.SetNextState(new AdvanceStab { Held = fixedAge });
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    public class AdvanceStab : BaseSkillState
    {
        public float Held;
        private Vector3 _dir, _dash;
        private bool _stabbed;

        public override void OnEnter()
        {
            base.OnEnter();
            ApolloModel.Play(gameObject, "lunge");
            DeadlockSounds.Play("apollo_lunge_cast", transform.position, gameObject);
            _dir = GetAimRay().direction;
            _dash = ApolloShared.Flat(_dir, characterDirection.forward); // the stab lunges where he aims (the hop was when he readied it)
            if (isAuthority && characterMotor != null) characterMotor.disableAirControlUntilCollision = false;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            float dashTime = ApolloData.StabDash / ApolloData.StabDashSpeed;
            if (fixedAge < dashTime)
            {
                characterMotor.velocity = Vector3.zero;
                characterMotor.rootMotion += _dash * ApolloData.StabDashSpeed * Time.fixedDeltaTime;
                return;
            }
            if (!_stabbed) Stab();
            if (fixedAge >= dashTime + 0.15f) outer.SetNextStateToMain();
        }

        private void Stab()
        {
            _stabbed = true;
            bool perfect = Held >= ApolloData.PerfectStart && Held <= ApolloData.PerfectStart + ApolloData.PerfectLength;
            float t = Mathf.InverseLerp(ApolloData.HoldMin, ApolloData.HoldMax, Held);
            float coeff = perfect ? ApolloData.StabPerfect : Mathf.Lerp(ApolloData.StabMin, ApolloData.StabMax, t);
            float length = ApolloData.StabLength * MysticExpansion.RadiusMultiplier(characterBody);
            var hit = ApolloShared.Along(characterBody.corePosition, _dir, length, ApolloData.StabRadius, GetTeam());
            if (hit.Count > 0)
                ApolloNet.ToServer(new ApolloHitMessage { Kind = perfect ? ApolloHitKind.PerfectStab : ApolloHitKind.Stab, Attacker = gameObject, Targets = hit.ToArray(), Coefficient = coeff, Dir = _dir, Crit = RollCrit() });
            if (ApolloSurvivor.WaveTracer != null)
                EffectManager.SpawnEffect(ApolloSurvivor.WaveTracer, new EffectData { origin = characterBody.corePosition + _dir * length, start = characterBody.corePosition }, true);
            GetComponent<ApolloAdvanceRecast>()?.Stabbed();
            if (characterMotor != null) characterMotor.velocity = _dash * characterBody.moveSpeed;
        }

        public override void OnSerialize(UnityEngine.Networking.NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Held); }
        public override void OnDeserialize(UnityEngine.Networking.NetworkReader reader) { base.OnDeserialize(reader); Held = reader.ReadSingle(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Utility alt: Riposte =================

    /// <summary>
    /// Riposte (Deadlock ability_fencer_riposte): a 0.8 s stance; the first hit that lands on him is deflected (ApolloNet). Nothing
    /// comes: the stance just ends. A parry no longer dashes on its own (user 3.0): it opens the counter (ApolloCounter), and pressing
    /// Riposte again then dashes into the counter slash (RiposteDash) on the enemy nearest the crosshair within 50 m.
    /// </summary>
    public class Riposte : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority && TryGetComponent(out ApolloCounter counter) && counter.Open)
            {
                CharacterBody? target = ApolloCounter.Target(characterBody);
                if (target == null)
                {
                    activatorSkillSlot?.AddOneStock(); // nobody in reach: keep the counter for a better moment
                    outer.SetNextStateToMain();
                    return;
                }
                counter.Use();
                outer.SetNextState(new RiposteDash { Target = target.gameObject });
                return;
            }
            ApolloModel.Play(gameObject, "riposte");
            DeadlockSounds.Play("apollo_riposte_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("apollo_vo_power3", gameObject); // Riposte's lines are use_power3 (Deadlock's captions)
            if (isAuthority) ApolloNet.ToServer(new ApolloParryMessage { Apollo = gameObject, On = true });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (characterMotor != null) characterMotor.moveDirection = inputBank != null ? inputBank.moveVector * 0.4f : Vector3.zero;
            ApolloShared.Float(characterMotor);
            if (fixedAge >= ApolloData.RiposteWindow) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (isAuthority) ApolloNet.ToServer(new ApolloParryMessage { Apollo = gameObject, On = false });
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    /// <summary>Riposte's counter: a fast dash to the chosen enemy (50 m at most), then a slash that stuns and slows everything close.</summary>
    public class RiposteDash : BaseState
    {
        public GameObject? Target;
        private bool _slashed;

        public override void OnEnter()
        {
            base.OnEnter();
            ApolloModel.Play(gameObject, "riposte_dash");
            DeadlockSounds.Play("apollo_riposte_dash", transform.position, gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || characterMotor == null) return;
            Vector3 goal = Target != null && Target.TryGetComponent(out CharacterBody t) ? t.corePosition : characterBody.corePosition + characterDirection.forward * 3f;
            Vector3 to = goal - characterBody.corePosition;
            bool far = to.magnitude > 2.5f && to.magnitude < ApolloData.RiposteCounterRange * 1.5f;
            if (!_slashed && far && fixedAge < 0.9f) // 50 m at 76 m/s takes ~0.66 s
            {
                characterMotor.velocity = Vector3.zero;
                characterMotor.rootMotion += to.normalized * Mathf.Min(to.magnitude - 2f, ApolloData.RiposteDashSpeed * Time.fixedDeltaTime);
                characterDirection.forward = ApolloShared.Flat(to, characterDirection.forward);
                return;
            }
            if (!_slashed) Slash(ApolloShared.Flat(to, characterDirection.forward));
            if (fixedAge >= 0.8f) outer.SetNextStateToMain();
        }

        private void Slash(Vector3 dir)
        {
            _slashed = true;
            var hit = new List<GameObject>();
            foreach (GameObject g in ApolloShared.Around(characterBody.corePosition, ApolloData.RiposteSlashRadius, GetTeam()))
            {
                Vector3 to = g.transform.position - characterBody.corePosition;
                to.y = 0f;
                if (to.sqrMagnitude < 1f || Vector3.Angle(dir, to) <= 45f) hit.Add(g); // Deadlock SlashConeAngle 90
            }
            if (hit.Count > 0) ApolloNet.ToServer(new ApolloHitMessage { Kind = ApolloHitKind.Riposte, Attacker = gameObject, Targets = hit.ToArray(), Coefficient = ApolloData.RiposteDamage, Dir = dir, Crit = RollCrit() });
        }

        public override void OnSerialize(UnityEngine.Networking.NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Target); }
        public override void OnDeserialize(UnityEngine.Networking.NetworkReader reader) { base.OnDeserialize(reader); Target = reader.ReadGameObject(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }

    // ================= Special: Itani Lo Sahn =================

    /// <summary>
    /// Itani Lo Sahn (Deadlock ability_fencer_ultimate): masked, he channels, then teleports 27 m along a 7 m-wide line on release.
    /// Everything struck is stuck in slow motion for 1.8 s (stunned, unless a boss) while he holds his pose, untouchable; then they take
    /// the blow (ApolloUltMark), harder if they're under half health.
    /// </summary>
    public class ItaniLoSahn : BaseSkillState
    {
        // Itani Lo Sahn (Deadlock ability_fencer_ultimate; reworked, user 2.5): he dons his mask, glowing red, says the line and
        // channels in place (ChannelMoveSpeed 0, FallSpeedMax 1, 70% less damage taken). After the 1.5 s cast delay he is ready;
        // letting go of the key strikes: at DashSpeed 10000 hu/s it is a teleport, 27 m along his aim (short of walls), slashing
        // everything within 7 m of the line. Let go early and he strikes the moment he's ready. Then the 1.8 s lock as before.
        private bool _ready, _released, _struck;
        private float _struckAt;
        private DeadlockVoice? _lock;

        public override void OnEnter()
        {
            base.OnEnter();
            ApolloModel.Play(gameObject, "ult_start");
            DeadlockSounds.Play("apollo_ult_cast", transform.position, gameObject);
            DeadlockSounds.Play("apollo_ult_charge", transform.position, gameObject);
            DeadlockSounds.Play("apollo_ult_voice", transform.position, gameObject); // "Itani lo sahn..." (fencer_use_power4_start_02)
            if (isAuthority) ApolloNet.ToServer(new ApolloUltMessage { Apollo = gameObject, Kind = ApolloUltKind.Channel });
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (!isAuthority || characterMotor == null) return;
            if (!_struck)
            {
                ApolloShared.Float(characterMotor);
                characterMotor.velocity = new Vector3(0f, Mathf.Max(characterMotor.velocity.y, -1f), 0f); // rooted, falling 1 m/s at most
                characterMotor.moveDirection = Vector3.zero;
                if (inputBank == null || !inputBank.skill4.down) _released = true;
                if (!_ready && fixedAge >= ApolloData.UltCharge)
                {
                    _ready = true;
                    DeadlockSounds.Play("apollo_lunge_glint", transform.position, gameObject); // ready to strike
                }
                if (_ready && (_released || fixedAge >= ApolloData.UltCharge + ApolloData.UltMaxHold)) Strike();
                return;
            }
            characterMotor.velocity = Vector3.zero; // holding the finish while they're in slow motion
            if (fixedAge - _struckAt >= ApolloData.UltLock) outer.SetNextStateToMain();
        }

        private void Strike()
        {
            _struck = true;
            _struckAt = fixedAge;
            Vector3 dir = GetAimRay().direction;
            dir = Vector3.Normalize(new Vector3(dir.x, Mathf.Clamp(dir.y, -0.5f, 0.5f), dir.z));
            Vector3 from = characterBody.corePosition;
            float range = ApolloData.UltDash * MysticExpansion.RadiusMultiplier(characterBody);
            // Short of walls (Deadlock GapDistanceToWall), with his body's room.
            float reach = range;
            if (Physics.SphereCast(from, 0.6f, dir, out RaycastHit wall, range, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) reach = Mathf.Max(0f, wall.distance - 0.5f);
            var targets = ApolloShared.Along(from, dir, reach + 1f, ApolloData.UltRadius * MysticExpansion.RadiusMultiplier(characterBody), GetTeam());
            Vector3 to = from + dir * reach;
            ApolloModel.Play(gameObject, "ult_dash");
            characterMotor.Motor.ForceUnground();
            characterMotor.Motor.SetPosition(characterBody.footPosition + (to - from), true);
            characterDirection.forward = ApolloShared.Flat(dir, characterDirection.forward);
            ApolloNet.ToServer(new ApolloUltMessage { Apollo = gameObject, Kind = ApolloUltKind.Strike, From = from, To = to });
            ApolloNet.ToServer(new ApolloHitMessage { Kind = ApolloHitKind.Ult, Attacker = gameObject, Targets = targets.ToArray(), Coefficient = ApolloData.UltImpact, Dir = dir, Crit = RollCrit() });
            _lock = DeadlockSounds.Loop("apollo_ult_lock", gameObject, ApolloData.UltLock); // (the server makes him untouchable on that message)
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_lock);
            if (isAuthority) ApolloNet.ToServer(new ApolloUltMessage { Apollo = gameObject, Kind = ApolloUltKind.End });
            ApolloModel.Play(gameObject, _struck ? "ult_finale" : "idle");
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
