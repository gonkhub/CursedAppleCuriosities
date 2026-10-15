using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Bebop;
using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Bebop
{
    internal static class BebopShared
    {
        public static List<HurtBox> Enemies(Vector3 center, float radius, TeamIndex team) =>
            new List<HurtBox>(new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes());

        public static Vector3 Muzzle(GameObject body)
        {
            Transform? model = body.GetComponent<ModelLocator>()?.modelTransform;
            Transform? m = model != null ? model.GetComponent<ChildLocator>()?.FindChild(BebopSurvivor.MuzzleName) : null;
            return m != null ? m.position : body.GetComponent<CharacterBody>().corePosition;
        }
    }

    // ================= Primary: Humble Pie =================

    /// <summary>
    /// Humble Pie (Deadlock citadel_weapon_bebop_set, EBulletHandler_Beam): hold to fire a continuous beam with no travel. A short wind-up
    /// (0.3 s; 0.05 s when he picks it straight back up), then a damage tick every 0.084 s on whatever the beam touches first, up to
    /// 32 m. The beam is drawn on every machine (BebopGunBeam); the ticks are the owner's bullets. Its sound is Deadlock's beam loop
    /// layered with one of the eight jazz loops.
    /// </summary>
    public class FireBeam : BaseSkillState
    {
        private BebopGunBeam? _fx;
        private float _windUp, _nextTick, _nextAnim;
        private bool _firing;
        private string _jazz = "a";
        private DeadlockVoice? _loop, _jazzLoop;

        public override void OnEnter()
        {
            base.OnEnter();
            _fx = GetComponent<BebopGunBeam>();
            bool quick = _fx != null && Time.time - _fx.LastStop < BebopData.BeamQuickWindow;
            _windUp = (quick ? BebopData.BeamQuickWindUp : BebopData.BeamWindUp) / attackSpeedStat;
            if (!quick) DeadlockSounds.Play("bebop_beam_windup", transform.position, gameObject);
            _jazz = BebopData.Jazz[Random.Range(0, BebopData.Jazz.Length)];
            StartAimMode(2f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (characterBody != null) characterBody.isSprinting = false;
            if (!_firing && fixedAge >= _windUp) Start();
            if (_firing && fixedAge >= _nextAnim) { _nextAnim = fixedAge + 0.35f; BebopModel.Play(gameObject, "shoot"); }
            if (!isAuthority) return;
            if (_firing && fixedAge >= _nextTick)
            {
                _nextTick = fixedAge + BebopData.BeamTick / attackSpeedStat;
                Tick();
            }
            if (inputBank == null || !inputBank.skill1.down) outer.SetNextStateToMain();
        }

        private void Start()
        {
            _firing = true;
            _nextTick = fixedAge;
            if (_fx != null) _fx.Active = true;
            if (isAuthority) CursedAppleCuriosities.CursedAppleCuriositiesPlugin.Logger?.LogDebug($"[Bebop] Humble Pie on (wind-up {_windUp:0.00} s)");
            DeadlockSounds.Play("bebop_beam_start", transform.position, gameObject);
            _loop = DeadlockSounds.Loop("bebop_beam_loop", gameObject, 600f);
            _jazzLoop = DeadlockSounds.Loop("bebop_beam_jazz_" + _jazz, gameObject, 600f);
        }

        private void Tick()
        {
            Ray aim = GetAimRay();
            new BulletAttack
            {
                owner = gameObject,
                weapon = gameObject,
                origin = aim.origin,
                aimVector = aim.direction,
                minSpread = 0f,
                maxSpread = 0f,
                damage = BebopData.BeamDamage * damageStat,
                procCoefficient = BebopData.BeamProc,
                force = 20f,
                hitEffectPrefab = BebopSurvivor.BeamHitEffect,
                muzzleName = BebopSurvivor.MuzzleName,
                isCrit = RollCrit(),
                radius = BebopData.BeamRadius,
                maxDistance = BebopData.BeamRange,
                smartCollision = true,
                falloffModel = BulletAttack.FalloffModel.None,
                modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                    info.damage *= Kit.Falloff(hit.distance, BebopData.BeamFalloffStart, BebopData.BeamFalloffEnd, BebopData.BeamFalloffMin),
                damageType = DamageSource.Primary,
            }.Fire();
        }

        public override void OnExit()
        {
            if (_fx != null) { _fx.Active = false; if (_firing) _fx.LastStop = Time.time; }
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Stop(_jazzLoop);
            if (_firing)
            {
                DeadlockSounds.Play("bebop_beam_end", transform.position, gameObject);
                DeadlockSounds.Play("bebop_beam_jazz_end_" + _jazz, transform.position, gameObject);
            }
            base.OnExit();
        }

        // Skill, not Any: held primary re-activates its skill every frame, and Any let that restart the beam (and its wind-up) each time.
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Sticky Bomb =================

    /// <summary>
    /// Sticky Bomb, the user's version: Seven's Static Charge flow with a very short reach (Deadlock's 6 m) and no stun. A tap sticks it
    /// on the enemy or ally (3.0) nearest the crosshair (reticle shows it; no one in reach = no cast, no cooldown); holding the key sticks it on
    /// himself (user 2.5, Deadlock's self-cast). Casting gives him +12 m/s for a moment. BebopStickyBomb does the rest.
    /// </summary>
    public class ThrowStickyBomb : BaseSkillState
    {
        /// <summary>
        /// The body nearest the crosshair in reach, enemy or ally (user 3.0: allies can carry it too; the blast only hurts enemies,
        /// and only Bebop himself takes the self-cast share). No one in the cone: whatever enemy the crosshair is on.
        /// </summary>
        public static CharacterBody? Target(CharacterBody body)
        {
            float range = BebopData.StickyRange * MysticExpansion.RadiusMultiplier(body);
            if (body.inputBank != null && body.teamComponent != null)
            {
                Ray aim = body.inputBank.GetAimRay();
                TeamMask teams = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);
                teams.AddTeam(body.teamComponent.teamIndex);
                var search = new BullseyeSearch
                {
                    teamMaskFilter = teams, filterByLoS = true, searchOrigin = aim.origin, searchDirection = aim.direction,
                    maxDistanceFilter = range, maxAngleFilter = BebopData.StickyConeDeg, sortMode = BullseyeSearch.SortMode.Angle,
                };
                search.RefreshCandidates();
                search.FilterOutGameObject(body.gameObject);
                foreach (HurtBox hb in search.GetResults())
                {
                    CharacterBody? b = hb != null && hb.healthComponent != null ? hb.healthComponent.body : null;
                    if (b != null && b.healthComponent.alive) return b;
                }
            }
            return EquipmentTargeting.FindEnemy(body, range, BebopData.StickyConeDeg, _ => true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            bool held = inputBank != null && inputBank.skill2.down;
            if (held && fixedAge < BebopData.StickySelfHold) return; // tap or hold? not yet known
            CharacterBody? target = held ? characterBody : Target(characterBody);
            if (target == null)
            {
                activatorSkillSlot?.AddOneStock(); // refund
                outer.SetNextStateToMain();
                return;
            }
            BebopModel.Play(gameObject, "sticky");
            DeadlockSounds.Play("bebop_sticky_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("bebop_vo_power3", gameObject); // Deadlock's Sticky Bomb lines are use_power3 (its captions)
            StartAimMode(1f);
            BebopNet.ToServer(new BebopStickyMessage { Attacker = gameObject, Target = target.gameObject, Crit = RollCrit() });
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary alt: Grapple Arm =================

    /// <summary>
    /// Grapple Arm (Deadlock citadel_ability_hook): his hand flies out along the aim (72 m/s, 30 m). The first enemy it touches is latched
    /// and reeled in (BebopHookPull) and takes more from his beam for a while; terrain or the end of its reach sends it back. Pressing
    /// the button again cancels it (user) without spending a charge, so Backup Magazine's extra charges each give another hook.
    /// </summary>
    public class FireHook : BaseSkillState
    {
        private enum Phase { Fly, Latched, Return }
        private Phase _phase;
        private Vector3 _point, _dir;
        private float _travelled, _latchedAt;
        private HealthComponent? _target;
        private GameObject? _chainObj, _head;
        private LineRenderer? _chain;
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            BebopModel.Play(gameObject, "hook");
            DeadlockSounds.Play("bebop_hook_shoot", transform.position, gameObject);
            DeadlockHeroVoice.Power("bebop_vo_power_hook", gameObject); // the hook's own lines (bebop_use_hook)
            Ray aim = GetAimRay();
            _dir = aim.direction;
            _point = BebopShared.Muzzle(gameObject);
            _loop = DeadlockSounds.Loop("bebop_hook_loop", gameObject, BebopData.HookRange / BebopData.HookSpeed * 2f + 2f);
            StartAimMode(1.5f);
            Build();
        }

        /// <summary>Every machine: the chain (a line from his gun) and the hand at its end (Loader's hook).</summary>
        private void Build()
        {
            _chainObj = new GameObject("BebopHookChain");
            _chain = _chainObj.AddComponent<LineRenderer>();
            _chain.positionCount = 2;
            _chain.widthMultiplier = 0.12f;
            _chain.material = BebopSurvivor.ChainMaterial;
            _chain.startColor = _chain.endColor = new Color(0.55f, 0.5f, 0.45f);
            if (BebopSurvivor.HookHead != null)
            {
                _head = Object.Instantiate(BebopSurvivor.HookHead);
                _head.SetActive(true);
            }
        }

        public override void Update()
        {
            base.Update();
            Vector3 from = BebopShared.Muzzle(gameObject);
            if (_chain != null) { _chain.SetPosition(0, from); _chain.SetPosition(1, _point); }
            if (_head != null) _head.transform.SetPositionAndRotation(_point, Util.QuaternionSafeLookRotation(_point - from));
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            float dt = Time.fixedDeltaTime;
            float range = BebopData.HookRange * MysticExpansion.RadiusMultiplier(characterBody);
            switch (_phase)
            {
                case Phase.Fly:
                    Vector3 step = _dir * BebopData.HookSpeed * dt;
                    if (isAuthority && Contact(step)) break;
                    if (Physics.SphereCast(_point, 0.2f, _dir, out RaycastHit wall, step.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        _point = wall.point;
                        DeadlockSounds.Play("bebop_hook_wall", _point);
                        _phase = Phase.Return;
                        break;
                    }
                    _point += step;
                    _travelled += step.magnitude;
                    if (_travelled >= range)
                    {
                        // A miss at full reach ends there: no long reel back (user 2.5; a 200 m hook took ages to come home).
                        DeadlockSounds.Play("bebop_hook_miss", _point);
                        if (isAuthority) outer.SetNextStateToMain();
                    }
                    break;
                case Phase.Latched:
                    if (_target != null && _target.alive) _point = BebopNet.Core(_target);
                    bool close = _target == null || !_target.alive || (UnityEngine.Networking.NetworkServer.active
                        ? _target.GetComponent<BebopHookPull>() == null && fixedAge - _latchedAt > 0.2f            // the pull has set it down
                        : Vector3.Distance(_point, characterBody.corePosition) <= BebopData.HookPullStop + 2f);   // a client can't see the pull
                    if (isAuthority && (close || fixedAge - _latchedAt > BebopData.HookPullTime + 1f)) outer.SetNextStateToMain();
                    break;
                case Phase.Return:
                    Vector3 home = BebopShared.Muzzle(gameObject);
                    _point = Vector3.MoveTowards(_point, home, BebopData.HookReturnSpeed * dt);
                    if (isAuthority && Vector3.Distance(_point, home) < 1f) outer.SetNextStateToMain();
                    break;
            }
            // Pressing it again lets go (user).
            if (isAuthority && fixedAge > 0.1f && inputBank != null && inputBank.skill2.justPressed && _phase != Phase.Return)
            {
                if (_phase == Phase.Latched && _target != null)
                    BebopNet.ToServer(new BebopHookMessage { Attacker = gameObject, Target = _target.gameObject, Release = true });
                _phase = Phase.Return;
            }
        }

        /// <summary>Owner: the hand sweeps its step; the first enemy it touches is latched.</summary>
        private bool Contact(Vector3 step)
        {
            RaycastHit[] hits = Physics.SphereCastAll(_point, BebopData.HookRadius, step.normalized, step.magnitude, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                var box = h.collider.GetComponent<HurtBox>();
                if (box == null || box.healthComponent == null || box.teamIndex == GetTeam() || !box.healthComponent.alive) continue;
                _target = box.healthComponent;
                _point = BebopNet.Core(_target);
                _phase = Phase.Latched;
                _latchedAt = fixedAge;
                BebopNet.ToServer(new BebopHookMessage { Attacker = gameObject, Target = _target.gameObject, Crit = RollCrit() });
                return true;
            }
            return false;
        }

        public override void OnExit()
        {
            DeadlockSounds.Stop(_loop);
            if (_chainObj != null) Object.Destroy(_chainObj);
            if (_head != null) Object.Destroy(_head);
            base.OnExit();
        }

        // Re-pressing must reach this state (to cancel) rather than fire another hook and spend a charge.
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Utility: Exploding Uppercut =================

    /// <summary>
    /// Exploding Uppercut (Deadlock citadel_ability_uppercut: a 120 deg cone, 6 m), the user's version: everything in a wide cone in front
    /// of him is punched into the air along his look direction, steeper the higher he looks; each explodes where it lands (BebopToss).
    /// Landing a punch, once a cast, buffs his beam and heals missing health.
    /// </summary>
    public class Uppercut : BaseSkillState
    {
        private bool _punched;

        public override void OnEnter()
        {
            base.OnEnter();
            BebopModel.Play(gameObject, "uppercut");
            DeadlockSounds.Play("bebop_uppercut", transform.position, gameObject);
            DeadlockHeroVoice.Power("bebop_vo_power1", gameObject);
            StartAimMode(1f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_punched && fixedAge >= 0.12f) Punch();
            if (fixedAge >= 0.45f) outer.SetNextStateToMain();
        }

        private void Punch()
        {
            _punched = true;
            Ray aim = GetAimRay();
            Vector3 flat = new Vector3(aim.direction.x, 0f, aim.direction.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : characterDirection.forward;
            float reach = BebopData.UppercutReach * MysticExpansion.RadiusMultiplier(characterBody);
            var hit = new List<GameObject>();
            foreach (HurtBox h in BebopShared.Enemies(characterBody.corePosition, reach + 1f, GetTeam()))
            {
                if (h.healthComponent == null) continue;
                Vector3 to = h.transform.position - characterBody.corePosition;
                to.y = 0f;
                if (to.sqrMagnitude > 0.25f && Vector3.Angle(flat, to) > BebopData.UppercutHalfAngle) continue;
                if (!hit.Contains(h.healthComponent.gameObject)) hit.Add(h.healthComponent.gameObject);
            }
            // The launch: along his look, pitched up from it (looking up sends them higher, looking level sends them farther).
            float lookPitch = Mathf.Asin(Mathf.Clamp(aim.direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(lookPitch + BebopData.UppercutLift, BebopData.UppercutMinPitch, BebopData.UppercutMaxPitch) * Mathf.Deg2Rad;
            Vector3 launch = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            if (hit.Count > 0) BebopNet.ToServer(new BebopUppercutMessage { Attacker = gameObject, Targets = hit.ToArray(), Dir = launch, Crit = RollCrit() });
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Special: Hyper Beam =================

    /// <summary>
    /// Hyper Beam (Deadlock citadel_ability_bebop_laser_beam): a 1 s charge, then 11 s of a huge beam (70 m, 2.9 m wide) that turns toward
    /// his aim at 55 deg/s and hits everything along it ten times a second (bosses half), slowing them. He moves slowly (1.8 m/s) and
    /// floats down in the air. Special again (after half a second) ends it early.
    /// </summary>
    public class HyperBeam : BaseSkillState
    {
        private BebopHyperBeamFx? _fx;
        private bool _airborne;
        private Vector3 _dir;
        private float _duration, _nextTick;
        private bool _firing;
        private DeadlockVoice? _loop;

        public override void OnEnter()
        {
            base.OnEnter();
            _fx = GetComponent<BebopHyperBeamFx>();
            _airborne = characterMotor != null && !characterMotor.isGrounded;
            if (_airborne && isAuthority) characterMotor!.velocity = new Vector3(characterMotor.velocity.x * 0.3f, 0f, characterMotor.velocity.z * 0.3f);
            _dir = GetAimRay().direction;
            _duration = BebopData.HyperDuration * DurationExtender.Multiplier(characterBody);
            BebopModel.Play(gameObject, "hyper_charge");
            DeadlockSounds.Play("bebop_hyper_prep", transform.position, gameObject);
            DeadlockSounds.Play("bebop_hyper_prep_jazz", transform.position, gameObject);
            DeadlockHeroVoice.Power("bebop_vo_power4", gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            float dt = Time.fixedDeltaTime;
            _dir = Vector3.RotateTowards(_dir, GetAimRay().direction, BebopData.HyperTurn * Mathf.Deg2Rad * dt, 0f);
            if (_fx != null) _fx.Direction = _dir;
            if (!_firing && fixedAge >= BebopData.HyperCast) Fire();
            if (!isAuthority) return;
            Move();
            if (_firing && fixedAge >= _nextTick) { _nextTick = fixedAge + BebopData.HyperTick; Tick(); }
            bool cancel = fixedAge > BebopData.HyperCast + BebopData.HyperCancelAfter && inputBank != null && inputBank.skill4.justPressed;
            if (cancel || fixedAge >= BebopData.HyperCast + _duration) outer.SetNextStateToMain();
        }

        private void Fire()
        {
            _firing = true;
            _nextTick = fixedAge;
            if (_fx != null) _fx.Active = true;
            BebopModel.Play(gameObject, "hyper_fire");
            DeadlockSounds.Play("bebop_hyper_launch", transform.position, gameObject);
            // (not bebop_hyperbeam_launch_jazz: a ten-second rising synth under the whole beam, user 2.5)
            _loop = DeadlockSounds.Loop("bebop_hyper_loop", gameObject, _duration + 1f);
        }

        /// <summary>Owner: crawling along at Deadlock's channel speed, floating down in the air.</summary>
        private void Move()
        {
            if (characterMotor == null || characterBody == null || inputBank == null) return;
            characterMotor.moveDirection = inputBank.moveVector * Mathf.Clamp01(BebopData.HyperMove / Mathf.Max(0.1f, characterBody.moveSpeed));
            characterDirection.forward = new Vector3(_dir.x, 0f, _dir.z);
            // Cast in the air, he hangs at that height for the whole beam, still crawling sideways (user 2.5).
            if (_airborne && !characterMotor.isGrounded)
            {
                Vector3 v = characterMotor.velocity;
                characterMotor.velocity = new Vector3(v.x, 0f, v.z);
            }
        }

        private void Tick()
        {
            float length = BebopData.HyperLength * MysticExpansion.RadiusMultiplier(characterBody);
            float radius = BebopData.HyperRadius * MysticExpansion.RadiusMultiplier(characterBody);
            Vector3 from = GetAimRay().origin;
            // Terrain stops it; everything on the way is hit, and everything around where it meets the terrain (user 2.5).
            bool blocked = Physics.Raycast(from, _dir, out RaycastHit wall, length, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (blocked) length = wall.distance;
            var targets = new List<GameObject>();
            if (blocked)
                foreach (HurtBox h in BebopShared.Enemies(wall.point, BebopData.HyperImpactRadius * MysticExpansion.RadiusMultiplier(characterBody), GetTeam()))
                    if (h.healthComponent != null && !targets.Contains(h.healthComponent.gameObject)) targets.Add(h.healthComponent.gameObject);
            foreach (RaycastHit h in Physics.SphereCastAll(from, radius, _dir, length, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
            {
                var box = h.collider.GetComponent<HurtBox>();
                if (box == null || box.healthComponent == null || box.teamIndex == GetTeam()) continue;
                if (!targets.Contains(box.healthComponent.gameObject)) targets.Add(box.healthComponent.gameObject);
            }
            if (targets.Count > 0) BebopNet.ToServer(new BebopHyperMessage { Attacker = gameObject, Targets = targets.ToArray(), Crit = RollCrit() });
        }

        public override void OnExit()
        {
            if (_fx != null) { _fx.Active = false; _fx.Direction = Vector3.zero; }
            DeadlockSounds.Stop(_loop);
            // The end hum only (its jazz sting, a rising warbly synth, is gone: user 2.5).
            if (_firing) DeadlockSounds.Play("bebop_hyper_end", transform.position, gameObject);
            BebopModel.Play(gameObject, "hyper_end");
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
