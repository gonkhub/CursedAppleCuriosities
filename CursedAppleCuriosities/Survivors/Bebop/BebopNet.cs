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

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// Server side of Bebop: his beam's bonuses (hooked enemies, after an uppercut), Sticky Bomb and its permanent stacks, the hook's
    /// pull, Exploding Uppercut's tosses and landings, and Hyper Beam's hits. States run on the owner and message the server.
    /// </summary>
    public static class BebopNet
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<BebopStickyMessage>();
            NetworkingAPI.RegisterMessageType<BebopStickyFxMessage>();
            NetworkingAPI.RegisterMessageType<BebopHookMessage>();
            NetworkingAPI.RegisterMessageType<BebopUppercutMessage>();
            NetworkingAPI.RegisterMessageType<BebopHyperMessage>();
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (NetworkServer.active && info != null && info.attacker != null && info.damageType.damageSource == DamageSource.Primary && self.body != null)
                {
                    var by = info.attacker.GetComponent<CharacterBody>();
                    if (IsBebop(by))
                    {
                        if (BebopSurvivor.HookAmpBuff != null && self.body.HasBuff(BebopSurvivor.HookAmpBuff)) info.damage *= 1f + BebopData.HookAmp;
                        if (BebopSurvivor.UppercutBuff != null && by!.HasBuff(BebopSurvivor.UppercutBuff)) info.damage *= 1f + BebopData.UppercutBuffDamage;
                    }
                }
                orig(self, info);
            };
            // Everything his Sticky Bombs earned is lost when he dies, whatever brings him back (user).
            GlobalEventManager.onCharacterDeathGlobal += report =>
            {
                if (NetworkServer.active && IsBebop(report.victimBody) && report.victimMaster != null && report.victimMaster.TryGetComponent(out BebopStickyStacks s)) s.Clear();
            };
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (BebopSurvivor.HyperSlow != null && body.HasBuff(BebopSurvivor.HyperSlow)) args.moveSpeedReductionMultAdd += BebopData.HyperSlow;
                if (BebopSurvivor.StickySpeedBuff != null)
                    args.baseMoveSpeedAdd += BebopData.StickySpeed * body.GetBuffCount(BebopSurvivor.StickySpeedBuff) / (float)BebopStickySpeed.Steps; // fades out (user 2.5)
            };
            // His beam barely procs (proc 0.05), but on a hooked enemy every proc is guaranteed while the hook's mark lasts (user 2.5):
            // that hit counts as proc 1 and every on-hit roll during it succeeds.
            On.RoR2.GlobalEventManager.OnHitEnemy += (orig, self, info, victim) =>
            {
                bool force = info != null && info.attacker != null && victim != null && info.damageType.damageSource == DamageSource.Primary
                    && BebopSurvivor.HookAmpBuff != null && victim.TryGetComponent(out CharacterBody vb) && vb.HasBuff(BebopSurvivor.HookAmpBuff)
                    && IsBebop(info.attacker.GetComponent<CharacterBody>());
                if (!force) { orig(self, info, victim); return; }
                info!.procCoefficient = 1f;
                _forceRolls++;
                try { orig(self, info, victim); }
                finally { _forceRolls--; }
            };
            On.RoR2.Util.CheckRoll_float_float_CharacterMaster += (orig, chance, luck, master) => _forceRolls > 0 && chance > 0f || orig(chance, luck, master);
            On.RoR2.Util.CheckRoll_float_CharacterMaster += (orig, chance, master) => _forceRolls > 0 && chance > 0f || orig(chance, master);
            DeadlockFallGuard.Hook();
        }

        private static int _forceRolls;

        public static bool IsBebop(CharacterBody? b) => b != null && BodyCatalog.GetBodyName(b.bodyIndex) == BebopSurvivor.BodyName;

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        public static Vector3 Core(HealthComponent hc) => hc.body != null ? hc.body.corePosition : hc.transform.position;
        public static bool IsBig(CharacterBody? b) => b != null && (b.isChampion || b.isBoss);
        /// <summary>Elites and minibosses (bosses too): x1.5 Sticky Bomb stacks (user).</summary>
        public static bool IsElite(CharacterBody? b) => b != null && (b.isElite || b.isChampion || b.isBoss || b.bodyIndex == BodyCatalog.FindBodyIndex("HalcyoniteBody"));

        /// <summary>Server: one hit from Bebop, with procs.</summary>
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
    }

    // ================= Secondary: Sticky Bomb =================

    /// <summary>Owner to server: a Sticky Bomb stuck to this enemy.</summary>
    public class BebopStickyMessage : INetMessage
    {
        public GameObject? Attacker, Target;
        public bool Crit;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Crit); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Crit = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null || Target == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            var hc = Target.GetComponent<HealthComponent>();
            if (body == null || hc == null || !hc.alive) return;
            (body.GetComponent<BebopStickySpeed>() ?? body.gameObject.AddComponent<BebopStickySpeed>()).Begin(); // +8 m/s, fading out (user 2.5)
            var go = new GameObject("CursedAppleBebopStickyBomb");
            go.transform.position = BebopNet.Core(hc);
            var bomb = go.AddComponent<BebopStickyBomb>();
            bomb.By = body;
            bomb.Target = hc;
            bomb.CarrierScale = BebopNet.IsElite(hc.body) ? BebopData.StickyEliteScale : 1f;
            bomb.Crit = Crit;
            bomb.Fuse = BebopData.StickyFuse * DurationExtender.Multiplier(body); // Duration Extender lengthens the fuse (user 2.5)
            bomb.Radius = BebopData.StickyRadius * MysticExpansion.RadiusMultiplier(body);
            EquipmentFx.Sound("bebop_sticky_attach", go.transform.position, Target);
            var fx = new BebopStickyFxMessage { Target = Target, Radius = bomb.Radius, Fuse = bomb.Fuse };
            fx.Send(NetworkDestination.Clients);
        }
    }

    /// <summary>Every client: the countdown ring and alarm on the bomb's carrier (Seven's Static Charge ring, red, without Seven's electric hum: user 2.5).</summary>
    public class BebopStickyFxMessage : INetMessage
    {
        public GameObject? Target;
        public float Radius, Fuse;

        public void Serialize(NetworkWriter w) { w.Write(Target); w.Write(Radius); w.Write(Fuse); }
        public void Deserialize(NetworkReader r) { Target = r.ReadGameObject(); Radius = r.ReadSingle(); Fuse = r.ReadSingle(); }

        public void OnReceived()
        {
            if (Target == null) return;
            var go = new GameObject("CursedAppleBebopStickyRing");
            go.transform.position = Target.transform.position;
            var ring = go.AddComponent<SevenRing>();
            ring.Follow = Target;
            ring.Radius = Radius;
            ring.Seconds = Fuse;
            ring.Color = new Color(1f, 0.12f, 0.1f, 0.9f);
            ring.Mute = true;
            DeadlockSounds.Loop("bebop_sticky_alarm", Target, Fuse);
        }
    }

    /// <summary>
    /// Server: one Sticky Bomb, riding its carrier. After the fuse it blows: 400% (plus Bebop's stacks) to every enemy in range. Each
    /// enemy it damages adds a permanent 1% to his Sticky Bomb, each kill 5%, elites and minibosses x1.5 (user). If the carrier dies first
    /// it goes off at once, where it died.
    /// </summary>
    public class BebopStickyBomb : MonoBehaviour
    {
        public CharacterBody? By;
        public HealthComponent? Target;
        public bool Crit;
        public float Fuse, Radius;
        /// <summary>Its carrier's stack scale, read when it was stuck on (the body may be gone when it dies).</summary>
        public float CarrierScale = 1f;
        private float _age;
        private bool _urgent;

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            bool carrierGone = Target == null || !Target.alive;
            if (!carrierGone) transform.position = BebopNet.Core(Target!);
            if (!_urgent && _age > Fuse - 1f) { _urgent = true; EquipmentFx.Sound("bebop_sticky_urgent", transform.position); }
            if (carrierGone || _age >= Fuse) Blow(carrierGone);
        }

        private void Blow(bool carrierDied = false)
        {
            if (By != null && By.teamComponent != null)
            {
                Vector3 at = transform.position;
                BebopNet.Effect(BebopSurvivor.StickyEffect, at, Radius);
                EquipmentFx.Sound("bebop_sticky_explode", at);
                var stacks = By.master != null ? By.master.GetComponent<BebopStickyStacks>() ?? By.master.gameObject.AddComponent<BebopStickyStacks>() : null;
                float bonus = 1f + (stacks != null ? stacks.Percent / 100f : 0f);
                float earned = 0f;
                // Stuck on himself: he takes a tenth of it (user 2.5), never the killing blow.
                if (Target != null && Target == By.healthComponent && Target.alive)
                    Target.TakeDamage(new DamageInfo
                    {
                        attacker = By.gameObject, inflictor = gameObject, damage = BebopData.StickyDamage * bonus * By.damage * BebopData.StickySelfDamage,
                        position = at, procCoefficient = 0f, damageColorIndex = DamageColorIndex.Default,
                        damageType = DamageType.NonLethal | DamageType.BypassBlock, crit = false,
                    });
                foreach (HealthComponent hc in BebopNet.EnemiesIn(at, Radius, By.teamComponent.teamIndex))
                {
                    DamageInfo? di = BebopNet.Hit(By, hc, BebopData.StickyDamage * bonus * By.damage, Crit, (hc.transform.position - at).normalized * 1000f, DamageSource.Secondary);
                    if (di == null || di.rejected) continue;
                    float scale = BebopNet.IsElite(hc.body) ? BebopData.StickyEliteScale : 1f;
                    earned += (hc.alive ? BebopData.StickyPerHit : BebopData.StickyPerKill) * scale;
                }
                // An enemy that dies carrying the bomb counts as the bomb's kill (user 2.5): it's already dead, so the blast never saw it.
                if (carrierDied && By.healthComponent != Target) earned += BebopData.StickyPerKill * CarrierScale;
                if (stacks != null && earned > 0f) stacks.Add(earned);
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Bebop] Sticky Bomb blew (x{bonus:0.00}): +{earned:0.#}% -> {(stacks != null ? stacks.Percent : 0f):0.#}%");
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Server, on Bebop: Sticky Bomb's speed boost, fading out (user 2.5): the buff's stacks (Steps of them) count down over
    /// StickySpeedSeconds, and his speed bonus is StickySpeed times the share left.
    /// </summary>
    public class BebopStickySpeed : MonoBehaviour
    {
        public const int Steps = 10;
        private CharacterBody _body = null!;
        private float _left;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin()
        {
            _left = BebopData.StickySpeedSeconds;
            Show();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _left <= 0f) return;
            _left = Mathf.Max(0f, _left - Time.fixedDeltaTime);
            Show();
        }

        private void Show()
        {
            if (BebopSurvivor.StickySpeedBuff == null) return;
            int want = Mathf.CeilToInt(Steps * _left / Mathf.Max(0.01f, BebopData.StickySpeedSeconds));
            if (_body.GetBuffCount(BebopSurvivor.StickySpeedBuff) != want) _body.SetBuffCount(BebopSurvivor.StickySpeedBuff.buffIndex, want);
        }
    }

    /// <summary>Server, on Bebop's master: his Sticky Bomb's permanent bonus (%), shown on his body as the stack buff. Cleared when he dies.</summary>
    public class BebopStickyStacks : MonoBehaviour
    {
        public float Percent;
        private CharacterMaster _master = null!;
        private float _next;

        private void Awake() => _master = GetComponent<CharacterMaster>();

        public void Add(float pct) { Percent += pct; Show(); }
        public void Clear() { Percent = 0f; Show(); }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Time.time < _next) return;
            _next = Time.time + 0.5f;
            Show(); // a new body (stage change) needs the count again
        }

        private void Show()
        {
            CharacterBody? b = _master != null ? _master.GetBody() : null;
            if (b == null || BebopSurvivor.StickyStacksBuff == null) return;
            int want = Mathf.FloorToInt(Percent);
            if (b.GetBuffCount(BebopSurvivor.StickyStacksBuff) != want) b.SetBuffCount(BebopSurvivor.StickyStacksBuff.buffIndex, want);
        }
    }

    // ================= Secondary alt: the hook =================

    /// <summary>Owner to server: the hook latched onto this enemy (pull it in, mark it for the beam), or let go of it.</summary>
    public class BebopHookMessage : INetMessage
    {
        public GameObject? Attacker, Target;
        public bool Crit, Release;

        public void Serialize(NetworkWriter w) { w.Write(Attacker); w.Write(Target); w.Write(Crit); w.Write(Release); }
        public void Deserialize(NetworkReader r) { Attacker = r.ReadGameObject(); Target = r.ReadGameObject(); Crit = r.ReadBoolean(); Release = r.ReadBoolean(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null || Target == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            var hc = Target.GetComponent<HealthComponent>();
            if (body == null || hc == null) return;
            if (Release) { if (Target.TryGetComponent(out BebopHookPull p)) p.Drop(); return; }
            if (!hc.alive) return;
            EquipmentFx.Sound("bebop_hook_hit", BebopNet.Core(hc));
            EquipmentFx.Sound("bebop_hook_latched", BebopNet.Core(hc));
            EquipmentFx.Sound("bebop_vo_hook_lands", body.corePosition, Attacker); // "Come here!" (bebop_hook_lands)
            BebopNet.Hit(body, hc, BebopData.HookDamage * body.damage, Crit, Vector3.zero, DamageSource.Secondary);
            if (!hc.alive || hc.body == null) return;
            using (DurationExtender.From(body))
                if (BebopSurvivor.HookAmpBuff != null) hc.body.AddTimedBuff(BebopSurvivor.HookAmpBuff, BebopData.HookAmpSeconds);
            if (BebopNet.IsBig(hc.body)) return; // bosses stay put: they still take the beam bonus
            var pull = Target.GetComponent<BebopHookPull>() ?? Target.AddComponent<BebopHookPull>();
            pull.Grab(body);
        }
    }

    /// <summary>Server, on a hooked enemy: reeled in toward Bebop (Deadlock: pulled to him), stunned on the way, until it's close.</summary>
    public class BebopHookPull : MonoBehaviour
    {
        private CharacterBody? _by, _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;
        private float _until, _speed;

        public void Grab(CharacterBody by)
        {
            _by = by;
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
            // A 200 m hook (user 2.5): reeled in fast enough to arrive in about HookPullTime.
            float dist = _body != null ? Vector3.Distance(_body.corePosition, by.corePosition) : 10f;
            _speed = Mathf.Max(BebopData.HookPullSpeed, dist / BebopData.HookPullTime);
            _until = Time.time + BebopData.HookPullTime + 0.5f;
            SetStateOnHurt.SetStunOnObject(gameObject, BebopData.HookPullTime + 0.5f);
            DeadlockFallGuard.Protect(gameObject, BebopData.HookPullTime + 1.5f);
            EquipmentFx.Sound("bebop_hook_yank", transform.position, gameObject);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (_by == null || _body == null || _body.healthComponent == null || !_body.healthComponent.alive || Time.time > _until) { Drop(); return; }
            // Reeled to the spot right in front of him, where he faces (user 2.5): set down there, lined up for an uppercut.
            Vector3 facing = _by.inputBank != null ? _by.inputBank.aimDirection : _by.transform.forward;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : _by.transform.forward;
            Vector3 spot = _by.corePosition + facing * (BebopData.HookPullStop + _body.radius);
            Vector3 to = spot - _body.corePosition;
            float step = _speed * Time.fixedDeltaTime;
            if (to.magnitude <= Mathf.Max(0.5f, step))
            {
                if (_motor != null) _motor.Motor.SetPosition(_motor.Motor.TransientPosition + to, true);
                else transform.position += to;
                Drop();
                return;
            }
            Vector3 v = to.normalized * _speed;
            if (_motor != null) { _motor.Motor.ForceUnground(); _motor.velocity = v; _motor.disableAirControlUntilCollision = true; }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = v;
            else transform.position += v * Time.fixedDeltaTime;
        }

        public void Drop()
        {
            if (_motor != null) { _motor.velocity = Vector3.zero; _motor.disableAirControlUntilCollision = false; }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = Vector3.zero;
            if (_body != null && _body.healthComponent != null && _body.healthComponent.alive) SetStateOnHurt.SetStunOnObject(gameObject, 0.5f); // Deadlock RestrictionDuration
            EquipmentFx.Sound("bebop_hook_return", transform.position);
            Destroy(this);
        }
    }

    // ================= Utility: Exploding Uppercut =================

    /// <summary>Owner to server: the uppercut caught these enemies; launch them along Dir.</summary>
    public class BebopUppercutMessage : INetMessage
    {
        public GameObject? Attacker;
        public GameObject[] Targets = new GameObject[0];
        public Vector3 Dir;
        public bool Crit;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Attacker); w.Write(Dir); w.Write(Crit);
            w.Write((byte)Mathf.Min(255, Targets.Length));
            for (int i = 0; i < Targets.Length && i < 255; i++) w.Write(Targets[i]);
        }

        public void Deserialize(NetworkReader r)
        {
            Attacker = r.ReadGameObject(); Dir = r.ReadVector3(); Crit = r.ReadBoolean();
            Targets = new GameObject[r.ReadByte()];
            for (int i = 0; i < Targets.Length; i++) Targets[i] = r.ReadGameObject();
        }

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            bool landed = false;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                DamageInfo? di = BebopNet.Hit(body, hc, BebopData.UppercutDamage * body.damage, Crit, Vector3.zero, DamageSource.Utility);
                if (di == null || di.rejected) continue;
                landed = true;
                BebopNet.Effect(BebopSurvivor.PunchEffect, BebopNet.Core(hc), 2f);
                if (!hc.alive || BebopNet.IsBig(hc.body)) continue; // bosses take the punch but don't fly
                var toss = t.GetComponent<BebopToss>() ?? t.AddComponent<BebopToss>();
                toss.Launch(body, Dir * BebopData.UppercutToss, Crit);
            }
            if (!landed) return;
            // Landing an uppercut, once a cast (user): a beam damage buff and a heal of missing health.
            EquipmentFx.Sound("bebop_uppercut_hit", body.corePosition, body.gameObject);
            if (BebopSurvivor.UppercutBuff != null) body.AddTimedBuff(BebopSurvivor.UppercutBuff, BebopData.UppercutBuffSeconds);
            HealthComponent self = body.healthComponent;
            if (self != null && self.alive) self.Heal(Mathf.Max(0f, self.fullHealth - self.health) * BebopData.UppercutHeal, default(ProcChainMask));
        }
    }

    /// <summary>Server, on a tossed enemy: flies on its arc, then explodes where it lands, hurting enemies around it (Deadlock LandingDamage).</summary>
    public class BebopToss : MonoBehaviour
    {
        private CharacterBody? _by, _body;
        private CharacterMotor? _motor;
        private Rigidbody? _rb;
        private float _age;
        private bool _crit, _flying;

        public void Launch(CharacterBody by, Vector3 velocity, bool crit)
        {
            _by = by; _crit = crit; _age = 0f; _flying = true;
            _body = GetComponent<CharacterBody>();
            _motor = GetComponent<CharacterMotor>();
            _rb = GetComponent<Rigidbody>();
            SetStateOnHurt.SetStunOnObject(gameObject, 1.5f);
            DeadlockFallGuard.Protect(gameObject, 4f);
            if (_motor != null) { _motor.Motor.ForceUnground(); _motor.velocity = velocity; _motor.disableAirControlUntilCollision = true; }
            else if (_rb != null && !_rb.isKinematic) _rb.velocity = velocity;
            EquipmentFx.Sound("bebop_uppercut_fly", transform.position, gameObject);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || !_flying) return;
            _age += Time.fixedDeltaTime;
            bool alive = _body != null && _body.healthComponent != null && _body.healthComponent.alive;
            bool down = _motor != null ? _age > 0.25f && _motor.isGrounded : _age > 1f; // fliers: about Deadlock's toss time
            if (!alive || down || _age > 4f) Land();
        }

        private void Land()
        {
            _flying = false;
            if (_by != null && _by.teamComponent != null && _body != null)
            {
                Vector3 at = _body.footPosition;
                float radius = BebopData.UppercutLandRadius * MysticExpansion.RadiusMultiplier(_by);
                BebopNet.Effect(BebopSurvivor.LandEffect, at, radius);
                EquipmentFx.Sound("bebop_uppercut_land", at);
                foreach (HealthComponent hc in BebopNet.EnemiesIn(at, radius, _by.teamComponent.teamIndex))
                    if (hc != _body.healthComponent) // the landing hurts the others around it, not the one that landed (user 2.5)
                        BebopNet.Hit(_by, hc, BebopData.UppercutLandDamage * _by.damage, _crit, Vector3.up * 600f, DamageSource.Utility, 0.5f);
            }
            if (_motor != null) _motor.disableAirControlUntilCollision = false;
            Destroy(this);
        }
    }

    // ================= Special: Hyper Beam =================

    /// <summary>Owner to server: one Hyper Beam tick hit these enemies.</summary>
    public class BebopHyperMessage : INetMessage
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

        public void OnReceived()
        {
            if (!NetworkServer.active || Attacker == null) return;
            var body = Attacker.GetComponent<CharacterBody>();
            if (body == null) return;
            foreach (GameObject t in Targets)
            {
                var hc = t != null ? t.GetComponent<HealthComponent>() : null;
                if (hc == null || !hc.alive) continue;
                float scale = BebopNet.IsBig(hc.body) ? BebopData.HyperBossScale : 1f;
                DamageInfo? di = BebopNet.Hit(body, hc, BebopData.HyperDps * BebopData.HyperTick * scale * body.damage, Crit, Vector3.zero, DamageSource.Special, BebopData.HyperProc);
                if (di != null && !di.rejected && body.healthComponent != null && body.healthComponent.alive) // 100% lifesteal (user 2.5)
                    body.healthComponent.Heal(di.damage * BebopData.HyperLifesteal, default(ProcChainMask));
                if (hc.alive && hc.body != null && BebopSurvivor.HyperSlow != null)
                    using (DurationExtender.From(body)) hc.body.AddTimedBuff(BebopSurvivor.HyperSlow, BebopData.HyperSlowSeconds);
                EquipmentFx.Sound("bebop_hyper_hit", BebopNet.Core(hc));
            }
        }
    }

    // ================= beams (every client) =================

    /// <summary>
    /// Every client, on Bebop: draws a beam (a vanilla laser prefab) from his gun's muzzle to where his aim meets something, while a
    /// state says so. Humble Pie and Hyper Beam each have one (width and reach differ).
    /// </summary>
    public class BebopBeamFx : MonoBehaviour
    {
        public GameObject? Prefab;
        public float Range = 32f, Width = 1f;
        /// <summary>Drawn as a solid coloured core and glow instead of the prefab's material (Humble Pie: red, the full reach; user 2.5).</summary>
        public Color? Solid;
        public bool HitEnemies = true;
        /// <summary>Set by a state (on every machine): the beam shows while true.</summary>
        public bool Active;
        /// <summary>The direction the beam points (Hyper Beam turns slower than the aim); zero = the aim.</summary>
        public Vector3 Direction;
        public Vector3 End { get; private set; }

        private LineRenderer? _line, _glow;
        private static Material? _solid;
        private CharacterBody _body = null!;
        private ChildLocator? _children;

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>Our own line, in the laser prefab's material (its own layout differs per prefab), so the beam always draws.</summary>
        private void Build()
        {
            var go = new GameObject("BebopBeam");
            _line = go.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.widthMultiplier = 0.25f * Width;
            if (Solid is Color c)
            {
                // No texture to fade out along it: a bright core inside a wider glow, end to end.
                _solid ??= CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.LineMaterial();
                _line.sharedMaterial = _solid;
                _line.startColor = _line.endColor = Color.Lerp(c, Color.white, 0.45f);
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var g = new GameObject("BebopBeamGlow");
                g.transform.SetParent(go.transform, false);
                _glow = g.AddComponent<LineRenderer>();
                _glow.positionCount = 2;
                _glow.useWorldSpace = true;
                _glow.sharedMaterial = _solid;
                _glow.startColor = _glow.endColor = new Color(c.r, c.g, c.b, 0.35f);
                _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return;
            }
            LineRenderer? source = Prefab != null ? Prefab.GetComponentInChildren<LineRenderer>(true) : null;
            _line.material = source != null && source.sharedMaterial != null ? source.sharedMaterial : BebopSurvivor.ChainMaterial;
            _line.colorGradient = source != null ? source.colorGradient : new Gradient();
            _line.textureMode = LineTextureMode.Tile;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (!Active) { if (_line != null) _line.enabled = false; if (_glow != null) _glow.enabled = false; return; }
            if (_line == null) Build();
            _line!.enabled = true;
            if (_glow != null) _glow.enabled = true;
            Vector3 from = Muzzle();
            Ray aim = _body.inputBank != null ? _body.inputBank.GetAimRay() : new Ray(_body.corePosition, transform.forward);
            Vector3 dir = Direction.sqrMagnitude > 0.01f ? Direction.normalized : aim.direction;
            End = BebopBeams.EndPoint(aim.origin, dir, Range * MysticExpansion.RadiusMultiplier(_body), HitEnemies, _body);
            _line.SetPosition(0, from);
            _line.SetPosition(1, End);
            // A slight flicker in width, like a live beam.
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 20f, 0f);
            _line.widthMultiplier = (Solid.HasValue ? 0.06f : 0.25f) * Width * flicker;
            if (_glow != null)
            {
                _glow.SetPosition(0, from);
                _glow.SetPosition(1, End);
                _glow.widthMultiplier = 0.2f * Width * flicker;
            }
        }

        private Vector3 Muzzle()
        {
            if (_children == null)
            {
                Transform? model = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
                _children = model != null ? model.GetComponent<ChildLocator>() : null;
            }
            Transform? m = _children != null ? _children.FindChild(BebopSurvivor.MuzzleName) : null;
            return m != null ? m.position : _body.corePosition;
        }

        private void OnDestroy() { if (_line != null) Destroy(_line.gameObject); }

        private void OnDisable() { if (_glow != null) _glow.enabled = false; }
    }

    public static class BebopBeams
    {
        /// <summary>Where a beam from origin along dir stops: terrain, or (Humble Pie) the first enemy.</summary>
        public static Vector3 EndPoint(Vector3 origin, Vector3 dir, float range, bool enemies, CharacterBody? self = null)
        {
            int mask = LayerIndex.world.mask | (enemies ? LayerIndex.entityPrecise.mask : 0);
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, range, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            TeamIndex team = self != null && self.teamComponent != null ? self.teamComponent.teamIndex : TeamIndex.None;
            foreach (RaycastHit h in hits)
            {
                var box = h.collider.GetComponent<HurtBox>();
                if (box != null && (box.teamIndex == team || box.healthComponent == null)) continue; // his own body, allies: the beam goes through
                return h.point;
            }
            return origin + dir * range;
        }
    }
}
