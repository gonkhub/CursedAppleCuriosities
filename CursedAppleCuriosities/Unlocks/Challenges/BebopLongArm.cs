using System;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Bebop's Chrome skin unlock (user, 2026-10-15): "Long Arm of the Law", as Bebop hook an enemy with Grapple Arm within
    /// 10 m of the hook's maximum reach (200 m by default; Mystic Expansion's longer reach moves the mark with it). Judged
    /// where the hand flies (Bebop's own machine): it knows how far the hand had gone when it latched.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.BebopChromeSkin, null, 0, null)]
    public class BebopLongArm : BaseAchievement
    {
        public const string Identifier = "CursedAppleBebopLongArm";
        public const float Margin = 10f;

        public static event Action<GameObject>? OnLongHook;

        /// <summary>The hook latched an enemy after the hand flew this far, out of this much reach.</summary>
        internal static void Latched(GameObject bebop, float distance, float reach)
        {
            if (distance >= reach - Margin) OnLongHook?.Invoke(bebop);
        }

        public override void OnInstall()
        {
            base.OnInstall();
            OnLongHook += Check;
        }

        public override void OnUninstall()
        {
            OnLongHook -= Check;
            base.OnUninstall();
        }

        private void Check(GameObject bebop)
        {
            if (localUser?.cachedBody != null && bebop == localUser.cachedBody.gameObject) Grant();
        }
    }
}
