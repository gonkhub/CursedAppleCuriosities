using System.Collections.Generic;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Server, on a Pride of Ixia sword wave: Deadlock fires it as a slow, wide projectile (citadel_weapon_fencer_set: 5000 hu/s,
    /// 10 hu radius, 1000 hu range, no gravity). Here a small spinning blade (user 2.5) that stops at the first enemy it reaches,
    /// sweeping its path each step so nothing slips between steps, or at terrain or the end of its range.
    /// </summary>
    [RequireComponent(typeof(ProjectileController))]
    public class ApolloWaveShot : MonoBehaviour
    {
        private readonly HashSet<HealthComponent> _hit = new HashSet<HealthComponent>();
        private Vector3 _last;
        private bool _started;

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            Vector3 now = transform.position;
            if (!_started) { _started = true; _last = now; return; }
            var pc = GetComponent<ProjectileController>();
            var pd = GetComponent<ProjectileDamage>();
            CharacterBody? by = pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            if (by == null || by.teamComponent == null) { Destroy(gameObject); return; }
            Vector3 d = now - _last;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;
            bool blocked = Physics.Raycast(_last, dir, out RaycastHit wall, len, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            float reach = blocked ? wall.distance : len;
            RaycastHit[] hits = Physics.SphereCastAll(_last, ApolloData.WaveRadius, dir, reach, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance)); // the nearest is the one it reaches first
            foreach (RaycastHit h in hits)
            {
                var box = h.collider != null ? h.collider.GetComponent<HurtBox>() : null;
                HealthComponent? hc = box != null ? box.healthComponent : null;
                if (hc == null || !hc.alive || box!.teamIndex == by.teamComponent.teamIndex || !_hit.Add(hc)) continue;
                ApolloNet.Hit(by, hc, pd != null ? pd.damage : by.damage * ApolloData.WaveDamage, pd != null && pd.crit, dir * 200f, DamageSource.Primary);
                ApolloNet.Effect(ApolloSurvivor.SlashEffect, ApolloNet.Core(hc), 1.2f, dir);
                Destroy(gameObject); // no pierce (user 2.5)
                return;
            }
            _last = now;
            if (blocked) Destroy(gameObject);
        }
    }
}
