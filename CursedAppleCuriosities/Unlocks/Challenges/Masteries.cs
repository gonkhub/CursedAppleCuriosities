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
        /// <summary>Abrams' mastery is his existing Green recolour (user).</summary>
        public const string AbramsSkin = "Abrams.Mastery";
    }

    [RegisterAchievement("CursedAppleAbramsMastery", "CursedApple.Skins." + Masteries.AbramsSkin, null, 0, null)]
    public class AbramsMastery : BasePerSurvivorClearGameMonsoonAchievement
    {
        protected override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CursedAppleAbramsBody");
    }
}
