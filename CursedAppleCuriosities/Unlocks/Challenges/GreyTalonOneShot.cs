using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Grey Talon's unlock (user, 2026-10-15): kill a boss (any champion) with a single hit from full health, as any survivor.
    /// One direct damage instance from your own body takes it from at least 99% of its combined health (shields included) to
    /// dead: no damage over time, no proc-made hits, no drones or minions; a shotgun's pellets are separate hits. Judged on the
    /// server (where damage lands), granted to that player.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.GreyTalon", null, 0, typeof(Server))]
    public class GreyTalonOneShot : BaseAchievement
    {
        public const string Identifier = "CursedAppleGreyTalonOneShot";
        public const float FullHealth = 0.99f;

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
                BossOneShot.Init();
                BossOneShot.OnOneShot += Check;
            }

            public override void OnUninstall()
            {
                BossOneShot.OnOneShot -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody attacker)
            {
                if (IsCurrentBody(attacker)) Grant();
            }
        }
    }

    /// <summary>Server: a champion killed by one direct hit from (nearly) full health, and by whom.</summary>
    internal static class BossOneShot
    {
        public static event Action<CharacterBody>? OnOneShot;
        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.HealthComponent.TakeDamage += TakeDamage;
        }

        private static void TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo info)
        {
            bool candidate = self != null && self.alive && self.body != null && self.body.isChampion
                && self.combinedHealthFraction >= GreyTalonOneShot.FullHealth
                && info != null && info.dotIndex == DotController.DotIndex.None && info.procChainMask.Equals(default(ProcChainMask))
                && info.attacker != null;
            orig(self!, info!);
            if (!candidate || self!.alive) return;
            CharacterBody? attacker = info!.attacker.GetComponent<CharacterBody>();
            if (attacker != null && attacker.isPlayerControlled) OnOneShot?.Invoke(attacker);
        }
    }
}
