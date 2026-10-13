using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>
    /// Artifact of the Catalog (user, 2.0; replaces the Curiosity Catalog item). Gold is shown
    /// as souls, where a basic chest always costs 800 (<see cref="Souls"/>). Each stage's item chests and multishops are
    /// re-tiered when it starts, 40% white / 40% green / 20% red, priced 800 / 3200 / 6400 souls and swapped for that
    /// tier's vanilla chest (<see cref="SoulChests"/>), and the Bazaar sells every Cursed Apple item (<see cref="SoulShop"/>).
    /// With the Artifact of Command, opening a chest offers a pick of its tier. Mod-only drops are the Artifact of the
    /// Curio's job (<see cref="CuriosityShop"/>), so the two combine.
    /// </summary>
    public class CatalogArtifact : ArtifactBase
    {
        public override string ArtifactName          => "Artifact of the Catalog";
        public override string ArtifactLangTokenName => "CATALOG";
        public override string ArtifactDescription   =>
            $"Gold becomes souls: a basic chest always costs {Souls.PerChest}. Every stage's chests are re-rolled " +
            $"{WhiteShare * 100:0}% white ({Souls.PerChest * WhiteMult}), {GreenShare * 100:0}% green ({Souls.PerChest * GreenMult}) " +
            $"and {RedShare * 100:0}% red ({Souls.PerChest * RedMult}). The Bazaar sells every Cursed Apple item.";

        protected override Color EnabledIconColor  => new Color(1f, 0.75f, 0.15f);
        protected override Color DisabledIconColor => new Color(0.35f, 0.35f, 0.35f);

        public static ArtifactDef? Def;
        public static float WhiteShare, GreenShare, RedShare;
        public static int WhiteMult, GreenMult, RedMult;

        /// <summary>True while a run has the artifact on (every machine).</summary>
        public static bool Active =>
            Def != null && RunArtifactManager.instance != null && RunArtifactManager.instance.IsArtifactEnabled(Def);

        public override void Init(ConfigFile config)
        {
            const string S = "Artifact of the Catalog";
            WhiteShare = config.Bind(S, "White Share", 0.4f, "Share of a stage's chests that become white.").Value;
            GreenShare = config.Bind(S, "Green Share", 0.4f, "Share of a stage's chests that become green.").Value;
            RedShare   = config.Bind(S, "Red Share", 0.2f, "Share of a stage's chests that become red.").Value;
            WhiteMult  = config.Bind(S, "White Price", 1, "A white chest's price, in basic chests (1 = 800 souls).").Value;
            GreenMult  = config.Bind(S, "Green Price", 4, "A green chest's price, in basic chests (4 = 3200 souls).").Value;
            RedMult    = config.Bind(S, "Red Price", 8, "A red chest's price, in basic chests (8 = 6400 souls).").Value;
            CreateLang();
            CreateArtifact();
            Def = ArtifactDef;
            Hooks();
        }

        public override void Hooks()
        {
            Souls.Hook();
            SoulChests.Hook();
            SoulShop.Hook();
        }

        public static int Mult(ItemTier tier) => tier switch
        {
            ItemTier.Tier2 => GreenMult,
            ItemTier.Tier3 => RedMult,
            _ => WhiteMult,
        };
    }
}
