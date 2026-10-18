using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Abrams' Heavy Melee unlock (user, 2026-10-15): "Freight Train", as Abrams slam 5 or more enemies into a wall with one
    /// Shoulder Charge. Counted on the server where the charge's wall slam lands (AbramsReleaseMessage): every carried enemy
    /// still alive when the wall stops him.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class AbramsFreightTrain : BaseAchievement
    {
        public const string Identifier = "CursedAppleAbramsFreightTrain";
        public const string Skill = "CursedAppleAbramsHeavyMelee";
        public const int Enemies = 5;

        /// <summary>Server: an Abrams charge hit a wall carrying this many live enemies.</summary>
        public static event Action<CharacterBody, int>? OnWallSlam;
        internal static void WallSlam(CharacterBody abrams, int carried) => OnWallSlam?.Invoke(abrams, carried);

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
                OnWallSlam += Check;
            }

            public override void OnUninstall()
            {
                OnWallSlam -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody abrams, int carried)
            {
                if (carried >= Enemies && IsCurrentBody(abrams)) Grant();
            }
        }
    }
}
