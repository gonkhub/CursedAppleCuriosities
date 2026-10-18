using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Bebop's Grapple Arm unlock (user, 2026-10-15): "Bomb Squad", as Bebop build up +200% Sticky Bomb damage. His permanent
    /// bonus (BebopStickyStacks, on his master, server) is wiped by any death, so it has to be built in one life.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class BebopBombSquad : BaseAchievement
    {
        public const string Identifier = "CursedAppleBebopBombSquad";
        public const string Skill = "CursedAppleBebopHook";
        public const float Percent = 200f;

        public static event Action<CharacterMaster>? OnBuiltUp;

        /// <summary>Server: his Sticky Bomb bonus just grew to this many percent.</summary>
        internal static void StacksNow(CharacterMaster? bebop, float percent)
        {
            if (bebop != null && percent >= Percent) OnBuiltUp?.Invoke(bebop);
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
                OnBuiltUp += Check;
            }

            public override void OnUninstall()
            {
                OnBuiltUp -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterMaster bebop)
            {
                if (networkUser != null && networkUser.master == bebop) Grant();
            }
        }
    }
}
