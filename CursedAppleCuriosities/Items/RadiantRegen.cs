using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class RadiantRegen : ItemBase
    {
        public override string ItemName => "Radiant Regen";
        public override string ItemLangTokenName => "RADIANTREGEN";

        public override string ItemPickupDesc => "Using non-primary skills heals you. Their hits build health regeneration.";
        public override string ItemFullDescription =>
            $"Using a <style=cIsUtility>secondary, utility or special skill</style> heals you for " +
            $"<style=cIsHealing>{HealFraction * 100:0.#}% of your maximum health</style> " +
            $"<style=cIsUtility>({BurstCooldown:0}s cooldown)</style>. " +
            $"Their hits on <style=cIsDamage>unique enemies</style> grant a stack of " +
            $"<style=cIsHealing>Radiance</style> " +
            $"(max <style=cIsUtility>{MaxStacksPerItem}</style>" +
            $"<style=cStack> +{MaxStacksPerItem} per stack</style>), " +
            $"each regenerating <style=cIsHealing>{RegenFraction * 100:0.#}% of your maximum health per second</style>. " +
            $"Any of their hits refreshes the buff duration.";

        public override string ItemLore => "Take care of yourself. That's not a request.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing };
        protected override string IconFileName => "icon_radiantregen.png";

        public static float HealFraction;   // of max health, on a non-primary skill cast
        public const float BurstCooldown  = 5f;
        public static float RegenFraction;  // of max health per second, per Radiance stack
        public const float BuffDuration   = 8f;
        public const int   MaxStacksPerItem = 6;

        public static ItemDef? StaticItemDef;
        public static BuffDef? RadiantBuffDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(1f, 0.88f, 0.35f);
            RadiantBuffDef          = ScriptableObject.CreateInstance<BuffDef>();
            RadiantBuffDef.name     = "CursedApple_Radiance";
            RadiantBuffDef.canStack = true;
            RadiantBuffDef.isDebuff = false;
            RadiantBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(RadiantBuffDef);

            // New keys in 1.8: the old ones were flat HP (20 and 2), which would read as 2000% here.
            HealFraction = 0.05f;
            RegenFraction = 0.01f;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            RadiantBuffDef.iconSprite = ItemDef?.pickupIconSprite ?? BuffIcon(null, color);
            Hooks();
        }

        public override void Hooks()
        {
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            On.RoR2.CharacterBody.OnSkillActivated   += OnSkillActivated;
            On.RoR2.GlobalEventManager.OnHitEnemy    += OnHitEnemy;
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (RadiantBuffDef == null || sender.inventory == null) return;
            int buffs = sender.GetBuffCount(RadiantBuffDef.buffIndex);
            if (buffs <= 0) return;
            if (sender.inventory.GetItemCountEffective(ItemDef) <= 0) return;
            args.baseRegenAdd += buffs * RegenFraction * sender.maxHealth; // more copies raise the Radiance cap instead
        }

        private void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;
            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<RadiantRegenTracker>();
            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<RadiantRegenTracker>();
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void OnSkillActivated(
            On.RoR2.CharacterBody.orig_OnSkillActivated orig,
            CharacterBody self, GenericSkill skill)
        {
            orig(self, skill);
            if (!NetworkServer.active) return;
            if ((self.inventory?.GetItemCountEffective(ItemDef) ?? 0) <= 0) return;
            if (self.skillLocator != null && skill == self.skillLocator.primary) return; // non-primary skills only (user, 1.8)
            self.GetComponent<RadiantRegenTracker>()?.TriggerBurstHeal();
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return; // ignore passive/item ticks
            const DamageSource Skills = DamageSource.Secondary | DamageSource.Utility | DamageSource.Special;
            if (((DamageSource)damageInfo.damageType & Skills) == 0) return; // non-primary skill hits only (user, 1.8)

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;
            if (attackerBody.inventory.GetItemCountEffective(ItemDef) <= 0) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            attackerBody.GetComponent<RadiantRegenTracker>()?.OnSkillHit(victimHC);
        }
    }

    public class RadiantRegenTracker : MonoBehaviour
    {
        private CharacterBody _body;
        private float _burstCooldown;
        private float _buffTimer;
        private readonly HashSet<HealthComponent> _uniqueVictims = new HashSet<HealthComponent>();

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void TriggerBurstHeal()
        {
            if (_burstCooldown > 0f || _body?.healthComponent == null) return;
            if (RadiantRegen.StaticItemDef == null || _body.inventory == null) return;
            if (_body.inventory.GetItemCountEffective(RadiantRegen.StaticItemDef) <= 0) return;
            _body.healthComponent.Heal(_body.maxHealth * RadiantRegen.HealFraction, default);
            _burstCooldown = RadiantRegen.BurstCooldown;
        }

        public void OnSkillHit(HealthComponent victimHC)
        {
            if (victimHC == null || _body == null || RadiantRegen.RadiantBuffDef == null) return;
            _buffTimer = RadiantRegen.BuffDuration * DurationExtender.Multiplier(_body); // any skill hit refreshes duration
            if (_uniqueVictims.Add(victimHC))       // new unique enemy → try to grant a stack
            {
                int itemCount = _body.inventory?.GetItemCountEffective(RadiantRegen.StaticItemDef) ?? 0;
                int current   = _body.GetBuffCount(RadiantRegen.RadiantBuffDef.buffIndex);
                if (current < RadiantRegen.MaxStacksPerItem * itemCount)
                    _body.AddBuff(RadiantRegen.RadiantBuffDef);
            }
        }

        private void Update()
        {
            if (_burstCooldown > 0f)
                _burstCooldown = Mathf.Max(0f, _burstCooldown - Time.deltaTime);

            if (!NetworkServer.active || _uniqueVictims.Count == 0) return;

            _buffTimer -= Time.deltaTime;
            if (_buffTimer <= 0f)
                ExpireBuff();
        }

        private void ExpireBuff()
        {
            _buffTimer = 0f;
            _uniqueVictims.Clear();
            if (_body == null || RadiantRegen.RadiantBuffDef == null) return;
            int stacks = _body.GetBuffCount(RadiantRegen.RadiantBuffDef.buffIndex);
            for (int i = 0; i < stacks; i++)
                _body.RemoveBuff(RadiantRegen.RadiantBuffDef.buffIndex);
        }
    }
}
