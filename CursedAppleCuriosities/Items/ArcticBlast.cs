using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    public class ArcticBlast : EquipmentBase
    {
        public override string EquipmentName          => "Arctic Blast";
        public override string EquipmentLangTokenName => "ARCTICBLAST";

        public override string PickupDesc => "Freeze all nearby enemies, then chill them.";
        public override string FullDescription =>
            $"Release a blast of cold, dealing <style=cIsDamage>{DamageCoefficient * 100:0}% damage</style> to enemies within " +
            $"<style=cIsUtility>{Radius}m</style> and <style=cIsUtility>freezing</style> them. Frozen enemies below " +
            $"<style=cIsHealth>30% health</style> are <style=cIsHealth>executed</style> when hit. Enemies that can't be frozen " +
            $"are heavily <style=cIsUtility>slowed</style>, and everything hit is <style=cIsUtility>chilled</style> for " +
            $"<style=cIsUtility>{ChillDuration}s</style> after.";

        public override string Lore =>
            "Keep it in the freezer. No, not that one, the other one.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_arcticblast.png";

        public static float CooldownSeconds;
        public static float DamageCoefficient;
        public static float Radius;
        public const float FreezeDuration = 2f;  // Freeze2s damage type
        public const float ChillDuration  = 4f;  // Slow60 after the freeze

        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Arctic Blast - Cooldown", 45f,
                "Equipment cooldown in seconds.").Value;
            DamageCoefficient = config.Bind("Equipment", "Arctic Blast - Damage", 3f,
                "Blast damage as a multiple of your base damage (3 = 300%).").Value;
            Radius = config.Bind("Equipment", "Arctic Blast - Radius", 20f,
                "Blast radius in meters.").Value;
            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
        }

        public override void RegisterSounds()
        {
            EquipmentFx.RegisterSound("arcticblast_cast", "sounds/mods/tech/arctic_blast/arctic_blast_cast", range: 70f, fallback: "Play_Chef_Secondary_IceBox_Blizzard");
            EquipmentFx.RegisterSound("arcticblast_freeze", "sounds/mods/tech/arctic_blast/arctic_blast_freeze_proc", volume: 0.6f, minGap: 0.05f);
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null || body.teamComponent == null) return false;

            var blast = new BlastAttack
            {
                attacker         = body.gameObject,
                inflictor        = body.gameObject,
                teamIndex        = body.teamComponent.teamIndex,
                position         = body.corePosition,
                radius           = Radius,
                falloffModel     = BlastAttack.FalloffModel.None,
                baseDamage       = body.damage * DamageCoefficient,
                crit             = body.RollCrit(),
                damageType       = new DamageTypeCombo(DamageType.Freeze2s, DamageTypeExtended.Generic, DamageSource.Equipment),
                damageColorIndex = DamageColorIndex.Default,
                procCoefficient  = 1f,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
            };
            var result = blast.Fire(); // Mystic Expansion scales this radius in its BlastAttack hook

            // The freeze comes from the damage type; bosses and other unfreezables get a hard slow instead.
            var chilled = new HashSet<CharacterBody>();
            foreach (var hit in result.hitPoints)
            {
                var victim = hit.hurtBox != null && hit.hurtBox.healthComponent != null ? hit.hurtBox.healthComponent.body : null;
                if (victim == null || !chilled.Add(victim)) continue;

                var state = victim.GetComponent<SetStateOnHurt>();
                bool frozen = state != null && state.canBeFrozen;
                if (!frozen) victim.AddTimedBuff(RoR2Content.Buffs.Slow80, FreezeDuration);
                else if (chilled.Count <= 4) EquipmentFx.Sound("arcticblast_freeze", victim.corePosition, victim.gameObject);
                victim.AddTimedBuff(RoR2Content.Buffs.Slow60, FreezeDuration + ChillDuration);
            }

            float shown = Radius * MysticExpansion.RadiusMultiplier(body);
            EquipmentFx.Effect("RoR2/Base/Elites/EliteIce/AffixWhiteExplosion.prefab", body.corePosition, shown);
            EquipmentFx.Effect("RoR2/Base/Characters/Mage/Skills/MageIceExplosion.prefab", body.corePosition, shown);
            EquipmentFx.Sound("arcticblast_cast", body.corePosition, body.gameObject);
            return true;
        }
    }
}
