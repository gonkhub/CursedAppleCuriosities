using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Server side of Mina: Rake's hits and kill heals, Sanguine Retreat's invulnerability, Nox Nostra's bats, and Love Bites
    /// (her damage builds a mark on each enemy; a full mark bursts and gives her a stack for Nox Nostra).
    /// </summary>
    public static class MinaNet
    {
        /// <summary>True while a Love Bites burst is being dealt: it doesn't build up another.</summary>
        [System.ThreadStatic] internal static bool Bursting;

        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<MinaRakeMessage>();
            NetworkingAPI.RegisterMessageType<MinaRetreatMessage>();
            NetworkingAPI.RegisterMessageType<MinaHoverMessage>();
            GlobalEventManager.onServerDamageDealt += MinaLoveBites.OnDamage;
            Stage.onServerStageBegin += _ => MinaLoveStacks.ResetAll();
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;

        /// <summary>Server: one hit from Mina, with procs.</summary>
        public static DamageInfo? Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f, GameObject? inflictor = null)
        {
            if (target == null || !target.alive || damage <= 0f) return null;
            var di = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = inflictor != null ? inflictor : attacker.gameObject,
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

        public static bool IsBoss(CharacterBody? b) => b != null && (b.isChampion || b.isBoss);
    }

    /// <summary>Owner to server: Rake caught these enemies.</summary>
    public class MinaRakeMessage : INetMessage
    {
        public GameObject? Attacker;
        public GameObject[] Targets = new GameObject[0];
        public bool Crit;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Attacker); w.Write(Crit);
            w.Write((byte)Mathf.Min(255, Targets.Length));
            for (int i = 0; i < Targets.Length && i < 255; i++) w.Write(Targets[i]);
        }

        public void Deserialize(NetworkReader r)
        {
            Attacker = r.ReadGameObject(); Crit = r.ReadBoolean();
            Targets = new GameObject[r.ReadByte()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        /// <summary>
        /// Rake (Deadlock ability_vampirebat_steallife): its damage plus a share of the target's missing health (capped, for
        /// bosses' long health bars); every kill heals her (Deadlock RakeHealPerKill).
        /// </summary>
        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            int kills = 0;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                float missing = Mathf.Max(0f, hc.fullCombinedHealth - hc.combinedHealth) * MinaData.RakeMissingHealth;
                float damage = MinaData.RakeDamage * body.damage + Mathf.Min(missing, MinaData.RakeMissingCap * body.damage);
                MinaNet.Hit(body, hc, damage, Crit, (hc.transform.position - body.transform.position).normalized * 600f, DamageSource.Secondary);
                EquipmentFx.Sound("mina_rake_hit", MinaNet.Core(hc));
                if (MinaSurvivor.SlashEffect != null)
                    EffectManager.SpawnEffect(MinaSurvivor.SlashEffect, new EffectData { origin = MinaNet.Core(hc), scale = 1.5f }, true);
                if (!hc.alive) kills++;
            }
            if (kills > 0 && body.healthComponent != null)
            {
                body.healthComponent.Heal(body.healthComponent.fullHealth * MinaData.RakeKillHeal * kills, default(ProcChainMask));
                EquipmentFx.Sound("mina_rake_heal", body.corePosition, body.gameObject);
            }
        }
    }

    /// <summary>Owner to server: she opened or closed her umbrella (the hover buff shows it to everyone).</summary>
    public class MinaHoverMessage : INetMessage
    {
        public GameObject? Body;
        public bool On;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(On); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); On = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null || MinaSurvivor.HoverBuff == null || !Body.TryGetComponent(out CharacterBody b)) return;
            if (On && !b.HasBuff(MinaSurvivor.HoverBuff)) b.AddBuff(MinaSurvivor.HoverBuff);
            else if (!On) while (b.HasBuff(MinaSurvivor.HoverBuff)) b.RemoveBuff(MinaSurvivor.HoverBuff);
        }
    }

    /// <summary>Owner to server: Sanguine Retreat started (she can't be hit or seen as bats) or ended.</summary>
    public class MinaRetreatMessage : INetMessage
    {
        public GameObject? Body;
        public float Seconds;

        public void Serialize(NetworkWriter w) { w.Write(Body); w.Write(Seconds); }
        public void Deserialize(NetworkReader r) { Body = r.ReadGameObject(); Seconds = r.ReadSingle(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null || !Body.TryGetComponent(out CharacterBody b)) return;
            // Untargetable and invulnerable while she's the bats (user): Grey Talon's owl channel uses the same pair.
            b.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, Seconds);
            b.AddTimedBuff(RoR2Content.Buffs.Cloak, Seconds);
        }
    }

    // ================= Passive: Love Bites =================

    /// <summary>
    /// Love Bites (Deadlock ability_vampirebat_lovebites), the user's version: Mina's damage builds a mark on an enemy (Deadlock's
    /// per-shot, per-bat and per-dagger amounts); at 100 it bursts for 400% and she gains a Love Bites stack for Nox Nostra.
    /// Then that enemy can't be Love Bitten again for 15 s (user). An idle mark fades (Deadlock's 3 s decay delay).
    /// Server state on the enemy; the mark shows as a stacking debuff (0-100), the wait as a timed debuff.
    /// </summary>
    public class MinaLoveBites : MonoBehaviour
    {
        private class Mark { public float BuildUp, LastHit; }
        private readonly Dictionary<CharacterBody, Mark> _marks = new Dictionary<CharacterBody, Mark>();
        private readonly Dictionary<CharacterBody, float> _readyAt = new Dictionary<CharacterBody, float>();
        private CharacterBody _body = null!;

        private void Awake() => _body = GetComponent<CharacterBody>();

        internal static void OnDamage(DamageReport report)
        {
            CharacterBody? mina = report.attackerBody;
            CharacterBody? victim = report.victimBody;
            if (MinaNet.Bursting || mina == null || victim == null || victim == mina || report.damageDealt <= 0f) return;
            if (BodyCatalog.GetBodyName(mina.bodyIndex) != MinaSurvivor.BodyName) return;
            if (victim.healthComponent == null || !victim.healthComponent.alive) return;
            DamageInfo info = report.damageInfo;
            float add;
            if (info.inflictor != null && info.inflictor.GetComponent<MinaBat>() != null) add = MinaData.BuildUpPerBat;
            else if (info.damageType.damageSource == DamageSource.Secondary) add = MinaData.BuildUpPerRake;
            else add = MinaData.BuildUpPerShot * Mathf.Clamp01(info.procCoefficient);
            if (add <= 0f) return;
            var lb = victim.GetComponent<MinaLoveBites>() ?? victim.gameObject.AddComponent<MinaLoveBites>();
            lb.Build(mina, add, info.crit);
        }

        private void Build(CharacterBody mina, float add, bool crit)
        {
            if (_readyAt.TryGetValue(mina, out float ready) && Time.time < ready) return; // still on Love Bites' per-target wait
            if (!_marks.TryGetValue(mina, out Mark m)) _marks[mina] = m = new Mark();
            float gained = Mathf.Min(add, MinaData.BuildUpMax - m.BuildUp);
            m.BuildUp += add;
            BonusHit(mina, gained, crit);
            if (_body.healthComponent == null || !_body.healthComponent.alive) { _marks.Remove(mina); return; } // the bonus finished it
            m.LastHit = Time.time;
            if (m.BuildUp >= MinaData.BuildUpMax)
            {
                _marks.Remove(mina);
                _readyAt[mina] = Time.time + MinaData.LoveBitesTargetCooldown;
                if (MinaSurvivor.LoveWait != null) _body.AddTimedBuff(MinaSurvivor.LoveWait, MinaData.LoveBitesTargetCooldown);
                Show();
                Burst(mina);
                return;
            }
            EquipmentFx.Sound("mina_lovebites_buildup", _body.corePosition, _body.gameObject);
            Show();
        }

        /// <summary>Server: the meter went up by gained: a small hit worth that share of a whole meter (user 3.0).</summary>
        private void BonusHit(CharacterBody mina, float gained, bool crit)
        {
            if (gained <= 0f || _body.healthComponent == null) return;
            MinaNet.Bursting = true; // its damage builds no meter of its own
            try { MinaNet.Hit(mina, _body.healthComponent, MinaData.BuildUpBonusPerMeter * gained / MinaData.BuildUpMax * mina.damage, crit, Vector3.zero, DamageSource.NoneSpecified, 0f); }
            finally { MinaNet.Bursting = false; }
        }

        private void Burst(CharacterBody mina)
        {
            HealthComponent hc = _body.healthComponent;
            EquipmentFx.Sound("mina_lovebites_proc", _body.corePosition);
            if (MinaSurvivor.LoveBitesEffect != null)
                EffectManager.SpawnEffect(MinaSurvivor.LoveBitesEffect, new EffectData { origin = _body.corePosition, scale = Mathf.Clamp(_body.bestFitRadius, 1f, 4f) }, true);
            MinaNet.Bursting = true;
            try { MinaNet.Hit(mina, hc, MinaData.LoveBitesDamage * mina.damage, mina.RollCrit(), Vector3.zero, DamageSource.NoneSpecified, 0.5f); }
            finally { MinaNet.Bursting = false; }
            if (mina.master != null)
            {
                var stacks = mina.master.GetComponent<MinaLoveStacks>() ?? mina.master.gameObject.AddComponent<MinaLoveStacks>();
                stacks.Gain();
            }
            EquipmentFx.Sound("mina_lovebites_gained", mina.corePosition, mina.gameObject);
            DeadlockHeroVoice.Power("mina_vo_power3", mina.gameObject);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _marks.Count == 0) return;
            bool changed = false;
            foreach (var kv in new List<KeyValuePair<CharacterBody, Mark>>(_marks))
            {
                if (kv.Key == null) { _marks.Remove(kv.Key!); changed = true; continue; }
                if (Time.time - kv.Value.LastHit < MinaData.BuildUpDecayDelay) continue;
                kv.Value.BuildUp -= MinaData.BuildUpMax / MinaData.BuildUpDecaySeconds * Time.fixedDeltaTime;
                if (kv.Value.BuildUp <= 0f) _marks.Remove(kv.Key);
                changed = true;
            }
            if (changed) Show();
        }

        /// <summary>The fullest mark on this enemy, as the Love Bites debuff's stacks.</summary>
        private void Show()
        {
            if (MinaSurvivor.LoveMark == null) return;
            float most = 0f;
            foreach (Mark m in _marks.Values) most = Mathf.Max(most, m.BuildUp);
            int want = Mathf.Clamp(Mathf.CeilToInt(most), 0, 99);
            if (_body.GetBuffCount(MinaSurvivor.LoveMark) != want) _body.SetBuffCount(MinaSurvivor.LoveMark.buffIndex, want);
        }
    }

    /// <summary>
    /// Server, on Mina's master: her Love Bites stacks this stage (user: they reset every stage), each one an extra Nox Nostra
    /// bat. Shown on her body as the Love Bites buff's count; a respawn on the same stage keeps them.
    /// </summary>
    public class MinaLoveStacks : MonoBehaviour
    {
        private static readonly List<MinaLoveStacks> All = new List<MinaLoveStacks>();
        public int Count;
        private CharacterMaster _master = null!;

        private void Awake() { _master = GetComponent<CharacterMaster>(); All.Add(this); }
        private void OnDestroy() => All.Remove(this);

        public static int Of(CharacterBody? body) =>
            body != null && body.master != null && body.master.TryGetComponent(out MinaLoveStacks s) ? s.Count : 0;

        public void Gain() { Count++; Show(); }

        public static void ResetAll()
        {
            foreach (MinaLoveStacks s in All) { s.Count = 0; s.Show(); }
        }

        private float _next;

        private void FixedUpdate()
        {
            // A new body (respawn, stage change) needs the buff count again.
            if (!NetworkServer.active || Time.time < _next) return;
            _next = Time.time + 0.5f;
            Show();
        }

        private void Show()
        {
            CharacterBody? b = _master != null ? _master.GetBody() : null;
            if (b == null || MinaSurvivor.LoveStacksBuff == null) return;
            if (b.GetBuffCount(MinaSurvivor.LoveStacksBuff) != Count) b.SetBuffCount(MinaSurvivor.LoveStacksBuff.buffIndex, Count);
        }
    }

    // ================= Special: Nox Nostra's bats =================

    /// <summary>
    /// Server, on a Nox Nostra bat: it flies off on its own arc toward her crosshair (followed as she aims), fluttering; an enemy that
    /// comes within a few metres catches it and it turns onto that enemy at a moderate rate (user 2.5: tracking was far too eager).
    /// A bat that touches an enemy deals 1% of its max health (bosses half: user).
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class MinaBat : MonoBehaviour, IProjectileImpactBehavior
    {
        private Rigidbody? _rb;
        private HurtBox? _target;
        private float _age, _phase, _freq, _nextSeek, _life = -1f, _seek;
        private Vector3? _goal;
        private CharacterBody? _owner;

        /// <summary>Where her crosshair meets the world now (her aim is synced to the server).</summary>
        private Vector3? Crosshair()
        {
            if (_owner == null || _owner.inputBank == null) return null;
            Ray aim = _owner.inputBank.GetAimRay();
            return Physics.Raycast(aim, out RaycastHit hit, 150f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : aim.GetPoint(150f);
        }
        private bool _done;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _phase = Random.value * 6.28f;
            _freq = Random.Range(3f, 8f); // Deadlock m_flSineFrequencyMin/Max-ish: each bat flutters its own way
        }

        private void Start() => DeadlockSounds.Play("mina_bat_whizby", transform.position, gameObject);

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _rb == null) return;
            float dt = Time.fixedDeltaTime;
            if (_life < 0f)
            {
                // Her items reach her bats (user 2.5): Duration Extender keeps them flying longer, Mystic Expansion lets them find
                // enemies farther off.
                var owner = GetComponent<ProjectileController>().owner;
                CharacterBody? by = owner != null ? owner.GetComponent<CharacterBody>() : null;
                _owner = by;
                _goal = Crosshair();
                _life = MinaData.BatLifetime * DurationExtender.Multiplier(by);
                _seek = MinaData.BatSeekRange * MysticExpansion.RadiusMultiplier(by);
                var simple = GetComponent<ProjectileSimple>();
                if (simple != null) simple.lifetime = _life + 0.5f;
            }
            _age += dt;
            if (_age > _life) { Destroy(gameObject); return; }
            if ((_target == null || _target.healthComponent == null || !_target.healthComponent.alive) && Time.time >= _nextSeek)
            {
                _nextSeek = Time.time + 0.15f;
                _target = Seek();
                _goal = Crosshair() ?? _goal;
            }
            Vector3 fwd = transform.forward;
            if (_target != null)
            {
                Vector3 to = (_target.transform.position - transform.position).normalized;
                fwd = Vector3.RotateTowards(fwd, to, MinaData.BatTurnDegPerSec * Mathf.Deg2Rad * dt, 0f);
            }
            else if (_goal.HasValue && _age > 0.1f)
            {
                Vector3 to = _goal.Value - transform.position;
                if (to.sqrMagnitude > 1f) fwd = Vector3.RotateTowards(fwd, to.normalized, MinaData.BatCursorTurn * Mathf.Deg2Rad * dt, 0f);
            }
            // A flutter around its line (Deadlock's sine-amplitude curl), stronger before it locks on.
            Vector3 side = Vector3.Cross(fwd, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            float wobble = Mathf.Sin(_age * _freq * 6.28f + _phase) * (_target != null ? 0.6f : 1.4f);
            fwd = Quaternion.AngleAxis(wobble * 90f * dt, Vector3.up) * Quaternion.AngleAxis(wobble * 40f * dt, side.normalized) * fwd;
            transform.rotation = Quaternion.LookRotation(fwd);
            _rb.velocity = fwd * MinaData.BatSpeed;
        }

        private HurtBox? Seek()
        {
            var pc = GetComponent<ProjectileController>();
            TeamIndex team = pc.teamFilter != null ? pc.teamFilter.teamIndex : TeamIndex.Player;
            var search = new BullseyeSearch
            {
                searchOrigin = transform.position,
                searchDirection = transform.forward,
                maxDistanceFilter = _seek,
                maxAngleFilter = MinaData.BatSeekConeDeg,
                teamMaskFilter = TeamMask.GetEnemyTeams(team),
                sortMode = BullseyeSearch.SortMode.DistanceAndAngle,
                filterByLoS = true,
            };
            search.RefreshCandidates();
            foreach (HurtBox h in search.GetResults()) return h;
            return null;
        }

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !NetworkServer.active) return;
            var pc = GetComponent<ProjectileController>();
            var body = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            HurtBox? box = info.collider != null ? info.collider.GetComponent<HurtBox>() : null;
            HealthComponent? hc = box != null ? box.healthComponent : null;
            if (body == null || hc == null || !hc.alive || box!.teamIndex == body.teamComponent.teamIndex)
            {
                if (box == null) { _done = true; Destroy(gameObject); } // terrain
                return;
            }
            _done = true;
            float share = MinaData.BatMaxHealth * (MinaNet.IsBoss(hc.body) ? MinaData.BatBossScale : 1f);
            MinaNet.Hit(body, hc, hc.fullCombinedHealth * share, false /* exactly the share: no crits */, transform.forward * 100f, DamageSource.Special, MinaData.BatProc, gameObject);
            EquipmentFx.Sound("mina_bat_hit", transform.position);
            Destroy(gameObject);
        }
    }
}
