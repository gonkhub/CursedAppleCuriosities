using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class HealingTempo : ItemBase
    {
        public override string ItemName => "Healing Tempo";
        public override string ItemLangTokenName => "HEALINGTEMPO";

        public override string ItemPickupDesc => "Allies healed near you gain speed and receive stronger healing.";
        public override string ItemFullDescription =>
            $"Emit a <style=cIsUtility>{AuraRange}m</style> aura. " +
            $"Allies inside the aura that receive <style=cIsHealing>active healing</style> gain " +
            $"<style=cIsDamage>+{AttackSpeedPerStack * 100:0}% attack speed</style> and " +
            $"<style=cIsUtility>+{MoveSpeedPerStack * 100:0}% movement speed</style> " +
            $"<style=cStack>per stack</style> for <style=cIsUtility>{BaseDuration}s</style> " +
            $"<style=cStack>(+{DurationPerStack}s per stack)</style>. " +
            $"Healing they receive inside the aura is also increased by " +
            $"<style=cIsHealing>+{HealPotencyFlat * 100:0}%</style>.";

        public override string ItemLore =>
            "Stay two steps ahead of death my friend.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_healingtempo.png";

        public const float AuraRange          = 30f;
        public static float HealPotencyFlat;
        public static float AttackSpeedPerStack;
        public static float MoveSpeedPerStack;
        public const float BaseDuration        = 8f;
        public const float DurationPerStack    = 6f;

        public static BuffDef? TempoBuffDef;
        public static ItemDef? StaticItemDef;

        public override void Init(ConfigFile config)
        {
            var color              = new Color(0.35f, 1f, 0.55f);
            TempoBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            TempoBuffDef.name      = "CursedApple_HealingTempo";
            TempoBuffDef.canStack  = false;
            TempoBuffDef.isDebuff  = false;
            TempoBuffDef.buffColor = color;
            TempoBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(TempoBuffDef);

            HealPotencyFlat = config.Bind("Red", "Healing Tempo - Bonus Heal Potency", 0.25f,
                "Extra healing received inside the aura.").Value;
            AttackSpeedPerStack = config.Bind("Red", "Healing Tempo - Attack Speed Per Stack", 0.75f,
                "Attack speed granted to healed allies, per stack.").Value;
            MoveSpeedPerStack = config.Bind("Red", "Healing Tempo - Move Speed Per Stack", 0.75f,
                "Move speed granted to healed allies, per stack.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.Heal            += HealthComponent_Heal;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        private float HealthComponent_Heal(
            On.RoR2.HealthComponent.orig_Heal orig,
            HealthComponent self,
            float amount,
            ProcChainMask procChainMask,
            bool nonRegen)
        {
            int htCount = 0;

            if (NetworkServer.active && nonRegen && self.body != null)
            {
                var teamIndex = self.body.teamComponent?.teamIndex ?? TeamIndex.None;
                htCount = FindCountInRange(self.body, teamIndex);

                if (htCount > 0)
                    amount *= 1f + HealPotencyFlat;
            }

            float result = orig(self, amount, procChainMask, nonRegen);

            if (result > 0f && htCount > 0 && self.body != null)
            {
                float duration = BaseDuration + DurationPerStack * (htCount - 1);
                var effect = self.body.GetComponent<HealingTempoEffect>()
                    ?? self.body.gameObject.AddComponent<HealingTempoEffect>();
                effect.Apply(htCount, duration);
            }

            return result;
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            var effect = sender.GetComponent<HealingTempoEffect>();
            if (effect == null || effect.Tier <= 0) return;

            args.attackSpeedMultAdd += AttackSpeedPerStack * effect.Tier;
            args.moveSpeedMultAdd   += MoveSpeedPerStack   * effect.Tier;
        }

        // Highest HT stack count among team members who are within AuraRange of the healed body.
        private static int FindCountInRange(CharacterBody healedBody, TeamIndex teamIndex)
        {
            if (teamIndex == TeamIndex.None || StaticItemDef == null || healedBody == null) return 0;

            int max = 0;
            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                var body = pcmc?.master?.GetBody();
                if (body?.inventory == null) continue;
                if (body.teamComponent?.teamIndex != teamIndex) continue;

                int c = body.inventory.GetItemCountEffective(StaticItemDef);
                if (c <= 0) continue;

                if (Vector3.Distance(healedBody.transform.position, body.transform.position) <= AuraRange)
                    if (c > max) max = c;
            }
            return max;
        }
    }

    // Lives on a buffed ally. Holds the magnitude tier and its own timer so repeated
    // heals refresh rather than stack endlessly.
    public class HealingTempoEffect : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timer;

        public int Tier { get; private set; }

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Apply(int tier, float duration)
        {
            Tier   = Mathf.Max(Tier, tier);
            _timer = Mathf.Max(_timer, duration);
            ShowBuff(true);
            _body?.RecalculateStats();
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Tier = 0;
                ShowBuff(false);
                _body?.RecalculateStats();
            }
        }

        private void OnDestroy() => ShowBuff(false);

        private void ShowBuff(bool on)
        {
            if (!NetworkServer.active || _body == null) return;
            if (HealingTempo.TempoBuffDef == null || HealingTempo.TempoBuffDef.buffIndex == BuffIndex.None) return;
            var idx = HealingTempo.TempoBuffDef.buffIndex;
            if (on && !_body.HasBuff(idx)) _body.AddBuff(idx);
            else if (!on && _body.HasBuff(idx)) _body.RemoveBuff(idx);
        }
    }
}
