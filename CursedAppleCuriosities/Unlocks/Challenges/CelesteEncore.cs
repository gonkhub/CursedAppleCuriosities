using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Celeste's Spirit Guardian skin unlock (user, 2026-10-15): "Encore", as Celeste hit the same enemy 12 times with one
    /// Shining Wonder. The orb counts its own hits per enemy on the server (prism bounces don't count).
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.CelesteGuardianSkin, null, 0, typeof(Server))]
    public class CelesteEncore : BaseAchievement
    {
        public const string Identifier = "CursedAppleCelesteEncore";
        public const int Hits = 12;

        public static event Action<CharacterBody>? OnEncore;

        /// <summary>Server, after a Shining Wonder hit: that enemy has now taken this many hits from the one orb.</summary>
        internal static void Hit(CharacterBody celeste, int sameEnemyHits)
        {
            if (sameEnemyHits == Hits) OnEncore?.Invoke(celeste);
        }

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
                OnEncore += Check;
            }

            public override void OnUninstall()
            {
                OnEncore -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody celeste)
            {
                if (IsCurrentBody(celeste)) Grant();
            }
        }
    }
}
