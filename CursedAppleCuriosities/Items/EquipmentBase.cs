using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    // Sibling to ItemBase, for active "equipment" items. Auto-discovered by the plugin the same
    // way items are; the plugin dispatches EquipmentSlot.PerformEquipmentAction to OnUse().
    public abstract class EquipmentBase
    {
        public abstract string EquipmentName { get; }
        public abstract string EquipmentLangTokenName { get; }
        public virtual string PickupDesc => string.Empty;
        public abstract string FullDescription { get; }
        public abstract string Lore { get; }

        public abstract float Cooldown { get; }

        // Override with an embedded PNG filename to give the equipment an icon.
        protected virtual string? IconFileName => null;

        public EquipmentDef? EquipmentDef { get; private set; }

        public abstract void Init(ConfigFile config);

        protected void CreateLang()
        {
            LanguageAPI.Add($"CURSEDAPPLE_{EquipmentLangTokenName}_NAME", EquipmentName);
            LanguageAPI.Add($"CURSEDAPPLE_{EquipmentLangTokenName}_PICKUP", PickupDesc);
            LanguageAPI.Add($"CURSEDAPPLE_{EquipmentLangTokenName}_DESC", FullDescription);
            LanguageAPI.Add($"CURSEDAPPLE_{EquipmentLangTokenName}_LORE", Lore);
        }

        protected void CreateEquipment()
        {
            EquipmentDef = ScriptableObject.CreateInstance<EquipmentDef>();
            EquipmentDef.name             = $"EQUIP_CURSEDAPPLE_{EquipmentLangTokenName}";
            EquipmentDef.nameToken        = $"CURSEDAPPLE_{EquipmentLangTokenName}_NAME";
            EquipmentDef.pickupToken      = $"CURSEDAPPLE_{EquipmentLangTokenName}_PICKUP";
            EquipmentDef.descriptionToken = $"CURSEDAPPLE_{EquipmentLangTokenName}_DESC";
            EquipmentDef.loreToken        = $"CURSEDAPPLE_{EquipmentLangTokenName}_LORE";
            EquipmentDef.cooldown         = Cooldown;
            EquipmentDef.canDrop          = true;
            EquipmentDef.enigmaCompatible = true;
            EquipmentDef.canBeRandomlyTriggered = true;
            EquipmentDef.appearsInSinglePlayer  = true; // without these it won't enter drop pools
            EquipmentDef.appearsInMultiPlayer   = true;
            EquipmentDef.isLunar          = false;
            EquipmentDef.isBoss           = false;
            EquipmentDef.pickupIconSprite = IconFileName != null ? ItemBase.LoadEmbeddedIcon(IconFileName) : null;

            ItemAPI.Add(new CustomEquipment(EquipmentDef, new ItemDisplayRuleDict(null)));
        }

        // Called when the player presses the equipment button. Return true to start the cooldown.
        public abstract bool OnUse(EquipmentSlot slot);

        // Override to register passive hooks the equipment needs (e.g. a TakeDamage hook).
        public virtual void Hooks() { }
    }
}
