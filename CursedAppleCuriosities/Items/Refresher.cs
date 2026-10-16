using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    public class Refresher : EquipmentBase
    {
        public override string EquipmentName          => "Refresher";
        public override string EquipmentLangTokenName => "REFRESHER";

        public override string PickupDesc => "Reset all of your skill cooldowns.";
        public override string FullDescription =>
            "<style=cIsUtility>Instantly reset</style> the cooldowns of <style=cIsUtility>all your skills</style> " +
            "and restore all of their <style=cIsUtility>charges</style>.";

        public override string Lore =>
            "Do it all again. Don't think about the bill.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_refresher.png";

        public static float CooldownSeconds;
        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = 80f;
            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
        }

        public override void RegisterSounds()
            => EquipmentFx.RegisterSound("refresher_cast", "sounds/mods/tech/refresher/refresher_cast", fallback: "Play_UI_cooldownRefresh");

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null || body.skillLocator == null) return false;

            // ResetSkills forwards itself to the owning client (player skills are client-authoritative).
            body.skillLocator.ResetSkills();

            EquipmentFx.Effect("RoR2/Base/Cleanse/CleanseEffect.prefab", body.corePosition, body.radius);
            EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUpEffect.prefab", body.corePosition, body.radius);
            EquipmentFx.Sound("refresher_cast", body.corePosition, body.gameObject);
            return true;
        }
    }
}
