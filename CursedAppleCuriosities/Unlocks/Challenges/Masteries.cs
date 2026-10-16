using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Mastery skins (user, 2026-10-15): as the hero, beat the game or obliterate on Monsoon (the game's own per-survivor
    /// mastery achievement does the judging). One class per hero, each unlocking that hero's mastery recolour.
    /// </summary>
    public static class Masteries
    {
        /// <summary>Abrams' mastery is his existing Green recolour (user); Vyper, Viscous, Celeste, Mina and Paradox reuse theirs too, Seven's is the new Overcharged.</summary>
        public const string AbramsSkin = "Abrams.Mastery";
        public const string LashSkin = "Lash.Mastery";
        public const string GreyTalonSkin = "GreyTalon.Mastery";
        public const string VictorSkin = "Victor.Mastery";
        public const string ShivSkin = "Shiv.Mastery";
        public const string BebopSkin = "Bebop.Mastery";
        /// <summary>Bebop's Chrome skin: its own challenge (BebopLongArm), not a mastery.</summary>
        public const string BebopChromeSkin = "Bebop.Chrome";
        public const string ApolloSkin = "Apollo.Mastery";
        /// <summary>Apollo's Sapphire skin: its own challenge (ApolloEnGarde, which used to unlock Riposte), not a mastery.</summary>
        public const string ApolloSapphireSkin = "Apollo.Sapphire";
        /// <summary>Abrams' Crimson skin: its own challenge (AbramsSeeingRed), not a mastery.</summary>
        public const string AbramsCrimsonSkin = "Abrams.Crimson";
        public const string SevenSkin = "Seven.Mastery";
        /// <summary>Seven's Bunny Seven skin (funnymonkeyman's model): its own challenge (SevenHareTrigger), not a mastery.</summary>
        public const string SevenBunnySkin = "Seven.Bunny";
        public const string VyperSkin = "Vyper.Mastery";
        public const string ViscousSkin = "Viscous.Mastery";
        public const string CelesteSkin = "Celeste.Mastery";
        /// <summary>Viscous' Pisscous skin: its own challenge (ViscousHoldItIn), not a mastery.</summary>
        public const string ViscousPisscousSkin = "Viscous.Pisscous";
        /// <summary>Celeste's Spirit Guardian skin (TSelky's model): its own challenge (CelesteEncore), not a mastery.</summary>
        public const string CelesteGuardianSkin = "Celeste.Guardian";
        public const string MinaSkin = "Mina.Mastery";
        /// <summary>Mina's Blackmore Colors: its own challenge (MinaHonorRoll), not a mastery.</summary>
        public const string MinaBlackmoreSkin = "Mina.Blackmore";
        public const string ParadoxSkin = "Paradox.Mastery";
    }

    [RegisterAchievement("CursedAppleAbramsMastery", "CursedApple.Skins." + Masteries.AbramsSkin, null, 0, null)]
    public class AbramsMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleAbramsBody");
    }

    [RegisterAchievement("CursedAppleSevenMastery", "CursedApple.Skins." + Masteries.SevenSkin, null, 0, null)]
    public class SevenMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleSevenBody");
    }

    [RegisterAchievement("CursedAppleVyperMastery", "CursedApple.Skins." + Masteries.VyperSkin, null, 0, null)]
    public class VyperMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleVyperBody");
    }

    [RegisterAchievement("CursedAppleViscousMastery", "CursedApple.Skins." + Masteries.ViscousSkin, null, 0, null)]
    public class ViscousMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleViscousBody");
    }

    [RegisterAchievement("CursedAppleCelesteMastery", "CursedApple.Skins." + Masteries.CelesteSkin, null, 0, null)]
    public class CelesteMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleCelesteBody");
    }

    [RegisterAchievement("CursedAppleMinaMastery", "CursedApple.Skins." + Masteries.MinaSkin, null, 0, null)]
    public class MinaMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleMinaBody");
    }

    [RegisterAchievement("CursedAppleParadoxMastery", "CursedApple.Skins." + Masteries.ParadoxSkin, null, 0, null)]
    public class ParadoxMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleParadoxBody");
    }

    [RegisterAchievement("CursedAppleLashMastery", "CursedApple.Skins." + Masteries.LashSkin, null, 0, null)]
    public class LashMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleLashBody");
    }

    [RegisterAchievement("CursedAppleGreyTalonMastery", "CursedApple.Skins." + Masteries.GreyTalonSkin, null, 0, null)]
    public class GreyTalonMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleGreyTalonBody");
    }

    [RegisterAchievement("CursedAppleVictorMastery", "CursedApple.Skins." + Masteries.VictorSkin, null, 0, null)]
    public class VictorMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleVictorBody");
    }

    [RegisterAchievement("CursedAppleShivMastery", "CursedApple.Skins." + Masteries.ShivSkin, null, 0, null)]
    public class ShivMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleShivBody");
    }

    [RegisterAchievement("CursedAppleBebopMastery", "CursedApple.Skins." + Masteries.BebopSkin, null, 0, null)]
    public class BebopMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleBebopBody");
    }

    [RegisterAchievement("CursedAppleApolloMastery", "CursedApple.Skins." + Masteries.ApolloSkin, null, 0, null)]
    public class ApolloMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleApolloBody");
    }
}
