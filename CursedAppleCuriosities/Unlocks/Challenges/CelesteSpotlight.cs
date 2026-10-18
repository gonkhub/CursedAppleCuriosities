using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Celeste's Radiant Daggers unlock (user, 2026-10-15): "Spotlight", as Celeste have 15 enemies marked by Light Eater at
    /// the same time (from any number of flares). Marks only appear when a flare lands, so the server counts the live marked
    /// enemies right after each of her flares.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Skills." + Skill, null, 0, typeof(Server))]
    public class CelesteSpotlight : BaseAchievement
    {
        public const string Identifier = "CursedAppleCelesteSpotlight";
        public const string Skill = "CursedAppleCelesteBeam";
        public const int Marked = 15;

        public static event Action<CharacterBody>? OnSpotlight;

        /// <summary>Server, after a Light Eater flare: count every live enemy carrying the mark.</summary>
        internal static void AfterFlare(CharacterBody celeste, BuffDef? mark)
        {
            if (mark == null || OnSpotlight == null) return;
            int marked = 0;
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
                if (b != null && b.healthComponent != null && b.healthComponent.alive && b.HasBuff(mark)) marked++;
            if (marked >= Marked) OnSpotlight(celeste);
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
                OnSpotlight += Check;
            }

            public override void OnUninstall()
            {
                OnSpotlight -= Check;
                base.OnUninstall();
            }

            private void Check(CharacterBody celeste)
            {
                if (IsCurrentBody(celeste)) Grant();
            }
        }
    }
}
