using System.Collections.Generic;
using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Vyper's unlock (user, 2026-10-15): "Rattler", fire 25 shots within 1 second, as any survivor. A shot is a bullet attack
    /// or projectile your own body fires; everything fired in the same physics step is one shot (a shotgun's pellets, however
    /// they're fired), and proc-made shots and drones don't count. Unlocks Vyper for that player.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Vyper", null, 0, null)]
    public class VyperRattler : BaseAchievement
    {
        public const string Identifier = "CursedAppleVyperRattler";
        public const int Shots = 25;
        public const float Window = 1f;

        private readonly Queue<float> _shots = new Queue<float>();
        private float _lastStep = -1f;

        public override void OnInstall()
        {
            base.OnInstall();
            ShotTracker.OnShot += Shot;
        }

        public override void OnUninstall()
        {
            ShotTracker.OnShot -= Shot;
            base.OnUninstall();
        }

        private void Shot(GameObject owner, float step)
        {
            CharacterBody? body = localUser?.cachedBody;
            if (body == null || owner != body.gameObject || step == _lastStep) return;
            _lastStep = step;
            _shots.Enqueue(step);
            while (_shots.Count > 0 && _shots.Peek() <= step - Window) _shots.Dequeue();
            if (_shots.Count >= Shots) Grant();
        }
    }
}
