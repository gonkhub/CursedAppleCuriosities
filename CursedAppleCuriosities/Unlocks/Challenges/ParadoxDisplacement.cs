using System;
using System.Runtime.CompilerServices;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Paradox's Time Wall unlock (user, 2026-10-15): "Temporal Displacement", as Paradox swap places with a boss (any champion)
    /// using Paradoxical Swap, then kill it within 5 seconds of the swap. Server: the swap notes the boss (ParadoxSwap.Begin);
    /// its death credited to her inside the window grants it.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class ParadoxDisplacement : BaseAchievement
    {
        public const string Identifier = "CursedAppleParadoxDisplacement";
        public const string Skill = "CursedAppleParadoxWall";
        public const float Window = 5f;

        private class Swap { public CharacterBody? Paradox; public float At; }
        private static readonly ConditionalWeakTable<CharacterBody, Swap> _swapped = new ConditionalWeakTable<CharacterBody, Swap>();
        public static event Action<CharacterBody>? OnDisplaced;
        private static bool _hooked;

        /// <summary>Server: Paradox's swap shot reached this enemy.</summary>
        internal static void Swapped(CharacterBody paradox, CharacterBody? enemy)
        {
            if (enemy == null || !enemy.isChampion) return;
            Swap s = _swapped.GetOrCreateValue(enemy);
            s.Paradox = paradox;
            s.At = Time.time;
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            GlobalEventManager.onCharacterDeathGlobal += report =>
            {
                if (report?.victimBody == null || report.attackerBody == null || !_swapped.TryGetValue(report.victimBody, out Swap s)) return;
                if (s.Paradox == report.attackerBody && Time.time - s.At <= Window) OnDisplaced?.Invoke(report.attackerBody);
            };
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
                Hook();
                OnDisplaced += Check;
            }

            public override void OnUninstall()
            {
                OnDisplaced -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody paradox)
            {
                if (IsCurrentBody(paradox)) Grant();
            }
        }
    }
}
