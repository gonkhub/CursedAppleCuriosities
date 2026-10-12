using System;
using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Victor;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Server side of Victor: Pain Battery's charge (from damage taken, nothing else) and bolts, Jumpstart's cost and
    /// heal over time, Aura of Suffering's ticks, and Shocking Reanimation. States run on the owning player and message
    /// the server. Every hit goes through <see cref="Hit"/> so items see it like any other skill's hit.
    /// </summary>
    public static class VictorNet
    {
        /// <summary>True while Victor pays Jumpstart's cost: that can't count as a lethal hit for Reanimation.</summary>
        private static bool _paying;

        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<VictorBatteryMessage>();
            NetworkingAPI.RegisterMessageType<VictorJumpstartMessage>();
            NetworkingAPI.RegisterMessageType<VictorAuraMessage>();

            // Shocking Reanimation: while it's ready, every hit on Victor is non-lethal; a hit that leaves him at 1 health is
            // the one that would have killed him, and starts the revive instead.
            // Pain Battery fills only from damage Victor actually takes (user), his own Aura and Jumpstart included, and damage
            // to his barrier and shield counts too (user): measured as the drop in health + shield + barrier across the hit.
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                VictorRevive? revive = NetworkServer.active && !_paying && info != null ? self.GetComponent<VictorRevive>() : null;
                bool armed = revive != null && revive.Ready && self.alive;
                if (armed) info!.damageType.damageType |= DamageType.NonLethal;
                VictorBattery? battery = NetworkServer.active ? self.GetComponent<VictorBattery>() : null;
                float before = self.combinedHealth;
                orig(self, info);
                if (battery != null && before > self.combinedHealth) battery.Add(before - self.combinedHealth);
                if (armed && !info!.rejected && self.alive && self.combinedHealth <= 1f) revive!.Begin();
            };

            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (VictorSurvivor.JumpstartBuff != null && body.baseMoveSpeed > 0f)
                {
                    int steps = body.GetBuffCount(VictorSurvivor.JumpstartBuff);
                    if (steps > 0) args.moveSpeedMultAdd += VictorData.JumpstartSpeed * steps / VictorData.JumpstartSpeedSteps / body.baseMoveSpeed;
                }
                if (VictorSurvivor.ReviveSlow != null)
                {
                    int steps = body.GetBuffCount(VictorSurvivor.ReviveSlow);
                    if (steps > 0) args.moveSpeedReductionMultAdd += VictorData.ReviveSlowPerStep * steps;
                }
            };

            // Reanimation's cooldown belongs to the player, not the body: a new stage's body picks up what's left of it.
            CharacterBody.onBodyStartGlobal += body =>
            {
                if (!NetworkServer.active || body == null || body.master == null || body.GetComponent<VictorRevive>() == null) return;
                float left = VictorReviveClock.On(body.master).Remaining;
                if (left > 0f && VictorSurvivor.ReviveCooldownBuff != null) body.AddTimedBuff(VictorSurvivor.ReviveCooldownBuff, left);
            };

            if (VictorSurvivor.ReanimatingBuff != null) SkillSilence.Register(VictorSurvivor.ReanimatingBuff);
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Victor, with procs (on-hit, lifesteal, crits all see it as a normal hit).</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, float proc, DamageTypeCombo type, Vector3 force = default)
        {
            if (target == null || !target.alive || damage <= 0f) return;
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
            if (di.rejected) return;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
        }

        /// <summary>Server: Victor hurts himself (Jumpstart, Aura). Ignores armor and block items, procs nothing; can kill.</summary>
        public static void HurtSelf(CharacterBody body, float damage, bool payment)
        {
            if (body.healthComponent == null || !body.healthComponent.alive || damage <= 0f) return;
            var di = new DamageInfo
            {
                attacker = null,
                inflictor = body.gameObject,
                damage = damage,
                position = body.corePosition,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Bleed,
                damageType = new DamageTypeCombo(DamageType.BypassArmor | DamageType.BypassBlock | (payment ? DamageType.NonLethal : DamageType.Generic),
                    DamageTypeExtended.Generic, DamageSource.NoneSpecified),
            };
            _paying = payment;
            try { body.healthComponent.TakeDamage(di); }
            finally { _paying = false; }
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        public static List<HealthComponent> EnemiesIn(Vector3 center, float radius, TeamIndex team)
        {
            var list = new List<HealthComponent>();
            HurtBox[] boxes = new SphereSearch { origin = center, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(team)).FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
            foreach (HurtBox h in boxes) if (h.healthComponent != null && h.healthComponent.alive && !list.Contains(h.healthComponent)) list.Add(h.healthComponent);
            return list;
        }

        /// <summary>Server: the first enemy along one bolt (walls stop it), and where the bolt ends.</summary>
        public static HealthComponent? Bolt(Vector3 origin, Vector3 dir, float range, TeamIndex team, out Vector3 end)
        {
            end = origin + dir * range;
            RaycastHit[] hits = Physics.SphereCastAll(origin, VictorData.BatteryBoltRadius, dir, range,
                LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                var box = h.collider.GetComponent<HurtBox>();
                if (box != null)
                {
                    if (box.healthComponent == null || !box.healthComponent.alive || box.teamIndex == team) continue;
                    end = h.distance > 0f ? h.point : Core(box.healthComponent);
                    return box.healthComponent;
                }
                if (h.distance <= 0f) continue; // started inside geometry: look past it
                end = h.point;
                return null;
            }
            return null;
        }
    }

    // ================= Pain Battery =================

    /// <summary>
    /// The Pain Battery skill: ready only when the battery is full (the charge is a synced buff, so the owner's client sees
    /// it too). Its 2 s cooldown is Deadlock's, after it empties; the charge itself never refills over time.
    /// </summary>
    public class VictorBatterySkillDef : SkillDef
    {
        private static bool Full(GenericSkill slot) =>
            VictorSurvivor.BatteryBuff != null && slot.characterBody != null && slot.characterBody.GetBuffCount(VictorSurvivor.BatteryBuff) >= VictorData.BatteryFull;

        public override bool CanExecute(GenericSkill skillSlot) => Full(skillSlot) && base.CanExecute(skillSlot);
        public override bool IsReady(GenericSkill skillSlot) => base.IsReady(skillSlot) && Full(skillSlot);
    }

    /// <summary>Server, on Victor: damage taken (health, shield or barrier) charges the battery (shown as 0..100 stacks of its buff).</summary>
    public class VictorBattery : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _stored;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public bool IsFull => VictorSurvivor.BatteryBuff != null && _body.GetBuffCount(VictorSurvivor.BatteryBuff) >= VictorData.BatteryFull;

        public void Add(float damage)
        {
            if (!NetworkServer.active || VictorSurvivor.BatteryBuff == null || _body.healthComponent == null) return;
            float full = Mathf.Max(1f, _body.healthComponent.fullHealth * VictorData.BatteryFillFraction);
            _stored = Mathf.Min(full, _stored + damage);
            int stacks = Mathf.Clamp(Mathf.FloorToInt(_stored / full * VictorData.BatteryFull), 0, VictorData.BatteryFull);
            if (stacks != _body.GetBuffCount(VictorSurvivor.BatteryBuff)) _body.SetBuffCount(VictorSurvivor.BatteryBuff.buffIndex, stacks);
        }

        public void Empty()
        {
            _stored = 0f;
            if (VictorSurvivor.BatteryBuff != null) _body.SetBuffCount(VictorSurvivor.BatteryBuff.buffIndex, 0);
        }
    }

    /// <summary>Pain Battery fires: 7 bolts in a fan, each enemy hit once, each one healing Victor (Deadlock's T3).</summary>
    public class VictorBatteryMessage : INetMessage
    {
        public GameObject? Body;
        public Vector3 Origin, Direction;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Origin); w.Write(Direction); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Origin = r.ReadVector3(); Direction = r.ReadVector3(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null) return;
            var body = Body.GetComponent<CharacterBody>();
            var battery = Body.GetComponent<VictorBattery>();
            if (body == null || battery == null || body.teamComponent == null || !battery.IsFull) return;
            battery.Empty();

            TeamIndex team = body.teamComponent.teamIndex;
            Vector3 dir = Direction.normalized;
            Vector3 axis = Vector3.Cross(dir, Vector3.Cross(Vector3.up, dir)).normalized; // the fan spreads across the screen
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.up;
            float range = VictorData.BatteryRange * MysticExpansion.RadiusMultiplier(body);
            var struck = new List<HealthComponent>();
            for (int i = 0; i < VictorData.BatteryBolts; i++)
            {
                float t = VictorData.BatteryBolts == 1 ? 0.5f : i / (float)(VictorData.BatteryBolts - 1);
                Vector3 boltDir = Quaternion.AngleAxis(Mathf.Lerp(-0.5f, 0.5f, t) * VictorData.BatterySpreadDeg, axis) * dir;
                HealthComponent? hc = VictorNet.Bolt(Origin, boltDir, range, team, out Vector3 end);
                if (VictorSurvivor.BoltTracer != null) EffectManager.SpawnEffect(VictorSurvivor.BoltTracer, new EffectData { origin = end, start = Origin }, true);
                if (hc != null && !struck.Contains(hc)) struck.Add(hc);
            }
            foreach (HealthComponent hc in struck)
            {
                if (VictorSurvivor.ZapEffect != null) EffectManager.SpawnEffect(VictorSurvivor.ZapEffect, new EffectData { origin = VictorNet.Core(hc), scale = 1.5f }, true);
                EquipmentFx.Sound("victor_battery_hit", VictorNet.Core(hc));
                VictorNet.Hit(body, hc, VictorData.BatteryDamage * body.damage, Crit, 1f, DamageSource.Secondary);
                // Deadlock's T3 (user: built in): each enemy hit heals a share of his missing health.
                HealthComponent own = body.healthComponent;
                if (own != null && own.alive) own.Heal((own.fullHealth - own.health) * VictorData.BatteryHealMissing, default);
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Victor] Pain Battery: {struck.Count} enemies hit");
        }
    }

    // ================= Jumpstart =================

    public class VictorJumpstartMessage : INetMessage
    {
        public GameObject? Body;

        public void Serialize(NetworkWriter w) => w.Write(Body);
        public void Deserialize(NetworkReader r) => Body = r.ReadGameObject();

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null) return;
            var body = Body.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;
            float cost = body.healthComponent.health * VictorData.JumpstartCost;
            VictorNet.HurtSelf(body, cost, payment: true);
            // User: the healing always totals more than he spent.
            float heal = Mathf.Max(cost * VictorData.JumpstartHealRatio, body.healthComponent.fullHealth * VictorData.JumpstartMinHeal);
            float seconds = VictorData.JumpstartSeconds * DurationExtender.Multiplier(body);
            (Body.GetComponent<VictorJumpstart>() ?? Body.AddComponent<VictorJumpstart>()).Begin(heal, seconds);
            if (VictorSurvivor.JumpstartBuff != null)
            {
                // The speed fades in steps: each stack ends a little later than the last.
                body.ClearTimedBuffs(VictorSurvivor.JumpstartBuff);
                for (int i = 1; i <= VictorData.JumpstartSpeedSteps; i++)
                    body.AddTimedBuff(VictorSurvivor.JumpstartBuff, seconds * i / VictorData.JumpstartSpeedSteps);
            }
        }
    }

    /// <summary>Server, on Victor: Jumpstart's heal over time, fastest at the start and fading out (Deadlock: decaying regen).</summary>
    public class VictorJumpstart : MonoBehaviour
    {
        private const float Tick = 0.25f;
        private float _total, _seconds, _age, _next;
        private bool _active;

        public void Begin(float total, float seconds)
        {
            _total = total; _seconds = Mathf.Max(0.5f, seconds); _age = 0f; _next = 0f; _active = true;
        }

        private void FixedUpdate()
        {
            if (!_active || !NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (_age < _next && _age < _seconds) return;
            float from = _next - Tick, to = Mathf.Min(_age, _seconds);
            if (from < 0f) from = 0f;
            _next = to + Tick;
            // Rate falls linearly to zero: the share healed between two times is the area under that line.
            float Share(float t) { float x = Mathf.Clamp01(t / _seconds); return 2f * x - x * x; }
            float amount = _total * (Share(to) - Share(from));
            var hc = GetComponent<HealthComponent>();
            if (hc != null && hc.alive && amount > 0f) hc.Heal(amount, default);
            if (_age >= _seconds)
            {
                _active = false;
                EquipmentFx.Sound("victor_jumpstart_end", transform.position);
            }
        }
    }

    // ================= Aura of Suffering =================

    public class VictorAuraMessage : INetMessage
    {
        public GameObject? Body;
        public bool On;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(On); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); On = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null) return;
            var aura = Body.GetComponent<VictorAura>();
            if (aura == null) return;
            if (On) aura.Begin(); else aura.Stop();
        }
    }

    /// <summary>
    /// Server, on Victor: Aura of Suffering. Every 0.25 s it hits each enemy within range (Special damage with a proc
    /// coefficient, rolled crits, on-hit and on-kill like any skill hit) and Victor himself, both ramping up over its
    /// first 8 s, then holding at max until it is toggled off or he dies. Mystic Expansion widens it.
    /// </summary>
    public class VictorAura : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private bool _active;
        private float _age, _tick;

        public bool Active => _active;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin()
        {
            if (_active || _body.healthComponent == null || !_body.healthComponent.alive) return;
            _active = true;
            _age = 0f;
            _tick = 0f;
            if (VictorSurvivor.AuraBuff != null && !_body.HasBuff(VictorSurvivor.AuraBuff)) _body.AddBuff(VictorSurvivor.AuraBuff);
        }

        public void Stop()
        {
            if (!_active) return;
            _active = false;
            if (VictorSurvivor.AuraBuff != null && _body.HasBuff(VictorSurvivor.AuraBuff)) _body.RemoveBuff(VictorSurvivor.AuraBuff);
            EquipmentFx.Sound("victor_aura_end", _body.corePosition);
        }

        private void FixedUpdate()
        {
            if (!_active || !NetworkServer.active) return;
            if (_body.healthComponent == null || !_body.healthComponent.alive ) { Stop(); return; }
            _age += Time.fixedDeltaTime;
            _tick -= Time.fixedDeltaTime;
            if (_tick > 0f) return;
            _tick += VictorData.AuraTick;

            float ramp = Mathf.Clamp01(_age / Mathf.Max(0.1f, VictorData.AuraRampSeconds)); // up to max over 8 s, then holds
            float coeff = Mathf.Lerp(VictorData.AuraMinDamage, VictorData.AuraMaxDamage, ramp) * VictorData.AuraTick;
            float radius = VictorData.AuraRadius * MysticExpansion.RadiusMultiplier(_body);
            foreach (HealthComponent hc in VictorNet.EnemiesIn(_body.corePosition, radius, _body.teamComponent.teamIndex))
                VictorNet.Hit(_body, hc, coeff * _body.damage, _body.RollCrit(), VictorData.AuraProc, DamageSource.Special);
            // It hurts him too, and can kill him (user): Reanimation catches that when it's ready.
            float self = Mathf.Lerp(VictorData.AuraSelfMin, VictorData.AuraSelfMax, ramp) * VictorData.AuraTick * _body.healthComponent.fullHealth;
            VictorNet.HurtSelf(_body, self, payment: false);
        }
    }

    /// <summary>
    /// Every client, on Victor: the aura's reach while it runs, drawn like Abrams' Siphon Life (user): a grey ring on the
    /// ground around him, hugging the terrain, sized to the real radius incl. Mystic Expansion.
    /// </summary>
    public class VictorAuraFx : MonoBehaviour
    {
        private const int Points = 64;
        private static readonly Color Grey = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        private CharacterBody _body = null!;
        private LineRenderer? _ring;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            bool on = VictorSurvivor.AuraBuff != null && _body.HasBuff(VictorSurvivor.AuraBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (!on) { Clear(); return; }
            if (_ring == null) _ring = Line();
            float radius = VictorData.AuraRadius * MysticExpansion.RadiusMultiplier(_body);
            Vector3 c = _body.footPosition;
            for (int i = 0; i < Points; i++)
            {
                float a = i * Mathf.PI * 2f / Points;
                Vector3 pt = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (Physics.Raycast(pt + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) pt.y = hit.point.y;
                _ring.SetPosition(i, pt + Vector3.up * 0.15f);
            }
        }

        private static LineRenderer Line()
        {
            var go = new GameObject("CursedAppleVictorAuraRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = VictorSurvivor.IndicatorMaterial;
            if (lr.material != null && lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", Grey);
            lr.startColor = lr.endColor = Grey;
            lr.startWidth = lr.endWidth = 0.08f;
            lr.positionCount = Points;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Clear()
        {
            if (_ring != null) Destroy(_ring.gameObject);
            _ring = null;
        }

        private void OnDisable() => Clear();
    }

    // ================= Shocking Reanimation =================

    /// <summary>Server, on Victor's master: when Reanimation is ready again (it outlives the body: user, 240 s).</summary>
    public class VictorReviveClock : MonoBehaviour
    {
        public float ReadyAt;

        public float Remaining => Run.instance != null ? Mathf.Max(0f, ReadyAt - Run.instance.fixedTime) : 0f;

        public static VictorReviveClock On(CharacterMaster master) =>
            master.GetComponent<VictorReviveClock>() ?? master.gameObject.AddComponent<VictorReviveClock>();
    }

    /// <summary>
    /// On Victor. Server: the revive itself (the slowing wave as he goes down, 3 s untouchable at 1 health, then up at
    /// half health with a stunning blast). Every client: puts the owner's body into <see cref="Reanimate"/> while down.
    /// </summary>
    public class VictorRevive : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private EntityStateMachine? _bodyMachine;
        private VictorReassembly? _parts;
        private bool _down, _shownDown;
        private float _age;

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
            _bodyMachine = EntityStateMachine.FindByCustomName(gameObject, "Body");
        }

        public bool Ready =>
            !_down && _body.master != null && VictorReviveClock.On(_body.master).Remaining <= 0f;

        public void Begin()
        {
            if (_down) return;
            _down = true;
            _age = 0f;
            _body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, VictorData.ReviveChannel + VictorData.RevivePost);
            if (VictorSurvivor.ReanimatingBuff != null) _body.AddBuff(VictorSurvivor.ReanimatingBuff);
            GetComponent<VictorAura>()?.Stop();
            Vector3 at = _body.corePosition;
            float radius = VictorData.ReviveRadius * MysticExpansion.RadiusMultiplier(_body);
            EquipmentFx.Sound("victor_revive_start", at);
            if (VictorSurvivor.WaveEffect != null) EffectManager.SpawnEffect(VictorSurvivor.WaveEffect, new EffectData { origin = at, scale = radius }, true);
            // The wave as he falls: a heavy slow that fades over 3 s (Deadlock: 120%, diminishing).
            if (VictorSurvivor.ReviveSlow != null)
                using (DurationExtender.From(_body))
                    foreach (HealthComponent hc in VictorNet.EnemiesIn(at, radius, _body.teamComponent.teamIndex))
                        if (hc.body != null)
                            for (int i = 1; i <= VictorData.ReviveSlowSteps; i++)
                                hc.body.AddTimedBuff(VictorSurvivor.ReviveSlow, VictorData.ReviveSlowSeconds * i / VictorData.ReviveSlowSteps);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo("[Victor] Shocking Reanimation: down");
        }

        private void FixedUpdate()
        {
            if (!_down || !NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (_age < VictorData.ReviveChannel) return;
            _down = false;
            HealthComponent hc = _body.healthComponent;
            if (hc != null && hc.alive) hc.Networkhealth = Mathf.Max(hc.health, hc.fullHealth * VictorData.ReviveHealth);
            if (VictorSurvivor.ReanimatingBuff != null && _body.HasBuff(VictorSurvivor.ReanimatingBuff)) _body.RemoveBuff(VictorSurvivor.ReanimatingBuff);

            Vector3 at = _body.corePosition;
            float radius = VictorData.ReviveRadius * MysticExpansion.RadiusMultiplier(_body);
            EquipmentFx.Sound("victor_revive_blast", at);
            if (VictorSurvivor.BlastEffect != null) EffectManager.SpawnEffect(VictorSurvivor.BlastEffect, new EffectData { origin = at, scale = radius }, true);
            bool crit = _body.RollCrit();
            List<HealthComponent> caught = VictorNet.EnemiesIn(at, radius, _body.teamComponent.teamIndex);
            foreach (HealthComponent e in caught) VictorNet.Hit(_body, e, VictorData.ReviveDamage * _body.damage, crit, 1f, DamageSource.Special);
            using (DurationExtender.From(_body))
                foreach (HealthComponent e in caught) if (e.alive) SetStateOnHurt.SetStunOnObject(e.gameObject, VictorData.ReviveStun);

            if (_body.master != null) VictorReviveClock.On(_body.master).ReadyAt = Run.instance.fixedTime + VictorData.ReviveCooldown;
            if (VictorSurvivor.ReviveCooldownBuff != null) _body.AddTimedBuff(VictorSurvivor.ReviveCooldownBuff, VictorData.ReviveCooldown);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Victor] Shocking Reanimation: up, {caught.Count} enemies in the blast");
        }

        private void Update()
        {
            // Every client: while he's down his body parts float back together (Deadlock's reanimation model).
            bool downNow = VictorSurvivor.ReanimatingBuff != null && _body.HasBuff(VictorSurvivor.ReanimatingBuff);
            if (downNow && !_shownDown)
            {
                ModelLocator? ml = _body.modelLocator;
                if (ml != null && ml.modelTransform != null) _parts = VictorReassembly.Spawn(ml.modelTransform.gameObject, VictorData.ReviveChannel);
            }
            else if (!downNow && _shownDown && _parts != null) { _parts.Finish(); _parts = null; }
            _shownDown = downNow;

            // The owner's client drives the body's state machine: lie down while the server says he's down.
            if (_bodyMachine == null || VictorSurvivor.ReanimatingBuff == null || !Util.HasEffectiveAuthority(gameObject)) return;
            if (_body.HasBuff(VictorSurvivor.ReanimatingBuff) && !(_bodyMachine.state is Reanimate))
                _bodyMachine.SetInterruptState(new Reanimate(), InterruptPriority.Death);
        }
    }

    // ================= Gun =================

    /// <summary>
    /// Every client, on Victor: Memento Mori's spin-up and layered sound (Deadlock: windup + first shot, then main + mech
    /// shots while held; no tail on release, user).
    /// </summary>
    public class VictorGun : MonoBehaviour
    {
        private float _spin, _last = -10f;
        private bool _firing;

        /// <summary>Seconds until the next shot (before attack speed), spinning the gun up a little.</summary>
        public static float Shot(GameObject body)
        {
            var g = body.GetComponent<VictorGun>();
            bool first = g == null || !g._firing;
            if (first) DeadlockSounds.Play("victor_gun_windup", body.transform.position, body);
            DeadlockSounds.Play(first ? "victor_gun_first" : "victor_gun_main", body.transform.position, body);
            DeadlockSounds.Play("victor_gun_mech", body.transform.position, body);
            if (g == null) return VictorData.GunCycleSlow;
            float cycle = Mathf.Lerp(VictorData.GunCycleSlow, VictorData.GunCycleFast, g._spin);
            g._spin = Mathf.Min(1f, g._spin + VictorData.GunSpinUp * cycle);
            g._firing = true;
            g._last = Time.time;
            return cycle;
        }

        private void Update()
        {
            if (Time.time - _last > VictorData.GunCycleSlow * 1.5f) _spin = Mathf.Max(0f, _spin - VictorData.GunSpinDecay * Time.deltaTime);
            if (_firing && Time.time - _last >= VictorData.GunCycleSlow * 2.5f) _firing = false; // the next shot is a "first" again (no tail sound: user)
        }
    }
}
