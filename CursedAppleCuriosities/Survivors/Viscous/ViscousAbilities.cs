using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Viscous;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

// Entity states live in the EntityStates namespace tree, as RoR2's own do.
namespace EntityStates.CursedApple.Viscous
{
    // ================= Primary alt: his alt fire =================

    /// <summary>
    /// Deadlock's alt fire (citadel_weapon_viscous_set_2): a glob flying dead straight (38 m/s, no gravity: user) that
    /// bursts on whatever it touches, 3.6 m wide (Mystic Expansion grows it). One every 1.26 s.
    /// </summary>
    public class FireAltGlob : BaseSkillState
    {
        private float _duration;

        public override void OnEnter()
        {
            base.OnEnter();
            _duration = ViscousData.AltCycle / attackSpeedStat;
            StartAimMode(2f);
            ViscousModel.Play(gameObject, "alt_shoot");
            DeadlockSounds.Play("viscous_alt_fire", transform.position, gameObject);
            AddRecoil(-1f, -1.5f, -0.3f, 0.3f);
            if (isAuthority && ViscousSurvivor.AltProjectile != null)
            {
                Ray aim = GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = ViscousSurvivor.AltProjectile,
                    position = aim.origin + aim.direction * 1f,
                    rotation = Quaternion.LookRotation(aim.direction),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = ViscousData.AltSpeed,
                });
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // ================= Secondary: Splatter =================

    /// <summary>
    /// Splatter (Deadlock viscous_goo_grenade): a goo glob thrown a little above the crosshair. It bursts each time it
    /// hits something (500%, 5 m, Mystic Expansion wider) and bounces on, 3 bursts in all (user). See <see cref="ViscousGlob"/>.
    /// </summary>
    public class ThrowSplatter : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            StartAimMode(1f);
            ViscousModel.Play(gameObject, "throw");
            DeadlockSounds.Play("viscous_splatter_cast", transform.position, gameObject);
            DeadlockHeroVoice.Power("viscous_vo_power1", gameObject);
            if (isAuthority && ViscousSurvivor.SplatterProjectile != null)
            {
                Ray aim = GetAimRay();
                Vector3 dir = (aim.direction + Vector3.up * ViscousData.SplatterLob).normalized;
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = ViscousSurvivor.SplatterProjectile,
                    position = aim.origin + aim.direction * 1f,
                    rotation = Quaternion.LookRotation(dir),
                    owner = gameObject,
                    damage = damageStat,
                    crit = RollCrit(),
                    force = 0f,
                    speedOverride = ViscousData.SplatterSpeed,
                });
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= 0.3f && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // ================= Secondary alt: Puddle Punch =================

