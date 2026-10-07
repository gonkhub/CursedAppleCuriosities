using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Berserker : ItemBase
    {
        public override string ItemName => "Berserker";
        public override string ItemLangTokenName => "BERSERKER";

        public override string ItemFullDescription =>
            $"Each time you take damage (once per <style=cIsUtility>{StackCooldown}s</style>), gain a " +
            $"<style=cIsDamage>Rage</style> stack granting <style=cIsDamage>+{DamagePerStack * 100:0}% damage</style> each. " +
            $"Max <style=cIsUtility>{BaseMaxStacks} Rage stacks</style> " +
            $"<style=cStack>(+{MaxStacksPerItemStack} max per stack)</style>. " +
            $"Stacks decay after <style=cIsUtility>{DecayDelay:0}s</style> without taking damage.";

        public override string ItemLore =>
            "Your enemies will curse your name!";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_berserker.png";

        public static float DamagePerStack;
        public const int   BaseMaxStacks         = 10;
        public const int   MaxStacksPerItemStack = 5;
        public const float StackCooldown         = 1f;
        public const float DecayDelay            = 5f;
        public const float DecayRate             = 1f;

        public static BuffDef? RageBuffDef;

        public override void Init(ConfigFile config)
        {
            var color            = new Color(0.9f, 0.15f, 0.1f);
            RageBuffDef          = ScriptableObject.CreateInstance<BuffDef>();
            RageBuffDef.name     = "CursedApple_BerserkerRage";
            RageBuffDef.canStack = true;
            RageBuffDef.isDebuff = false;
            RageBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(RageBuffDef);

            DamagePerStack = config.Bind("Green", "Berserker - Damage Per Rage Stack", 0.08f,
                "Bonus damage per Rage stack.").Value;
            CreateLang();
            CreateItem();
            RageBuffDef.iconSprite = ItemDef?.pickupIconSprite ?? BuffIcon(null, color);
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage      += HealthComponent_TakeDamage;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            if (sender.inventory.GetItemCountEffective(ItemDef) <= 0) return;

            var tracker = sender.GetComponent<BerserkerTracker>();
            if (tracker == null) return;

            args.damageMultAdd += tracker.CurrentStacks * DamagePerStack;
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            orig(self, damageInfo);

            if (!NetworkServer.active) return;
            if (damageInfo == null || damageInfo.damage <= 0f) return;
            if (self.body?.inventory == null) return;

            int itemCount = self.body.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            var tracker = self.body.GetComponent<BerserkerTracker>()
                ?? self.body.gameObject.AddComponent<BerserkerTracker>();

            int maxStacks = BaseMaxStacks + (itemCount - 1) * MaxStacksPerItemStack;
            tracker.TryAddStack(maxStacks);
        }
    }

    public class BerserkerTracker : MonoBehaviour
    {
        private int           _stacks;
        private float         _stackCooldown;
        private float         _decayClock;
        private float         _decayAccum;
        private CharacterBody _body;

        public int CurrentStacks => _stacks;

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
        }

        private void OnDestroy()
        {
            SyncBuff(-_stacks);
        }

        public void TryAddStack(int maxStacks)
        {
            if (_stackCooldown > 0f) return;

            _decayClock = Berserker.DecayDelay;
            _decayAccum = 0f;

            if (_stacks >= maxStacks) return;

            _stacks++;
            _stackCooldown = Berserker.StackCooldown;
            SyncBuff(1);
            _body?.RecalculateStats();
        }

        private void Update()
        {
            if (_stackCooldown > 0f)
                _stackCooldown -= Time.deltaTime;

            if (_stacks <= 0) return;

            _decayClock -= Time.deltaTime;
            if (_decayClock > 0f) return;

            _decayAccum += Time.deltaTime * Berserker.DecayRate;
            while (_decayAccum >= 1f && _stacks > 0)
            {
                _decayAccum -= 1f;
                _stacks--;
                SyncBuff(-1);
                _body?.RecalculateStats();
            }

            if (_stacks == 0)
                _decayAccum = 0f;
        }

        private void SyncBuff(int delta)
        {
            if (!NetworkServer.active || _body == null) return;
            if (Berserker.RageBuffDef == null || Berserker.RageBuffDef.buffIndex == BuffIndex.None) return;

            if (delta > 0)
                for (int i = 0; i < delta; i++) _body.AddBuff(Berserker.RageBuffDef.buffIndex);
            else
                for (int i = 0; i < -delta; i++) _body.RemoveBuff(Berserker.RageBuffDef.buffIndex);
        }
    }
}
