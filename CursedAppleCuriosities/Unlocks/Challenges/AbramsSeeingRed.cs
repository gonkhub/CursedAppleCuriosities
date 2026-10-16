using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Abrams' Crimson skin unlock (user, 2026-10-15): "Seeing Red", as Abrams heal 5,000 health with Siphon Life in a single stage.
    /// Health actually restored (overhealing at full health doesn't count); the total starts over each stage.
    /// Server: Siphon Life's heal (AbramsSiphon) reports what it restored.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skins." + Masteries.AbramsCrimsonSkin, null, 0, typeof(Server))]
    public class AbramsSeeingRed : BaseAchievement
    {
        public const string Identifier = "CursedAppleAbramsSeeingRed";
        public const float Healing = 5000f; // 1,000 until 2026-10-16 (user)

        public static event Action<CharacterBody, float>? OnSiphonHeal;
        internal static void Siphoned(CharacterBody abrams, float healed) { if (healed > 0f) OnSiphonHeal?.Invoke(abrams, healed); }

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
            private float _total;

            public override void OnInstall()
            {
                base.OnInstall();
                OnSiphonHeal += Add;
                Stage.onServerStageBegin += NewStage;
            }

            public override void OnUninstall()
            {
                OnSiphonHeal -= Add;
                Stage.onServerStageBegin -= NewStage;
                base.OnUninstall();
            }

            private void NewStage(Stage stage) => _total = 0f;

            private void Add(CharacterBody abrams, float healed)
            {
                if (!IsCurrentBody(abrams)) return;
                _total += healed;
                if (_total >= Healing) Grant();
            }
        }
    }
}
