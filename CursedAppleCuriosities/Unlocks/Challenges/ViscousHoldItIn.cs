using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Viscous' Pisscous skin unlock (user, 2026-10-16): "Hold It In", as Viscous stay inside one Cube for 6 seconds straight. The
    /// Cube lasts 3 s, so it takes Duration Extenders (13 copies: x2.05). Breaking out early or dying in it doesn't count.
    /// Server: The Cube reports how long each one held its body (ViscousCubes.Finish).
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.ViscousPisscousSkin, null, 0, typeof(Server))]
    public class ViscousHoldItIn : BaseAchievement
    {
        public const string Identifier = "CursedAppleViscousHoldItIn";
        public const float Seconds = 6f;

        public static event Action<CharacterBody, float>? OnCubeEnded;
        internal static void CubeEnded(CharacterBody body, float seconds) => OnCubeEnded?.Invoke(body, seconds);

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
                OnCubeEnded += Ended;
            }

            public override void OnUninstall()
            {
                OnCubeEnded -= Ended;
                base.OnUninstall();
            }

            private void Ended(CharacterBody body, float seconds)
            {
                if (seconds + 0.05f >= Seconds && Survivors.Viscous.ViscousSurvivor.Is(body) && IsCurrentBody(body)) Grant();
            }
        }
    }
}
