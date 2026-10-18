using System.Collections;
using System.Reflection;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Shiv's unlock (user 3.0, replaced "Killing Blow"): "Bloodletting", have 50 of your own Bleed stacks on a single enemy at once (100 until 3.1, user),
    /// as any survivor (Tri-Tip Daggers, Bandit, ...). Only stacks you inflicted count, not other players'. Checked on the server
    /// each time a bleed lands, by counting the victim's DoT stacks from your body.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Shiv", null, 0, typeof(Server))]
    public class ShivBloodletting : BaseAchievement
    {
        public const string Identifier = "CursedAppleShivBloodletting";
        public const int Stacks = 50;

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
            // DotController keeps its stacks in a private list of a private class: read them by reflection.
            private static readonly FieldInfo? StackList = typeof(DotController).GetField("dotStackList", BindingFlags.NonPublic | BindingFlags.Instance);
            private static FieldInfo? _attacker, _index;

            public override void OnInstall()
            {
                base.OnInstall();
                DotController.onDotInflictedServerGlobal += OnDot;
            }

            public override void OnUninstall()
            {
                DotController.onDotInflictedServerGlobal -= OnDot;
                base.OnUninstall();
            }

            private void OnDot(DotController dot, ref InflictDotInfo info)
            {
                if (info.dotIndex != DotController.DotIndex.Bleed || info.attackerObject == null) return;
                CharacterBody? me = info.attackerObject.GetComponent<CharacterBody>();
                if (me == null || !IsCurrentBody(me)) return;
                if (StackList?.GetValue(dot) is not IList list) return;
                int mine = 0;
                foreach (object s in list)
                {
                    if (s == null) continue;
                    _attacker ??= s.GetType().GetField("attackerObject");
                    _index ??= s.GetType().GetField("dotIndex");
                    if (_attacker == null || _index == null) return;
                    if ((DotController.DotIndex)_index.GetValue(s) == DotController.DotIndex.Bleed && (GameObject)_attacker.GetValue(s) == info.attackerObject) mine++;
                }
                if (mine >= Stacks) Grant();
            }
        }
    }
}
