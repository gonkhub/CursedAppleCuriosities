using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Abrams' unlock (user, 2026-10-16; he started unlocked until then): "Infernal Resilience", take 3,000 damage in a single stage,
    /// as any survivor. Damage actually dealt to you (after armor, blocks and barrier... as the game reports it), from anything; the
    /// total starts over each stage. Server: read from the game's damage reports.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Abrams", null, 0, typeof(Server))]
    public class AbramsInfernalResilience : BaseAchievement
    {
        public const string Identifier = "CursedAppleAbramsInfernalResilience";
        public const float Damage = 3000f;

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
                GlobalEventManager.onServerDamageDealt += Hurt;
                Stage.onServerStageBegin += NewStage;
            }

            public override void OnUninstall()
            {
                GlobalEventManager.onServerDamageDealt -= Hurt;
                Stage.onServerStageBegin -= NewStage;
                base.OnUninstall();
            }

            private void NewStage(Stage stage) => _total = 0f;

            private void Hurt(DamageReport report)
            {
                if (report?.victimBody == null || report.damageDealt <= 0f || !IsCurrentBody(report.victimBody)) return;
                _total += report.damageDealt;
                if (_total >= Damage) Grant();
            }
        }
    }
}
