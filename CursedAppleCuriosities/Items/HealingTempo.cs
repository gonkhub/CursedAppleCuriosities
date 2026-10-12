using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    // Deadlock's Healing Tempo (upgrade_healbuff): heal amp, and heals you give speed the target up.
    // RoR2 healing is mostly self-proc spam, so here the speed comes from OVERHEAL instead: healing that
    // lands while you're already full fills a Tempo meter instead of being wasted.
    public class HealingTempo : ItemBase
    {
        public override string ItemName => "Healing Tempo";
        public override string ItemLangTokenName => "HEALINGTEMPO";

        public override string ItemPickupDesc => "Increase healing. Overhealing builds Tempo, granting attack and movement speed.";
        public override string ItemFullDescription =>
            $"Increase all <style=cIsHealing>healing</style> received by <style=cIsHealing>{HealPotencyFlat * 100:0}%</style> " +
            $"<style=cStack>(+{HealPotencyPerStack * 100:0}% per stack)</style>. " +
            $"Every <style=cIsHealing>{OverhealPerTempo * 100:0}%</style> of your maximum health in <style=cIsHealing>overhealing</style> " +
            $"grants a <style=cIsUtility>Tempo</style> stack: " +
            $"<style=cIsDamage>+{AttackSpeedPerTempo * 100:0}% attack speed</style> and " +
            $"<style=cIsUtility>+{MoveSpeedPerTempo * 100:0}% movement speed</style> each. " +
            $"Max <style=cIsUtility>{BaseMaxTempo}</style> <style=cStack>(+{MaxTempoPerStack} per stack)</style>. " +
            $"Stacks decay after <style=cIsUtility>{DecayDelay}s</style> without overhealing.";

        public override string ItemLore =>
            "Stay two steps ahead of death my friend.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_healingtempo.png";

        public static float HealPotencyFlat;
        public static float HealPotencyPerStack;
        public static float OverhealPerTempo;     // fraction of max health per Tempo stack
        public static float AttackSpeedPerTempo;
        public static float MoveSpeedPerTempo;
        public static int   BaseMaxTempo;
        public const  int   MaxTempoPerStack = 2;
        public const  float DecayDelay       = 7f; // Deadlock's BuffDuration
        public const  float DecayRate        = 1f; // stacks per second once decaying

        public static BuffDef? TempoBuffDef;
        public static ItemDef? StaticItemDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(0.35f, 1f, 0.55f);
            TempoBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            TempoBuffDef.name       = "CursedApple_HealingTempo";
            TempoBuffDef.canStack   = true;
            TempoBuffDef.isDebuff   = false;
            TempoBuffDef.buffColor  = color;
            TempoBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(TempoBuffDef);

            HealPotencyFlat = config.Bind("Red", "Healing Tempo - Bonus Heal Potency", 0.25f,
                "Extra healing received with one stack.").Value;
            HealPotencyPerStack = config.Bind("Red", "Healing Tempo - Bonus Heal Potency Per Stack", 0.10f,
                "Extra healing received per additional stack.").Value;
            OverhealPerTempo = config.Bind("Red", "Healing Tempo - Overheal Per Tempo", 0.10f,
                "Overhealing (as a fraction of max health) needed for one Tempo stack.").Value;
            AttackSpeedPerTempo = config.Bind("Red", "Healing Tempo - Attack Speed Per Tempo", 0.10f,
                "Attack speed per Tempo stack.").Value;
            MoveSpeedPerTempo = config.Bind("Red", "Healing Tempo - Move Speed Per Tempo", 0.06f,
                "Movement speed per Tempo stack.").Value;
            BaseMaxTempo = config.Bind("Red", "Healing Tempo - Max Tempo", 4,
                "Max Tempo stacks with one item (+2 per additional stack).").Value;

            EquipmentFx.RegisterSound("healingtempo_proc", "sounds/mods/armor/healing_tempo/healing_tempo_proc", volume: 0.6f, range: 30f, minGap: 0.4f);
            EquipmentFx.RegisterSound("healingtempo_end", "sounds/mods/armor/healing_tempo/healing_tempo_end", volume: 0.5f, range: 30f);
            EquipmentFx.RegisterBuffFx(TempoBuffDef, "RoR2/Base/EnergizedOnEquipmentUse/EnergizedEffect.prefab");

            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.Heal               += HealthComponent_Heal;
            HealthComponent.onCharacterOverhealServer += OnOverheal;
            RecalculateStatsAPI.GetStatCoefficients    += GetStatCoefficients;
        }

        private static int Count(CharacterBody? body)
            => body != null && body.inventory != null && StaticItemDef != null
                ? body.inventory.GetItemCountEffective(StaticItemDef) : 0;

        // Heal amp on everything, regen included (Deadlock amps both).
        private float HealthComponent_Heal(
            On.RoR2.HealthComponent.orig_Heal orig,
            HealthComponent self,
            float amount,
            ProcChainMask procChainMask,
            bool nonRegen)
        {
            int count = Count(self.body);
            if (count > 0)
                amount *= 1f + HealPotencyFlat + HealPotencyPerStack * (count - 1);
            return orig(self, amount, procChainMask, nonRegen);
        }

        // The game's own overheal figure (the same one Aegis turns into barrier). Regen doesn't count,
        // or standing still at full health would keep Tempo up forever.
        private static void OnOverheal(HealthComponent hc, float amount, float overheal, ProcChainMask procChainMask, bool isRegen)
        {
            if (isRegen || overheal <= 0f || hc == null) return;
            var body = hc.body;
            int count = Count(body);
            if (count <= 0 || hc.fullHealth <= 0f) return;

            var tracker = body!.GetComponent<HealingTempoTracker>()
                ?? body.gameObject.AddComponent<HealingTempoTracker>();
            tracker.AddOverheal(overheal / hc.fullHealth, BaseMaxTempo + MaxTempoPerStack * (count - 1));
        }

        // Reads the synced buff count, so the owning client (which runs its own movement and skills) gets it too.
        private static void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (TempoBuffDef == null || TempoBuffDef.buffIndex == BuffIndex.None) return;
            int tempo = sender.GetBuffCount(TempoBuffDef);
            if (tempo <= 0) return;

            args.attackSpeedMultAdd += AttackSpeedPerTempo * tempo;
            args.moveSpeedMultAdd   += MoveSpeedPerTempo   * tempo;
        }
    }

    // Server only: the overheal meter, the Tempo stack count and its decay. Stacks are mirrored to the buff.
    public class HealingTempoTracker : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private float _meter;      // overheal banked toward the next stack, in fractions of max health
        private int   _stacks;
        private float _decayClock;
        private float _decayAccum;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy() => SyncBuff(0);

        public void AddOverheal(float fraction, int maxStacks)
        {
            if (HealingTempo.OverhealPerTempo <= 0f) return;
            _decayClock = HealingTempo.DecayDelay;
            _decayAccum = 0f;
            if (_stacks >= maxStacks) { _meter = 0f; return; } // full: refresh only, don't bank

            _meter += fraction;
            int gained = 0;
            while (_meter >= HealingTempo.OverhealPerTempo && _stacks < maxStacks)
            {
                _meter -= HealingTempo.OverhealPerTempo;
                _stacks++;
                gained++;
            }
            if (_stacks >= maxStacks) _meter = 0f;
            if (gained == 0) return;

            SyncBuff(_stacks);
            EquipmentFx.Sound("healingtempo_proc", _body.corePosition, _body.gameObject);
        }

        private void Update()
        {
            if (_stacks <= 0) return;

            _decayClock -= Time.deltaTime;
            if (_decayClock > 0f) return;

            _decayAccum += Time.deltaTime * HealingTempo.DecayRate;
            while (_decayAccum >= 1f && _stacks > 0)
            {
                _decayAccum -= 1f;
                _stacks--;
            }
            SyncBuff(_stacks);

            if (_stacks == 0)
            {
                _decayAccum = 0f;
                _meter      = 0f;
                EquipmentFx.Sound("healingtempo_end", _body.corePosition, _body.gameObject);
            }
        }

        // AddBuff/RemoveBuff mark stats dirty, so no RecalculateStats call is needed here.
        private void SyncBuff(int desired)
        {
            if (!NetworkServer.active || _body == null) return;
            var buff = HealingTempo.TempoBuffDef;
            if (buff == null || buff.buffIndex == BuffIndex.None) return;
            var idx     = buff.buffIndex;
            int current = _body.GetBuffCount(idx);
            while (current > desired) { _body.RemoveBuff(idx); current--; }
            while (current < desired) { _body.AddBuff(idx);    current++; }
        }
    }
}
