using System;
using System.Runtime.CompilerServices;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Viscous' Puddle Punch unlock (user, 2026-10-15): "Strike!", as Viscous kill 10 enemies in one Goo Ball roll. The server
    /// counts kills by the ball's hits (ViscousNet.Hit); a roll is one GooBall state on his Body machine (each roll is a new
    /// state object), so the count starts over with each roll.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class ViscousStrike : BaseAchievement
    {
        public const string Identifier = "CursedAppleViscousStrike";
        public const string Skill = "CursedAppleViscousPunch";
        public const int Kills = 10;

        public static event Action<CharacterBody>? OnStrike;

        private class Roll { public object? State; public int Kills; }
        private static readonly ConditionalWeakTable<CharacterBody, Roll> _rolls = new ConditionalWeakTable<CharacterBody, Roll>();

        /// <summary>Server: the ball killed an enemy.</summary>
        internal static void BallKill(CharacterBody viscous)
        {
            object? state = EntityStateMachine.FindByCustomName(viscous.gameObject, "Body")?.state;
            Roll roll = _rolls.GetOrCreateValue(viscous);
            if (!ReferenceEquals(roll.State, state)) { roll.State = state; roll.Kills = 0; }
            if (++roll.Kills == Kills) OnStrike?.Invoke(viscous);
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
                OnStrike += Check;
            }

            public override void OnUninstall()
            {
                OnStrike -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody viscous)
            {
                if (IsCurrentBody(viscous)) Grant();
            }
        }
    }
}
