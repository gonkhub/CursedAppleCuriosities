using System;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks
{
    /// <summary>
    /// Every shot a body fires itself, on the machine that fires it (players fire on their own client): a bullet attack or
    /// a projectile, not one a proc made (missiles, Ukulele...: their proc chain isn't empty). Challenges listen and keep
    /// only their own player's body (drones and turrets are other bodies).
    /// </summary>
    public static class ShotTracker
    {
        /// <summary>(the shooter's body object, Time.fixedTime of the shot).</summary>
        public static event Action<GameObject, float>? OnShot;

        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.BulletAttack.Fire += BulletFire;
            On.RoR2.Projectile.ProjectileManager.FireProjectile_FireProjectileInfo += ProjectileFire;
        }

        private static void BulletFire(On.RoR2.BulletAttack.orig_Fire orig, BulletAttack self)
        {
            orig(self);
            if (self.owner != null && self.procChainMask.Equals(default(ProcChainMask))) OnShot?.Invoke(self.owner, Time.fixedTime);
        }

        private static void ProjectileFire(On.RoR2.Projectile.ProjectileManager.orig_FireProjectile_FireProjectileInfo orig, ProjectileManager self, FireProjectileInfo info)
        {
            orig(self, info);
            if (info.owner != null && info.procChainMask.Equals(default(ProcChainMask))) OnShot?.Invoke(info.owner, Time.fixedTime);
        }
    }
}