    /// <summary>
    /// Puddle Punch (Deadlock viscous_telepunch). Hold Secondary to aim (user): the local player sees where the fist will
    /// come out (a ring flat on whatever surface the crosshair is on, floor, slope, wall or ceiling, the punch's real area
    /// incl. Mystic Expansion) and an arrow straight out of that surface. Release to punch: the portal telegraphs 0.45 s, then
    /// 0.3 s later the fist comes out there (Deadlock; on himself no telegraph), up to 40 m (Mystic Expansion farther).
    /// Everything in front of it is thrown perpendicular to the surface (user): enemies take 700% too, allies only fly,
    /// bosses don't move. The throw holds its speed for Deadlock's 0.6 s toss, then carries with no braking (user).
    /// Viscous punching himself flies too, with much more air control and no air drag for a moment (user).
    /// The owner launches himself; the server does everyone else.
    /// </summary>
    public class PuddlePunch : BaseSkillState
    {
        private Vector3 _point, _normal = Vector3.up;
        private bool _released, _punched;
        private float _releasedAt, _windup;
        private PunchPreview? _preview;

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority && characterBody.isPlayerControlled) _preview = new PunchPreview();
        }

        /// <summary>
        /// Where the fist comes out, and of which surface (user: any angle, as Deadlock's can go on walls): the surface under
        /// the crosshair within range, with its normal. Aimed at nothing, the floor below the end of the range.
        /// </summary>
        public static Vector3 Target(CharacterBody body, Ray aim, out Vector3 normal)
        {
            float range = ViscousData.PunchRange * MysticExpansion.RadiusMultiplier(body);
            if (Physics.Raycast(aim, out RaycastHit hit, range, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                return hit.point;
            }
            Vector3 end = aim.GetPoint(range);
            if (Physics.Raycast(end + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 60f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                normal = floor.normal;
                return floor.point;
            }
            normal = Vector3.up;
            return end;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (!_released)
            {
                Ray aim = GetAimRay();
                _point = Target(characterBody, aim, out _normal);
                StartAimMode(0.5f);
                _preview?.Show(_point, _normal, ViscousNet.PunchLaunch(_normal), ViscousData.PunchRadius * MysticExpansion.RadiusMultiplier(characterBody));
                if (inputBank.skill2.down) return;
                Release();
            }
            if (!_punched && fixedAge - _releasedAt >= _windup)
            {
                _punched = true;
                bool self = ViscousNet.InPunch(characterBody, _point, _normal);
                if (self) LaunchSelf();
                ViscousNet.ToServer(new ViscousPunchMessage { Attacker = gameObject, Point = _point, Normal = _normal, SelfHit = self });
            }
            if (fixedAge - _releasedAt >= _windup + 0.15f) outer.SetNextStateToMain();
        }

        private void Release()
        {
            _released = true;
            _releasedAt = fixedAge;
            // Deadlock: the portal telegraphs 0.45 s before the fist on others; punching yourself has no telegraph.
            _windup = ViscousData.PunchWindup + (ViscousNet.InPunch(characterBody, _point, _normal) ? 0f : ViscousData.PunchTelegraph);
            _preview?.Destroy();
            _preview = null;
            ViscousModel.Play(gameObject, "punch");
            DeadlockHeroVoice.Power("viscous_vo_power3", gameObject);
            ViscousNet.ToServer(new ViscousPunchMessage { Attacker = gameObject, Point = _point, Normal = _normal, Telegraph = true });
        }

        /// <summary>His own launch (his motor is his owner's): the same throw as everyone, plus the air control boost.</summary>
        private void LaunchSelf()
        {
            characterMotor.Motor.ForceUnground();
            characterMotor.disableAirControlUntilCollision = false;
            Vector3 v = ViscousNet.PunchLaunch(_normal);
            characterMotor.velocity = v;
            PunchToss.Hold(gameObject, v, ViscousData.PunchToss);
            var mover = GetComponent<DeadlockMover>();
            mover?.BoostAirControl(ViscousData.PunchSelfAirControl, ViscousData.PunchSelfAirSeconds);
            mover?.SuspendAirDrag(ViscousData.PunchSelfAirSeconds); // the launch carries (user)
        }

        public override void OnExit()
        {
            _preview?.Destroy();
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => _released ? InterruptPriority.PrioritySkill : InterruptPriority.Skill;
    }

    /// <summary>
    /// The local player's aiming preview for Puddle Punch: a green ring flat on the surface it will come out of, the size of
    /// the punch, and an arrow from its centre along the real launch (out of the surface, plus lift and push). Line
    /// renderers in world space (Spirit Snatch's ring material).
    /// </summary>
    internal class PunchPreview
    {
        private const int Segments = 48;
        private static readonly Color Tint = new Color(0.45f, 1f, 0.45f, 0.9f);
        private readonly LineRenderer? _ring, _arrow;

        public PunchPreview()
        {
            _ring = Line("CursedApple_PunchRing", Segments, true);
            _arrow = Line("CursedApple_PunchArrow", 5, false);
        }

        private static LineRenderer? Line(string name, int points, bool loop)
        {
            Material? mat = CursedAppleCuriosities.Items.SpiritSnatch.RingMaterial;
            if (mat == null) return null;
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.material = mat;
            if (lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", Tint);
            lr.startColor = lr.endColor = Tint;
            lr.startWidth = lr.endWidth = 0.12f;
            lr.positionCount = points;
            lr.loop = loop;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        public void Show(Vector3 at, Vector3 normal, Vector3 launch, float radius)
        {
            Vector3 c = at + normal * 0.15f;
            // Two axes across the surface for the ring.
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 w = Vector3.Cross(normal, u);
            if (_ring != null)
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    _ring.SetPosition(i, c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius);
                }
            if (_arrow != null)
            {
                // Shaft from the centre along the launch, then the two barbs of the head (one line: tip, barb, tip, barb).
                Vector3 dir = launch.sqrMagnitude > 0.01f ? launch.normalized : normal;
                Vector3 tip = c + dir * radius * 1.4f;
                Vector3 across = Vector3.Cross(dir, normal);
                if (across.sqrMagnitude < 0.01f) across = u; // straight out of the surface: barbs across it
                Vector3 side = across.normalized * radius * 0.3f;
                Vector3 back = -dir * radius * 0.35f;
                _arrow.SetPosition(0, c);
                _arrow.SetPosition(1, tip);
                _arrow.SetPosition(2, tip + back + side);
                _arrow.SetPosition(3, tip);
                _arrow.SetPosition(4, tip + back - side);
            }
        }

        public void Destroy()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            if (_arrow != null) Object.Destroy(_arrow.gameObject);
        }
    }

    // ================= Utility: The Cube =================

    /// <summary>
    /// The Cube (Deadlock viscous_restorative_goo), user's controls: tap Utility to cube the ally under the crosshair
    /// (20 m, line of sight; nobody there = no cast, no cooldown), hold it to cube himself. Runs on its own state
    /// machine, so he keeps moving and shooting while deciding. The cube itself is server-side (<see cref="ViscousCubes"/>).
    /// </summary>
    public class CubeCast : BaseSkillState
    {
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (fixedAge >= ViscousData.CubeHold) { Cast(characterBody); return; }
            if (inputBank.skill3.down) return;
            CharacterBody? ally = ViscousCubes.FindAlly(characterBody);
            if (ally != null) Cast(ally);
            else
            {
                if (activatorSkillSlot != null) activatorSkillSlot.AddOneStock(); // nobody to cube: no cooldown spent
                outer.SetNextStateToMain();
            }
        }

        private void Cast(CharacterBody target)
        {
            ViscousModel.Play(gameObject, "cube");
            DeadlockHeroVoice.Power("viscous_vo_power2", gameObject);
            ViscousNet.ToServer(new ViscousCubeMessage { Caster = gameObject, Target = target.gameObject });
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// His goo globs (alt fire and Splatter), on a vanilla projectile's flight and networking (Kit.CloneProjectile). The
    /// server bursts them: a BlastAttack, so Mystic Expansion widens it (its hook) and the effect grows to match.
    /// Splatter bounces off what it hits and bursts again, 3 times in all; the alt fire glob bursts once. Their own
    /// gravity (a share of the world's), and every client scales the ghost by the owner's Mystic Expansion.
    /// </summary>
    public class ViscousGlob : MonoBehaviour, IProjectileImpactBehavior
    {
        public bool Splatter;

        private ProjectileController _pc = null!;
        private ProjectileDamage? _pd;
        private Rigidbody? _rb;
        private Vector3 _lastVel;
        private float _age, _lastBurst = -10f;
        private int _bursts;
        private bool _scaled, _done;
        private readonly System.Collections.Generic.HashSet<HealthComponent> _passed = new System.Collections.Generic.HashSet<HealthComponent>();
        private DeadlockVoice? _hum;

        // The alt glob flies its Deadlock range (m_flRange) and bursts there; Splatter bounces until it's spent or times out.
        private float Lifetime => Splatter ? ViscousData.SplatterLifetime
            : ViscousData.AltRange * MysticExpansion.RadiusMultiplier(_pc != null && _pc.owner != null ? _pc.owner.GetComponent<CharacterBody>() : null) / ViscousData.AltSpeed; // Mystic Expansion: farther

        private void Awake()
        {
            _pc = GetComponent<ProjectileController>();
            _pd = GetComponent<ProjectileDamage>();
            _rb = GetComponent<Rigidbody>();
        }

        private void Start() => _hum = DeadlockSounds.Loop(Splatter ? "viscous_splatter_loop" : "viscous_alt_loop", gameObject, Lifetime + 0.5f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private void Update()
        {
            // Mystic Expansion makes the glob itself bigger too (user), once the owner is known on this client.
            if (_scaled || _pc.ghost == null || _pc.owner == null) return;
            _scaled = true;
            _pc.ghost.transform.localScale *= MysticExpansion.RadiusMultiplier(_pc.owner.GetComponent<CharacterBody>());
        }

        private void FixedUpdate()
        {
            if (_rb != null && !_rb.isKinematic)
            {
                if (Splatter) _rb.velocity += Physics.gravity * Time.fixedDeltaTime; // the alt glob flies dead straight (user)
                _rb.angularVelocity = Vector3.zero;
                _lastVel = _rb.velocity;
                _lastPos = _rb.position;
            }
            if (_done || !UnityEngine.Networking.NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (_age >= Lifetime)
            {
                Burst(transform.position); // the alt glob splats at the end of its range (user); Splatter's last burst
                Finish();
            }
        }

        private Vector3 _lastPos;

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !UnityEngine.Networking.NetworkServer.active) return;
            // Splatter goes straight through enemies (user): no burst, no bounce, and that body is ignored from now on. It
            // hurts each enemy it passes through, once (user 2.5).
            if (Splatter && info.collider != null && (info.collider.GetComponent<HurtBox>() != null || info.collider.GetComponentInParent<CharacterBody>() != null))
            {
                foreach (Collider mine in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, info.collider);
                PassThrough(info.collider);
                if (_rb != null) { _rb.position = _lastPos; _rb.velocity = _lastVel; }
                return;
            }
            if (Time.time - _lastBurst < ViscousData.SplatterRearm) return; // the same contact again
            HurtBox? box = info.collider != null ? info.collider.GetComponent<HurtBox>() : null;
            Vector3 n = info.estimatedImpactNormal;
            if (box != null) n = (transform.position - box.transform.position).normalized + Vector3.up * 0.5f;
            if (n.sqrMagnitude < 0.01f) n = _lastVel.sqrMagnitude > 0.01f ? -_lastVel.normalized : Vector3.up;
            n.Normalize();
            Burst(transform.position);
            if (!Splatter || _bursts >= Mathf.Max(1, ViscousData.SplatterBounces)) { Finish(); return; }
            // Bounce on, hard (user: twice the earlier bounce), always lively enough to reach its next burst.
            Vector3 v = Vector3.Reflect(_lastVel, n) * ViscousData.SplatterRestitution;
            if (Vector3.Dot(v, n) < ViscousData.SplatterMinPop) v += n * (ViscousData.SplatterMinPop - Vector3.Dot(v, n));
            if (_rb != null) _rb.velocity = v;
            _lastVel = v;
            transform.position += n * 0.25f;
        }

        /// <summary>Server: a hit on an enemy the glob flies through (once per enemy, whichever hurtbox it touched).</summary>
        private void PassThrough(Collider c)
        {
            GameObject? owner = _pc.owner;
            HurtBox? box = c.GetComponent<HurtBox>();
            HealthComponent? hc = box != null ? box.healthComponent : c.GetComponentInParent<HealthComponent>();
            if (owner == null || hc == null || !hc.alive || !_passed.Add(hc)) return;
            if (TeamComponent.GetObjectTeam(hc.gameObject) == TeamComponent.GetObjectTeam(owner)) return;
            var di = new DamageInfo
            {
                attacker = owner, inflictor = gameObject, damage = (_pd != null ? _pd.damage : 0f) * ViscousData.SplatterPassDamage,
                crit = _pd != null && _pd.crit, position = transform.position, force = _lastVel.normalized * 300f, procCoefficient = 1f,
                damageColorIndex = DamageColorIndex.Default, damageType = DamageSource.Secondary,
            };
            hc.TakeDamage(di);
            if (di.rejected) return;
            GlobalEventManager.instance.OnHitEnemy(di, hc.gameObject);
            GlobalEventManager.instance.OnHitAll(di, hc.gameObject);
            if (ViscousSurvivor.SplatterEffect != null) EffectManager.SpawnEffect(ViscousSurvivor.SplatterEffect, new EffectData { origin = transform.position, scale = 1.5f }, true);
            EquipmentFx.Sound("viscous_splatter_burst", transform.position);
        }

        private void Burst(Vector3 at)
        {
            _bursts++;
            _lastBurst = Time.time;
            GameObject? owner = _pc.owner;
            if (owner == null) return;
            float radius = Splatter ? ViscousData.SplatterRadius : ViscousData.AltRadius;
            float coeff = Splatter ? ViscousData.SplatterDamage : ViscousData.AltDamage;
            new BlastAttack
            {
                attacker = owner,
                inflictor = gameObject,
                teamIndex = TeamComponent.GetObjectTeam(owner),
                baseDamage = (_pd != null ? _pd.damage : 0f) * coeff,
                crit = _pd != null && _pd.crit,
                radius = radius,               // Mystic Expansion's BlastAttack hook widens it
                position = at,
                procCoefficient = 1f,
                falloffModel = BlastAttack.FalloffModel.None,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                damageType = Splatter ? (DamageTypeCombo)DamageSource.Secondary : DamageSource.Primary,
            }.Fire();
            GameObject? fx = Splatter ? ViscousSurvivor.SplatterEffect : ViscousSurvivor.AltBurstEffect;
            if (fx != null) EffectManager.SpawnEffect(fx, new EffectData { origin = at, scale = radius }, true); // grown to the blast by the hook
            EquipmentFx.Sound(Splatter ? "viscous_splatter_burst" : "viscous_alt_burst", at);
        }

        private void Finish()
        {
            _done = true;
            Destroy(gameObject);
        }
    }
}
