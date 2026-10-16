using System;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Apollo's Riposte unlock (user, 2026-10-15; En Garde moved to his Sapphire skin): "Pride of Ixia", as Apollo kill 10 enemies
    /// with one Itani Lo Sahn. Its strike and the finishing blow that follows both count (Special damage from Apollo). Each cast
    /// opens a window as long as the finisher's lock plus a second; a new cast starts the count over.
    /// Server: the strike's hit (ApolloNet) reports the cast; deaths are read from the game.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class ApolloPrideOfIxia : BaseAchievement
    {
        public const string Identifier = "CursedAppleApolloPrideOfIxia";
        public const string Skill = "CursedAppleApolloRiposte";
        public const int Kills = 10;

        public static event Action<CharacterBody, float>? OnCast;
        /// <summary>Server: an Itani Lo Sahn strike landed; its finishers come within this many seconds.</summary>
        internal static void Cast(CharacterBody apollo, float window) => OnCast?.Invoke(apollo, window);

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
            private float _until = -1f;
            private int _kills;

            public override void OnInstall()
            {
                base.OnInstall();
                OnCast += Started;
                GlobalEventManager.onCharacterDeathGlobal += Died;
            }

            public override void OnUninstall()
            {
                OnCast -= Started;
                GlobalEventManager.onCharacterDeathGlobal -= Died;
                base.OnUninstall();
            }

            private void Started(CharacterBody apollo, float window)
            {
                if (!IsCurrentBody(apollo)) return;
                _kills = 0;
                _until = Time.time + window;
            }

            private void Died(DamageReport report)
            {
                if (Time.time > _until || report?.attackerBody == null || report.damageInfo == null || !IsCurrentBody(report.attackerBody)) return;
                if (report.damageInfo.damageType.damageSource != DamageSource.Special) return;
                if (++_kills >= Kills) Grant();
            }
        }
    }
}
