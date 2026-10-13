using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Entity states run on the player who owns Grey Talon; damage, stuns and buffs happen on the server. These messages
    /// carry that over (on a host they simply run locally). The Guided Owl flies on its owner's machine (it is steered
    /// from there, camera and all); its position goes to the server and on to everyone else as a ghost.
    /// </summary>
    public static class GreyTalonNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<GreyTalonBuffMessage>();
            NetworkingAPI.RegisterMessageType<GreyTalonOwlMoveMessage>();
            NetworkingAPI.RegisterMessageType<GreyTalonOwlGhostMessage>();
            NetworkingAPI.RegisterMessageType<GreyTalonOwlBlastMessage>();
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Grey Talon, with procs.</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc, Vector3? at = null)
        {
            if (target == null || !target.alive || damage <= 0f) return;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = at ?? Core(target),
                force = force,
                procCoefficient = proc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
        }

        /// <summary>Enemies of a team whose hurtboxes touch a sphere (one per body).</summary>
        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            HurtBox[] boxes = new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
            foreach (HurtBox h in boxes) if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        public static void Effect(GameObject? prefab, Vector3 at, float scale, Vector3? normal = null)
        {
            if (prefab != null && NetworkServer.active)
                EffectManager.SpawnEffect(prefab, new EffectData { origin = at, scale = scale, rotation = Util.QuaternionSafeLookRotation(normal ?? Vector3.up) }, true);
        }
    }

    public enum GreyTalonBuffKind : byte { ChargeStart, ChargeFire, OwlStart, OwlLaunch, OwlStop }

    /// <summary>Charged Shot drawn / loosed, Guided Owl cast / launched / over: the server sets the synced buffs.</summary>
    public class GreyTalonBuffMessage : INetMessage
    {
        public GameObject? Attacker;
        public GreyTalonBuffKind Kind;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write((byte)Kind); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Kind = (GreyTalonBuffKind)r.ReadByte(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            const float ownerMax = GreyTalonData.OwlCastDelay + GreyTalonData.OwlFlight + 2f;
            switch (Kind)
            {
                case GreyTalonBuffKind.ChargeStart:
                    if (GreyTalonSurvivor.ChargingBuff == null) return;
                    body.ClearTimedBuffs(GreyTalonSurvivor.ChargingBuff);
                    body.AddTimedBuff(GreyTalonSurvivor.ChargingBuff, 60f); // ended by ChargeFire (held as long as he likes)
                    break;
                case GreyTalonBuffKind.ChargeFire:
                    if (GreyTalonSurvivor.ChargingBuff != null) body.ClearTimedBuffs(GreyTalonSurvivor.ChargingBuff);
                    break;
                case GreyTalonBuffKind.OwlStart:
                    // Untargetable while casting and steering (user): no damage, enemies lose him, no skills.
                    using (DurationExtender.Exact())
                    {
                        if (GreyTalonSurvivor.OwlChannelBuff != null) body.AddTimedBuff(GreyTalonSurvivor.OwlChannelBuff, ownerMax);
                        body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, ownerMax);
                        body.AddTimedBuff(RoR2Content.Buffs.Cloak, ownerMax);
                    }
                    break;
                case GreyTalonBuffKind.OwlLaunch:
                    if (GreyTalonSurvivor.OwlFlyingBuff != null) using (DurationExtender.Exact()) body.AddTimedBuff(GreyTalonSurvivor.OwlFlyingBuff, GreyTalonData.OwlFlight * DurationExtender.Multiplier(body) + 1f);
                    break;
                case GreyTalonBuffKind.OwlStop:
                    // He has control again. These were his own channel's (timed to it): cleared, not left to run out. Then a
                    // short grace (user): still invulnerable and unseen, so he can get away from whatever closed in on him.
                    if (GreyTalonSurvivor.OwlChannelBuff != null) body.ClearTimedBuffs(GreyTalonSurvivor.OwlChannelBuff);
                    body.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
                    body.ClearTimedBuffs(RoR2Content.Buffs.Cloak);
                    using (DurationExtender.Exact())
                    {
                        body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, GreyTalonData.OwlExitGrace);
                        body.AddTimedBuff(RoR2Content.Buffs.Cloak, GreyTalonData.OwlExitGrace);
                    }
                    break;
            }
        }
    }

    /// <summary>Owner -> server: where the owl is (Gone = it burst or ran out). The server passes it on as a ghost message.</summary>
    public class GreyTalonOwlMoveMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Pos, Dir;
        public bool Gone;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Pos); w.Write(Dir); w.Write(Gone); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Pos = r.ReadVector3(); Dir = r.ReadVector3(); Gone = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            new GreyTalonOwlGhostMessage { Attacker = Attacker, Pos = Pos, Dir = Dir, Gone = Gone }.Send(NetworkDestination.Clients);
            GreyTalonOwlGhostMessage.Show(Attacker, Pos, Dir, Gone); // the host's own view
        }
    }

    /// <summary>Server -> clients: the owl ghost everyone but its owner sees.</summary>
    public class GreyTalonOwlGhostMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Pos, Dir;
        public bool Gone;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Pos); w.Write(Dir); w.Write(Gone); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Pos = r.ReadVector3(); Dir = r.ReadVector3(); Gone = r.ReadBoolean(); }

        public void OnReceived() { if (!NetworkServer.active && Attacker != null) Show(Attacker, Pos, Dir, Gone); }

        public static void Show(GameObject attacker, Vector3 pos, Vector3 dir, bool gone)
        {
            var body = attacker.GetComponent<CharacterBody>();
            if (body == null || body.hasEffectiveAuthority) return; // the owner flies the real one
            GreyTalonOwlGhost.Update(attacker, pos, dir, gone);
        }
    }

    /// <summary>Owner -> server: the owl burst here. Server: the blast (damage, stun, boss bonus).</summary>
    public class GreyTalonOwlBlastMessage : INetMessage
    {
        public GameObject? Attacker;
        public Vector3 Point;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Point); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Point = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null || body.teamComponent == null) return;
            if (GreyTalonSurvivor.OwlFlyingBuff != null) body.ClearTimedBuffs(GreyTalonSurvivor.OwlFlyingBuff);
            float radius = GreyTalonData.OwlRadius * MysticExpansion.RadiusMultiplier(body);
            GreyTalonNet.Effect(GreyTalonSurvivor.OwlBlastEffect, Point, radius);
            GreyTalonNet.Effect(GreyTalonSurvivor.OwlBlastWave, Point, radius);
            EquipmentFx.Sound("greytalon_owl_explode", Point);
            GreyTalonOwlTrophies? trophies = body.master != null ? GreyTalonOwlTrophies.On(body.master) : null;
            float damage = GreyTalonData.OwlDamage * (1f + GreyTalonData.OwlBossBonus * (trophies != null ? trophies.Kills : 0)) * body.damage;
            int bossKills = 0;
            foreach (HealthComponent hc in GreyTalonNet.EnemiesIn(Point, radius, body.teamComponent.teamIndex))
            {
                bool boss = hc.body != null && (hc.body.isChampion || hc.body.isBoss);
                Vector3 away = GreyTalonNet.Core(hc) - Point;
                GreyTalonNet.Hit(body, hc, damage, Crit, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.up) * 1200f, DamageSource.Special, 1f);
                if (!hc.alive) { if (boss) bossKills++; continue; }
                using (DurationExtender.From(body)) SetStateOnHurt.SetStunOnObject(hc.gameObject, GreyTalonData.OwlStun);
            }
            if (trophies != null && bossKills > 0)
            {
                trophies.Kills += bossKills;
                trophies.Show(body);
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[GreyTalon] owl killed {bossKills} boss(es): {trophies.Kills} this run, owl damage x{1f + GreyTalonData.OwlBossBonus * trophies.Kills:0.##}");
            }
        }
    }

    /// <summary>Server, on the player's master: bosses the Guided Owl has killed this run (each makes it stronger: user).</summary>
    public class GreyTalonOwlTrophies : MonoBehaviour
    {
        public int Kills;

        public static GreyTalonOwlTrophies On(CharacterMaster master) =>
            master.GetComponent<GreyTalonOwlTrophies>() ?? master.gameObject.AddComponent<GreyTalonOwlTrophies>();

        /// <summary>The count as stacks of a buff on his body, so the player sees it.</summary>
        public void Show(CharacterBody body)
        {
            BuffDef? buff = GreyTalonSurvivor.OwlTrophyBuff;
            if (buff == null || !NetworkServer.active) return;
            int have = body.GetBuffCount(buff);
            for (int i = have; i < Kills; i++) body.AddBuff(buff);
            for (int i = Kills; i < have; i++) body.RemoveBuff(buff);
        }
    }

    /// <summary>Server, on his body: puts the run's owl trophies back on each new body (respawns, new stages).</summary>
    public class GreyTalonTrophySync : MonoBehaviour
    {
        private void Start()
        {
            if (!NetworkServer.active) return;
            var body = GetComponent<CharacterBody>();
            if (body != null && body.master != null) GreyTalonOwlTrophies.On(body.master).Show(body);
        }
    }

    /// <summary>Every client, on his body: whether Rain of Arrows' glide is on (the state sets it where it runs; the bow reads it).</summary>
    public class GreyTalonRain : MonoBehaviour
    {
        private float _until;

        public static void Begin(GameObject body, float seconds) =>
            (body.GetComponent<GreyTalonRain>() ?? body.AddComponent<GreyTalonRain>())._until = Time.time + seconds;

        public static void End(GameObject body)
        {
            var r = body.GetComponent<GreyTalonRain>();
            if (r != null) r._until = 0f;
        }

        public static bool Active(GameObject body)
        {
            var r = body.GetComponent<GreyTalonRain>();
            return r != null && Time.time < r._until;
        }
    }

    /// <summary>
    /// Server, on the Charged Shot arrow: every tick it sweeps the path it just flew (a sphere the arrow's size) and hits
    /// every enemy on it once (Deadlock: pierces through enemies); terrain stops it.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class GreyTalonChargedArrow : MonoBehaviour
    {
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();
        private Vector3 _last;
        private bool _started, _done;

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _done) return;
            Vector3 now = transform.position;
            if (!_started) { _started = true; _last = now; return; }
            Vector3 d = now - _last;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var pc = GetComponent<ProjectileController>();
            var pd = GetComponent<ProjectileDamage>();
            CharacterBody? by = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            if (by == null || by.teamComponent == null) { Destroy(gameObject); return; }
            RaycastHit[] hits = Physics.SphereCastAll(_last, GreyTalonData.ChargedRadius, d / len, len, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (((1 << h.collider.gameObject.layer) & LayerIndex.world.mask) != 0)
                {
                    Vector3 at = h.point != Vector3.zero ? h.point : _last;
                    GreyTalonNet.Effect(GreyTalonSurvivor.ChargedHitEffect, at, 1f, h.normal);
                    _done = true;
                    Destroy(gameObject);
                    return;
                }
                HurtBox? box = h.collider.GetComponent<HurtBox>();
                HealthComponent? hc = box != null ? box.healthComponent : null;
                if (hc == null || !hc.alive || box!.teamIndex == by.teamComponent.teamIndex || !_hit.Add(hc)) continue;
                GreyTalonNet.Hit(by, hc, pd != null ? pd.damage : by.damage * GreyTalonData.ChargedDamage, pd != null && pd.crit, d / len * (pd != null ? pd.force : 1500f), DamageSource.Secondary, 1f, h.point);
                GreyTalonNet.Effect(GreyTalonSurvivor.ChargedHitEffect, h.point != Vector3.zero ? h.point : GreyTalonNet.Core(hc), 1f, -d);
                EquipmentFx.Sound("greytalon_charge_hit", GreyTalonNet.Core(hc));
            }
            _last = now;
        }
    }
}
