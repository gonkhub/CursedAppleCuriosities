using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Ivy's Obsidian skin unlock (user, 2026-10-15): "Rock Bottom", as Ivy kill 5 enemies with a single Stone Form slam.
    /// The server counts the enemies the slam's hit kills (IvyStoneMessage, Slam).
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.IvyObsidianSkin, null, 0, typeof(Server))]
    public class IvyRockBottom : BaseAchievement
    {
        public const string Identifier = "CursedAppleIvyRockBottom";
        public const int Kills = 5;

        public static event Action<CharacterBody>? OnBigSlam;
        internal static void Slammed(CharacterBody ivy, int kills) { if (kills >= Kills) OnBigSlam?.Invoke(ivy); }

        public override void OnInstall()
        {
            base.OnInstall();
            SetServerTracked(true);
        }

        public override void OnUninstall()
        {
            SetServerTracked(false);
            base.OnUninstall();
        }

        private class Server : BaseServerAchievement
        {
            public override void OnInstall()
            {
                base.OnInstall();
                OnBigSlam += Check;
            }

            public override void OnUninstall()
            {
                OnBigSlam -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody ivy)
            {
                if (IsCurrentBody(ivy)) Grant();
            }
        }
    }
}
