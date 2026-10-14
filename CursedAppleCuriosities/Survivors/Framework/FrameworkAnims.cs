using System.Collections.Generic;

namespace CursedAppleCuriosities.Survivors.Framework
{
    /// <summary>
    /// The movement half of an animation table, for AnimGraph2 heroes whose clips follow Deadlock's usual names (with the few
    /// names that differ per hero passed in), plus the hero's own ability rows.
    /// </summary>
    public static class FrameworkAnims
    {
        public class Names
        {
            public string Idle = "weapon_stand_idle", Run = "weapon_run_", Sprint = "out_of_combat_run_";
            public string Jump = "jump_ground", AirJump = "jump_ground", InAir = "in_air_loop_apex", Falling = "in_air_loop_down";
            public string Land = "landing_impact_idle", DashGround = "dash_ground", DashAir = "dash_air_";
            public string SlideStart = "slide_start", SlideLoop = "slide_loop", SlideGetup = "slide_getup";
            public string Shoot = "shoot_idle", ShootLayer = "delta", Parry = "parry", Death = "knockdown_large_start";
        }

        public static (string, string, bool, string, string)[] Table(Names n, params (string State, string Clip, bool Loop, string Next, string Layer)[] abilities)
        {
            var t = new List<(string, string, bool, string, string)> { ("idle", n.Idle, true, "", "full") };
            foreach (string d in new[] { "n", "ne", "e", "se", "s", "sw", "w", "nw" })
            {
                t.Add(("run_" + d, n.Run + d, true, "", "full"));
                t.Add(("sprint_" + d, n.Sprint + d, true, "", "full"));
            }
            t.Add(("jump", n.Jump, false, "in_air", "full"));
            t.Add(("air_jump", n.AirJump, false, "in_air", "full"));
            t.Add(("wall_jump", n.Jump, false, "in_air", "full"));
            t.Add(("in_air", n.InAir, true, "", "full"));
            t.Add(("falling", n.Falling, true, "", "full"));
            t.Add(("land", n.Land, false, "locomotion", "additive"));
            t.Add(("dash_ground", n.DashGround, false, "locomotion", "full"));
            foreach (string d in new[] { "forward", "back", "left", "right" }) t.Add(("dash_air_" + d, n.DashAir + d, false, "in_air", "full"));
            t.Add(("slide_start", n.SlideStart, false, "slide", "full"));
            t.Add(("slide", n.SlideLoop, true, "", "full"));
            t.Add(("slide_getup", n.SlideGetup, false, "locomotion", "full"));
            t.Add(("shoot", n.Shoot, false, "locomotion", n.ShootLayer));
            t.Add(("parry", n.Parry, false, "locomotion", "full"));
            t.Add(("parry_air", n.Parry, false, "in_air", "full"));
            t.Add(("death", n.Death, false, "hold", "full"));
            foreach (var a in abilities) t.Add(a);
            return t.ToArray();
        }

        public static HashSet<string> Locked(params string[] more)
        {
            var s = new HashSet<string> { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death" };
            foreach (string m in more) s.Add(m);
            return s;
        }

        /// <summary>The VO, effort and movement sounds every hero has, by Deadlock's usual file names.</summary>
        public static List<CursedAppleCuriosities.Deadlock.DeadlockSound> CommonSounds(string hero, string vo, string footstep, string land, string dash, bool lowHealthInEmote = false)
        {
            var l = new List<CursedAppleCuriosities.Deadlock.DeadlockSound>();
            void Add(string id, string path, float volume = 1f, float range = 40f, float chance = 1f, float gap = 0.1f, bool voice = false) =>
                l.Add(new CursedAppleCuriosities.Deadlock.DeadlockSound { Id = hero + "_" + id, VpkPrefix = path, Volume = volume, Range = range, Chance = chance, MinGap = gap, Voice = voice });
            for (int i = 1; i <= 4; i++)
            {
                Add("vo_power" + i, $"sounds/vo/{vo}/{vo}_use_power{i}", 1f, i == 4 ? 60f : 40f, i == 4 ? 1f : 0.5f, 4f, true);
                Add("vo_desperation_power" + i, $"sounds/vo/{vo}/{vo}_desperation_power{i}", 1f, i == 4 ? 60f : 40f, 0.7f, 4f, true);
            }
            Add("vo_select", $"sounds/vo/{vo}/{vo}_select", gap: 2f, voice: true);
            Add("vo_kill", $"sounds/vo/{vo}/{vo}_kill_anyhero", chance: 0.15f, gap: 8f, voice: true);
            Add("vo_killstreak", $"sounds/vo/{vo}/{vo}_killstreak_start", gap: 30f, voice: true);
            Add("vo_pain", $"sounds/vo/{vo}/emote/{vo}_pain_big", 0.9f, chance: 0.35f, gap: 3f, voice: true);
            Add("vo_pain_small", $"sounds/vo/{vo}/emote/{vo}_pain_small", 0.8f, chance: 0.25f, gap: 2f, voice: true);
            Add("vo_low_health", lowHealthInEmote ? $"sounds/vo/{vo}/emote/{vo}_pain_low_health_warning" : $"sounds/vo/{vo}/{vo}_low_health_warning", gap: 20f, voice: true);
            Add("vo_death", $"sounds/vo/{vo}/emote/{vo}_pain_death", gap: 2f, voice: true);
            Add("vo_effort", $"sounds/vo/{vo}/emote/{vo}_effort_dash", 0.8f, chance: 0.5f, gap: 0.5f, voice: true);
            Add("vo_efforts", $"sounds/vo/{vo}/emote/{vo}_effort_general", 0.7f, chance: 0.4f, gap: 1f, voice: true);
            Add("footstep", footstep, 0.3f, 20f, gap: 0.12f);
            Add("land", land, 0.7f, 25f, gap: 0.2f);
            Add("dash", dash, 0.8f, 25f, gap: 0.1f);
            Add("slide", "sounds/gameplay/slide/enter", 0.8f, 20f, gap: 0.2f);
            Add("slide_loop", "sounds/gameplay/slide/lp2_texture", 0.6f, 20f);
            Add("slide_exit", "sounds/gameplay/slide/exit", 0.7f, 20f, gap: 0.2f);
            Add("jump", "sounds/player/movement/generic/generic_jump_start", 0.5f, 20f);
            Add("wall_jump", "sounds/player/wall_jump", 0.9f, 25f);
            return l;
        }
    }
}
