using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// A Deadlock hero calls out what they ping, with Deadlock's own ping lines (sounds/vo/{vo}/ping/{vo}_ping_*)
    /// matched to what RoR2's ping landed on: an enemy, an ally, a pickup, a chest, a shop, a shrine, the teleporter,
    /// a portal or just a spot. Re-pinging the same thing (RoR2's cancel) is "Never mind." Every client hears it:
    /// PingerController.RebuildPing runs on all of them.
    /// </summary>
    public static class DeadlockPingVoice
    {
        // Line sets: each is "{hero}_ping_{key}" for every key, one picked at random.
        private static readonly string[] Spot = { "look_here", "heading_here", "meet_here", "on_way" };
        private static readonly string[] Hurt = { "need_heal" };
        private static readonly string[] Crowd = { "enemy_count_big", "danger_area" };
        private static readonly string[] Enemy = { "attack_enemy", "see_enemy" };
        private static readonly string[] Boss = { "watch_out", "danger_area" };
        private static readonly string[] Ally = { "with_you" };
        private static readonly string[] Loot = { "item_suggestion" };
        private static readonly string[] Shop = { "going_shop" };
        private static readonly string[] Gamble = { "sinners" };          // Shrine of Chance: Deadlock's Sinner's Sacrifice
        private static readonly string[] Money = { "loose_souls" };       // a barrel: Deadlock's soul bag
        private static readonly string[] Teleporter = { "meet_here", "request_follow" };
        private static readonly string[] Portal = { "leaving" };
        private static readonly string[] Cancel = { "nevermind" };

        // Every key above; each is also the file name ("_NN" variants share it), and both heroes have them all.
        private static readonly string[] Lines =
        {
            "look_here", "heading_here", "meet_here", "on_way", "need_heal", "enemy_count_big", "danger_area",
            "attack_enemy", "see_enemy", "watch_out", "with_you", "item_suggestion", "going_shop", "sinners",
            "loose_souls", "request_follow", "leaving", "nevermind",
        };

        public const float LowHealth = 0.35f;     // pinging a spot while this hurt asks for healing
        public const int CrowdSize = 6;           // this many enemies near the ping is "a lot of 'em"
        public const float CrowdRadius = 20f;
        public const float Gap = 1.2f;            // per pinger, so ping spam doesn't stack lines

        private class Hero { public string Id = "", Vo = ""; }
        private static readonly Dictionary<string, Hero> BodyToHero = new Dictionary<string, Hero>();
        private static readonly Dictionary<PingerController, GameObject?> LastTarget = new Dictionary<PingerController, GameObject?>();
        private static readonly Dictionary<PingerController, float> LastLine = new Dictionary<PingerController, float>();
        private static bool _hooked;

        /// <param name="hero">Sound id prefix ("abrams").</param>
        /// <param name="vo">Deadlock's VO folder for the hero ("atlas").</param>
        public static void Track(string bodyName, string hero, string vo)
        {
            var h = new Hero { Id = hero, Vo = vo };
            foreach (string key in Lines) Register(h, key, key);
            // Pinging another Deadlock hero on your team: "You ready, Abrams?"
            foreach (Hero other in BodyToHero.Values)
            {
                Register(h, "check_on_" + other.Id, "check_on_" + other.Vo);
                Register(other, "check_on_" + h.Id, "check_on_" + h.Vo);
            }
            BodyToHero[bodyName] = h;
            if (_hooked) return;
            _hooked = true;
            On.RoR2.PingerController.RebuildPing += OnRebuildPing;
        }

        private static void Register(Hero h, string key, string file) => DeadlockSounds.Register(new DeadlockSound
        {
            Id = $"{h.Id}_ping_{key}",
            VpkPrefix = $"sounds/vo/{h.Vo}/ping/{h.Vo}_ping_{file}",
            Range = 400f, // Deadlock plays pings flat to the whole team; this keeps them audible map-wide
        });

        private static void OnRebuildPing(On.RoR2.PingerController.orig_RebuildPing orig, PingerController self, PingerController.PingInfo info)
        {
            orig(self, info);
            LastTarget.TryGetValue(self, out GameObject? before);
            LastTarget[self] = info.active ? info.targetGameObject : null;

            CharacterBody? body = self.GetComponent<CharacterMaster>()?.GetBody();
            if (body == null || !BodyToHero.TryGetValue(BodyCatalog.GetBodyName(body.bodyIndex) ?? "", out Hero hero)) return;
            if (LastLine.TryGetValue(self, out float last) && Time.time - last < Gap) return;

            string? key = info.active ? KeyFor(body, info.targetGameObject, info.origin, hero) : CancelKey(before);
            if (key == null) return;
            LastLine[self] = Time.time;
            DeadlockSounds.Play($"{hero.Id}_ping_{key}", body.corePosition, body.gameObject);
        }

        /// <summary>A cancel is re-pinging a target that is still there and usable; one used up, killed or picked up says nothing.</summary>
        private static string? CancelKey(GameObject? before)
        {
            if (before == null) return null;
            if (before.TryGetComponent(out CharacterBody b) && (b.healthComponent == null || !b.healthComponent.alive)) return null;
            if (before.TryGetComponent(out PurchaseInteraction p) && !p.available) return null;
            return Pick(Cancel);
        }

        private static string KeyFor(CharacterBody me, GameObject? target, Vector3 at, Hero hero)
        {
            if (target == null)
            {
                if (me.healthComponent != null && me.healthComponent.combinedHealthFraction < LowHealth) return Pick(Hurt);
                return EnemiesNear(me, at) >= CrowdSize ? Pick(Crowd) : Pick(Spot);
            }

            // RoR2 calls a body with no interaction an enemy ping; sort allies out of those.
            if (target.TryGetComponent(out CharacterBody body) && target.GetComponent<IInteractable>() == null)
            {
                if (body.teamComponent != null && body.teamComponent.teamIndex == me.teamComponent.teamIndex)
                {
                    string name = BodyCatalog.GetBodyName(body.bodyIndex) ?? "";
                    return BodyToHero.TryGetValue(name, out Hero other) && other != hero ? "check_on_" + other.Id : Pick(Ally);
                }
                if (body.isChampion || body.isBoss) return Pick(Boss);
                return EnemiesNear(me, body.corePosition) >= CrowdSize ? Pick(Crowd) : Pick(Enemy);
            }

            if (target.GetComponent<TeleporterInteraction>()) return Pick(Teleporter);
            if (target.GetComponent<SceneExitController>()) return Pick(Portal);
            if (target.GetComponent<GenericPickupController>() || target.GetComponent<PickupPickerController>()) return Pick(Loot);
            if (target.GetComponent<ShopTerminalBehavior>()) return Pick(Shop);
            if (target.GetComponent<ShrineChanceBehavior>()) return Pick(Gamble);
            if (target.GetComponent<BarrelInteraction>()) return Pick(Money);
            if (target.GetComponent<ChestBehavior>() || target.GetComponent<SummonMasterBehavior>()) return Pick(Loot);
            return Pick(Spot);
        }

        private static int EnemiesNear(CharacterBody me, Vector3 at)
        {
            int n = 0;
            TeamIndex mine = me.teamComponent.teamIndex;
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
            {
                if (b == null || b.teamComponent == null || b.teamComponent.teamIndex == mine || b.teamComponent.teamIndex == TeamIndex.Neutral) continue;
                if (b.healthComponent == null || !b.healthComponent.alive) continue;
                if ((b.corePosition - at).sqrMagnitude <= CrowdRadius * CrowdRadius) n++;
            }
            return n;
        }

        private static string Pick(string[] keys) => keys[Random.Range(0, keys.Length)];
    }
}
