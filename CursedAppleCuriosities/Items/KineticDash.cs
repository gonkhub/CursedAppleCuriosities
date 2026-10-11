using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class KineticDash : ItemBase
    {
        public override string ItemName => "Kinetic Dash";
        public override string ItemLangTokenName => "KINETICDASH";

        public override string ItemPickupDesc => "Using your utility skill increases attack speed.";
        public override string ItemFullDescription =>
            $"Using your <style=cIsUtility>utility skill</style> grants " +
            $"<style=cIsDamage>+{AttackSpeedPerStack * 100:0}% attack speed</style> " +
            $"<style=cStack>(+{AttackSpeedPerStack * 100:0}% per stack)</style> " +
            $"for <style=cIsUtility>{BuffDuration}s</style>.";

        public override string ItemLore =>
            "Exquisite, isn't it?";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_kineticdash.png";

        public static float AttackSpeedPerStack;
        public const float BuffDuration        = 6f;

        public static BuffDef? DashBuffDef;

        public override void Init(ConfigFile config)
        {
            var color             = new Color(0.3f, 0.8f, 1f);
            DashBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            DashBuffDef.name      = "CursedApple_KineticDash";
            DashBuffDef.canStack  = false;
            DashBuffDef.isDebuff  = false;
            DashBuffDef.buffColor = color;
            DashBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(DashBuffDef);

            AttackSpeedPerStack = config.Bind("White", "Kinetic Dash - Attack Speed Per Stack", 0.20f,
                "Attack speed granted per stack after using your utility skill.").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            On.RoR2.CharacterBody.OnSkillActivated   += OnSkillActivated;
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
        }

        private void OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<KineticDashTracker>();

            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<KineticDashTracker>();
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void OnSkillActivated(
            On.RoR2.CharacterBody.orig_OnSkillActivated orig,
            CharacterBody self,
            GenericSkill skill)
        {
            orig(self, skill);

            if (!NetworkServer.active) return;
            if (self.inventory == null) return;

            int count = self.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            var loc = self.skillLocator;
            if (loc == null || skill != loc.utility) return;

            var tracker = self.GetComponent<KineticDashTracker>()
                ?? self.gameObject.AddComponent<KineticDashTracker>();
            tracker.OnUtilityUsed(self);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            var tracker = sender.GetComponent<KineticDashTracker>();
            if (tracker == null || !tracker.IsActive) return;

            int count = sender.inventory?.GetItemCountEffective(ItemDef) ?? 0;
            if (count <= 0) return;

            args.attackSpeedMultAdd += AttackSpeedPerStack * count;
        }
    }

    public class KineticDashTracker : MonoBehaviour
    {
        private float _timer;

        public bool IsActive => _timer > 0f;

        public void OnUtilityUsed(CharacterBody body)
        {
            bool wasInactive = _timer <= 0f;
            _timer = KineticDash.BuffDuration;

            if (wasInactive)
            {
                if (KineticDash.DashBuffDef != null && KineticDash.DashBuffDef.buffIndex != BuffIndex.None)
                    body.AddBuff(KineticDash.DashBuffDef.buffIndex);
                body.RecalculateStats();
            }
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            var body = GetComponent<CharacterBody>();
            if (body == null) return;

            if (KineticDash.DashBuffDef != null && KineticDash.DashBuffDef.buffIndex != BuffIndex.None
                && body.HasBuff(KineticDash.DashBuffDef.buffIndex))
                body.RemoveBuff(KineticDash.DashBuffDef.buffIndex);

            body.RecalculateStats();
        }

        private void OnDestroy()
        {
            if (_timer <= 0f) return;
            var body = GetComponent<CharacterBody>();
            if (body == null || KineticDash.DashBuffDef == null
                || KineticDash.DashBuffDef.buffIndex == BuffIndex.None) return;
            if (body.HasBuff(KineticDash.DashBuffDef.buffIndex))
                body.RemoveBuff(KineticDash.DashBuffDef.buffIndex);
        }
    }
}
