using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Apollo's unlock (user, 2026-10-15): "Flawless Advance", complete a teleporter event without taking any damage, as any
    /// survivor. From the teleporter's activation (whoever activates it) until it is fully charged and its boss is dead, no hit
    /// may take health or shield from you: hits fully soaked by barrier or blocked don't count, and neither does damage you
    /// deal yourself (REX's costs, Blood Tribute, Victor's payments). Falls and everything else do. Judged on the server.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Apollo", null, 0, typeof(Server))]
    public class ApolloFlawless : BaseAchievement
    {
        public const string Identifier = "CursedAppleApolloFlawless";

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
            private TeleporterInteraction? _tp;
            private bool _hurt;

            public override void OnInstall()
            {
                base.OnInstall();
                HealthLost.Init();
                HealthLost.OnLost += Lost;
                TeleporterInteraction.onTeleporterBeginChargingGlobal += Begin;
                TeleporterInteraction.onTeleporterChargedGlobal += Charged;
                BossGroup.onBossGroupDefeatedServer += BossDefeated;
                Stage.onServerStageBegin += NewStage;
            }

            public override void OnUninstall()
            {
                HealthLost.OnLost -= Lost;
                TeleporterInteraction.onTeleporterBeginChargingGlobal -= Begin;
                TeleporterInteraction.onTeleporterChargedGlobal -= Charged;
                BossGroup.onBossGroupDefeatedServer -= BossDefeated;
                Stage.onServerStageBegin -= NewStage;
                base.OnUninstall();
            }

            private void NewStage(Stage stage) => _tp = null;

            private void Begin(TeleporterInteraction tp)
            {
                _tp = tp;
                _hurt = false;
            }

            private void Lost(CharacterBody body)
            {
                if (_tp != null && IsCurrentBody(body)) _hurt = true;
            }

            private void Charged(TeleporterInteraction tp)
            {
                if (tp == _tp && (tp.bossGroup == null || !tp.bossGroup.enabled)) Finish();
            }

            private void BossDefeated(BossGroup group)
            {
                if (_tp != null && group == _tp.bossGroup && _tp.isCharged) Finish();
            }

            private void Finish()
            {
                bool flawless = !_hurt && GetCurrentBody() != null;
                _tp = null;
                if (flawless) Grant();
            }
        }
    }

    /// <summary>Server: a player lost health or shield to a hit that wasn't their own doing.</summary>
    internal static class HealthLost
    {
        public static event Action<CharacterBody>? OnLost;
        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.HealthComponent.TakeDamage += TakeDamage;
        }

        private static void TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo info)
        {
            CharacterBody? body = self != null ? self.body : null;
            bool watch = body != null && info != null && body.isPlayerControlled
                && info.attacker != body.gameObject && !(info.attacker == null && info.inflictor == body.gameObject);
            float before = watch ? self!.health + self.shield : 0f;
            orig(self!, info!);
            if (watch && self!.health + self.shield < before) OnLost?.Invoke(body!);
        }
    }
}
