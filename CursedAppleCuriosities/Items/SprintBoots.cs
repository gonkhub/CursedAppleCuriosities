using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class SprintBoots : ItemBase
    {
        public override string ItemName => "Sprint Boots";
        public override string ItemLangTokenName => "SPRINTBOOTS";

        public override string ItemPickupDesc => "Build up movement speed while sprinting.";
        public override string ItemFullDescription =>
            $"While <style=cIsUtility>sprinting</style>, build up to " +
            $"<style=cIsUtility>+{BaseMaxBonus * 100:0}% movement speed</style> " +
            $"<style=cStack>(+{MaxBonusPerStack * 100:0}% per stack)</style> over " +
            $"<style=cIsUtility>{MaxRampTime:0}s</style>. The bonus resets when you stop sprinting.";

        public override string ItemLore =>
            "Look who's ready for a 2 AM metro ride!";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_sprintboots.png";

        public const float MaxRampTime     = 6f;
        public static float BaseMaxBonus;
        public const float MaxBonusPerStack = 0.12f;

        public static ItemDef?  StaticItemDef;
        public static BuffDef?  SprintBuffDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(0.4f, 0.9f, 1f);
            SprintBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            SprintBuffDef.name      = "CursedApple_SprintBoots";
            SprintBuffDef.canStack  = false;
            SprintBuffDef.isDebuff  = false;
            SprintBuffDef.buffColor = color;
            SprintBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(SprintBuffDef);

            BaseMaxBonus = config.Bind("White", "Sprint Boots - Max Move Speed", 0.25f,
                "Maximum sprint move-speed bonus at 1 stack.").Value;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += CharacterBody_OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<SprintBootsTracker>();

            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<SprintBootsTracker>().Initialize(ItemDef);
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            int count = sender.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var tracker = sender.GetComponent<SprintBootsTracker>();
            if (tracker == null || tracker.RampRatio <= 0f) return;

            float maxBonus = BaseMaxBonus + MaxBonusPerStack * (count - 1);
            args.moveSpeedMultAdd += maxBonus * tracker.RampRatio;
        }
    }

    public class SprintBootsTracker : MonoBehaviour
    {
        private ItemDef       _itemDef;
        private CharacterBody _body;
        private float         _ramp;       // 0 .. MaxRampTime
        private float         _lastRatio;
        private bool          _buffActive;

        private const float RecalcEpsilon = 0.04f;

        public float RampRatio => Mathf.Clamp01(_ramp / SprintBoots.MaxRampTime);

        public SprintBootsTracker Initialize(ItemDef itemDef)
        {
            _itemDef = itemDef;
            return this;
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy() => SyncBuff(false);

        private void FixedUpdate()
        {
            if (_body == null || _itemDef == null) return;

            if (_body.isSprinting)
                _ramp = Mathf.Min(SprintBoots.MaxRampTime, _ramp + Time.fixedDeltaTime);
            else
                _ramp = 0f; // the bonus is lost the moment you stop sprinting

            float ratio = RampRatio;
            // Re-apply stats when the ramp shifts meaningfully (or hits 0) so move speed tracks it.
            if (Mathf.Abs(ratio - _lastRatio) > RecalcEpsilon || (ratio <= 0f && _lastRatio > 0f))
            {
                _lastRatio = ratio;
                SyncBuff(ratio > 0f);
                _body.RecalculateStats();
            }
        }

        private void SyncBuff(bool active)
        {
            if (!NetworkServer.active || _body == null) return;
            if (SprintBoots.SprintBuffDef == null || SprintBoots.SprintBuffDef.buffIndex == BuffIndex.None) return;
            if (active == _buffActive) return;

            _buffActive = active;
            var idx = SprintBoots.SprintBuffDef.buffIndex;
            if (active && !_body.HasBuff(idx)) _body.AddBuff(idx);
            else if (!active && _body.HasBuff(idx)) _body.RemoveBuff(idx);
        }
    }
}
