using System;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Shiv's Alt Fire unlock (user, 2026-10-15): "Chain Reaction", as Shiv chain 6 Killing Blow executes. Each execute refunds
    /// the cooldown; a Killing Blow that ends without executing breaks the chain, and so do more than 10 s between executes.
    /// Judged where the lunge runs (Shiv's own machine): only it knows whether a lunge ended in an execute.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, null)]
    public class ShivChainReaction : BaseAchievement
    {
        public const string Identifier = "CursedAppleShivChainReaction";
        public const string Skill = "CursedAppleShivAlt";
        public const int Chain = 6;
        public const float MaxGap = 10f;

        /// <summary>A Killing Blow ended on the lunging body's own machine: did it execute?</summary>
        public static event Action<GameObject, bool>? OnKillingBlow;
        internal static void KillingBlowEnded(GameObject shiv, bool executed) => OnKillingBlow?.Invoke(shiv, executed);

        private int _chain;
        private float _last = -999f;

        public override void OnInstall()
        {
            base.OnInstall();
            OnKillingBlow += Ended;
        }

        public override void OnUninstall()
        {
            OnKillingBlow -= Ended;
            base.OnUninstall();
        }

        private void Ended(GameObject shiv, bool executed)
        {
            CharacterBody? body = localUser?.cachedBody;
            if (body == null || shiv != body.gameObject) return;
            if (!executed) { _chain = 0; return; }
            if (Time.time - _last > MaxGap) _chain = 0;
            _last = Time.time;
            if (++_chain >= Chain) Grant();
        }
    }
}
