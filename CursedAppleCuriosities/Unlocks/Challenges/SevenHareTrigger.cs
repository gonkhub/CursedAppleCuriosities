using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Seven's Bunny Seven skin unlock (user, 2026-10-16): "Hare Trigger", as Seven kill 25 enemies during a single Power Surge.
    /// Any kill credited to him counts (his bursts, the chain lightning, his other abilities, procs) while the Surge buff is on
    /// him; casting a new Surge starts the count over. Server: the Surge cast (SevenNet) and the game's death reports.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.SevenBunnySkin, null, 0, typeof(Server))]
    public class SevenHareTrigger : BaseAchievement
    {
        public const string Identifier = "CursedAppleSevenHareTrigger";
        public const int Kills = 25;

        public static event Action<CharacterBody>? OnSurge;
        internal static void SurgeStarted(CharacterBody seven) => OnSurge?.Invoke(seven);

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
            private int _kills;

            public override void OnInstall()
            {
                base.OnInstall();
                OnSurge += Started;
                GlobalEventManager.onCharacterDeathGlobal += Died;
            }

            public override void OnUninstall()
            {
                OnSurge -= Started;
                GlobalEventManager.onCharacterDeathGlobal -= Died;
                base.OnUninstall();
            }

            private void Started(CharacterBody seven)
            {
                if (IsCurrentBody(seven)) _kills = 0;
            }

            private void Died(DamageReport report)
            {
                CharacterBody? seven = report?.attackerBody;
                BuffDef? surge = Survivors.Seven.SevenSurvivor.SurgeBuff;
                if (seven == null || surge == null || !seven.HasBuff(surge) || !IsCurrentBody(seven)) return;
                if (++_kills >= Kills) Grant();
            }
        }
    }
}
