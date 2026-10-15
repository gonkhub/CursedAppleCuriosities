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

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// Server side of Shiv: Bloodletting (a share of every hit deferred into a 6 s drain), Rage (built by dealing damage,
    /// drains when he stops), his hits (Slice'n'Dice, Killing Blow, the knife) and Killing Blow's execute. States run on
    /// the owning player and message the server; on a host they simply run locally.
    /// </summary>
    public static class ShivNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<ShivHitMessage>();
            NetworkingAPI.RegisterMessageType<ShivExecuteMessage>();
            NetworkingAPI.RegisterMessageType<ShivPendingMessage>();
            ShivBloodletting.Hook();
            ShivRage.Hook();
            ShivRageHud.Hook();
            ShivBloodlettingBar.Hook();
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ShivSurvivor.KnifeSlow != null && body.HasBuff(ShivSurvivor.KnifeSlow)) args.moveSpeedReductionMultAdd += ShivData.KnifeSlow;
            };
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Shiv, with procs.</summary>
        public static DamageInfo? Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f)
        {
            if (target == null || !target.alive || damage <= 0f) return null;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                damage = damage,
                crit = crit,
                position = Core(target),
                force = force,
                procCoefficient = proc,
                damageColorIndex = DamageColorIndex.Default,
                damageType = type,
            };
            target.TakeDamage(di);
            if (di.rejected) return di;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
            return di;
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        /// <summary>Server: Tri-Tip's Bleed, stacks of it, lengthened by Shiv's Duration Extender (the DoT hook reads the attacker).</summary>
        public static void Bleed(CharacterBody attacker, HealthComponent target, int stacks)
        {
            if (target == null || !target.alive) return;
            for (int i = 0; i < stacks; i++)
            {
                var info = new InflictDotInfo
                {
                    victimObject = target.gameObject,
                    attackerObject = attacker.gameObject,
                    dotIndex = DotController.DotIndex.Bleed,
                    duration = ShivData.BleedSeconds,
                    damageMultiplier = 1f, // full rage doubles it in ShivRage's DoT hook
                };
                DotController.InflictDot(ref info);
            }
        }

        /// <summary>Is this enemy under Killing Blow's threshold for Shiv right now? Every machine (health and rage are synced).</summary>
        public static bool Executable(CharacterBody shiv, HealthComponent? target)
        {
            if (target == null || !target.alive || target.body == null) return false;
            bool boss = target.body.isChampion || target.body.isBoss;
            return target.combinedHealthFraction <= ShivData.Threshold(ShivRage.Full(shiv), boss);
        }

        /// <summary>Server: Killing Blow's execute. The kill is Shiv's; his Bloodletting is cleansed and he gains a lot of rage.</summary>
        public static void Execute(CharacterBody shiv, HealthComponent target)
        {
            if (target == null || !target.alive) return;
            EquipmentFx.Sound("shiv_lunge_kill", Core(target));
            if (ShivSurvivor.ExecuteEffect != null)
                EffectManager.SpawnEffect(ShivSurvivor.ExecuteEffect, new EffectData { origin = Core(target), scale = target.body != null ? target.body.bestFitRadius * 2f : 3f }, true);
            target.Suicide(shiv.gameObject, shiv.gameObject, DamageSource.Special);
            if (shiv.TryGetComponent(out ShivBloodletting blood)) blood.Cleanse();
            if (shiv.TryGetComponent(out ShivRage rage)) rage.Add(ShivData.ExecuteRage);
            EquipmentFx.Sound("shiv_cleanse", shiv.corePosition, shiv.gameObject);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Shiv] Killing Blow executed {target.name}");
        }
    }

    public enum ShivHitKind : byte { Slice, Lunge }

    /// <summary>Owner to server: Slice'n'Dice / Killing Blow struck these enemies.</summary>
    public class ShivHitMessage : INetMessage
    {
        public ShivHitKind Kind;
        public GameObject? Attacker;
        public GameObject[] Targets = new GameObject[0];
        public bool Crit;
        public Vector3 Dir;

        public void Serialize(NetworkWriter w)
        {
            w.Write((byte)Kind); w.Write(Attacker); w.Write(Crit); w.Write(Dir);
            w.Write((byte)Mathf.Min(255, Targets.Length));
            for (int i = 0; i < Targets.Length && i < 255; i++) w.Write(Targets[i]);
        }

        public void Deserialize(NetworkReader r)
        {
            Kind = (ShivHitKind)r.ReadByte(); Attacker = r.ReadGameObject(); Crit = r.ReadBoolean(); Dir = r.ReadVector3();
            Targets = new GameObject[r.ReadByte()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            float coeff = Kind == ShivHitKind.Slice ? ShivData.DashDamage * (ShivRage.Full(body) ? 2f : 1f) : ShivData.LungeDamage; // full rage: Slice'n'Dice x2 (user)
            DamageTypeCombo type = Kind == ShivHitKind.Slice ? DamageSource.Utility : DamageSource.Special;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                ShivNet.Hit(body, hc, coeff * body.damage, Crit, Dir * 800f, type);
                EquipmentFx.Sound(Kind == ShivHitKind.Slice ? "shiv_dash_hit" : "shiv_lunge_hit", ShivNet.Core(hc));
                if (ShivSurvivor.SlashEffect != null)
                    EffectManager.SpawnEffect(ShivSurvivor.SlashEffect, new EffectData { origin = ShivNet.Core(hc), rotation = Util.QuaternionSafeLookRotation(Dir), scale = 1.5f }, true);
            }
        }
    }

    /// <summary>Owner to server: Killing Blow collided with an enemy under the threshold. The server checks it again.</summary>
    public class ShivExecuteMessage : INetMessage
    {
        public GameObject? Attacker, Target;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null || Target == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            var hc = Target.GetComponent<HealthComponent>();
            if (body == null || hc == null || !hc.alive) return;
            // A little slack for health that ticked between the owner's check and now (regen, a heal).
            bool boss = hc.body != null && (hc.body.isChampion || hc.body.isBoss);
            if (hc.combinedHealthFraction > ShivData.Threshold(ShivRage.Full(body), boss) + 0.03f)
            {
                ShivNet.Hit(body, hc, ShivData.LungeDamage * body.damage, false, Vector3.zero, DamageSource.Special);
                return;
            }
            ShivNet.Execute(body, hc);
        }
    }

    // ================= Passive: Bloodletting =================

    /// <summary>
    /// Bloodletting on a Shiv body (Deadlock citadel_ability_shiv_defer_damage, made passive: user). Server: a share of every
    /// hit he takes (user: 50%) is taken off the hit and dealt to him instead over 6 s, evenly, each hit its own pool. Once all
    /// of his remaining health is already bloodletting, hits land in full (user). Every client: Pending, for the health bar.
    /// </summary>
    public class ShivBloodletting : MonoBehaviour
    {
        private class Pool { public float PerTick; public int TicksLeft; public GameObject? Attacker; }
        private readonly List<Pool> _pools = new List<Pool>();
        private float _nextTick;
        private CharacterBody _body = null!;
        [System.ThreadStatic] private static bool _ticking;
        /// <summary>Bloodletting's own ticks: never deferred again, wherever and whenever they reach TakeDamage (user 2.5: it re-deferred itself).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DamageInfo, object> Ticks = new System.Runtime.CompilerServices.ConditionalWeakTable<DamageInfo, object>();

        /// <summary>One of Bloodletting's ticks (by identity, or by its signature: a silent bleed-coloured DoT with no procs).</summary>
        private static bool IsTick(DamageInfo info) =>
            Ticks.TryGetValue(info, out _) ||
            info.damageColorIndex == DamageColorIndex.Bleed && info.procCoefficient == 0f
            && (info.damageType.damageType & (DamageType.DoT | DamageType.Silent)) == (DamageType.DoT | DamageType.Silent);

        /// <summary>Health still to drain (every client).</summary>
        public float Pending { get; set; }

        private void Awake() => _body = GetComponent<CharacterBody>();

        public static void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                ShivBloodletting? b = null;
                Pool? added = null;
                float before = 0f, sent = 0f;
                if (NetworkServer.active && !_ticking && info != null && !IsTick(info) && !info.rejected && info.damage > 0f && self.alive && self.TryGetComponent(out b))
                {
                    before = self.combinedHealth;
                    added = b!.Defer(info);
                    sent = info!.damage;
                }
                orig(self, info);
                if (added == null) return;
                if (info!.rejected) { b!._pools.Remove(added); b.Sync(); return; } // blocked after all (Tougher Times, a ward): nothing to drain
                // The deferred share was taken off the raw hit, before the game's damage maths (armour, crits, curse and elite
                // multipliers, the attacker's items). What the rest of the hit really cost tells how much the share is really worth,
                // so the yellow bar shows the health that will actually drain (user 3.0: it came off both bars).
                float lost = before - self.combinedHealth;
                if (sent > 0.01f && lost > 0f)
                {
                    added.PerTick *= Mathf.Clamp(lost / sent, 0f, 10f);
                    b!.Sync();
                }
            };
        }

        /// <summary>Server: take the deferred share off this hit and pool it. Null when nothing could be deferred.</summary>
        private Pool? Defer(DamageInfo info)
        {
            HealthComponent hc = _body.healthComponent;
            float room = hc.combinedHealth - PendingTotal();
            if (room <= 0f) return null; // all of his health is already bloodletting: the hit lands in full (user)
            float share = Mathf.Min(info.damage * ShivData.DeferredShare, room);
            if (share <= 0f) return null;
            info.damage -= share;
            int ticks = Mathf.Max(1, Mathf.RoundToInt(ShivData.DeferredSeconds / ShivData.DeferredTick));
            var pool = new Pool { PerTick = share / ticks, TicksLeft = ticks, Attacker = info.attacker };
            _pools.Add(pool);
            Sync();
            return pool;
        }

        private float PendingTotal()
        {
            float p = 0f;
            foreach (Pool x in _pools) p += x.PerTick * x.TicksLeft;
            return p;
        }

        /// <summary>Server: Killing Blow's execute clears what's left (Deadlock: Bloodletting's cleanse).</summary>
        public void Cleanse()
        {
            _pools.Clear();
            Sync();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _nextTick) return;
            _nextTick = Time.time + ShivData.DeferredTick;
            if (_pools.Count == 0) return;
            HealthComponent hc = _body.healthComponent;
            if (hc == null || !hc.alive) { _pools.Clear(); Sync(); return; }
            // Each pool keeps its own attacker, so the kill (if it comes to that) is credited to whoever dealt it. A tick is health
            // already worked out (see the hook): it skips the damage maths (no crit, armour or multipliers a second time), so each
            // tick takes exactly what it pays off the yellow bar.
            foreach (Pool p in _pools.ToArray())
            {
                p.TicksLeft--;
                if (!hc.alive) break;
                var di = new DamageInfo
                {
                    attacker = p.Attacker,
                    damage = p.PerTick,
                    crit = false,
                    position = _body.corePosition,
                    procCoefficient = 0f,
                    damageColorIndex = DamageColorIndex.Bleed,
                    damageType = new DamageTypeCombo(DamageType.DoT | DamageType.BypassArmor | DamageType.BypassBlock | DamageType.BypassOneShotProtection | DamageType.Silent,
                        DamageTypeExtended.BypassDamageCalculations, DamageSource.NoneSpecified),
                };
                Ticks.Add(di, di);
                _ticking = true;
                try { hc.TakeDamage(di); }
                finally { _ticking = false; }
            }
            _pools.RemoveAll(x => x.TicksLeft <= 0);
            Sync();
        }

        /// <summary>Server: the pending total, here and on every client.</summary>
        private void Sync()
        {
            float pending = PendingTotal();
            if (Mathf.Abs(pending - Pending) < 0.01f) return;
            Pending = pending;
            new ShivPendingMessage { Body = gameObject, Pending = pending }.Send(NetworkDestination.Clients);
        }
    }

    /// <summary>Server to every client: how much health Shiv's Bloodletting has yet to drain.</summary>
    public class ShivPendingMessage : INetMessage
    {
        public GameObject? Body;
        public float Pending;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Pending); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Pending = r.ReadSingle(); }

        public void OnReceived()
        {
            if (Body != null && Body.TryGetComponent(out ShivBloodletting b)) b.Pending = Pending;
        }
    }

    /// <summary>
    /// On every health bar (the HUD's and allies' overhead ones): Bloodletting's pending damage, drawn yellow over the end
    /// of his current health (user), so the player sees what he'll drop to.
    /// </summary>
    public class ShivBloodlettingBar : MonoBehaviour
    {
        private static readonly Color Tint = new Color(1f, 0.85f, 0.15f, 0.9f);

        private RoR2.UI.HealthBar _bar = null!;
        private RectTransform? _fill;
        private HealthComponent? _source;
        private ShivBloodletting? _blood;

        public static void Hook()
        {
            On.RoR2.UI.HealthBar.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<ShivBloodlettingBar>()._bar = self; };
        }

        private void LateUpdate()
        {
            HealthComponent? src = _bar != null ? _bar.source : null;
            if (src != _source) { _source = src; _blood = src != null ? src.GetComponent<ShivBloodletting>() : null; }
            float pending = _blood != null && src != null && src.alive ? Mathf.Min(_blood.Pending, src.health + src.shield) : 0f;
            if (pending <= 0.5f || _bar.barContainer == null) { if (_fill != null) _fill.gameObject.SetActive(false); return; }
            if (_fill == null) Build();
            HealthComponent.HealthBarValues v = src!.GetHealthBarValues();
            float per = (1f - v.curseFraction) / Mathf.Max(1f, src.fullCombinedHealth);
            float to = v.healthFraction + v.shieldFraction;
            float from = Mathf.Max(0f, to - pending * per);
            _fill!.gameObject.SetActive(to > from);
            _fill.anchorMin = new Vector2(from, 0f);
            _fill.anchorMax = new Vector2(to, 1f);
            _fill.SetAsLastSibling();
        }

        private void Build()
        {
            var go = new GameObject("ShivBloodletting", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            _fill = (RectTransform)go.transform;
            _fill.SetParent(_bar.barContainer, false);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = Tint;
            img.raycastTarget = false;
        }
    }

    // ================= Passive: Rage =================

    /// <summary>
    /// Rage on a Shiv body (Deadlock: Killing Blow's passive). Server: dealing damage builds it (full after dealing
    /// RageFillDamage x his damage stat: user); 2 s after he last dealt damage it drains (user). Every client sees it as the
    /// Rage buff's stack count (0-100). At full rage his bleeds and Slice'n'Dice deal double and Killing Blow executes
    /// higher (user).
    /// </summary>
    public class ShivRage : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _rage, _lastDealt = -10f;
        private bool _wasFull;

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>Full rage, read from the synced buff (every machine).</summary>
        public static bool Full(CharacterBody? body) =>
            body != null && ShivSurvivor.RageBuff != null && body.GetBuffCount(ShivSurvivor.RageBuff) >= ShivData.RageMax;

        public static void Hook()
        {
            GlobalEventManager.onServerDamageDealt += report =>
            {
                CharacterBody? by = report.attackerBody;
                if (by == null || report.damageDealt <= 0f || report.victimBody == by || !by.TryGetComponent(out ShivRage rage)) return;
                if (report.victimTeamIndex == by.teamComponent.teamIndex) return;
                rage._lastDealt = Time.time;
                rage.Add(report.damageDealt / Mathf.Max(1f, ShivData.RageFillDamage * by.damage) * ShivData.RageMax);
            };
            // Full rage: every bleed Shiv inflicts deals double (his knife's and Tri-Tip's alike: user, "his bleed effects").
            On.RoR2.DotController.InflictDot_refInflictDotInfo += (On.RoR2.DotController.orig_InflictDot_refInflictDotInfo orig, ref InflictDotInfo info) =>
            {
                if (info.dotIndex == DotController.DotIndex.Bleed && info.attackerObject != null && Full(info.attackerObject.GetComponent<CharacterBody>()))
                    info.damageMultiplier *= 2f;
                orig(ref info);
            };
        }

        /// <summary>Server: add rage (capped), and show it.</summary>
        public void Add(float amount)
        {
            if (!NetworkServer.active) return;
            _rage = Mathf.Clamp(_rage + amount, 0f, ShivData.RageMax);
            if (amount > 0f) _lastDealt = Time.time;
            Show();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _rage <= 0f) return;
            // Full rage holds longer before it starts to fall (user 3.0); once falling it drains at the usual rate.
            float delay = _rage >= ShivData.RageMax ? ShivData.RageFullDrainDelay : ShivData.RageDrainDelay;
            if (Time.time - _lastDealt < delay) return;
            _rage = Mathf.Max(0f, _rage - ShivData.RageMax / Mathf.Max(0.1f, ShivData.RageDrainSeconds) * Time.fixedDeltaTime);
            Show();
        }

        private void Show()
        {
            if (ShivSurvivor.RageBuff == null) return;
            int want = _rage >= ShivData.RageMax - 0.01f ? (int)ShivData.RageMax : Mathf.FloorToInt(_rage);
            int have = _body.GetBuffCount(ShivSurvivor.RageBuff);
            if (want != have) _body.SetBuffCount(ShivSurvivor.RageBuff.buffIndex, want);
            bool full = want >= ShivData.RageMax;
            if (full && !_wasFull) EquipmentFx.Sound("shiv_rage_full", _body.corePosition, _body.gameObject);
            else if (!full && _wasFull) EquipmentFx.Sound("shiv_rage_end", _body.corePosition, _body.gameObject);
            _wasFull = full;
        }
    }

    // ================= Secondary: Serrated Knife =================

    /// <summary>
    /// Server, on the Serrated Knife: the first enemy it hits takes the hit, Tri-Tip's Bleed (3 stacks: user) and a slow.
    /// It homes a little onto an enemy it nearly touches (Deadlock's tracking trigger).
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class ShivKnife : MonoBehaviour, IProjectileImpactBehavior
    {
        private bool _done;
        private Rigidbody? _rb;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void Start() => DeadlockSounds.Play("shiv_knife_whizby", transform.position, gameObject); // every client

        private void FixedUpdate()
        {
            // A light drop, like Deadlock's 0.2 gravity scale.
            if (_rb != null && !_rb.isKinematic) _rb.velocity += Physics.gravity * ShivData.KnifeGravity * Time.fixedDeltaTime;
            if (_rb != null && _rb.velocity.sqrMagnitude > 1f) transform.rotation = Quaternion.LookRotation(_rb.velocity);
        }

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !NetworkServer.active) return;
            _done = true;
            var pc = GetComponent<ProjectileController>();
            var pd = GetComponent<ProjectileDamage>();
            var body = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            HurtBox? box = info.collider != null ? info.collider.GetComponent<HurtBox>() : null;
            HealthComponent? hc = box != null ? box.healthComponent : null;
            bool onEnemy = body != null && hc != null && hc.body != null && box!.teamIndex != body.teamComponent.teamIndex;
            EquipmentFx.Sound(onEnemy ? "shiv_knife_hit" : "shiv_knife_miss", transform.position);
            if (onEnemy)
            {
                ShivNet.Hit(body!, hc!, ShivData.KnifeDamage * (pd != null ? pd.damage : body!.damage), pd != null && pd.crit, Vector3.zero, DamageSource.Secondary);
                if (hc!.alive)
                {
                    EquipmentFx.Sound("shiv_knife_bleed", ShivNet.Core(hc));
                    ShivNet.Bleed(body!, hc, ShivData.KnifeBleeds);
                    if (ShivSurvivor.KnifeSlow != null && hc.body != null)
                        using (DurationExtender.From(body)) hc.body.AddTimedBuff(ShivSurvivor.KnifeSlow, ShivData.KnifeSlowSeconds);
                }
            }
            Destroy(gameObject);
        }
    }
}
