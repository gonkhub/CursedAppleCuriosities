using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Viscous' unlock (user, 2026-10-15): "The Cube", survive a single hit that would have dealt more than your maximum health,
    /// as any survivor. The hit's raw damage (before armour, block or barrier; crits doubled) is over your full health plus
    /// shield, and you're still alive after it: soaked by barrier, armour, Tougher Times, invulnerability, anything. Damage over
    /// time, non-lethal and self-inflicted hits don't count, and a revive (Dio's...) isn't surviving. Judged on the server.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Viscous", null, 0, typeof(Server))]
    public class ViscousCube : BaseAchievement
    {
        public const string Identifier = "CursedAppleViscousCube";

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
                HugeHitSurvived.Init();
                HugeHitSurvived.OnSurvived += Check;
            }

            public override void OnUninstall()
            {
                HugeHitSurvived.OnSurvived -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody body)
            {
                if (IsCurrentBody(body)) Grant();
            }
        }
    }

    /// <summary>Server: a player lived through one hit bigger than their whole health bar.</summary>
    internal static class HugeHitSurvived
    {
        public static event Action<CharacterBody>? OnSurvived;
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
            bool candidate = body != null && info != null && body.isPlayerControlled && self!.alive
                && info.dotIndex == DotController.DotIndex.None
                && (info.damageType.damageType & DamageType.NonLethal) == 0
                && info.attacker != body.gameObject
                && info.damage * (info.crit ? 2f : 1f) > self.fullHealth + self.fullShield;
            orig(self!, info!);
            if (candidate && self!.alive) OnSurvived?.Invoke(body!);
        }
    }
}
