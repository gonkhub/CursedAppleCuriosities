using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Spellslinger : ItemBase
    {
        public override string ItemName => "Spellslinger";
        public override string ItemLangTokenName => "SPELLSLINGER";

        public override string ItemPickupDesc => "Using abilities builds attack speed.";
        public override string ItemFullDescription =>
            $"Using a <style=cIsUtility>secondary, utility, or special ability</style> grants a " +
            $"<style=cIsDamage>Spellslinger</style> stack, adding " +
            $"<style=cIsDamage>+{AttackSpeedPerStack * 100:0}% attack speed</style> per Spellslinger stack. " +
            $"Max <style=cIsUtility>{BaseMaxStacks} stacks</style> " +
            $"<style=cStack>(+{StacksPerItemStack} per stack)</style>. " +
            $"Stacks decay {DecayRate}/s after {DecayDelay}s without ability use.";

        public override string ItemLore =>
            "I got one of those for the missus. Big fan!";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_spellslinger.png";

        public const int   BaseMaxStacks      = 6;
        public const int   StacksPerItemStack  = 6;
        public static float AttackSpeedPerStack;
        public const float DecayDelay          = 3f;
        public const float DecayRate           = 1f;

        public static BuffDef? SpellslingerBuffDef;

        public override void Init(ConfigFile config)
        {
            var color                    = new Color(0.5f, 0.2f, 1f);
            SpellslingerBuffDef          = ScriptableObject.CreateInstance<BuffDef>();
            SpellslingerBuffDef.name     = "CursedApple_Spellslinger";
            SpellslingerBuffDef.canStack = true;
            SpellslingerBuffDef.isDebuff = false;
            SpellslingerBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(SpellslingerBuffDef);

            AttackSpeedPerStack = config.Bind("Red", "Spellslinger - Attack Speed Per Stack", 0.07f,
                "Attack speed per Spellslinger stack (gained on non-primary skill use).").Value;
            CreateLang();
            CreateItem();
            SpellslingerBuffDef.iconSprite = ItemDef?.pickupIconSprite ?? BuffIcon(null, color);
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnSkillActivated  += OnSkillActivated;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        private void OnSkillActivated(
            On.RoR2.CharacterBody.orig_OnSkillActivated orig,
            CharacterBody self,
            GenericSkill skill)
        {
            orig(self, skill);

            if (!NetworkServer.active) return;
            if (self.inventory == null) return;

            int itemCount = self.inventory.GetItemCountEffective(ItemDef);
            if (itemCount <= 0) return;

            var loc = self.skillLocator;
            if (loc == null || skill == loc.primary) return;

            var tracker = self.GetComponent<SpelllsingerTracker>()
                ?? self.gameObject.AddComponent<SpelllsingerTracker>();

            int maxStacks = BaseMaxStacks + (itemCount - 1) * StacksPerItemStack;
            tracker.AddStack(maxStacks);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            if (sender.inventory.GetItemCountEffective(ItemDef) <= 0) return;

            var tracker = sender.GetComponent<SpelllsingerTracker>();
            if (tracker == null) return;

            args.attackSpeedMultAdd += tracker.CurrentStacks * AttackSpeedPerStack;
        }
    }

    public class SpelllsingerTracker : MonoBehaviour
    {
        private int           _stacks;
        private float         _decayClock;
        private float         _decayAccum;
        private CharacterBody _body;

        public int CurrentStacks => _stacks;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy() => SyncBuff(0);

        public void AddStack(int maxStacks)
        {
            _decayClock = Spellslinger.DecayDelay;
            _decayAccum = 0f;

            if (_stacks >= maxStacks) return;

            _stacks++;
            SyncBuff(_stacks);
            _body?.RecalculateStats();
        }

        private void Update()
        {
            if (_stacks <= 0) return;

            _decayClock -= Time.deltaTime;
            if (_decayClock > 0f) return;

            _decayAccum += Time.deltaTime * Spellslinger.DecayRate;
            while (_decayAccum >= 1f && _stacks > 0)
            {
                _decayAccum -= 1f;
                _stacks--;
                _body?.RecalculateStats();
            }

            SyncBuff(_stacks);

            if (_stacks == 0)
                _decayAccum = 0f;
        }

        private void SyncBuff(int desired)
        {
            if (!NetworkServer.active || _body == null) return;
            if (Spellslinger.SpellslingerBuffDef == null || Spellslinger.SpellslingerBuffDef.buffIndex == BuffIndex.None) return;
            var idx     = Spellslinger.SpellslingerBuffDef.buffIndex;
            int current = _body.GetBuffCount(idx);
            while (current > desired) { _body.RemoveBuff(idx); current--; }
            while (current < desired) { _body.AddBuff(idx);    current++; }
        }
    }
}
