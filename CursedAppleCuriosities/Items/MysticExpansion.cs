using BepInEx.Configuration;
using RoR2;

namespace CursedAppleCuriosities.Items
{
    public class MysticExpansion : ItemBase
    {
        public override string ItemName => "Mystic Expansion";
        public override string ItemLangTokenName => "MYSTICEXPANSION";

        public override string ItemPickupDesc => "Your explosions, auras and reach are larger.";
        public override string ItemFullDescription =>
            $"Increase the <style=cIsDamage>radius</style> of all your <style=cIsDamage>area-of-effect attacks</style>, " +
            "<style=cIsUtility>auras and wards</style>, and the <style=cIsUtility>range</style> of your items, equipment and abilities by " +
            $"<style=cIsDamage>{RadiusPerStack * 100:0}%</style> <style=cStack>(+{RadiusPerStack * 100:0}% per stack)</style>.";

        public override string ItemLore =>
            "Same spell, bigger circle. Mind your eyebrows.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_mysticexpansion.png";

        public static float RadiusPerStack;

        private static ItemDef? _def;

        public override void Init(ConfigFile config)
        {
            RadiusPerStack = config.Bind("White", "Mystic Expansion - Radius Per Stack", 0.30f, // 0.15 at first; doubled by playtest
                "Area-of-effect radius increase per stack (linear).").Value;
            CreateLang();
            CreateItem();
            _def = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            // Nearly every area attack in the game (explosive projectiles, grenades, mines, Gasoline,
            // Will-o'-the-wisp, Arctic Blast...) resolves through BlastAttack.Fire with the owner as attacker.
            On.RoR2.BlastAttack.Fire += BlastAttack_Fire;
            MysticExpansionReach.Hook();
        }

        private static BlastAttack.Result BlastAttack_Fire(On.RoR2.BlastAttack.orig_Fire orig, BlastAttack self)
        {
            if (MysticExpansionReach.SkipBlastScale) return orig(self); // a projectile explosion already grew it
            float m = RadiusMultiplier(self.attacker != null ? self.attacker.GetComponent<CharacterBody>() : null);
            if (m != 1f)
            {
                MysticExpansionReach.Remember(self.position, self.radius, m); // its effect grows to match
                self.radius *= m;
            }
            return orig(self);
        }

        public static float RadiusMultiplier(CharacterBody? body)
        {
            float curse = Broker.CursePenalties.RadiusFactor(body); // a Cursed Broker range penalty shrinks it
            if (_def == null || body == null || body.inventory == null) return curse;
            int count = body.inventory.GetItemCountEffective(_def);
            return (count > 0 ? 1f + RadiusPerStack * count : 1f) * curse;
        }
    }
}
