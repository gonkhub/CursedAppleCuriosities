using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// A Deadlock hero's combat voice, by sound id "{hero}_vo_*": grunts when hit (big hits more often), a warning when
    /// health runs low, kill lines (a killstreak line for twelve kills within 10 s) and a death cry.
    /// </summary>
    public static class DeadlockHeroVoice
    {
        private static readonly Dictionary<string, string> BodyToHero = new Dictionary<string, string>();
        private static readonly Dictionary<GameObject, float> LastHealth = new Dictionary<GameObject, float>();
        private static readonly Dictionary<string, List<float>> RecentKills = new Dictionary<string, List<float>>();
        private static bool _hooked;

        public static void Track(string bodyName, string hero)
        {
            BodyToHero[bodyName] = hero;
            if (_hooked) return;
            _hooked = true;
            GlobalEventManager.onCharacterDeathGlobal += OnDeath;
            GlobalEventManager.onClientDamageNotified += OnDamaged;
        }

        private static string? HeroOf(CharacterBody? body)
        {
            if (body == null) return null;
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            return name != null && BodyToHero.TryGetValue(name, out string hero) ? hero : null;
        }

        /// <summary>Every client.</summary>
        private static void OnDamaged(DamageDealtMessage msg)
        {
            if (msg.victim == null) return;
            var hc = msg.victim.GetComponent<HealthComponent>();
            string? hero = hc != null ? HeroOf(hc.body) : null;
            if (hero == null || !hc!.alive) return;
            float max = Mathf.Max(1f, hc.fullCombinedHealth);
            Vector3 at = msg.victim.transform.position;
            DeadlockSounds.Play(hero + (msg.damage >= max * 0.15f ? "_vo_pain" : "_vo_pain_small"), at, msg.victim);
            float frac = hc.combinedHealth / max;
            float before = LastHealth.TryGetValue(msg.victim, out float b) ? b : 1f;
            LastHealth[msg.victim] = frac;
            if (frac < 0.25f && before >= 0.25f) DeadlockSounds.Play(hero + "_vo_low_health", at, msg.victim);
        }

        private static void OnDeath(DamageReport report)
        {
            string? killer = HeroOf(report.attackerBody);
            if (killer != null && report.attackerBody!.hasEffectiveAuthority)
            {
                if (!RecentKills.TryGetValue(killer, out List<float> kills)) RecentKills[killer] = kills = new List<float>();
                kills.Add(Time.time);
                kills.RemoveAll(t => Time.time - t > 10f);
                if (kills.Count >= 12) { DeadlockSounds.Play(killer + "_vo_killstreak", report.attackerBody.corePosition, report.attackerBody.gameObject); kills.Clear(); }
                else DeadlockSounds.Play(killer + "_vo_kill", report.attackerBody.corePosition, report.attackerBody.gameObject);
            }
            string? victim = HeroOf(report.victimBody);
            if (victim != null) DeadlockSounds.Play(victim + "_vo_death", report.victimBody!.corePosition);
            if (report.victimBody != null) LastHealth.Remove(report.victimBody.gameObject);
        }
    }
}
