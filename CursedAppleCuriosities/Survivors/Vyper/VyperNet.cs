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

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Server side of Vyper: Lethal Venom, Petrify (stunned and immune to everything but Venom), Screwjab's stacking
    /// slow, and the dagger and bola projectiles' hits. States run on the owning player and message the server.
    /// </summary>
    public static class VyperNet
    {
        /// <summary>True while a Lethal Venom detonation is being dealt: Petrify lets it through (Deadlock).</summary>
        private static bool _venomHit;

        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<VyperVenomMessage>();
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (VyperSurvivor.PetrifyBuff != null && self.body != null && self.body.HasBuff(VyperSurvivor.PetrifyBuff) && !_venomHit)
                {
                    info.rejected = true;
                    if (VyperSurvivor.BlockEffect != null) EffectManager.SimpleImpactEffect(VyperSurvivor.BlockEffect, info.position, Vector3.up, true);
                }
                orig(self, info);
            };
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (VyperSurvivor.ScrewjabSlow != null && body.HasBuff(VyperSurvivor.ScrewjabSlow))
                {
                    int stacks = VyperSurvivor.ScrewjabStacks != null ? Mathf.Max(1, body.GetBuffCount(VyperSurvivor.ScrewjabStacks)) : 1;
                    args.moveSpeedReductionMultAdd += VyperData.DaggerSlow + VyperData.DaggerSlowPerStack * (stacks - 1);
                }
                if (VyperSurvivor.BolaSlow != null && body.HasBuff(VyperSurvivor.BolaSlow)) args.moveSpeedReductionMultAdd += VyperData.BolaSlow;
            };
            DeadlockFallGuard.Hook();
        }

        public static void ToServer(INetMessage m)
        {
            if (NetworkServer.active) m.OnReceived();
            else m.Send(NetworkDestination.Server);
        }

        /// <summary>Server: one hit from Vyper, with procs.</summary>
        public static void Hit(CharacterBody attacker, HealthComponent target, float damage, bool crit, Vector3 force, DamageTypeCombo type, float proc = 1f, bool venom = false)
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
                damageColorIndex = venom ? DamageColorIndex.Poison : DamageColorIndex.Default,
                damageType = type,
            };
            _venomHit = venom;
            try { target.TakeDamage(di); }
            finally { _venomHit = false; }
            if (di.rejected) return;
            GlobalEventManager.instance.OnHitEnemy(di, target.gameObject);
            GlobalEventManager.instance.OnHitAll(di, target.gameObject);
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

        /// <summary>Server: petrify (stun + immunity); bosses only get a short stun and stay damageable (user).</summary>
        public static void Petrify(HealthComponent hc)
        {
            if (hc == null || !hc.alive || hc.body == null) return;
            if (hc.body.isChampion)
            {
                SetStateOnHurt.SetStunOnObject(hc.gameObject, VyperData.PetrifyBossSeconds);
                return;
            }
            if (VyperSurvivor.PetrifyBuff != null) hc.body.AddTimedBuff(VyperSurvivor.PetrifyBuff, VyperData.PetrifySeconds);
            if (VyperSurvivor.BlockEffect != null) EffectManager.SpawnEffect(VyperSurvivor.BlockEffect, new EffectData { origin = hc.body.corePosition, scale = hc.body.bestFitRadius * 2f }, true);
            if (hc.GetComponent<VyperPetrifyEnd>() == null) hc.gameObject.AddComponent<VyperPetrifyEnd>();
            if (VyperSurvivor.BolaSlow != null) hc.body.AddTimedBuff(VyperSurvivor.BolaSlow, VyperData.PetrifySeconds + VyperData.BolaSlowSeconds);
            SetStateOnHurt.SetStunOnObject(hc.gameObject, VyperData.PetrifySeconds);
        }
    }

    /// <summary>Lethal Venom lands on a target: it detonates after the delay, harder the more health it's missing.</summary>
    public class VyperVenomMessage : INetMessage
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
            if (VyperSurvivor.VenomBuff != null && hc.body != null) hc.body.AddTimedBuff(VyperSurvivor.VenomBuff, VyperData.VenomDelay);
            var v = Target.AddComponent<VyperVenom>();
            v.By = body;
            v.Crit = Crit;
        }
    }

    /// <summary>Server, on the poisoned enemy: one pending Lethal Venom.</summary>
    public class VyperVenom : MonoBehaviour
    {
        public CharacterBody? By;
        public bool Crit;
        private float _age;

        private void FixedUpdate()
        {
            _age += Time.fixedDeltaTime;
            if (_age < VyperData.VenomDelay) return;
            var hc = GetComponent<HealthComponent>();
            if (By != null && hc != null && hc.alive)
            {
                float t = Mathf.InverseLerp(VyperData.VenomMinAt, VyperData.VenomMaxAt, hc.combinedHealthFraction);
                float coeff = Mathf.Lerp(VyperData.VenomMinDamage, VyperData.VenomMaxDamage, t);
                if (VyperSurvivor.VenomEffect != null) EffectManager.SpawnEffect(VyperSurvivor.VenomEffect, new EffectData { origin = VyperNet.Core(hc), scale = 2f }, true);
                EquipmentFx.Sound(t < 0.34f ? "vyper_venom_weak" : t < 0.67f ? "vyper_venom_medium" : "vyper_venom_strong", VyperNet.Core(hc));
                VyperNet.Hit(By, hc, coeff * By.damage, Crit, Vector3.zero, DamageSource.Utility, 1f, venom: true);
            }
            Destroy(this);
        }
    }

    /// <summary>Server, on the Screwjab Dagger projectile: the first enemy it hits takes the stacking damage and slow.</summary>
    [RequireComponent(typeof(ProjectileController))]
    public class VyperDagger : MonoBehaviour, IProjectileImpactBehavior
    {
        private bool _done;
        private DeadlockVoice? _hum;

        private void Start() => _hum = DeadlockSounds.Loop("vyper_dagger_loop", gameObject, 2.5f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

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
            EquipmentFx.Sound(onEnemy ? "vyper_dagger_impact" : "vyper_dagger_miss", transform.position);
            if (onEnemy)
            {
                int before = VyperSurvivor.ScrewjabStacks != null ? hc!.body!.GetBuffCount(VyperSurvivor.ScrewjabStacks) : 0;
                float coeff = VyperData.DaggerDamage + VyperData.DaggerStackDamage * before;
                VyperNet.Hit(body!, hc!, coeff * (pd != null ? pd.damage : body!.damage), pd != null && pd.crit, Vector3.zero, DamageSource.Secondary);
                if (hc.alive && VyperSurvivor.ScrewjabStacks != null && VyperSurvivor.ScrewjabSlow != null)
                {
                    // Every dagger refreshes the stacks (Deadlock StackDuration) and the slow.
                    int stacks = Mathf.Min(VyperData.DaggerMaxStacks, before + 1);
                    using var extend = DurationExtender.From(body); // Duration Extender: Vyper's
                    hc.body.ClearTimedBuffs(VyperSurvivor.ScrewjabStacks);
                    for (int i = 0; i < stacks; i++) hc.body.AddTimedBuff(VyperSurvivor.ScrewjabStacks, VyperData.DaggerStackSeconds);
                    hc.body.ClearTimedBuffs(VyperSurvivor.ScrewjabSlow);
                    hc.body.AddTimedBuff(VyperSurvivor.ScrewjabSlow, VyperData.DaggerSlowSeconds);
                }
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Server, on the Petrifying Bola: bursts on whatever it touches (or at the end of its flight). Everything in the
    /// blast takes damage and is petrified (Deadlock's T3, given free); a direct hit takes extra damage first.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class VyperBola : MonoBehaviour, IProjectileImpactBehavior
    {
        private bool _done;
        private float _age;
        private DeadlockVoice? _hum;

        private void Start() => _hum = DeadlockSounds.Loop("vyper_bola_loop", gameObject, VyperData.BolaLifetime + 0.5f); // every client
        private void OnDestroy() => DeadlockSounds.Stop(_hum);

        public void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (_done || !NetworkServer.active) return;
            HurtBox? box = info.collider != null ? info.collider.GetComponent<HurtBox>() : null;
            // Where the bola really is (estimatedPointOfImpact isn't always set: blasts landed far from anything).
            Burst(box != null ? box.healthComponent : null, transform.position, info.collider != null ? info.collider.name : "?");
        }

        private Rigidbody? _rb;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void FixedUpdate()
        {
            // Straight with a little drop (no spin, no steering: it corkscrewed).
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.angularVelocity = Vector3.zero;
                _rb.velocity += Vector3.down * VyperData.BolaDrop * Time.fixedDeltaTime;
            }
            if (_done || !NetworkServer.active) return;
            _age += Time.fixedDeltaTime;
            if (_age >= VyperData.BolaLifetime) Burst(null, transform.position, "end of flight");
        }

        private void Burst(HealthComponent? direct, Vector3 at, string why)
        {
            _done = true;
            var pc = GetComponent<ProjectileController>();
            var pd = GetComponent<ProjectileDamage>();
            var body = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            if (body != null && body.teamComponent != null)
            {
                float dmg = pd != null ? pd.damage : body.damage;
                bool crit = pd != null && pd.crit;
                float radius = VyperData.BolaRadius * MysticExpansion.RadiusMultiplier(body);
                if (VyperSurvivor.BolaEffect != null) EffectManager.SpawnEffect(VyperSurvivor.BolaEffect, new EffectData { origin = at, scale = radius }, true);
                EquipmentFx.Sound("vyper_bola_explode", at);
                if (direct != null && direct.alive && direct.body != null && direct.body.teamComponent.teamIndex != body.teamComponent.teamIndex)
                {
                    EquipmentFx.Sound("vyper_bola_direct", at);
                    VyperNet.Hit(body, direct, VyperData.BolaDirectDamage * dmg, crit, Vector3.zero, DamageSource.Special);
                }
                List<HealthComponent> caught = VyperNet.EnemiesIn(at, radius, body.teamComponent.teamIndex);
                foreach (HealthComponent hc in caught) VyperNet.Hit(body, hc, VyperData.BolaDamage * dmg, crit, Vector3.zero, DamageSource.Special);
                using (DurationExtender.From(body)) // Duration Extender: Vyper's, for the petrify, stun and slow
                    foreach (HealthComponent hc in caught) VyperNet.Petrify(hc); // after the damage: petrified enemies block it
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Vyper] Petrifying Bola burst ({why}) {Vector3.Distance(at, body.corePosition):0.#} m from Vyper after {_age:0.##} s: " +
                    $"{caught.Count} caught, direct {(direct != null ? direct.name : "none")}");
            }
            Destroy(gameObject);
        }
    }
}

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Every client, on Vyper: The Rattler's layered sound, as Deadlock plays it. The first shot of a string is shot_first,
    /// the rest shot_mid; when shots stop coming, its tail and the trigger release.
    /// </summary>
    public class VyperGunAudio : MonoBehaviour
    {
        private float _last = -10f, _spread;
        private bool _firing;

        /// <summary>The spread for this shot (degrees), then widened for the next (Deadlock's shoot spread penalty).</summary>
        public static float Spread(GameObject body)
        {
            var a = body.GetComponent<VyperGunAudio>();
            if (a == null) return VyperData.GunSpreadMin;
            float now = a._spread;
            a._spread = Mathf.Min(VyperData.GunSpreadMax - VyperData.GunSpreadMin, a._spread + VyperData.GunSpreadPerShot);
            return VyperData.GunSpreadMin + now;
        }

        public static void Shot(GameObject body)
        {
            var a = body.GetComponent<VyperGunAudio>();
            bool first = a == null || !a._firing;
            DeadlockSounds.Play(first ? "vyper_gun_first" : "vyper_gun_mid", body.transform.position, body);
            if (a != null) { a._firing = true; a._last = Time.time; }
        }

        private void Update()
        {
            if (Time.time - _last > VyperData.GunSpreadRecoverDelay) _spread = Mathf.Max(0f, _spread - VyperData.GunSpreadRecover * Time.deltaTime);
            if (!_firing || Time.time - _last < VyperData.GunCycle * 2.5f) return;
            _firing = false;
            DeadlockSounds.Play("vyper_gun_tail", transform.position, gameObject);
            DeadlockSounds.Play("vyper_gun_release", transform.position, gameObject);
        }
    }

    /// <summary>
    /// Server, on a petrified body: Petrify's end sound for everyone when it wears off.
    /// </summary>
    public class VyperPetrifyEnd : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private bool _had;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            bool has = VyperSurvivor.PetrifyBuff != null && _body != null && _body.HasBuff(VyperSurvivor.PetrifyBuff);
            if (_had && !has)
            {
                if (_body!.healthComponent != null && _body.healthComponent.alive) EquipmentFx.Sound("vyper_petrify_end", _body.corePosition); // to everyone
                Destroy(this);
                return;
            }
            _had = has;
        }
    }
}
