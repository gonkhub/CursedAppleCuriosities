using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Shiv's unlock (user, 2026-10-15): "Killing Blow", kill a boss (any champion) while below 10% health, as any survivor.
    /// Health counts your shield too (a full shield isn't "low"), not your barrier (a barrier can keep you alive down there).
    /// The kill is credited to your own body (your damage over time and procs included; drones and minions aren't you).
    /// Checked on the server the moment the boss dies.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Shiv", null, 0, typeof(Server))]
    public class ShivKillingBlow : BaseAchievement
    {
        public const string Identifier = "CursedAppleShivKillingBlow";
        public const float LowHealth = 0.1f;

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
                GlobalEventManager.onCharacterDeathGlobal += OnDeath;
            }

            public override void OnUninstall()
            {
                GlobalEventManager.onCharacterDeathGlobal -= OnDeath;
                base.OnUninstall();
            }

            private void OnDeath(DamageReport report)
            {
                CharacterBody? me = report?.attackerBody;
                if (me == null || report!.victimBody == null || !report.victimBody.isChampion || !IsCurrentBody(me)) return;
                HealthComponent? hc = me.healthComponent;
                if (hc == null || !hc.alive) return;
                float full = hc.fullHealth + hc.fullShield;
                if (full > 0f && (hc.health + hc.shield) / full < LowHealth) Grant();
            }
        }
    }
}
