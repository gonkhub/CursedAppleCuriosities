using System;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Seven's unlock (user, 2026-10-16; he started unlocked until then): "Electric Chair", die to a lightning strike, as any survivor
    /// (user's name and rule). Counts: the storms' lightning strikes (Seekers of
    /// the Storm's LightningStrikeInstance, the inflictor of its blast), lightning orbs landing (Royal Capacitor and Charged Perforator
    /// strikes, Ukulele chain lightning: on monsters through Evolution, the Void Fields...), and the Overloading elites' and Worm's
    /// lightning. Server: the death report, plus a flag held while a lightning orb lands (its damage, and so the death, happens inside).
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Seven", null, 0, typeof(Server))]
    public class SevenElectricChair : BaseAchievement
    {
        public const string Identifier = "CursedAppleSevenElectricChair";

        [ThreadStatic] private static int _orbLanding;
        private static bool _hooked;

        /// <summary>Once: mark the moments lightning orbs land.</summary>
        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.Orbs.LightningStrikeOrb.OnArrival += (orig, self) => { _orbLanding++; try { orig(self); } finally { _orbLanding--; } };
            On.RoR2.Orbs.SimpleLightningStrikeOrb.OnArrival += (orig, self) => { _orbLanding++; try { orig(self); } finally { _orbLanding--; } };
            On.RoR2.Orbs.LightningOrb.OnArrival += (orig, self) => { _orbLanding++; try { orig(self); } finally { _orbLanding--; } };
        }

        /// <summary>Was this death lightning?</summary>
        private static bool IsLightning(DamageReport report)
        {
            if (_orbLanding > 0) return true;
            GameObject? inflictor = report.damageInfo?.inflictor;
            GameObject? attacker = report.damageInfo?.attacker;
            if (inflictor != null && (inflictor.GetComponent<LightningStrikeInstance>() != null || inflictor.name.IndexOf("Lightning", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (attacker != null && attacker.GetComponent<LightningStrikeInstance>() != null) return true;
            CharacterBody? by = report.attackerBody;
            return by != null && by.name.StartsWith("ElectricWorm", StringComparison.Ordinal); // the Overloading Worm
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
                GlobalEventManager.onCharacterDeathGlobal += Died;
            }

            public override void OnUninstall()
            {
                GlobalEventManager.onCharacterDeathGlobal -= Died;
                base.OnUninstall();
            }

            private void Died(DamageReport report)
            {
                if (report?.victimBody == null || !IsCurrentBody(report.victimBody)) return;
                if (IsLightning(report)) Grant();
            }
        }
    }
}
