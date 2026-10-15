using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Apollo's Riposte unlock (user, 2026-10-15): "En Garde", as Apollo heal 1,000 health from Flawless Advance's perfect stabs
    /// in a single stage. Health actually restored (overhealing at full health doesn't count); the total starts over each stage.
    /// Server: the perfect stab's heal (ApolloNet) reports what it restored.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class ApolloEnGarde : BaseAchievement
    {
        public const string Identifier = "CursedAppleApolloEnGarde";
        public const string Skill = "CursedAppleApolloRiposte";
        public const float Healing = 1000f;

        public static event Action<CharacterBody, float>? OnPerfectHeal;
        internal static void PerfectHeal(CharacterBody apollo, float healed) { if (healed > 0f) OnPerfectHeal?.Invoke(apollo, healed); }

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
                OnPerfectHeal += Add;
                Stage.onServerStageBegin += NewStage;
            }

            public override void OnUninstall()
            {
                OnPerfectHeal -= Add;
                Stage.onServerStageBegin -= NewStage;
                base.OnUninstall();
            }

            private void NewStage(Stage stage) => _total = 0f;

            private void Add(CharacterBody apollo, float healed)
            {
                if (!IsCurrentBody(apollo)) return;
                _total += healed;
                if (_total >= Healing) Grant();
            }
        }
    }
}
