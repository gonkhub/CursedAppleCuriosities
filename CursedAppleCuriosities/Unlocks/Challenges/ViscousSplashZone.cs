using System;
using CursedAppleCuriosities.Survivors.Viscous;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Viscous' Goo Glob unlock (user, 2026-10-15): "Splash Zone", as Viscous kill 4 enemies with a single Splatter. Its
    /// bounce bursts and pass-through hits both name the thrown glob as inflictor, so the server counts kills per glob
    /// (once, globally, then tells the thrower's achievement).
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class ViscousSplashZone : BaseAchievement
    {
        public const string Identifier = "CursedAppleViscousSplashZone";
        public const string Skill = "CursedAppleViscousAlt";
        public const int Kills = 4;

        public static event Action<CharacterBody>? OnSplash;
        private static bool _hooked;

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            GlobalEventManager.onCharacterDeathGlobal += report =>
            {
                ViscousGlob? glob = report?.damageInfo?.inflictor != null ? report.damageInfo.inflictor.GetComponent<ViscousGlob>() : null;
                if (glob == null || !glob.Splatter || report!.attackerBody == null) return;
                if (++glob.ChallengeKills == Kills) OnSplash?.Invoke(report.attackerBody);
            };
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
                Hook();
                OnSplash += Check;
            }

            public override void OnUninstall()
            {
                OnSplash -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody viscous)
            {
                if (IsCurrentBody(viscous)) Grant();
            }
        }
    }
}
