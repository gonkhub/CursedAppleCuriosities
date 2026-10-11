using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class HuntersAura : ItemBase
    {
        public override string ItemName => "Hunter's Aura";
        public override string ItemLangTokenName => "HUNTERSAURA";

        public override string ItemPickupDesc => "Nearby enemies take more damage, especially when isolated.";
        public override string ItemFullDescription =>
            $"Emit a fixed <style=cIsUtility>{BaseRange}m</style> aura that amplifies the " +
            $"<style=cIsDamage>incoming damage</style> of nearby enemies by " +
            $"<style=cIsDamage>+{DamageAmplifyPerStack * 100:0}% per stack</style>. " +
            $"If the target is <style=cIsDamage>isolated</style> — the only enemy in range — " +
            $"the bonus is <style=cIsDamage>tripled to +{DamageAmplifyPerStack * FocusedStacks * 100:0}% per stack</style>.";

        public override string ItemLore =>
            "Now, I don't go sellin' these to just anyone.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_huntersaura.png";

        public const float BaseRange             = 15f;
        public const float PulseInterval        = 0.5f;
        public static float DamageAmplifyPerStack;
        public const int   NormalStacks         = 1;
        public const int   FocusedStacks        = 3;

        public static BuffDef? HuntersDebuffDef;

        public override void Init(ConfigFile config)
        {
            var color                   = new Color(0.2f, 0.85f, 0.3f);
            HuntersDebuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            HuntersDebuffDef.name       = "CursedApple_HuntersAura";
            HuntersDebuffDef.canStack   = true;
            HuntersDebuffDef.isDebuff   = true;
            HuntersDebuffDef.buffColor  = color;
            HuntersDebuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(HuntersDebuffDef);

            DamageAmplifyPerStack = config.Bind("Green", "Hunters Aura - Damage Amp Per Stack", 0.08f,
                "Incoming-damage amplification on enemies in the aura, per stack (tripled when isolated).").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += CharacterBody_OnInventoryChanged;
            On.RoR2.HealthComponent.TakeDamage       += HealthComponent_TakeDamage;
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count = self.inventory.GetItemCountEffective(ItemDef);
            var ctrl  = self.GetComponent<HuntersAuraController>();

            // Minions (e.g. Engineer turrets) inherit the item but must NOT emit the aura —
            // multiple emitters would fight over each enemy's debuff stack count and flicker it.
            bool isMinion   = self.master?.minionOwnership?.ownerMaster != null;
            bool shouldEmit = count > 0 && !isMinion;

            if (shouldEmit && ctrl == null)
                self.gameObject.AddComponent<HuntersAuraController>().Initialize(this);
            else if (!shouldEmit && ctrl != null)
                UnityEngine.Object.Destroy(ctrl);
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active
                && damageInfo != null
                && self.body != null
                && HuntersDebuffDef != null
                && HuntersDebuffDef.buffIndex != BuffIndex.None)
            {
                int stacks = self.body.GetBuffCount(HuntersDebuffDef.buffIndex);
                if (stacks > 0)
                    damageInfo.damage *= 1f + DamageAmplifyPerStack * stacks;
            }

            orig(self, damageInfo);
        }
    }

    public class HuntersAuraController : MonoBehaviour
    {
        private float         _timer;
        private CharacterBody _body;
        private HuntersAura   _item;

        private readonly HashSet<CharacterBody> _debuffedBodies = new HashSet<CharacterBody>();

        public HuntersAuraController Initialize(HuntersAura item)
        {
            _item = item;
            return this;
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy()
        {
            foreach (var body in _debuffedBodies)
                if (body != null) SyncDebuff(body, 0);
            _debuffedBodies.Clear();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null || _item == null) return;
            if (HuntersAura.HuntersDebuffDef == null || HuntersAura.HuntersDebuffDef.buffIndex == BuffIndex.None) return;

            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;
            _timer = HuntersAura.PulseInterval;

            int itemCount = _body.inventory?.GetItemCountEffective(_item.ItemDef) ?? 0;
            if (itemCount <= 0) return;

            PulseAura(itemCount);
        }

        private void PulseAura(int itemCount)
        {
            if (_body.teamComponent == null) return;

            float range = HuntersAura.BaseRange;

            var search = new SphereSearch();
            search.origin = _body.transform.position;
            search.radius = range;
            search.mask   = LayerIndex.entityPrecise.mask;

            var hurtBoxes = new List<HurtBox>();
            search
                .RefreshCandidates()
                .FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(_body.teamComponent.teamIndex))
                .OrderCandidatesByDistance()
                .FilterCandidatesByDistinctHurtBoxEntities()
                .GetHurtBoxes(hurtBoxes);

            var inRange = new HashSet<CharacterBody>();
            foreach (var hb in hurtBoxes)
            {
                if (hb?.healthComponent?.body == null) continue;
                inRange.Add(hb.healthComponent.body);
            }

            int desiredStacks = (inRange.Count == 1
                ? HuntersAura.FocusedStacks
                : HuntersAura.NormalStacks) * itemCount;

            foreach (var body in inRange)
                SyncDebuff(body, desiredStacks);

            foreach (var body in _debuffedBodies)
                if (!inRange.Contains(body) && body != null)
                    SyncDebuff(body, 0);

            _debuffedBodies.Clear();
            foreach (var body in inRange)
                _debuffedBodies.Add(body);
        }

        private static void SyncDebuff(CharacterBody body, int desired)
        {
            if (HuntersAura.HuntersDebuffDef == null || HuntersAura.HuntersDebuffDef.buffIndex == BuffIndex.None) return;
            var idx     = HuntersAura.HuntersDebuffDef.buffIndex;
            int current = body.GetBuffCount(idx);
            while (current > desired) { body.RemoveBuff(idx); current--; }
            while (current < desired) { body.AddBuff(idx);    current++; }
        }
    }
}
