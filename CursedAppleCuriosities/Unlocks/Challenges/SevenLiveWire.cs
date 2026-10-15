using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Seven's Static Charge unlock (user, 2026-10-15): "Live Wire", as Seven have one Lightning Ball shock 10 different
    /// enemies before it fades. Counted on the server by the ball itself (SevenBall), each enemy once.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class SevenLiveWire : BaseAchievement
    {
        public const string Identifier = "CursedAppleSevenLiveWire";
        public const string Skill = "CursedAppleSevenStatic";
        public const int Enemies = 10;

        /// <summary>Server: one of this Seven's balls has shocked <see cref="Enemies"/> different enemies.</summary>
        public static event Action<CharacterBody>? OnBallFull;
        internal static void BallFull(CharacterBody seven) => OnBallFull?.Invoke(seven);

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
                OnBallFull += Check;
            }

            public override void OnUninstall()
            {
                OnBallFull -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody seven)
            {
                if (IsCurrentBody(seven)) Grant();
            }
        }
    }
}
