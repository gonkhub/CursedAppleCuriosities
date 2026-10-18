using System;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Mina's Blackmore Colors unlock (user, 2026-10-15): "Honor Roll", as Mina cast Nox Nostra with all of its bonus bats: one per
    /// Love Bites burst this stage, up to Deadlock's BonusBatsMax (50). Judged where the cast starts (Mina's own machine), from the
    /// synced Love Bites stack count the cast reads.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.MinaBlackmoreSkin, null, 0, null)]
    public class MinaHonorRoll : BaseAchievement
    {
        public const string Identifier = "CursedAppleMinaHonorRoll";
        public static int Bonus => Survivors.Mina.MinaData.BonusBatsMax;

        public static event Action<GameObject>? OnFullCast;

        /// <summary>Nox Nostra was cast with this many Love Bites stacks (bonus bats).</summary>
        internal static void Cast(GameObject mina, int stacks)
        {
            if (stacks >= Bonus) OnFullCast?.Invoke(mina);
        }

        public override void OnInstall()
        {
            base.OnInstall();
            OnFullCast += Check;
        }

        public override void OnUninstall()
        {
            OnFullCast -= Check;
            base.OnUninstall();
        }

        private void Check(GameObject mina)
        {
            if (localUser?.cachedBody != null && mina == localUser.cachedBody.gameObject) Grant();
        }
    }
}
