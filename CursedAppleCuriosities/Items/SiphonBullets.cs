using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class SiphonBullets : ItemBase
    {
        public override string ItemName          => "Siphon Bullets";
        public override string ItemLangTokenName => "SIPHONBULLETS";

        public override string ItemPickupDesc => "Hits steal max health from enemies.";
        public override string ItemFullDescription =>
            $"Hitting an enemy steals " +
            $"<style=cIsHealing>{StealPerHitPercent * 100:0}% of the damage dealt</style> " +
            $"as bonus max HP per hit, up to " +
            $"<style=cIsHealing>{MaxStealPercent * 100:0}%</style> " +
            $"<style=cStack>(+{MaxStealPercent * 100:0}% per stack)</style> " +
            $"of that enemy's max HP. " +
            $"<style=cIsHealth>Stolen HP is lost when that enemy dies.</style>";

        public override string ItemLore =>
            "Allow me to provide the answer to the age old question: what if Oathkeeper was a gun?";

        public override ItemTier  Tier     => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Damage };
        protected override string IconFileName => "icon_siphonbullets.png";

        public static float StealPerHitPercent;          // fraction of damage dealt stolen per hit
        public static float MaxStealPercent;             // cap per enemy per stack (of enemy max HP)

        /// <summary>
        /// Hidden: one stack per point of stolen max HP. The tracker lives on the server; every client recalculates its own
        /// body's max HP, so the total travels as this synced buff (a client's HUD used to miss the bonus).
        /// </summary>
        public static BuffDef? StolenHealthBuff;

        public override void Init(ConfigFile config)
        {
            StolenHealthBuff = ScriptableObject.CreateInstance<BuffDef>();
            StolenHealthBuff.name = "CursedApple_SiphonStolenHealth";
            StolenHealthBuff.canStack = true;
            StolenHealthBuff.isHidden = true;
            StolenHealthBuff.isDebuff = false;
            StolenHealthBuff.iconSprite = CreatePlaceholderSprite(Color.red);
            ContentAddition.AddBuffDef(StolenHealthBuff);

            StealPerHitPercent = 0.01f;
            MaxStealPercent = 0.10f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy      += OnHitEnemy;
            On.RoR2.GlobalEventManager.OnCharacterDeath += OnCharacterDeath;
            RecalculateStatsAPI.GetStatCoefficients     += GetStatCoefficients;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)
            if (!NetworkServer.active) return;
            if (damageInfo?.procCoefficient <= 0f) return;
            if (damageInfo.attacker == null || victim == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            var tracker = attackerBody.GetComponent<SiphonBulletsTracker>()
                ?? attackerBody.gameObject.AddComponent<SiphonBulletsTracker>();
            tracker.OnHit(victimHC, count, damageInfo.damage);
        }

        private void OnCharacterDeath(
            On.RoR2.GlobalEventManager.orig_OnCharacterDeath orig,
            GlobalEventManager self, DamageReport report)
        {
            orig(self, report);
            if (!NetworkServer.active) return;

            var victimHC = report?.victimBody?.healthComponent;
            if (victimHC == null) return;

            // Strip contribution from every player's tracker when the source enemy dies.
            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                var body = pcmc.master?.GetBody();
                body?.GetComponent<SiphonBulletsTracker>()?.OnEnemyDied(victimHC);
            }
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            // Unity null checks (?. misses destroyed objects, e.g. a body torn down between stages).
            if (sender == null || sender.inventory == null || ItemDef == null || StolenHealthBuff == null) return;
            if (sender.inventory.GetItemCountEffective(ItemDef) <= 0) return;
            args.baseHealthAdd += sender.GetBuffCount(StolenHealthBuff); // synced: right on every machine
        }
    }

    public class SiphonBulletsTracker : MonoBehaviour
    {
        private CharacterBody                      _body;
        private Dictionary<HealthComponent, float> _stolen = new Dictionary<HealthComponent, float>();
        private float                              _cleanupTimer;

        public float TotalStolen
        {
            get
            {
                float total = 0f;
                foreach (var v in _stolen.Values) total += v;
                return total;
            }
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void OnHit(HealthComponent victimHC, int itemCount, float damageDealt)
        {
            float enemyMax = victimHC.fullHealth;
            if (enemyMax <= 0f) return;

            // Steal is a fraction of the damage dealt, but the running total per enemy is
            // still capped at a fraction of that enemy's max HP (scaling with item count).
            float cap     = enemyMax * SiphonBullets.MaxStealPercent * itemCount;
            float current = _stolen.TryGetValue(victimHC, out var v) ? v : 0f;
            float toSteal = Mathf.Min(damageDealt * SiphonBullets.StealPerHitPercent, cap - current);
            if (toSteal <= 0f) return;

            _stolen[victimHC] = current + toSteal;
            Sync();
        }

        public void OnEnemyDied(HealthComponent victimHC)
        {
            if (!_stolen.Remove(victimHC)) return;
            Sync();
            ClampCurrentHP();
        }

        private void Update()
        {
            // Periodically clean up entries for enemies that despawned without a death event.
            _cleanupTimer += Time.deltaTime;
            if (_cleanupTimer < 2f) return;
            _cleanupTimer = 0f;

            var toRemove = new List<HealthComponent>();
            foreach (var hc in _stolen.Keys)
                if (hc == null || !hc.alive) toRemove.Add(hc);

            if (toRemove.Count == 0) return;
            foreach (var hc in toRemove) _stolen.Remove(hc);
            Sync();
            ClampCurrentHP();
        }

        /// <summary>Server: the stolen total, whole points, into the synced buff (setting it marks stats dirty).</summary>
        private void Sync()
        {
            if (_body == null || !NetworkServer.active || SiphonBullets.StolenHealthBuff == null) return;
            _body.SetBuffCount(SiphonBullets.StolenHealthBuff.buffIndex, Mathf.RoundToInt(TotalStolen));
            _body.RecalculateStats();
        }

        private void OnDestroy()
        {
            if (_body != null && NetworkServer.active && SiphonBullets.StolenHealthBuff != null)
                _body.SetBuffCount(SiphonBullets.StolenHealthBuff.buffIndex, 0);
        }

        private void ClampCurrentHP()
        {
            if (_body?.healthComponent == null) return;
            _body.healthComponent.health = Mathf.Min(
                _body.healthComponent.health, _body.maxHealth);
        }
    }
}
