using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>Server side of Ivy: the thorn patches, Kudzu Connection's tether, Stone Form's slam and Air Drop's bomb.</summary>
    public static class IvyNet
    {
        [System.ThreadStatic] private static bool _sharing;

        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<IvyTetherFxMessage>();
            NetworkingAPI.RegisterMessageType<IvyPatchSoundMessage>();
            NetworkingAPI.RegisterMessageType<IvyStoneMessage>();
            NetworkingAPI.RegisterMessageType<IvyBombMessage>();
            NetworkingAPI.RegisterMessageType<IvyRingMessage>();
            IvyCarry.Register();
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (IvySurvivor.TetherBuff != null)
                {
                    int n = body.GetBuffCount(IvySurvivor.TetherBuff); // 2 = connected, 1 = alone (Deadlock: half the bonuses)
                    if (n > 0) args.attackSpeedMultAdd += IvyData.TetherAttackSpeed * (n >= 2 ? 1f : 0.5f);
                }
                if (IvySurvivor.ThornsSlow != null && body.HasBuff(IvySurvivor.ThornsSlow)) args.moveSpeedReductionMultAdd += IvyData.ThornsSlow;
                if (IvySurvivor.AirDropBuff != null && body.HasBuff(IvySurvivor.AirDropBuff)) args.damageMultAdd += IvyData.AirDropBuff;
            };
            // Kudzu lifesteal (Deadlock BulletLifestealPercent), for both ends of the tether.
            GlobalEventManager.onServerDamageDealt += report =>
            {
                CharacterBody? by = report.attackerBody;
                if (by == null || IvySurvivor.TetherBuff == null || report.damageDealt <= 0f || report.victimBody == by) return;
                int n = by.GetBuffCount(IvySurvivor.TetherBuff);
                if (n <= 0 || by.healthComponent == null || !by.healthComponent.alive) return;
                by.healthComponent.Heal(report.damageDealt * IvyData.TetherLifesteal * (n >= 2 ? 1f : 0.5f) * Mathf.Clamp01(report.damageInfo.procCoefficient), default(ProcChainMask));
            };
            // Kudzu shared healing (Deadlock TetherSharedHealPct): heals one end gets (not regen) reach the other too.
            On.RoR2.HealthComponent.Heal += (orig, self, amount, procChainMask, nonRegen) =>
            {
                float healed = orig(self, amount, procChainMask, nonRegen);
                if (!NetworkServer.active || _sharing || !nonRegen || healed <= 0f || self.body == null) return healed;
                IvyTether? t = IvyTether.Of(self.body);
                CharacterBody? other = t == null ? null : t.Ivy == self.body ? t.Partner : t.Ivy;
                if (other != null && other.healthComponent != null && other.healthComponent.alive)
                {
                    _sharing = true;
                    try { other.healthComponent.Heal(healed * IvyData.TetherSharedHeal, procChainMask, true); }
                    finally { _sharing = false; }
                }
                return healed;
            };
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        public static DamageInfo? Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f)
        {
            if (target == null || !target.alive || damage <= 0f) return null;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject, inflictor = attacker.gameObject, damage = damage, crit = crit, position = Core(target),
                force = force, procCoefficient = proc, damageColorIndex = DamageColorIndex.Default, damageType = type,
            };
            target.TakeDamage(di);
            if (di.rejected) return di;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
            return di;
        }

        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            foreach (HurtBox h in new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                         .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes())
                if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        public static void Effect(GameObject? prefab, Vector3 at, float scale)
        {
            if (prefab != null && NetworkServer.active) EffectManager.SpawnEffect(prefab, new EffectData { origin = at, scale = scale }, true);
        }

        /// <summary>Server: a ring on every client (thorn patch, slam, bomb) that fades over its seconds.</summary>
        public static void Ring(Vector3 at, float radius, float seconds, GameObject? follow = null) =>
            new IvyRingMessage { Point = at, Radius = radius, Seconds = seconds, Follow = follow }.Send(NetworkDestination.Clients);
    }

    public class IvyRingMessage : INetMessage
    {
        public Vector3 Point;
        public float Radius, Seconds;
        public GameObject? Follow;

        public void Serialize(NetworkWriter w) { w.Write(Point); w.Write(Radius); w.Write(Seconds); w.Write(Follow); }
        public void Deserialize(NetworkReader r) { Point = r.ReadVector3(); Radius = r.ReadSingle(); Seconds = r.ReadSingle(); Follow = r.ReadGameObject(); }

        public void OnReceived()
        {
            IvyGroundRing.Spawn(Point, Radius, Seconds).Follow = Follow; // her own marker (user 2.5: not Seven's ring and its hum)
        }
    }

    // ================= Secondary: Kudzu Bomb =================

    /// <summary>On the thrown Kudzu Bomb: it hums in flight (every client); on the server it lands and a 6 m kudzu patch grows there that hurts and slows every enemy in it.</summary>
    [RequireComponent(typeof(ProjectileController))]
    public class IvySeed : MonoBehaviour, IProjectileImpactBehavior
    {
        private bool _done;
        private DeadlockVoice? _hum;

        private void Start() => _hum = DeadlockSounds.Loop("ivy_bomb_proj_loop", gameObject, 6f);
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !NetworkServer.active) return;
            _done = true;
            var pc = GetComponent<ProjectileController>();
            var by = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            var pd = GetComponent<ProjectileDamage>();
            if (by != null)
            {
                // On the ground under where it hit (a seed that hits a wall or an enemy grows below it).
                Vector3 at = transform.position;
                if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit ground, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) at = ground.point;
                var go = new GameObject("CursedAppleIvyThorns");
                go.transform.position = at;
                var patch = go.AddComponent<IvyThorns>();
                patch.By = by;
                patch.Crit = pd != null && pd.crit;
                patch.Radius = IvyData.ThornsRadius * MysticExpansion.RadiusMultiplier(by);
                patch.Duration = IvyData.ThornsDuration * DurationExtender.Multiplier(by);
                EquipmentFx.Sound("ivy_bomb_impact", at);
                IvyNet.Ring(at, patch.Radius, patch.Duration);
                IvyNet.Effect(IvySurvivor.KudzuBurstEffect, at, patch.Radius);
                new IvyPatchSoundMessage { At = at, Seconds = patch.Duration }.Send(NetworkDestination.Clients);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Server: one thorn patch (Deadlock: 6 m, 4 s, ticks every 0.25 s, 28% slow).</summary>
    public class IvyThorns : MonoBehaviour
    {
        public CharacterBody? By;
        public bool Crit;
        public float Radius, Duration;
        private float _age, _next;

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (By == null || _age >= Duration) { EquipmentFx.Sound("ivy_thorns_end", transform.position); Destroy(gameObject); return; }
            if (_age < _next || By.teamComponent == null) return;
            _next = _age + IvyData.ThornsTick;
            foreach (HealthComponent hc in IvyNet.EnemiesIn(transform.position, Radius, By.teamComponent.teamIndex))
            {
                IvyNet.Hit(By, hc, IvyData.ThornsDps * IvyData.ThornsTick * By.damage, Crit, Vector3.zero, DamageSource.Secondary, 0.5f);
                if (hc.alive && hc.body != null && IvySurvivor.ThornsSlow != null) hc.body.AddTimedBuff(IvySurvivor.ThornsSlow, IvyData.ThornsTick * 2f);
            }
        }
    }

    /// <summary>Every client: the kudzu patch's loop (Deadlock's stone squall aoe loop) for as long as it lasts.</summary>
    public class IvyPatchSoundMessage : INetMessage
    {
        public Vector3 At;
        public float Seconds;

        public void Serialize(NetworkWriter w) { w.Write(At); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { At = r.ReadVector3(); Seconds = r.ReadSingle(); }

        public void OnReceived()
        {
            var go = new GameObject("CursedAppleIvyPatchSound");
            go.transform.position = At;
            DeadlockSounds.Loop("ivy_bomb_aoe_loop", go, Seconds);
            Object.Destroy(go, Seconds + 0.5f);
        }
    }

    // ================= Passive: Kudzu Connection =================

    /// <summary>
    /// Server, on Ivy: Kudzu Connection as a passive (user 2.5). Four times a second she re-binds to the nearest living ally within
    /// 16 m (Mystic Expansion widens it): a player, a drone, a turret, a minion. With nobody near she keeps half the bonuses (Deadlock).
    /// </summary>
    public class IvyKudzuPassive : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _next;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _next) return;
            _next = Time.time + 0.25f;
            var t = GetComponent<IvyTether>();
            if (_body.healthComponent == null || !_body.healthComponent.alive) { if (t != null) Destroy(t); return; }
            CharacterBody? best = Nearest();
            if (t != null && t.Ivy == _body && t.Partner == best) return;
            t ??= gameObject.AddComponent<IvyTether>();
            t.Begin(_body, best, float.PositiveInfinity);
            if (best != null)
            {
                EquipmentFx.Sound("ivy_tether_attach", _body.corePosition, gameObject);
                new IvyTetherFxMessage { Ivy = gameObject, Partner = best.gameObject, Seconds = 1e6f }.Send(NetworkDestination.Clients);
            }
        }

        private CharacterBody? Nearest()
        {
            float range = IvyData.TetherRange * MysticExpansion.RadiusMultiplier(_body);
            CharacterBody? best = null;
            float bestSq = range * range;
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
            {
                if (b == null || b == _body || b.teamComponent == null || _body.teamComponent == null || b.teamComponent.teamIndex != _body.teamComponent.teamIndex) continue;
                if (b.healthComponent == null || !b.healthComponent.alive) continue;
                float sq = (b.corePosition - _body.corePosition).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = b; }
            }
            return best;
        }
    }

    /// <summary>Server, on Ivy: one Kudzu Connection. Both ends hold the tether buff (2 stacks connected, 1 alone).</summary>
    public class IvyTether : MonoBehaviour
    {
        private static readonly List<IvyTether> Live = new List<IvyTether>();
        public CharacterBody? Ivy, Partner;
        public float Seconds;
        private float _until;

        public static IvyTether? Of(CharacterBody b)
        {
            foreach (IvyTether t in Live) if (t.Ivy == b || t.Partner == b) return t;
            return null;
        }

        private void OnEnable() => Live.Add(this);
        private void OnDisable() { Live.Remove(this); Clear(); }

        public void Begin(CharacterBody ivy, CharacterBody? partner, float seconds)
        {
            Clear();
            Ivy = ivy; Partner = partner; Seconds = seconds;
            _until = Time.time + seconds;
            Set(Ivy, partner != null ? 2 : 1);
            if (partner != null) Set(partner, 2);
        }

        private static void Set(CharacterBody? b, int n)
        {
            if (b != null && IvySurvivor.TetherBuff != null) b.SetBuffCount(IvySurvivor.TetherBuff.buffIndex, n);
        }

        private void Clear()
        {
            Set(Ivy, 0);
            Set(Partner, 0);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Ivy == null) return;
            bool broken = Partner != null && (Partner.healthComponent == null || !Partner.healthComponent.alive || Vector3.Distance(Partner.corePosition, Ivy.corePosition) > IvyData.TetherBreakRange);
            if (Time.time < _until && !broken) return;
            EquipmentFx.Sound("ivy_tether_end", Ivy.corePosition, Ivy.gameObject);
            Clear();
            Ivy = Partner = null;
            Destroy(this);
        }
    }

    /// <summary>Every client: the vine between Ivy and her partner.</summary>
    public class IvyTetherFxMessage : INetMessage
    {
        public GameObject? Ivy, Partner;
        public float Seconds;

        public void Serialize(NetworkWriter w) { w.Write(Ivy); w.Write(Partner); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { Ivy = r.ReadGameObject(); Partner = r.ReadGameObject(); Seconds = r.ReadSingle(); }

        public void OnReceived()
        {
            if (Ivy == null || Partner == null) return;
            var a = Ivy.GetComponent<CharacterBody>();
            if (a != null && IvyVine.ByIvy.TryGetValue(a, out IvyVine old) && old != null) old.End(); // re-bound to someone else
            var go = new GameObject("CursedAppleIvyVine");
            var vine = go.AddComponent<IvyVine>();
            vine.A = a;
            if (a != null) IvyVine.ByIvy[a] = vine;
            vine.B = Partner.GetComponent<CharacterBody>();
            vine.Until = Time.time + Seconds;
            vine.Loop = DeadlockSounds.Loop("ivy_tether_loop", Ivy, Seconds);
        }
    }

    public class IvyVine : MonoBehaviour
    {
        public CharacterBody? A, B;
        public float Until;
        public DeadlockVoice? Loop;
        private LineRenderer? _line;
        public static readonly Dictionary<CharacterBody, IvyVine> ByIvy = new Dictionary<CharacterBody, IvyVine>();

        public void End()
        {
            DeadlockSounds.Stop(Loop);
            if (A != null && ByIvy.TryGetValue(A, out IvyVine v) && v == this) ByIvy.Remove(A);
            Destroy(gameObject);
        }

        private void Start()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = 0.08f;
            _line.material = IvySurvivor.VineMaterial;
            _line.startColor = _line.endColor = new Color(0.4f, 0.85f, 0.3f, 0.9f);
        }

        private void LateUpdate()
        {
            bool alive(CharacterBody? b) => b != null && b.healthComponent != null && b.healthComponent.alive;
            bool still = IvySurvivor.TetherBuff != null && A != null && A.GetBuffCount(IvySurvivor.TetherBuff) >= 2;
            if (Time.time > Until || !alive(A) || !alive(B) || !still) { End(); return; }
            _line!.SetPosition(0, A!.corePosition);
            _line.SetPosition(1, B!.corePosition);
        }
    }

    // ================= Utility: Stone Form =================

    /// <summary>Owner to server: Stone Form began (stone skin: invulnerable, healing) or slammed down (the blast), or ended.</summary>
    public enum IvyStoneKind : byte { Start, Slam, End }

    public class IvyStoneMessage : INetMessage
    {
        public GameObject? Ivy;
        public IvyStoneKind Kind;
        public Vector3 At;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Ivy); w.Write((byte)Kind); w.Write(At); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Ivy = r.ReadGameObject(); Kind = (IvyStoneKind)r.ReadByte(); At = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Ivy == null || !Ivy.TryGetComponent(out CharacterBody b) || IvySurvivor.StoneBuff == null) return;
            switch (Kind)
            {
                case IvyStoneKind.Start:
                    // Until she breaks out (End): a fall can last a while, the timers only back that up.
                    b.AddTimedBuff(IvySurvivor.StoneBuff, 10f);
                    b.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, 10f);
                    DeadlockFallGuard.ProtectNextLanding(Ivy, 10f);
                    break;
                case IvyStoneKind.Slam:
                    float radius = IvyData.StoneRadius * MysticExpansion.RadiusMultiplier(b);
                    IvyNet.Effect(IvySurvivor.StoneSlamEffect ?? IvySurvivor.SlamEffect, At, radius);
                    EquipmentFx.Sound("ivy_stone_impact", At);
                    EquipmentFx.Sound("ivy_stone_explosion", At);
                    IvyNet.Ring(At, radius, 0.6f);
                    if (b.teamComponent == null) break;
                    int kills = 0;
                    foreach (HealthComponent hc in IvyNet.EnemiesIn(At, radius, b.teamComponent.teamIndex))
                    {
                        IvyNet.Hit(b, hc, IvyData.StoneDamage * b.damage, Crit, Vector3.up * 800f, DamageSource.Utility);
                        if (hc.alive) using (DurationExtender.From(b)) SetStateOnHurt.SetStunOnObject(hc.gameObject, IvyData.StoneStun);
                        else kills++;
                    }
                    CursedAppleCuriosities.Unlocks.Challenges.IvyRockBottom.Slammed(b, kills);
                    break;
                case IvyStoneKind.End:
                    b.ClearTimedBuffs(IvySurvivor.StoneBuff);
                    b.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
                    break;
            }
        }
    }

    /// <summary>Server, on Ivy: heals while she's stone (Deadlock MaxHealthRegen: a share of max health per second).</summary>
    public class IvyStoneHeal : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (!NetworkServer.active || IvySurvivor.StoneBuff == null || !_body.HasBuff(IvySurvivor.StoneBuff) || _body.healthComponent == null) return;
            _body.healthComponent.Heal(_body.healthComponent.fullHealth * IvyData.StoneHeal * Time.fixedDeltaTime, default(ProcChainMask), nonRegen: false);
        }
    }

    // ================= Special: Air Drop =================

    /// <summary>Owner to server: drop the bomb here, moving like this.</summary>
    public class IvyBombMessage : INetMessage
    {
        public GameObject? Ivy;
        public Vector3 At, Velocity;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Ivy); w.Write(At); w.Write(Velocity); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Ivy = r.ReadGameObject(); At = r.ReadVector3(); Velocity = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Ivy == null || !Ivy.TryGetComponent(out CharacterBody b) || IvySurvivor.BombProjectile == null) return;
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = IvySurvivor.BombProjectile,
                position = At,
                rotation = Util.QuaternionSafeLookRotation(Velocity),
                owner = Ivy,
                damage = b.damage,
                crit = Crit,
                force = 0f,
                speedOverride = Velocity.magnitude,
            });
            EquipmentFx.Sound("ivy_airdrop_launch", At);
            // Deadlock: Air Drop leaves her (and her passenger) dealing more damage.
            if (IvySurvivor.AirDropBuff != null) b.AddTimedBuff(IvySurvivor.AirDropBuff, IvyData.AirDropBuffSeconds);
        }
    }

    /// <summary>Server, on the Air Drop bomb: it falls and explodes where it lands (Deadlock: 16-20 m).</summary>
    [RequireComponent(typeof(ProjectileController))]
    public class IvyBomb : MonoBehaviour, IProjectileImpactBehavior
    {
        private bool _done;
        private float _age;
        private DeadlockVoice? _hum;

        private void Start() => _hum = DeadlockSounds.Loop("ivy_airdrop_fall", gameObject, 8f);
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _done) return;
            _age += Time.fixedDeltaTime;
            if (_age > 8f) Blow();
        }

        public void OnProjectileImpact(ProjectileImpactInfo info) { if (!_done && NetworkServer.active) Blow(); }

        private void Blow()
        {
            _done = true;
            var pc = GetComponent<ProjectileController>();
            var by = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            var pd = GetComponent<ProjectileDamage>();
            if (by != null && by.teamComponent != null)
            {
                Vector3 at = transform.position;
                float radius = IvyData.AirDropRadius * MysticExpansion.RadiusMultiplier(by);
                IvyNet.Effect(IvySurvivor.BombEffect, at, radius);
                EquipmentFx.Sound("ivy_airdrop_explode", at);
                foreach (HealthComponent hc in IvyNet.EnemiesIn(at, radius, by.teamComponent.teamIndex))
                    IvyNet.Hit(by, hc, IvyData.AirDropDamage * (pd != null ? pd.damage : by.damage), pd != null && pd.crit, (hc.transform.position - at).normalized * 1500f, DamageSource.Special);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Every client, on Ivy: her gun's layered sound (first shot, then mid shots; tail and release when the trigger lets go), as Vyper's.
    /// </summary>
    public class IvyGunAudio : MonoBehaviour
    {
        private float _last = -10f;
        private bool _firing;

        public static void Shot(GameObject body)
        {
            var a = body.GetComponent<IvyGunAudio>();
            bool first = a == null || !a._firing;
            DeadlockSounds.Play(first ? "ivy_gun_first" : "ivy_gun", body.transform.position, body);
            if (a != null) { a._firing = true; a._last = Time.time; }
        }

        private void Update()
        {
            if (!_firing || Time.time - _last < IvyData.GunCycle * 2.5f) return;
            _firing = false;
            DeadlockSounds.Play("ivy_gun_tail", transform.position, gameObject);
            DeadlockSounds.Play("ivy_gun_release", transform.position, gameObject);
        }
    }
}
