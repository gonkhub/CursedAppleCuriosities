using System.Collections.Generic;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Celeste's unlock (user, 2026-10-15): "Showstopper", kill 30 enemies within 5 seconds, as any survivor. Kills credited to
    /// your own body count (your damage over time and item procs included); drones, turrets and minions are other bodies.
    /// A rolling window on the server.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Celeste", null, 0, typeof(Server))]
    public class CelesteShowstopper : BaseAchievement
    {
        public const string Identifier = "CursedAppleCelesteShowstopper";
        public const int Kills = 30;
        public const float Window = 5f;

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
            private readonly Queue<float> _kills = new Queue<float>();

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
                if (report?.attackerBody == null || !IsCurrentBody(report.attackerBody)) return;
                if (report.victimTeamIndex == report.attackerTeamIndex) return;
                float now = Time.fixedTime;
                _kills.Enqueue(now);
                while (_kills.Count > 0 && _kills.Peek() <= now - Window) _kills.Dequeue();
                if (_kills.Count >= Kills) Grant();
            }
        }
    }
}
