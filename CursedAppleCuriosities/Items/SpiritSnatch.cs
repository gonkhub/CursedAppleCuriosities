using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class SpiritSnatch : ItemBase
    {
        public override string ItemName => "Spirit Snatch";
        public override string ItemLangTokenName => "SPIRITSNATCH";

        public override string ItemPickupDesc => "Hits on nearby enemies deal bonus damage and steal their armor.";
        public override string ItemFullDescription =>
            $"Hitting an enemy within <style=cIsUtility>{Range}m</style> " +
            $"deals a bonus <style=cIsDamage>{BonusDamageCoefficient * 100:0}% base damage burst</style> " +
            $"<style=cStack>(+{BonusDamagePerStack * 100:0}% per stack)</style> and " +
            $"<style=cIsDamage>snatches {BaseArmorShred} armor</style> " +
            $"<style=cStack>(+{ArmorShredPerStack} per stack)</style> from them onto " +
            $"<style=cIsHealing>yourself</style> for <style=cIsUtility>{DebuffDuration}s</style>.";

        public override string ItemLore =>
            "Bought that off a Knickerbocker that was about to get his legs broken.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_spiritsnatch.png";

        public const float Range                  = 10f;
        public static float BaseArmorShred;
        public const float ArmorShredPerStack     = 15f;
        public const float DebuffDuration         = 5f;
        public const float BonusDamageCoefficient = 0.4f;
        public const float BonusDamagePerStack    = 0.1f;

        public override void Init(ConfigFile config)
        {
            BaseArmorShred = config.Bind("Green", "Spirit Snatch - Armor Snatched", 20f,
                "Armor stripped from the enemy and granted to you at 1 stack.").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy  += OnHitEnemy;
            RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
        }

        // Runs for every body during stat recalc. Enemies lose armor (the debuff); the attacker
        // gains the same armor (the "snatch") — the defining upgrade over Spirit Strike.
        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            var debuff = sender.GetComponent<SpiritSnatchDebuff>();
            if (debuff != null && debuff.IsActive)
                args.armorAdd -= debuff.ArmorShred;

            var snatch = sender.GetComponent<SpiritSnatchSelfArmor>();
            if (snatch != null && snatch.IsActive)
                args.armorAdd += snatch.Armor;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self,
            DamageInfo damageInfo,
            GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active) return;
            if (damageInfo == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f) return;
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            float distance = Vector3.Distance(damageInfo.attacker.transform.position, victim.transform.position);
            if (distance > Range) return;

            float shred = BaseArmorShred + ArmorShredPerStack * (count - 1);

            // Strip the victim's armor (refreshes duration, does not stack)...
            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody != null)
            {
                var debuff = victimBody.GetComponent<SpiritSnatchDebuff>()
                    ?? victimBody.gameObject.AddComponent<SpiritSnatchDebuff>();
                debuff.Apply(shred, DebuffDuration);
                victimBody.RecalculateStats();
            }

            // ...and snatch that armor onto the attacker for the same duration.
            var selfArmor = attackerBody.GetComponent<SpiritSnatchSelfArmor>()
                ?? attackerBody.gameObject.AddComponent<SpiritSnatchSelfArmor>();
            selfArmor.Apply(shred, DebuffDuration);
            attackerBody.RecalculateStats();

            // Bonus damage burst
            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            float damageCoeff = BonusDamageCoefficient + BonusDamagePerStack * (count - 1);

            var proc = new DamageInfo();
            proc.attacker         = damageInfo.attacker;
            proc.inflictor        = damageInfo.attacker;
            proc.damage           = attackerBody.damage * damageCoeff;
            proc.crit             = false;
            proc.position         = damageInfo.position;
            proc.procCoefficient  = 0f;
            proc.damageColorIndex = DamageColorIndex.Item;

            victimHC.TakeDamage(proc);
        }
    }

    public class SpiritSnatchDebuff : MonoBehaviour
    {
        private float _duration;

        public float ArmorShred { get; private set; }
        public bool  IsActive   => _duration > 0f;

        public void Apply(float shred, float duration)
        {
            ArmorShred = shred;
            _duration  = duration;
        }

        private void Update()
        {
            if (_duration > 0f)
            {
                _duration -= Time.deltaTime;
                if (_duration <= 0f)
                    GetComponent<CharacterBody>()?.RecalculateStats();
            }
        }
    }

    // The stolen armor living on the attacker (mirror of the victim's debuff).
    public class SpiritSnatchSelfArmor : MonoBehaviour
    {
        private float _duration;

        public float Armor    { get; private set; }
        public bool  IsActive => _duration > 0f;

        public void Apply(float armor, float duration)
        {
            Armor     = armor;
            _duration = duration;
        }

        private void Update()
        {
            if (_duration > 0f)
            {
                _duration -= Time.deltaTime;
                if (_duration <= 0f)
                    GetComponent<CharacterBody>()?.RecalculateStats();
            }
        }
    }
}
