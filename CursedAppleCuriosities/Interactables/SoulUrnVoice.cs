using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Interactables
{
    /// <summary>
    /// The Soul Urn talks with Deadlock's spirit jar lines (sounds/vo/spirit_jar). While it waits it calls out from its
    /// spot every so often, loud enough to be heard on the way over; it reacts to being picked up (a line of its own for
    /// each Deadlock hero), carried too long, dropped and delivered. <see cref="SoulUrn"/>'s events fire on every
    /// machine, so each client plays its own copy; nothing here is networked.
    /// Takes were picked by the user from a review page: lines naming Deadlock places (the temple, New York...) are left out.
    /// </summary>
    public static class SoulUrnVoice
    {
        private const string Vo = "sounds/vo/spirit_jar/spirit_jar_";

        // Deadlock take numbers per line set.
        private static readonly Dictionary<string, string[]> Sets = new Dictionary<string, string[]>
        {
            ["idol_waiting_in_street"] = new[] { "02", "02_alt_01", "03", "04", "05", "06", "07", "08", "09" },
            ["idol_waiting_on_bridge"] = new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13", "17", "18", "19", "20" },
            ["idol_picked_up_on_street"] = new[] { "01", "02", "03", "04", "07", "08", "09" },
            ["idol_picked_up_on_bridge"] = new[] { "01", "02", "06", "07" },
            ["idol_delivered"] = new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09" },
            ["idol_dropped"] = new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "10" },
            ["idol_holder_gets_stunned"] = new[] { "01", "02", "03", "04", "05" },
            ["idol_holder_takes_big_damage"] = new[] { "01", "02", "03", "04", "05" },
            ["idol_holder_stalls"] = new[] { "01", "02", "03", "04", "05" },
            // Picked up by a Deadlock hero: Deadlock's per-hero carrier lines (Lash: "Don't you dare take me to The Bear Pit").
            ["holder_stalls_lash"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_atlas"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_gigawatt"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_viper"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_orion"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_frank"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_viscous"] = new[] { "01", "02", "03", "04", "05" },
            ["holder_stalls_unicorn"] = new[] { "01", "02", "04", "05" },
        };

        private static readonly string[] Waiting = { "idol_waiting_in_street", "idol_waiting_on_bridge" };
        private static readonly string[] PickedUp = { "idol_picked_up_on_street", "idol_picked_up_on_bridge" };

        public const float WaitingRange = 50f;     // heard on the way over, silent across the map (user: needed attenuation)
        public const float CarriedRange = 35f;
        public const float FirstCall = 6f;         // after it appears or snaps back home
        public const float CallGapMin = 14f, CallGapMax = 22f;
        public const float StallShare = 0.4f;      // "you do have a plan, yes?" once this share of the time limit is left

        private static readonly Dictionary<SoulUrnController, float> NextCall = new Dictionary<SoulUrnController, float>();
        private static readonly HashSet<SoulUrnController> Stalled = new HashSet<SoulUrnController>();
        private static readonly Dictionary<string, int> LastTake = new Dictionary<string, int>();

        public static void Init(GameObject host)
        {
            foreach (var set in Sets)
                foreach (string take in set.Value)
                    DeadlockSounds.Register(new DeadlockSound
                    {
                        Id = $"urn_{set.Key}_{take}",
                        VpkPrefix = $"{Vo}{set.Key}_{take}",
                        Range = set.Key.StartsWith("idol_waiting") ? WaitingRange : CarriedRange,
                        Voice = true,
                    });
            DeadlockSounds.Ensure(host);
            host.AddComponent<Ticker>();
            SoulUrn.Waiting += urn => { NextCall[urn] = Time.time + FirstCall; Stalled.Remove(urn); };
            SoulUrn.PickedUp += OnPickedUp;
            SoulUrn.Dropped += OnDropped;
            SoulUrn.Delivered += (urn, carrier) => Say("idol_delivered", urn, carrier);
        }

        private static void OnPickedUp(SoulUrnController urn, CharacterBody carrier)
        {
            NextCall.Remove(urn);
            Stalled.Remove(urn);
            string? vo = DeadlockHeroVoice.VoOf(carrier);
            Say(vo != null && Sets.ContainsKey("holder_stalls_" + vo) ? "holder_stalls_" + vo : Pick(PickedUp), urn, carrier);
        }

        private static void OnDropped(SoulUrnController urn, CharacterBody? carrier, SoulUrnDropReason reason)
        {
            // Hit out of the carrier's hands: "They're shooting at me?", or "Get up!" when the hit stunned them.
            string set = reason == SoulUrnDropReason.Damaged
                ? (Stunned(carrier) ? "idol_holder_gets_stunned" : "idol_holder_takes_big_damage")
                : "idol_dropped";
            // It snaps back home right after, but the line reacts to the drop: it comes from where that happened, so the
            // player who dropped it hears it.
            Say(set, urn, null, carrier != null ? carrier.corePosition : (Vector3?)null);
        }

        private static bool Stunned(CharacterBody? body)
        {
            var esm = body != null ? EntityStateMachine.FindByCustomName(body.gameObject, "Body") : null;
            var state = esm != null ? esm.state : null;
            return state is EntityStates.StunState || state is EntityStates.FrozenState || state is EntityStates.ShockState;
        }

        /// <summary>
        /// A random take (never the last one again): riding on its carrier while carried, else from <paramref name="at"/>
        /// or the urn's spot. It is always its own speaker ("urn"), never gated by its carrier's lines.
        /// </summary>
        private static void Say(string set, SoulUrnController urn, CharacterBody? carrier, Vector3? at = null)
        {
            if (urn == null || !Sets.TryGetValue(set, out string[] takes) || takes.Length == 0) return;
            int i = Random.Range(0, takes.Length);
            if (takes.Length > 1 && LastTake.TryGetValue(set, out int last) && i == last) i = (i + 1 + Random.Range(0, takes.Length - 1)) % takes.Length;
            LastTake[set] = i;
            GameObject? follow = carrier != null ? carrier.gameObject : null;
            DeadlockSounds.Play($"urn_{set}_{takes[i]}", follow != null ? follow.transform.position : at ?? urn.Home + Vector3.up, follow, "urn");
        }

        private static string Pick(string[] sets) => sets[Random.Range(0, sets.Length)];

        /// <summary>Every client: the waiting calls, and one "do you have a plan?" when a carry runs long.</summary>
        private class Ticker : MonoBehaviour
        {
            private void Update()
            {
                foreach (SoulUrnController urn in SoulUrnController.Instances)
                {
                    if (urn == null) continue;
                    if (urn.IsWaiting)
                    {
                        if (!NextCall.TryGetValue(urn, out float at)) NextCall[urn] = at = Time.time + FirstCall;
                        if (Time.time < at) continue;
                        NextCall[urn] = Time.time + Random.Range(CallGapMin, CallGapMax);
                        Say(Pick(Waiting), urn, null);
                    }
                    else if (urn.Carrier != null && !Stalled.Contains(urn) && urn.Remaining > 0f && urn.Remaining < SoulUrn.TimeLimit * StallShare)
                    {
                        Stalled.Add(urn);
                        Say("idol_holder_stalls", urn, urn.Carrier);
                    }
                }
                if (NextCall.Count > SoulUrnController.Instances.Count)
                {
                    var gone = new List<SoulUrnController>();
                    foreach (SoulUrnController urn in NextCall.Keys) if (urn == null) gone.Add(urn!);
                    foreach (SoulUrnController urn in gone) { NextCall.Remove(urn); Stalled.Remove(urn); }
                }
            }
        }
    }
}
