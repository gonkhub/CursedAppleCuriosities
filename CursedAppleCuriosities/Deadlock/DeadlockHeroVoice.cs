using System.Collections.Generic;
using CursedAppleCuriosities.Survivors.Lash;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// A Deadlock hero's combat voice, by sound id "{hero}_vo_*": grunts when hit (big hits more often), a warning when
    /// health runs low, desperate ability lines while it stays low, kill lines (a killstreak line for twelve kills within 10 s,
    /// sometimes Deadlock's big-soul-difference kill line for a boss or elite), a death cry, and sometimes a teamwork line
    /// when the teleporter finishes charging.
    /// </summary>
    public static class DeadlockHeroVoice
    {
        private static readonly Dictionary<string, string> BodyToHero = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> BodyToVo = new Dictionary<string, string>();
        private static readonly Dictionary<GameObject, float> LastHealth = new Dictionary<GameObject, float>();
        private static readonly Dictionary<GameObject, List<float>> RecentKills = new Dictionary<GameObject, List<float>>();
        private static readonly Dictionary<GameObject, float> LastBigKill = new Dictionary<GameObject, float>();
        private static bool _hooked;

        /// <summary>Below this share of health the hero warns once, casts with desperation lines and skips teleporter lines.</summary>
        public const float LowHealth = 0.35f;

        // Boss / elite kills (user: both only sometimes): Deadlock's kill_high_networth, one take per hero.
        public const float BigKillBossChance = 0.35f;
        public const float BigKillEliteChance = 0.2f;
        public const float BigKillCooldown = 60f;   // per hero body

        // Teleporter charged: one random healthy Deadlock hero, this often, server-picked so everyone hears the same line.
        public const float TeleporterChance = 0.5f;

        /// <param name="hero">Sound id prefix ("abrams").</param>
        /// <param name="vo">Deadlock's VO folder for the hero ("atlas").</param>
        public static void Track(string bodyName, string hero, string vo)
        {
            BodyToHero[bodyName] = hero;
            BodyToVo[bodyName] = vo;
            DeadlockSounds.Register(new DeadlockSound { Id = hero + "_vo_kill_big", VpkPrefix = $"sounds/vo/{vo}/{vo}_kill_high_networth", Voice = true });
            DeadlockSounds.Register(new DeadlockSound { Id = hero + "_vo_teleporter_congrats", VpkPrefix = $"sounds/vo/{vo}/{vo}_congrats", Range = 150f, MinGain = DeadlockPingVoice.TeamCallout, Voice = true });
            DeadlockSounds.Register(new DeadlockSound { Id = hero + "_vo_teleporter_happy", VpkPrefix = $"sounds/vo/{vo}/{vo}_happy", Range = 150f, MinGain = DeadlockPingVoice.TeamCallout, Voice = true });
            if (_hooked) return;
            _hooked = true;
            GlobalEventManager.onCharacterDeathGlobal += OnDeath;
            GlobalEventManager.onClientDamageNotified += OnDamaged;
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
        }

        /// <summary>
        /// An ability line, "{hero}_vo_powerN", or Deadlock's desperate take on it ("{hero}_vo_desperation_powerN")
        /// while the caster is below <see cref="LowHealth"/> and the hero has one.
        /// </summary>
        public static void Power(string id, GameObject speaker)
        {
            var hc = speaker.GetComponent<HealthComponent>();
            string desperate = id.Replace("_vo_power", "_vo_desperation_power");
            if (hc != null && hc.alive && hc.combinedHealth < hc.fullCombinedHealth * LowHealth && DeadlockSounds.Has(desperate))
                id = desperate;
            DeadlockSounds.Play(id, speaker.transform.position, speaker);
        }

        /// <summary>Deadlock's VO folder name for a tracked hero body ("atlas" for Abrams), or null.</summary>
        public static string? VoOf(CharacterBody? body)
        {
            if (body == null) return null;
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            return name != null && BodyToVo.TryGetValue(name, out string vo) ? vo : null;
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
            if (frac < LowHealth && before >= LowHealth) DeadlockSounds.Play(hero + "_vo_low_health", at, msg.victim);
        }

        /// <summary>Server (RoR2 only reports deaths there): the line is picked here and sent to every client.</summary>
        private static void OnDeath(DamageReport report)
        {
            if (!NetworkServer.active) return;
            CharacterBody? attacker = report.attackerBody;
            string? killer = HeroOf(attacker);
            if (killer != null)
            {
                GameObject who = attacker!.gameObject;
                if (!RecentKills.TryGetValue(who, out List<float> kills)) RecentKills[who] = kills = new List<float>();
                kills.Add(Time.time);
                kills.RemoveAll(t => Time.time - t > 10f);
                string line = "_vo_kill";
                if (kills.Count >= 12) { line = "_vo_killstreak"; kills.Clear(); }
                else if (BigKill(report.victimBody, who)) line = "_vo_kill_big";
                SoundMessage.Everyone(killer + line, attacker.corePosition, who);
            }
            string? victim = HeroOf(report.victimBody);
            if (victim != null) SoundMessage.Everyone(victim + "_vo_death", report.victimBody!.corePosition);
            if (report.victimBody != null)
            {
                LastHealth.Remove(report.victimBody.gameObject);
                RecentKills.Remove(report.victimBody.gameObject);
                LastBigKill.Remove(report.victimBody.gameObject);
            }
        }

        private static bool BigKill(CharacterBody? victim, GameObject killer)
        {
            if (victim == null) return false;
            float chance = victim.isChampion || victim.isBoss ? BigKillBossChance : victim.isElite ? BigKillEliteChance : 0f;
            if (chance <= 0f || Random.value > chance) return false;
            if (LastBigKill.TryGetValue(killer, out float last) && Time.time - last < BigKillCooldown) return false;
            LastBigKill[killer] = Time.time;
            return true;
        }

        /// <summary>Server: sometimes one living Deadlock hero above <see cref="LowHealth"/> cheers the team on.</summary>
        private static void OnTeleporterCharged(TeleporterInteraction _)
        {
            if (!NetworkServer.active || Random.value > TeleporterChance) return;
            var heroes = new List<CharacterBody>();
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
            {
                if (b == null || !b.isPlayerControlled || HeroOf(b) == null) continue;
                if (b.healthComponent == null || !b.healthComponent.alive || b.healthComponent.combinedHealthFraction < LowHealth) continue;
                heroes.Add(b);
            }
            if (heroes.Count == 0) return;
            CharacterBody speaker = heroes[Random.Range(0, heroes.Count)];
            string hero = HeroOf(speaker)!;
            // Every take equally likely: Deadlock has 5 congrats and 10 happy takes (Celeste: congrats only). A host without
            // Deadlock knows no takes, so it falls back to those counts.
            int congrats = DeadlockSounds.Takes(hero + "_vo_teleporter_congrats"), happy = DeadlockSounds.Takes(hero + "_vo_teleporter_happy");
            if (congrats + happy == 0) { congrats = 5; happy = 10; }
            string id = Random.Range(0, congrats + happy) < congrats ? "_vo_teleporter_congrats" : "_vo_teleporter_happy";
            SoundMessage.Everyone(hero + id, speaker.corePosition, speaker.gameObject);
        }
    }
}
