using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    public class CultistSacrifice : EquipmentBase
    {
        public override string EquipmentName          => "Cultist Sacrifice";
        public override string EquipmentLangTokenName => "CULTISTSACRIFICE";

        public override string PickupDesc => "Sacrifice a lesser enemy for a big payout and a health blessing.";
        public override string FullDescription =>
            $"<style=cIsDamage>Instantly kill</style> the enemy nearest your crosshair within <style=cIsUtility>{Range}m</style> " +
            $"(not bosses or champions). It pays out <style=cShrine>{GoldMultiplier:0.#}x</style> the " +
            $"<style=cShrine>gold</style> it would have dropped, so richer enemies, like elites, pay far more. " +
            $"You are <style=cIsHealing>blessed</style> for <style=cIsUtility>{BlessingDuration:0}s</style>, gaining " +
            $"<style=cIsHealing>+{BlessingHealth:0} max health</style>.";

        public override string Lore =>
            "They were going to die anyway. Probably. Eventually.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_cultistsacrifice.png";

        public static float CooldownSeconds;
        public static float GoldMultiplier;
        public static float BlessingHealth;
        public static float BlessingDuration;
        public const float Range    = 40f;
        public const float AimAngle = 20f;

        public static BuffDef? BlessingBuffDef;
        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = 120f;
            GoldMultiplier = 4f;
            BlessingHealth = 50f;
            BlessingDuration = 105f;

            var color                  = new Color(0.75f, 0.15f, 0.2f);
            BlessingBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            BlessingBuffDef.name       = "CursedApple_CultistBlessing";
            BlessingBuffDef.canStack   = false;
            BlessingBuffDef.isDebuff   = false;
            BlessingBuffDef.buffColor  = color;
            BlessingBuffDef.iconSprite = ItemBase.BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(BlessingBuffDef);

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            EquipmentTargeting.Register(new EquipmentTargeting.Rule { Def = EquipmentDef, Range = Range, Angle = AimAngle, Allow = Sacrificable });
            Hooks();
        }

        public override void RegisterSounds()
            => EquipmentFx.RegisterSound("cultist_cast", "sounds/mods/weapon/cultist_sacrifice/cultist_sacrifice_cast", range: 60f, fallback: "Play_bleedOnCritAndExplode_explode");

        public override void Hooks()
        {
            RecalculateStatsAPI.GetStatCoefficients += (sender, args) =>
            {
                if (BlessingBuffDef != null && sender.HasBuff(BlessingBuffDef))
                    args.baseHealthAdd += BlessingHealth;
            };
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null || body.teamComponent == null || body.inputBank == null) return false;

            var victim = EquipmentTargeting.FindEnemy(body, Range * MysticExpansion.RadiusMultiplier(body), AimAngle, Sacrificable);
            if (victim == null) return false; // no target → no cooldown

            // DeathRewards pays the normal 1x on death; add the rest. goldReward already carries the elite multiplier.
            var rewards = victim.GetComponent<DeathRewards>();
            uint bonus = rewards != null ? (uint)(rewards.goldReward * Mathf.Max(0f, GoldMultiplier - 1f)) : 0u;

            EquipmentFx.Effect("RoR2/Base/Imp/ImpBlinkEffect.prefab", victim.corePosition, victim.radius * 2f);
            EquipmentFx.Effect("RoR2/Base/BleedOnHitAndExplode/BleedOnHitAndExplode_Explosion.prefab", victim.corePosition, victim.radius * 2f);
            EquipmentFx.Sound("cultist_cast", victim.corePosition);

            victim.healthComponent.Suicide(body.gameObject, body.gameObject);

            if (bonus > 0 && TeamManager.instance != null)
                TeamManager.instance.GiveTeamMoney(body.teamComponent.teamIndex, bonus, bypassStatManager: false);

            if (BlessingBuffDef != null)
                body.AddTimedBuff(BlessingBuffDef, BlessingDuration); // refreshes if still blessed
            return true;
        }

        private static bool Sacrificable(CharacterBody b) => !b.isBoss && !b.isChampion && !b.isPlayerControlled;
    }
}
