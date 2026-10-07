using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Spellbreaker : ItemBase
    {
        public override string ItemName => "Spellbreaker";
        public override string ItemLangTokenName => "SPELLBREAKER";

        public override string ItemFullDescription =>
            $"Taking a hit that deals <style=cIsHealth>{HealthFraction * 100:0}%+ of your current HP</style> " +
            $"reduces that hit by <style=cIsHealth>{DamageReduction * 100:0}%</style>. " +
            $"Cooldown: <style=cIsUtility>{BaseCooldown}s</style> " +
            $"<style=cStack>(-50% per stack)</style>.";

        public override string ItemLore =>
            "My friend, survival is paramount.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_spellbreaker.png";

        public const float HealthFraction    = 0.25f;
        public static float DamageReduction;
        public const float BaseCooldown      = 30f;
        public const float CooldownReduction = 0.5f;

        public static BuffDef? CooldownBuffDef;

        public override void Init(ConfigFile config)
        {
            var color                  = new Color(0.4f, 0.4f, 0.4f);
            CooldownBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            CooldownBuffDef.name       = "CursedApple_SpellbreakerCooldown";
            CooldownBuffDef.canStack   = false;
            CooldownBuffDef.isDebuff   = false;
            CooldownBuffDef.buffColor  = color;
            CooldownBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(CooldownBuffDef);

            DamageReduction = config.Bind("Red", "Spellbreaker - Damage Reduction", 0.75f,
                "Fraction of a heavy hit negated when the block triggers.").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            On.RoR2.HealthComponent.TakeDamage       += HealthComponent_TakeDamage;
        }

        private void OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<SpellbreakerTracker>();

            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<SpellbreakerTracker>();
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active
                && damageInfo != null
                && self.body?.inventory != null
                && self.health > 0f
                && damageInfo.damage >= self.health * HealthFraction)
            {
                int count = self.body.inventory.GetItemCountEffective(ItemDef);
                if (count > 0)
                {
                    var tracker = self.body.GetComponent<SpellbreakerTracker>();
                    if (tracker != null && !tracker.OnCooldown)
                    {
                        damageInfo.damage *= 1f - DamageReduction;
                        tracker.SetCooldown(BaseCooldown * Mathf.Pow(CooldownReduction, count - 1));
                    }
                }
            }

            orig(self, damageInfo);
        }
    }

    public class SpellbreakerTracker : MonoBehaviour
    {
        private float         _cooldown;
        private CharacterBody _body;

        public bool OnCooldown => _cooldown > 0f;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void OnDestroy()
        {
            if (_cooldown > 0f) RemoveBuff();
        }

        private void Update()
        {
            if (_cooldown <= 0f) return;
            _cooldown -= Time.deltaTime;
            if (_cooldown <= 0f)
                RemoveBuff();
        }

        public void SetCooldown(float duration)
        {
            _cooldown = duration;
            AddBuff();
        }

        private void AddBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (Spellbreaker.CooldownBuffDef == null || Spellbreaker.CooldownBuffDef.buffIndex == BuffIndex.None) return;
            if (!_body.HasBuff(Spellbreaker.CooldownBuffDef.buffIndex))
                _body.AddBuff(Spellbreaker.CooldownBuffDef.buffIndex);
        }

        private void RemoveBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (Spellbreaker.CooldownBuffDef == null || Spellbreaker.CooldownBuffDef.buffIndex == BuffIndex.None) return;
            if (_body.HasBuff(Spellbreaker.CooldownBuffDef.buffIndex))
                _body.RemoveBuff(Spellbreaker.CooldownBuffDef.buffIndex);
        }
    }
}
