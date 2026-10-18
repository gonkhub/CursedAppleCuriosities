using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class HeroicAura : ItemBase
    {
        public override string ItemName => "Heroic Aura";
        public override string ItemLangTokenName => "HEROICAURA";

        public override string ItemPickupDesc => "You and nearby allies take less damage and attack faster.";
        public override string ItemFullDescription =>
            $"Emit a <style=cIsUtility>{AuraRange}m</style> aura granting you and nearby allies " +
            $"<style=cIsHealing>-{BaseDamageResist * 100:0}% incoming damage</style> and " +
            $"<style=cIsDamage>+{BaseAttackSpeed * 100:0}% attack speed</style> " +
            $"<style=cStack>(+{BaseDamageResist * 100:0}% and +{BaseAttackSpeed * 100:0}% per stack)</style>.";

        public override string ItemLore =>
            "I see someone appreciates fine craftsmanship.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_heroicaura.png";

        public const float AuraRange        = 20f;
        public static float BaseDamageResist;
        public static float BaseAttackSpeed;
        public const float PulseInterval    = 0.5f;

        public static BuffDef? AuraBuffDef;

        public override void Init(ConfigFile config)
        {
            var color              = new Color(1f, 0.85f, 0.2f);
            AuraBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            AuraBuffDef.name       = "CursedApple_HeroicAura";
            AuraBuffDef.canStack   = true;
            AuraBuffDef.isDebuff   = false;
            AuraBuffDef.buffColor  = color;
            AuraBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(AuraBuffDef);

            BaseDamageResist = 0.08f;
            BaseAttackSpeed = 0.08f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged  += CharacterBody_OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients   += GetStatCoefficients;
            On.RoR2.HealthComponent.TakeDamage        += HealthComponent_TakeDamage;
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count = self.inventory.GetItemCountEffective(ItemDef);
            var ctrl  = self.GetComponent<HeroicAuraController>();

            if (count > 0 && ctrl == null)
                self.gameObject.AddComponent<HeroicAuraController>().Initialize(this);
            else if (count <= 0 && ctrl != null)
                UnityEngine.Object.Destroy(ctrl);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (AuraBuffDef == null || AuraBuffDef.buffIndex == BuffIndex.None) return;
            int stacks = sender.GetBuffCount(AuraBuffDef.buffIndex);
            if (stacks > 0)
                args.attackSpeedMultAdd += BaseAttackSpeed * stacks;
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (FinalDamage(damageInfo)) { orig(self, damageInfo); return; } // already worked out (Bloodletting's ticks)
            if (NetworkServer.active
                && damageInfo != null
                && self.body != null
                && AuraBuffDef != null
                && AuraBuffDef.buffIndex != BuffIndex.None)
            {
                int stacks = self.body.GetBuffCount(AuraBuffDef.buffIndex);
                if (stacks > 0)
                {
                    float resist = Mathf.Min(0.80f, BaseDamageResist * stacks);
                    damageInfo.damage *= 1f - resist;
                }
            }

            orig(self, damageInfo);
        }
    }

    public class HeroicAuraController : MonoBehaviour
    {
        private float         _timer;
        private CharacterBody _body;
        private HeroicAura    _item;

        // How many aura stacks THIS emitter has contributed to each body. Multiple emitters
        // (the player + each minion that inherits the item, e.g. Engineer turrets) each run
        // their own controller and contribute their own stacks via DELTAS (AddBuff/RemoveBuff),
        // so contributions ADD UP on shared allies: player + 1 turret => 2x, +2 turrets => 3x.
        // (The old SyncBuff SET an absolute count, so emitters overwrote each other instead.)
        private readonly Dictionary<CharacterBody, int> _contributed = new Dictionary<CharacterBody, int>();

        public HeroicAuraController Initialize(HeroicAura item)
        {
            _item = item;
            return this;
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy() => ClearContributions();

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null || _item == null) return;
            if (HeroicAura.AuraBuffDef == null || HeroicAura.AuraBuffDef.buffIndex == BuffIndex.None) return;

            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;
            _timer = HeroicAura.PulseInterval;

            int itemCount = GetEmitterStrength();
            if (itemCount <= 0) { ClearContributions(); return; }

            PulseAura(itemCount);
        }

        // A minion that inherits the item (e.g. an Engineer turret) emits at its OWNER's aura
        // strength, not its own. Engineer turrets inherit item PRESENCE but not the full stack
        // count, so reading the minion's own count under-emits (1 instead of the player's 10).
        // Routing through the owner makes every viable minion duplicate the player's full aura.
        // Non-minions (the player) and minions whose owner is gone fall back to their own count.
        private int GetEmitterStrength()
        {
            if (_item.ItemDef == null) return 0;
            var ownerInventory  = _body.master?.minionOwnership?.ownerMaster?.inventory;
            var sourceInventory = ownerInventory ?? _body.inventory;
            return sourceInventory?.GetItemCountEffective(_item.ItemDef) ?? 0;
        }

        private void PulseAura(int itemCount)
        {
            if (_body.teamComponent == null) return;
            var idx = HeroicAura.AuraBuffDef.buffIndex;

            // Bodies (self + allies) currently inside the aura.
            var inRange = new HashSet<CharacterBody> { _body };

            var search = new SphereSearch();
            search.origin = _body.transform.position;
            search.radius = HeroicAura.AuraRange * MysticExpansion.RadiusMultiplier(_body);
            search.mask   = LayerIndex.entityPrecise.mask;

            var allyMask = TeamMask.none;
            allyMask.AddTeam(_body.teamComponent.teamIndex);

            var hurtBoxes = new List<HurtBox>();
            search
                .RefreshCandidates()
                .FilterCandidatesByHurtBoxTeam(allyMask)
                .OrderCandidatesByDistance()
                .FilterCandidatesByDistinctHurtBoxEntities()
                .GetHurtBoxes(hurtBoxes);

            foreach (var hb in hurtBoxes)
            {
                var b = hb?.healthComponent?.body;
                if (b != null) inRange.Add(b);
            }

            // Apply the change in OUR contribution to every body we touched last pulse or this one.
            var touched = new HashSet<CharacterBody>(_contributed.Keys);
            touched.UnionWith(inRange);

            foreach (var body in touched)
            {
                if (body == null) continue;
                int oldC = _contributed.TryGetValue(body, out var c) ? c : 0;
                int newC = inRange.Contains(body) ? itemCount : 0;
                ApplyDelta(body, idx, newC - oldC);
            }

            _contributed.Clear();
            foreach (var body in inRange)
                if (body != null) _contributed[body] = itemCount;
        }

        // Remove every stack this emitter contributed (item lost, or controller/body destroyed).
        private void ClearContributions()
        {
            if (HeroicAura.AuraBuffDef == null || HeroicAura.AuraBuffDef.buffIndex == BuffIndex.None) return;
            var idx = HeroicAura.AuraBuffDef.buffIndex;
            foreach (var kv in _contributed)
                ApplyDelta(kv.Key, idx, -kv.Value);
            _contributed.Clear();
        }

        private static void ApplyDelta(CharacterBody body, BuffIndex idx, int delta)
        {
            if (body == null || delta == 0) return;
            if (delta > 0) for (int i = 0; i < delta;  i++) body.AddBuff(idx);
            else           for (int i = 0; i < -delta; i++) body.RemoveBuff(idx);
            body.RecalculateStats(); // attack-speed bonus is read in GetStatCoefficients
        }
    }
}
