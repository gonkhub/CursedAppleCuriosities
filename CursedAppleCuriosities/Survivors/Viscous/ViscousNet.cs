using System.Collections.Generic;
using System.Linq;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates.CursedApple.Viscous;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// The Goo Ball rolls on the player who owns Viscous; damage, stuns, knocks, the rolling buff and the bounce
    /// sounds everyone hears happen on the server. These messages carry that over (on a host they simply run locally).
    /// </summary>
    public static class ViscousNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<ViscousHitMessage>();
            NetworkingAPI.RegisterMessageType<ViscousBallMessage>();
            NetworkingAPI.RegisterMessageType<ViscousFxMessage>();
            NetworkingAPI.RegisterMessageType<ViscousPunchMessage>();
            NetworkingAPI.RegisterMessageType<ViscousCubeMessage>();
            NetworkingAPI.RegisterMessageType<ViscousCubeBreakMessage>();
            ViscousCubes.Hook();
            DeadlockFallGuard.Hook();
            // No fall damage while he's a Goo Ball (user): the server zeroes the landing's impact speed (as DeadlockFallGuard does).
            On.RoR2.GlobalEventManager.OnCharacterHitGroundServer += (orig, self, body, info) =>
            {
                if (body != null && ViscousSurvivor.BallBuff != null && body.HasBuff(ViscousSurvivor.BallBuff)) info.velocity = Vector3.zero;
                orig(self, body, info);
            };
            // 50% resist while rolling (user; Deadlock 35% bullet and spirit), as armor (100 armor = half damage).
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ViscousSurvivor.BallBuff == null || !body.HasBuff(ViscousSurvivor.BallBuff)) return;
                float r = Mathf.Clamp(ViscousData.BallResist, 0f, 0.95f);
                args.armorAdd += 100f * r / (1f - r);
            };
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        // ---------------- Puddle Punch ----------------

        /// <summary>
        /// Is this body in front of the fist? The punch is a cylinder standing out of the surface it was put on (user: floor,
        /// slope, wall or ceiling): its radius across the surface (Mystic Expansion wider), its half height out of it.
        /// </summary>
        public static bool InPunch(CharacterBody body, Vector3 point, Vector3 normal, CharacterBody? attacker = null)
        {
            float radius = ViscousData.PunchRadius * MysticExpansion.RadiusMultiplier(attacker ?? body);
            Vector3 d = body.corePosition - point;
            float along = Vector3.Dot(d, normal);
            if (along < -1f || along > ViscousData.PunchHalfHeight) return false;
            return (d - normal * along).magnitude <= radius + body.radius;
        }

        /// <summary>The launch: straight out of the surface the fist came from (user), tipped a little upward (user).</summary>
        public static Vector3 PunchLaunch(Vector3 normal) => (normal.normalized + Vector3.up * ViscousData.PunchUpBias).normalized * ViscousData.PunchUp;

        /// <summary>Server: send one body flying. Its motor's owner applies it (players included); rigidbody flyers are pushed.</summary>
        public static void Launch(CharacterBody b, Vector3 v, float toss = 0f)
        {
            if (Immovable(b)) return;
            if (b.characterMotor != null)
                b.characterMotor.ApplyForceImpulse(new PhysForceInfo
                {
                    force = v, massIsOne = true, resetVelocity = true, ignoreGroundStick = true,
                    disableAirControlUntilCollision = toss > 0f, // the throw carries: no braking until it hits something (user)
                });
            else if (b.rigidbody != null) b.rigidbody.velocity = v;
            if (toss > 0f && b.hasAuthority) PunchToss.Hold(b.gameObject, v, toss);
        }

        /// <summary>Bosses and the biggest monsters aren't knocked: the ball bounces off them.</summary>
        public static bool Immovable(CharacterBody? b) =>
            b == null || b.isChampion || b.hullClassification == HullClassification.BeetleQueen;

        /// <summary>Can't be stunned (no SetStateOnHurt, or stuns turned off): the ball can't stop it, so it stops the ball.</summary>
        public static bool Unstunnable(CharacterBody? b)
        {
            if (b == null) return true;
            SetStateOnHurt? s = b.GetComponent<SetStateOnHurt>();
            return s == null || !s.canBeStunned;
        }

        /// <summary>The Goo Ball's impact hit (user): bosses, the biggest monsters, and anything it can't stun.</summary>
        public static bool BallImpact(CharacterBody? b) => Immovable(b) || Unstunnable(b);

        /// <summary>Is this body a Goo Ball right now (curling up or rolling)? Read from its Body state machine, so true on every client.</summary>
        public static bool IsBall(GameObject? body, bool castToo = true)
        {
            if (body == null) return false;
            EntityStateMachine? m = EntityStateMachine.FindByCustomName(body, "Body");
            return m != null && (m.state is GooBall || castToo && m.state is GooBallCast);
        }

        private static float Mass(CharacterBody b) =>
            b.characterMotor != null ? b.characterMotor.mass : b.rigidbody != null ? b.rigidbody.mass : 100f;

        /// <summary>Server: one Goo Ball hit, with procs, a knock and a stun.</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 dir, bool boss = false)
        {
            if (target == null || !target.alive || damage <= 0f) return;
            CharacterBody? victim = target.body;
            Vector3 force = Vector3.zero;
            if (victim != null && !Immovable(victim))
                force = (dir * ViscousData.BallKnock + Vector3.up * ViscousData.BallKnockUp) * Mass(victim);
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = victim != null ? victim.corePosition : target.transform.position,
                force = force,
                procCoefficient = ViscousData.BallProc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = DamageSource.Special,
            };
            target.TakeDamage(di);
            if (!target.alive) Unlocks.Challenges.ViscousStrike.BallKill(attacker);
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
            if (!boss && target.alive && ViscousData.BallStun > 0f) // bosses: double damage instead of a stun (user)
                using (DurationExtender.From(attacker)) SetStateOnHurt.SetStunOnObject(target.gameObject, ViscousData.BallStun);
            EquipmentFx.Sound("viscous_ball_hit", di.position);
            if (boss) EquipmentFx.Sound("viscous_ball_wall", di.position);
            if (ViscousSurvivor.HitBigEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.HitBigEffect, new EffectData { origin = di.position, scale = boss ? 6f : 2.5f }, true);
        }
    }

    /// <summary>Goo Ball hits on a list of enemies (the owner found them touching the ball).</summary>
    public class ViscousHitMessage : INetMessage
    {
        public GameObject? Attacker;
        public GameObject[] Targets = System.Array.Empty<GameObject>();
        public float Damage;
        public bool Crit, Boss;
        public Vector3 Dir;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Attacker);
            w.Write(Damage);
            w.Write(Crit);
            w.Write(Boss);
            w.Write(Dir);
            w.Write(Targets.Length);
            foreach (GameObject t in Targets) w.Write(t);
        }

        public void Deserialize(NetworkReader r)
        {
            Attacker = r.ReadGameObject();
            Damage = r.ReadSingle();
            Crit = r.ReadBoolean();
            Boss = r.ReadBoolean();
            Dir = r.ReadVector3();
            Targets = new GameObject[r.ReadInt32()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc != null) ViscousNet.Hit(body, hc, Damage, Crit, Dir, Boss);
            }
        }
    }

    /// <summary>The rolling buff (damage resist, HUD timer) on or off.</summary>
    public class ViscousBallMessage : INetMessage
    {
        public GameObject? Body;
        public bool Rolling;
        public float Seconds;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Rolling); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Rolling = r.ReadBoolean(); Seconds = r.ReadSingle(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null || ViscousSurvivor.BallBuff == null) return;
            var body = Body.GetComponent<CharacterBody>();
            if (body == null) return;
            body.ClearTimedBuffs(ViscousSurvivor.BallBuff);
            while (body.HasBuff(ViscousSurvivor.BallBuff)) body.RemoveBuff(ViscousSurvivor.BallBuff);
            // Popped out mid-air: no landing guard. Out of the ball he takes fall damage like anyone (user).
            if (Rolling)
                using (DurationExtender.Exact()) body.AddTimedBuff(ViscousSurvivor.BallBuff, Seconds + 0.25f); // the state already extended it
        }
    }

    /// <summary>
    /// Puddle Punch at a point: the telegraph (the cast) or the punch itself. Server: enemies over it take 700% and
    /// fly; allies fly (their landing is guarded from fall damage); Viscous launched himself on his own client already.
    /// </summary>
    public class ViscousPunchMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Point, Normal = Vector3.up;
        public bool Telegraph, SelfHit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Point); w.Write(Normal); w.Write(Telegraph); w.Write(SelfHit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Point = r.ReadVector3(); Normal = r.ReadVector3(); Telegraph = r.ReadBoolean(); SelfHit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.teamComponent == null) return;
            float radius = ViscousData.PunchRadius; // effects are scaled by Mystic Expansion below
            float grown = radius * MysticExpansion.RadiusMultiplier(body);
            if (Telegraph)
            {
                EquipmentFx.Sound("viscous_punch_portal", Point);
                if (ViscousSurvivor.PunchPortalEffect != null)
                    EffectManager.SpawnEffect(ViscousSurvivor.PunchPortalEffect, new EffectData { origin = Point, rotation = Quaternion.FromToRotation(Vector3.up, Normal), scale = grown }, true);
                return;
            }
            EquipmentFx.Sound("viscous_punch_hit", Point);
            if (ViscousSurvivor.PunchEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.PunchEffect, new EffectData { origin = Point, rotation = Quaternion.FromToRotation(Vector3.up, Normal), scale = grown * 1.5f }, true);
            // Punching himself up has no landing guard (user: fall damage whenever he isn't in the ball or cube).
            bool crit = body.RollCrit();
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList.ToArray())
            {
                if (b == null || b == body || b.healthComponent == null || !b.healthComponent.alive || b.teamComponent == null) continue;
                if (!ViscousNet.InPunch(b, Point, Normal, body)) continue;
                bool enemy = b.teamComponent.teamIndex != body.teamComponent.teamIndex;
                if (enemy)
                {
                    var di = new DamageInfo
                    {
                        attacker = body.gameObject, inflictor = body.gameObject, damage = ViscousData.PunchDamage * body.damage, crit = crit,
                        position = b.corePosition, force = Vector3.zero, procCoefficient = 1f, damageColorIndex = DamageColorIndex.Default,
                        damageType = DamageSource.Secondary,
                    };
                    b.healthComponent.TakeDamage(di);
                    GlobalEventManager.instance.OnHitEnemy(di, b.gameObject);
                    GlobalEventManager.instance.OnHitAll(di, b.gameObject);
                    if (!b.healthComponent.alive) continue;
                }
                else DeadlockFallGuard.ProtectNextLanding(b.gameObject, 8f); // allies fly, but never die of the landing
                ViscousNet.Launch(b, ViscousNet.PunchLaunch(Normal), ViscousData.PunchToss);
            }
        }
    }

    /// <summary>
    /// Deadlock's toss (TossDuration): for a moment after Puddle Punch the body keeps exactly its launch velocity, with no
    /// gravity or braking, so the throw carries (user). Runs where the body's motor is simulated (the server for monsters,
    /// the owner for players). Ends early on landing or when it hits something.
    /// </summary>
    public class PunchToss : MonoBehaviour
    {
        private CharacterMotor? _motor;
        private Rigidbody? _rb;
        private Vector3 _v;
        private float _until, _from;

        public static void Hold(GameObject go, Vector3 v, float seconds)
        {
            var t = go.GetComponent<PunchToss>() ?? go.AddComponent<PunchToss>();
            t._motor = go.GetComponent<CharacterMotor>();
            t._rb = t._motor == null ? go.GetComponent<Rigidbody>() : null;
            t._v = v;
            t._from = Time.fixedTime;
            t._until = Time.fixedTime + seconds;
            t.enabled = true;
        }

        private void FixedUpdate()
        {
            if (Time.fixedTime >= _until) { enabled = false; return; }
            if (_motor != null)
            {
                // Grounded again, or stopped by a wall: the toss is over.
                if (Time.fixedTime - _from > 0.1f && (_motor.isGrounded || _motor.velocity.sqrMagnitude < 0.25f * _v.sqrMagnitude)) { enabled = false; return; }
                _motor.velocity = _v;
            }
            else if (_rb != null) _rb.velocity = _v;
            else enabled = false;
        }
    }

    public enum ViscousFxKind : byte { Wall }

    /// <summary>A bounce everyone sees and hears (the owner's ball found the wall).</summary>
    public class ViscousFxMessage : INetMessage
    {
        public ViscousFxKind Kind;
        public Vector3 Point, Normal;

        public void Serialize(NetworkWriter w) { w.Write((byte)Kind); w.Write(Point); w.Write(Normal); }
        public void Deserialize(NetworkReader r) { Kind = (ViscousFxKind)r.ReadByte(); Point = r.ReadVector3(); Normal = r.ReadVector3(); }

        public void OnReceived()
        {
            if (!NetworkServer.active) return;
            EquipmentFx.Sound("viscous_ball_wall", Point);
            if (ViscousSurvivor.WallEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.WallEffect, new EffectData
                {
                    origin = Point, rotation = Quaternion.LookRotation(Normal.sqrMagnitude > 0.01f ? Normal : Vector3.up), scale = 3f,
                }, true);
        }
    }

    /// <summary>
    /// Draws the Goo Ball, on every client and the lobby display: his model's "inflated" mesh group (Viscous blown up
    /// into a ball, hands and feet poking out) is hidden until he rolls; then his body, gun and outline hide and the
    /// ball shows. Its bone (inflated_center) is put at the ball's centre, sized to the ball's radius and turned as a
    /// ball rolling on the ground would, from how the body actually moved (so remote players roll too). It runs after
    /// the hero's animator, which leaves that bone wherever the clip under it put it.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class ViscousBallLook : MonoBehaviour
    {
        private DeadlockModelSwap? _swap;
        private CharacterBody? _body;
        private GameObject? _hero;
        private Transform? _bone;
        private readonly List<GameObject> _person = new List<GameObject>();
        private readonly List<GameObject> _ball = new List<GameObject>();
        private Quaternion _rest = Quaternion.identity, _base = Quaternion.identity, _roll = Quaternion.identity;
        private Vector3 _lastFoot, _spinAxis = Vector3.right;
        private float _spinRate;
        private bool? _shown;

        private CharacterModel? _model;

        private void Awake()
        {
            _swap = GetComponent<DeadlockModelSwap>();
            _model = GetComponent<CharacterModel>();
        }

        private void LateUpdate()
        {
            if (_body == null && _model != null) _body = _model.body; // none on the lobby display
            GameObject? hero = _swap != null ? _swap.HeroObject : null;
            if (hero == null) return;
            if (hero != _hero) Find(hero);
            bool rolling = _body != null && _body.healthComponent != null && _body.healthComponent.alive && ViscousNet.IsBall(_body.gameObject, castToo: false);
            if (_shown != rolling)
            {
                _shown = rolling;
                foreach (GameObject g in _person) if (g != null) g.SetActive(!rolling);
                foreach (GameObject g in _ball) if (g != null) g.SetActive(rolling);
                if (rolling && _body != null)
                {
                    _base = hero.transform.rotation * _rest; // upright, facing the way he faced when he curled up
                    _roll = Quaternion.identity;
                    _spinRate = 0f;
                    _lastFoot = _body.footPosition;
                }
            }
            if (rolling && _bone != null && _body != null) Place();
        }

        private void Find(GameObject hero)
        {
            _hero = hero;
            _person.Clear();
            _ball.Clear();
            _bone = null;
            foreach (Renderer r in hero.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^inflated_\d+$")) _ball.Add(r.gameObject);
                else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^(body|gun|bodyoutline)_\d+$")) _person.Add(r.gameObject);
            }
            foreach (Transform t in hero.GetComponentsInChildren<Transform>(true))
                if (t.name == ViscousData.BallBone) { _bone = t; break; }
            GlbModel? model = DeadlockInstall.Model(ViscousData.Hero);
            _rest = model != null && model.RestInRoot.TryGetValue(ViscousData.BallBone, out Matrix4x4 m) ? m.rotation : Quaternion.identity;
            _shown = null;
        }

        private void Place()
        {
            float dt = Time.deltaTime;
            Vector3 foot = _body!.footPosition;
            float radius = ViscousData.Radius(_body); // Mystic Expansion grows the ball (user)
            if (dt > 0f)
            {
                Vector3 v = (foot - _lastFoot) / dt;
                v.y = 0f;
                bool grounded = _body.characterMotor == null || _body.characterMotor.isGrounded;
                float speed = v.magnitude;
                if (speed > 60f) { } // a teleport: keep spinning as before
                else if (grounded && speed > 0.05f)
                {
                    _spinAxis = Vector3.Cross(Vector3.up, v / speed);
                    _spinRate = speed / radius; // rolling without slipping
                }
                else if (grounded) _spinRate = 0f;
                _roll = Quaternion.AngleAxis(_spinRate * dt * Mathf.Rad2Deg, _spinAxis) * _roll; // in the air it keeps its spin
            }
            _lastFoot = foot;
            Transform bone = _bone!;
            float parentScale = bone.parent != null ? Mathf.Max(1e-4f, bone.parent.lossyScale.x) : 1f;
            bone.localScale = Vector3.one * (radius / ViscousData.BallModelRadius / parentScale);
            bone.SetPositionAndRotation(foot + Vector3.up * radius, _roll * _base);
        }
    }
}

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// Every machine, on Viscous: while he rolls, his main hurtbox (Commando's capsule) becomes the Goo Ball's sphere, so hits
    /// land where the ball is drawn; it goes back when he unrolls. Follows the synced body state, like ViscousBallLook.
    /// </summary>
    public class ViscousBallHurtbox : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private CapsuleCollider? _capsule;
        private float _radius, _height;
        private Vector3 _center;
        private int _direction;
        private bool _rolling;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (_capsule == null)
            {
                _capsule = _body.mainHurtBox != null ? _body.mainHurtBox.GetComponent<CapsuleCollider>() : null;
                if (_capsule == null) return;
                _radius = _capsule.radius; _height = _capsule.height; _center = _capsule.center; _direction = _capsule.direction;
            }
            bool rolling = _body.healthComponent != null && _body.healthComponent.alive && ViscousNet.IsBall(gameObject, castToo: false);
            if (rolling)
            {
                Transform t = _capsule.transform;
                float scale = Mathf.Max(1e-4f, t.lossyScale.x);
                float r = ViscousData.Radius(_body);
                _capsule.radius = r / scale;
                _capsule.height = 2f * r / scale;
                _capsule.center = t.InverseTransformPoint(_body.footPosition + Vector3.up * r);
            }
            else if (_rolling)
            {
                _capsule.radius = _radius; _capsule.height = _height; _capsule.center = _center; _capsule.direction = _direction;
            }
            _rolling = rolling;
        }
    }
}
