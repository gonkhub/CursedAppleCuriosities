using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using CursedAppleCuriosities.Survivors.Seven;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>Server side of Apollo: his abilities' hits, Riposte's parry, Itani Lo Sahn's slow-motion mark and its delayed blow.</summary>
    public static class ApolloNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<ApolloHitMessage>();
            NetworkingAPI.RegisterMessageType<ApolloParryMessage>();
            NetworkingAPI.RegisterMessageType<ApolloRiposteMessage>();
            NetworkingAPI.RegisterMessageType<ApolloRingMessage>();
            NetworkingAPI.RegisterMessageType<ApolloUltMessage>();
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ApolloSurvivor.SigilSlow != null && body.HasBuff(ApolloSurvivor.SigilSlow)) args.moveSpeedReductionMultAdd += ApolloData.SigilSlow;
                if (ApolloSurvivor.RiposteSlow != null && body.HasBuff(ApolloSurvivor.RiposteSlow)) args.moveSpeedReductionMultAdd += ApolloData.RiposteSlow;
                if (ApolloSurvivor.SigilHasteBuff != null && body.HasBuff(ApolloSurvivor.SigilHasteBuff)) args.attackSpeedMultAdd += ApolloData.SigilHaste;
            };
            // Itani Lo Sahn: masked, he takes 70% less damage (Deadlock IncomingDamageReductionPercent).
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (NetworkServer.active && info != null && ApolloSurvivor.UltMaskBuff != null && self.body != null && self.body.HasBuff(ApolloSurvivor.UltMaskBuff))
                    info.damage *= ApolloData.UltDamageTaken;
                orig(self, info);
            };
            // Riposte: the first hit Apollo takes in the stance is deflected (Deadlock: the next incoming attack), and he counters.
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (NetworkServer.active && info != null && !info.rejected && ApolloSurvivor.ParryBuff != null && self.body != null && self.body.HasBuff(ApolloSurvivor.ParryBuff)
                    && info.attacker != null && info.attacker != self.gameObject && !info.damageType.damageType.HasFlag(DamageType.DoT))
                {
                    info.rejected = true;
                    self.body.ClearTimedBuffs(ApolloSurvivor.ParryBuff);
                    self.body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, ApolloData.RiposteInvulnerable);
                    if (ApolloSurvivor.RiposteLifestealBuff != null) // lifesteal after a parry (user 2.5)
                        self.body.AddTimedBuff(ApolloSurvivor.RiposteLifestealBuff, ApolloData.RiposteLifestealSeconds * DurationExtender.Multiplier(self.body));
                    EquipmentFx.Sound("apollo_riposte_success", self.body.corePosition);
                    var attacker = info.attacker.GetComponent<CharacterBody>();
                    new ApolloRiposteMessage { Apollo = self.gameObject, Target = attacker != null ? attacker.gameObject : null }.Send(NetworkDestination.Clients);
                }
                orig(self, info);
            };
            GlobalEventManager.onServerDamageDealt += report =>
            {
                CharacterBody? by = report.attackerBody;
                if (by == null || ApolloSurvivor.RiposteLifestealBuff == null || report.damageDealt <= 0f || report.victimBody == by || !by.HasBuff(ApolloSurvivor.RiposteLifestealBuff)) return;
                if (by.healthComponent != null) by.healthComponent.Heal(report.damageDealt * ApolloData.RiposteLifesteal * Mathf.Clamp01(report.damageInfo.procCoefficient), default(ProcChainMask));
            };
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;
        public static bool IsBig(CharacterBody? b) => b != null && (b.isChampion || b.isBoss);

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

        public static void Effect(GameObject? prefab, Vector3 at, float scale, Vector3? dir = null)
        {
            if (prefab != null && NetworkServer.active)
                EffectManager.SpawnEffect(prefab, new EffectData { origin = at, scale = scale, rotation = Util.QuaternionSafeLookRotation(dir ?? Vector3.up) }, true);
        }
    }

    public enum ApolloHitKind : byte { Sigil, Stab, PerfectStab, Riposte, Ult }

    /// <summary>Owner to server: one of Apollo's abilities caught these enemies.</summary>
    public class ApolloHitMessage : INetMessage
    {
        public ApolloHitKind Kind;
        public GameObject? Attacker;
        public GameObject[] Targets = new GameObject[0];
        public float Coefficient;
        public Vector3 Dir;
        public bool Crit;

        public void Serialize(NetworkWriter w)
        {
            w.Write((byte)Kind); w.Write(Attacker); w.Write(Coefficient); w.Write(Dir); w.Write(Crit);
            w.Write((byte)Mathf.Min(255, Targets.Length));
            for (int i = 0; i < Targets.Length && i < 255; i++) w.Write(Targets[i]);
        }

        public void Deserialize(NetworkReader r)
        {
            Kind = (ApolloHitKind)r.ReadByte(); Attacker = r.ReadGameObject(); Coefficient = r.ReadSingle(); Dir = r.ReadVector3(); Crit = r.ReadBoolean();
            Targets = new GameObject[r.ReadByte()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            DamageTypeCombo type = Kind switch
            {
                ApolloHitKind.Sigil => DamageSource.Secondary,
                ApolloHitKind.Ult => DamageSource.Special,
                _ => DamageSource.Utility,
            };
            if (Kind == ApolloHitKind.PerfectStab)
            {
                EquipmentFx.Sound("apollo_lunge_perfect", body.corePosition, Attacker);
                // A perfect stab that lands heals him, once a stab (user 2.5).
                if (body.healthComponent != null && System.Array.Exists(Targets, t => t != null && t.TryGetComponent(out HealthComponent h) && h.alive))
                {
                    float before = body.healthComponent.health;
                    body.healthComponent.HealFraction(ApolloData.PerfectHeal, default(ProcChainMask));
                    CursedAppleCuriosities.Unlocks.Challenges.ApolloEnGarde.PerfectHeal(body, body.healthComponent.health - before);
                }
            }
            // Itani Lo Sahn: Apollo is invulnerable while his targets are in slow motion (Deadlock).
            if (Kind == ApolloHitKind.Ult) body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, ApolloData.UltLock * DurationExtender.Multiplier(body));
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                ApolloNet.Hit(body, hc, Coefficient * body.damage, Crit, Dir * 500f, type);
                ApolloNet.Effect(ApolloSurvivor.SlashEffect, ApolloNet.Core(hc), 1.5f, Dir);
                switch (Kind)
                {
                    case ApolloHitKind.Sigil:
                        EquipmentFx.Sound("apollo_sigil_hit", ApolloNet.Core(hc));
                        if (hc.alive && hc.body != null && ApolloSurvivor.SigilSlow != null)
                            using (DurationExtender.From(body)) hc.body.AddTimedBuff(ApolloSurvivor.SigilSlow, ApolloData.SigilSlowSeconds);
                        break;
                    case ApolloHitKind.Stab:
                    case ApolloHitKind.PerfectStab:
                        EquipmentFx.Sound("apollo_lunge_hit", ApolloNet.Core(hc));
                        break;
                    case ApolloHitKind.Riposte:
                        EquipmentFx.Sound("apollo_riposte_hit", ApolloNet.Core(hc));
                        if (!hc.alive || hc.body == null) break;
                        using (DurationExtender.From(body))
                        {
                            SetStateOnHurt.SetStunOnObject(hc.gameObject, ApolloData.RiposteStun);
                            if (ApolloSurvivor.RiposteSlow != null) hc.body.AddTimedBuff(ApolloSurvivor.RiposteSlow, ApolloData.RiposteSlowSeconds);
                        }
                        break;
                    case ApolloHitKind.Ult:
                        EquipmentFx.Sound("apollo_ult_hit", ApolloNet.Core(hc));
                        if (!hc.alive) break;
                        var mark = t.GetComponent<ApolloUltMark>() ?? t.AddComponent<ApolloUltMark>();
                        mark.Begin(body, Crit);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Server, on an enemy struck by Itani Lo Sahn: stuck in slow motion (stunned, unless a boss) for 1.8 s, then the devastating blow,
    /// with Deadlock's bonus against enemies under half health.
    /// </summary>
    public class ApolloUltMark : MonoBehaviour
    {
        private CharacterBody? _by;
        private bool _crit;
        private float _until;

        public void Begin(CharacterBody by, bool crit)
        {
            _by = by; _crit = crit;
            float lockFor = ApolloData.UltLock * DurationExtender.Multiplier(by);
            _until = Time.time + lockFor;
            var hc = GetComponent<HealthComponent>();
            if (hc != null && !ApolloNet.IsBig(hc.body)) SetStateOnHurt.SetStunOnObject(gameObject, lockFor);
            if (hc != null && hc.body != null && ApolloSurvivor.UltMarkedBuff != null) hc.body.AddTimedBuff(ApolloSurvivor.UltMarkedBuff, lockFor + 0.2f); // its red glow (user 2.5)
            EquipmentFx.Sound("apollo_ult_timer", transform.position, gameObject);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _until) return;
            var hc = GetComponent<HealthComponent>();
            if (_by != null && hc != null && hc.alive)
            {
                float bonus = hc.combinedHealthFraction < ApolloData.UltLowHealth ? 1f + ApolloData.UltLowHealthBonus : 1f;
                ApolloNet.Hit(_by, hc, ApolloData.UltDelayed * bonus * _by.damage, _crit, Vector3.zero, DamageSource.Special);
                ApolloNet.Effect(ApolloSurvivor.UltEffect, ApolloNet.Core(hc), 3f);
                EquipmentFx.Sound("apollo_ult_end", ApolloNet.Core(hc));
            }
            Destroy(this);
        }
    }

    /// <summary>Owner to server: Riposte's stance is up (the next hit is parried), or over.</summary>
    public class ApolloParryMessage : INetMessage
    {
        public GameObject? Apollo;
        public bool On;

        public void Serialize(NetworkWriter w) { w.Write(Apollo); w.Write(On); }
        public void Deserialize(NetworkReader r) { Apollo = r.ReadGameObject(); On = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Apollo == null || !Apollo.TryGetComponent(out CharacterBody b) || ApolloSurvivor.ParryBuff == null) return;
            if (On) b.AddTimedBuff(ApolloSurvivor.ParryBuff, ApolloData.RiposteWindow + 0.1f);
            else b.ClearTimedBuffs(ApolloSurvivor.ParryBuff);
        }
    }

    /// <summary>Server to every client: Apollo parried; his owner's stance ends and the counter opens (ApolloCounter, user 3.0).</summary>
    public class ApolloRiposteMessage : INetMessage
    {
        public GameObject? Apollo, Target;

        public void Serialize(NetworkWriter w) { w.Write(Apollo); w.Write(Target); }
        public void Deserialize(NetworkReader r) { Apollo = r.ReadGameObject(); Target = r.ReadGameObject(); }

        public void OnReceived()
        {
            if (Apollo == null || !Apollo.TryGetComponent(out CharacterBody b) || !b.hasEffectiveAuthority) return;
            EntityStateMachine? machine = EntityStateMachine.FindByCustomName(Apollo, "Body");
            if (machine != null && machine.state is EntityStates.CursedApple.Apollo.Riposte) machine.SetNextStateToMain();
            (Apollo.GetComponent<ApolloCounter>() ?? Apollo.AddComponent<ApolloCounter>()).Arm();
        }
    }

    /// <summary>Every client: a gold ring (the sigil, the lunge line's end) that fades over its seconds.</summary>
    public class ApolloRingMessage : INetMessage
    {
        public Vector3 Point;
        public float Radius, Seconds;

        public void Serialize(NetworkWriter w) { w.Write(Point); w.Write(Radius); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { Point = r.ReadVector3(); Radius = r.ReadSingle(); Seconds = r.ReadSingle(); }

        public void OnReceived() => Show(Point, Radius, Seconds);

        public static void Show(Vector3 at, float radius, float seconds)
        {
            var go = new GameObject("CursedAppleApolloRing");
            go.transform.position = at;
            var ring = go.AddComponent<SevenRing>();
            ring.Radius = radius;
            ring.Seconds = seconds;
            ring.Color = new Color(1f, 0.85f, 0.45f, 0.9f);
        }
    }

    /// <summary>
    /// Owner side, on Apollo: Flawless Advance's three stabs (Deadlock MaxStabs 3, RecastTime 5 s). Each stab hands its charge back while
    /// stabs remain; the cooldown starts after the third, or when 5 s pass without the next.
    /// </summary>
    public class ApolloAdvanceRecast : MonoBehaviour
    {
        private int _used;
        private float _until;
        private GenericSkill? _slot;

        public int Used => _used;

        public void Stabbed()
        {
            _slot = GetComponent<SkillLocator>()?.utility;
            if (Time.time > _until) _used = 0;
            _used++;
            if (_slot == null) return;
            if (_used < ApolloData.Stabs) { _slot.AddOneStock(); _until = Time.time + ApolloData.StabRecastWindow; }
            else { _used = 0; _until = 0f; }
        }

        private void Update()
        {
            if (_used == 0 || Time.time < _until) return;
            _used = 0;
            if (_slot != null && _slot.stock > 0) _slot.DeductStock(1);
        }
    }

    /// <summary>Every client, on Apollo: which way the next sword wave swings (Deadlock alternates his left and right cuts).</summary>
    public class ApolloSwing : MonoBehaviour { public bool Left; }
}
