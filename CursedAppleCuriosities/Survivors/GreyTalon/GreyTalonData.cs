using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Grey Talon's numbers (Deadlock's hero_orion: model heroes_staging/archer, abilities ability_charged_shot,
    /// ability_power_jump, ability_guided_arrow, weapon citadel_weapon_archer_set; read 2026-10-12). Deadlock values are kept
    /// where RoR2 has the same scale (metres, seconds, speeds; 39.37 hu = 1 m); damage becomes RoR2 damage coefficients and
    /// cooldowns RoR2-length, except Guided Owl's (user: very long). Kit decisions are the user's. Config: [Grey Talon].
    /// </summary>
    public static class GreyTalonData
    {
        public const string Hero = "greytalon";
        public const string Section = "Grey Talon";

        // ---- survivor: a long-range marksman (Deadlock 780 health, 1.5 regen, 6.3 m/s) ----
        public static float BaseHealth = 100f;
        public const float HealthPerLevel = 30f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.3f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 1.5f;            // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: Deadlock's own 4 stamina (as Vyper), 1 air jump, 1 air dash ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = GreyTalonModel.Play,
            StaminaPips = 4,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 6.3f,    // Deadlock ESprintSpeed over his run
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: the bow (slow, very long range hitscan: user) ----
        public static float BowDamage = 3f;
        public static float BowCycle = 0.6f;            // Deadlock m_flCycleTime
        public const float BowRange = 600f;             // Deadlock 7000 hu (178 m): far longer here (user: very long range); Mystic Expansion: farther (every bullet)
        // Falloff (user: the primary only, full damage across about half of most maps first). Deadlock: 18 m -> 54 m, to 10%.
        public static float BowFalloffStart = 150f;
        public static float BowFalloffEnd = 300f;
        public const float BowFalloffMin = 0.5f;
        public const float BowRadius = 0.2f;            // Deadlock m_flBulletRadius 5 hu, a little forgiving
        public const float BowForce = 400f;

        // ---- secondary: Charged Shot (user: 1000%, 800% before 1.7; Deadlock: pierces, hold to keep it drawn) ----
        public static float ChargedDamage = 10f;        // (Deadlock Damage 80 +1x spirit)
        public static float ChargedCooldown = 6f;       // Deadlock 17
        public const float ChargeTime = 0.5f;           // Deadlock AbilityCastDelay: drawn after this, then held
        public const float ChargedMoveSpeed = 1.5f;     // Deadlock ChannelMoveSpeed 1.5 m/s while drawing and holding
        public const float ChargedSpeed = 406.4f;       // Deadlock m_flBulletSpeed 8000 hu/s (203 m/s), doubled (user, 2.0)
        public static float ChargedRadius = 3f;         // wider than Deadlock's 24 hu (0.61 m) (user; 1.5 until 2.0). Enemies only: walls stop it at ChargedWallRadius
        public const float ChargedWallRadius = 0.3f;    // m: the arrow's own thickness against terrain, so a fat hitbox doesn't snag the floor
        public const float ChargedRange = 600f;         // Deadlock 10000 hu; no falloff (user); Mystic Expansion: farther
        public const float ChargedDrop = 3f;            // m/s2 (Deadlock gravity scale 0.3)
        // Camera while drawn (Deadlock CameraHeightOffset 20 hu, CameraHorizontalOffset 15 hu): over the right shoulder, zoomed.
        public const float ChargedFov = 45f;
        public const float ChargedCamUp = 0.5f, ChargedCamRight = 0.38f;

        // ---- utility: Rain of Arrows (user: leap up, glide with slow WASD, primary splits into 5 arrows in a fan) ----
        public static float RainCooldown = 8f;          // Deadlock 25
        public const float RainCastDelay = 0.2f;        // Deadlock AbilityCastDelay
        // The leap (user): much faster than Deadlock's 27.5 m/s arc, straight up its 60 deg line, and stopped dead at the glide
        // height (about where Deadlock's arc peaked). Cancelled on the way up (jump), he keeps the launch speed: a movement tool.
        public static float RainLaunchSpeed = 45f;
        public const float RainPitchDeg = 60f;          // Deadlock JumpPitch -60
        public static float RainHeight = 10f;           // metres up where the leap stops and the glide begins
        public static float RainDuration = 4f;          // Deadlock AbilityDuration
        public const float RainFallSpeed = 0.64f;       // Deadlock FallSpeedMax 25 hu/s
        public const float RainAirSpeed = 6.64f;        // Deadlock AirSpeedMax 261.4 hu/s
        public const float RainAirAccel = 15f;          // m/s2 toward the wished drift
        public const int RainArrows = 5;                // Deadlock BulletSplitShot
        public const float RainFanDeg = 3.5f;           // between neighbouring arrows
        public const float RainDamageBonus = 0.13f;     // Deadlock WeaponDamageBonus +3 on the bow's 23.5
        public static float RainRepeatHit = 0.5f;       // later arrows of one fan on an enemy already hit by it (user: first full, the rest half)

        // ---- special: Guided Owl (user: 1200%, steered; very long cooldown; untargetable while casting) ----
        public static float OwlDamage = 25f;            // user: 1200% -> 2500% (Deadlock Damage 230 +0.94x spirit)
        public const float OwlExecute = 0.75f;          // a blast dealing more than this share of the target's current health (with shield and barrier) kills it (user, 1.7)
        public static float OwlBossBonus = 0.1f;        // owl damage per boss, elite or miniboss it killed, whole run (user: 20% bosses only until 2.0; Deadlock: +8 spirit per kill)
        public static float OwlCooldown = 125f;         // Deadlock's own, kept (user: very very long)
        public const float OwlCastDelay = 1.5f;         // Deadlock AbilityCastDelay
        public const float OwlFlight = 20f;             // Deadlock AbilityChannelTime
        public static float OwlSpeed = 6f;              // slow until forward is held (user; Deadlock m_flBaseProjectileSpeed 590.6 hu/s = 15)
        public const float OwlMaxSpeed = 36f;           // Deadlock m_flMaxProjectileSpeed 1417 hu/s (hold forward)
        public const float OwlAccel = 20.3f;            // Deadlock m_flSpeedAccel 800 hu/s2
        public const float OwlDecel = 7.6f;             // Deadlock m_flSpeedDeccel 300 hu/s2
        public static float OwlTurnDegPerSec = 110f;    // Deadlock m_flTrackAmount 110
        public const float OwlHitRadius = 0.76f;        // Deadlock m_flTriggerRadius 30 hu
        public static float OwlRadius = 36f;            // Deadlock ExplosionRadius 12; tripled (user, 2.0)
        public const float OwlStun = 0.75f;             // Deadlock StunDuration
        public static float OwlExitGrace = 2f;          // seconds still invulnerable and cloaked once he has control back (user)
        public const float OwlCameraHold = 0.8f;        // Deadlock m_flCameraHoldAtExplosion
        public const float OwlCameraBack = 4f, OwlCameraUp = 1f;

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Grey Talon max health at level 1 (+30 per level).").Value;
            BowDamage = c.Bind(Section, "Bow - Damage", BowDamage, "Damage coefficient per arrow.").Value;
            BowCycle = c.Bind(Section, "Bow - Time Between Shots", BowCycle, "Seconds between shots before attack speed.").Value;
            BowFalloffStart = c.Bind(Section, "Bow - Falloff Start", BowFalloffStart, "Metres of full damage.").Value;
            BowFalloffEnd = c.Bind(Section, "Bow - Falloff End", BowFalloffEnd, "Metres where damage reaches its floor (50%).").Value;
            ChargedDamage = c.Bind(Section, "Charged Shot - Damage", ChargedDamage, "Damage coefficient to every enemy it pierces.").Value;
            ChargedRadius = c.Bind(Section, "Charged Shot - Hit Radius", ChargedRadius, "Metres around the arrow's path that it hits.").Value;
            ChargedCooldown = c.Bind(Section, "Charged Shot - Cooldown", ChargedCooldown, "Seconds.").Value;
            RainCooldown = c.Bind(Section, "Rain of Arrows - Cooldown", RainCooldown, "Seconds.").Value;
            RainLaunchSpeed = c.Bind(Section, "Rain of Arrows - Leap Speed", RainLaunchSpeed, "Metres per second of the leap (kept if you cancel it on the way up).").Value;
            RainHeight = c.Bind(Section, "Rain of Arrows - Leap Height", RainHeight, "Metres up where the leap stops and the glide begins.").Value;
            RainRepeatHit = c.Bind(Section, "Rain of Arrows - Repeat Hit Damage", RainRepeatHit, "Damage multiplier for each further arrow of one 5-arrow shot that hits the same enemy.").Value;
            RainDuration = c.Bind(Section, "Rain of Arrows - Glide", RainDuration, "Seconds of glide and split arrows.").Value;
            OwlDamage = c.Bind(Section, "Guided Owl - Damage", OwlDamage, "Damage coefficient of the owl's blast.").Value;
            OwlBossBonus = c.Bind(Section, "Guided Owl - Boss Kill Bonus", OwlBossBonus, "Extra owl damage for each boss, elite or miniboss it has killed this run (0.1 = +10%).").Value;
            OwlRadius = c.Bind(Section, "Guided Owl - Radius", OwlRadius, "Metres.").Value;
            OwlCooldown = c.Bind(Section, "Guided Owl - Cooldown", OwlCooldown, "Seconds.").Value;
            OwlSpeed = c.Bind(Section, "Guided Owl - Cruise Speed", OwlSpeed, "Metres per second without forward held (it speeds up to 36 while forward is held).").Value;
            OwlExitGrace = c.Bind(Section, "Guided Owl - Exit Grace", OwlExitGrace, "Seconds Grey Talon stays invulnerable and unseen after the owl ends or is released.").Value;
            OwlTurnDegPerSec = c.Bind(Section, "Guided Owl - Turn Rate", OwlTurnDegPerSec, "Degrees per second the owl turns toward where you look.").Value;
        }

        /// <summary>The bow's damage at a distance: full to the falloff start, easing to the floor at its end.</summary>
        public static float BowFalloff(float distance)
        {
            if (distance <= BowFalloffStart) return 1f;
            float t = UnityEngine.Mathf.Clamp01((distance - BowFalloffStart) / UnityEngine.Mathf.Max(1f, BowFalloffEnd - BowFalloffStart));
            return UnityEngine.Mathf.Lerp(1f, BowFalloffMin, t * t * (3f - 2f * t));
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/archer/archer.vmdl_c";
        public const string OwlKey = "greytalon_owl";
        public const string DlOwlModel = "models/heroes_staging/archer/archer_guided_arrow.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 0.95f; // Lash's size, then x0.95 (user, 1.7: all but Vyper a little smaller)
        public const string MuzzleBone = "muzzle_fx";        // under weapon_hand_L: the bow follows his left hand
        public const string PerchedOwlMesh = "owl_bodygroup"; // the owl on his arm: hidden (user)

        public const string IconPortrait = "panorama/images/heroes/archer_sm_psd.vtex_c";
        public const string IconCharged = "panorama/images/hud/abilities/archer/archer_charged_shot_psd.vtex_c";
        public const string IconRain = "panorama/images/hud/abilities/archer/archer_power_jump_psd.vtex_c";
        public const string IconOwl = "panorama/images/hud/abilities/archer/archer_guided_arrow_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Grey Talon is an AnimGraph2 hero (models/heroes_staging/archer); clips
        /// by vnmclip name. His set has no in-air loop (jump_ground holds instead). "charged.*" is the locomotion set while
        /// Charged Shot is drawn (ability_charged_shot_walk_*).
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "weapon_stand_idle", true, "", "full"),
            ("run_n", "weapon_run_n", true, "", "full"), ("run_ne", "weapon_run_ne", true, "", "full"),
            ("run_e", "weapon_run_e", true, "", "full"), ("run_se", "weapon_run_se", true, "", "full"),
            ("run_s", "weapon_run_s", true, "", "full"), ("run_sw", "weapon_run_sw", true, "", "full"),
            ("run_w", "weapon_run_w", true, "", "full"), ("run_nw", "weapon_run_nw", true, "", "full"),
            ("sprint_n", "out_of_combat_run_n", true, "", "full"), ("sprint_ne", "out_of_combat_run_ne", true, "", "full"),
            ("sprint_e", "out_of_combat_run_e", true, "", "full"), ("sprint_se", "out_of_combat_run_se", true, "", "full"),
            ("sprint_s", "out_of_combat_run_s", true, "", "full"), ("sprint_sw", "out_of_combat_run_sw", true, "", "full"),
            ("sprint_w", "out_of_combat_run_w", true, "", "full"), ("sprint_nw", "out_of_combat_run_nw", true, "", "full"),
            ("charged.idle", "ability_charged_shot_on_ground_hold", true, "", "full"),
            ("charged.run_n", "ability_charged_shot_walk_n", true, "", "full"), ("charged.run_ne", "ability_charged_shot_walk_ne", true, "", "full"),
            ("charged.run_e", "ability_charged_shot_walk_e", true, "", "full"), ("charged.run_se", "ability_charged_shot_walk_se", true, "", "full"),
            ("charged.run_s", "ability_charged_shot_walk_s", true, "", "full"), ("charged.run_sw", "ability_charged_shot_walk_sw", true, "", "full"),
            ("charged.run_w", "ability_charged_shot_walk_w", true, "", "full"), ("charged.run_nw", "ability_charged_shot_walk_nw", true, "", "full"),
            ("jump", "jump_ground", false, "hold", "full"),
            ("air_jump", "jump_ground", false, "hold", "full"),
            ("wall_jump", "jump_ground", false, "hold", "full"),
            ("in_air", "jump_ground", false, "hold", "full"),
            ("falling", "jump_ground", false, "hold", "full"),
            ("land", "landing_impact_idle", false, "locomotion", "additive"),
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "dash_air_forward", false, "in_air", "full"),
            ("dash_air_back", "dash_air_back", false, "in_air", "full"),
            ("dash_air_left", "dash_air_left", false, "in_air", "full"),
            ("dash_air_right", "dash_air_right", false, "in_air", "full"),
            ("slide_start", "slide_start", false, "slide", "full"),
            ("slide", "slide_loop", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "shoot_idle", false, "locomotion", "delta"),   // a full pose clip (no additive version): played as its motion only
            ("charge_draw", "ability_charged_shot_on_ground_draw", false, "locomotion", "full"),
            ("charge_air", "ability_charged_shot_in_air", false, "hold", "full"),
            ("charge_release", "ability_charged_shot_on_ground_release", false, "locomotion", "full"),
            ("rain_leap", "ability_power_jump_leap_up", false, "rain_air", "full"),
            ("rain_air", "ability_power_jump_in_air_loop", true, "", "full"),
            ("rain_fire", "ability_power_jump_charged_shot_fire", false, "rain_air", "full"),
            ("owl_intro", "ability_guided_arrow_intro", false, "owl_loop_intro", "full"),
            ("owl_loop_intro", "ability_guided_arrow_loop_intro", false, "owl_loop", "full"),
            ("owl_loop", "ability_guided_arrow_loop", true, "", "full"),
            ("owl_fire", "ability_guided_arrow_fire", false, "owl_channel", "full"),
            ("owl_channel", "ability_guided_arrow_missile_loop", true, "", "full"),
            ("owl_outro", "ability_guided_arrow_outro", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "death_idle_01", false, "hold", "full"),
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death", "rain_leap" };

        /// <summary>
        /// Deadlock sounds, approved by the user by ear (2026-10-12, sound_review\greytalon_review.html). Rain of Arrows' cast
        /// (archer_rain_of_arrows_cast) is not what plays in game (user): left out, so the cast is silent for now.
        /// </summary>
        public const bool SoundsApproved = true;

        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "greytalon_bow_fire", VpkPrefix = "sounds/weapons/orion/archer_weapon_fire", Volume = 0.8f, Range = 80f, MinGap = 0.05f, PitchJitter = 0.03f, Fallback = "Play_huntress_m1_shoot" },
            new DeadlockSound { Id = "greytalon_rain_fire", VpkPrefix = "sounds/abilities/archer/archer_rain_of_arrows_wpn_fire", Volume = 0.8f, Range = 80f, MinGap = 0.05f },
            new DeadlockSound { Id = "greytalon_zoom_in", VpkPrefix = "sounds/weapons/orion/zoom/in", Volume = 0.6f, Range = 15f },
            new DeadlockSound { Id = "greytalon_zoom_out", VpkPrefix = "sounds/weapons/orion/zoom/out", Volume = 0.6f, Range = 15f },
            new DeadlockSound { Id = "greytalon_charge_start", VpkPrefix = "sounds/abilities/archer/archer_charged_shot_charging_start", Volume = 0.9f, Range = 40f },
            new DeadlockSound { Id = "greytalon_charge_loop", VpkPrefix = "sounds/abilities/archer/archer_charged_shot_charging_lp", Volume = 0.6f, Range = 25f },
            new DeadlockSound { Id = "greytalon_charge_fire", VpkPrefix = "sounds/abilities/archer/archer_charged_shot_fire", Range = 100f },
            new DeadlockSound { Id = "greytalon_charge_hit", VpkPrefix = "sounds/abilities/archer/archer_charged_shot_hit", Range = 60f, MinGap = 0.05f },
            new DeadlockSound { Id = "greytalon_charge_whizby", VpkPrefix = "sounds/abilities/archer/archer_charged_shot_whizby", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "greytalon_rain_loop", VpkPrefix = "sounds/abilities/archer/archer_rain_of_arrows_flight_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "greytalon_rain_end", VpkPrefix = "sounds/abilities/archer/archer_rain_of_arrows_flight_expire", Volume = 0.8f, Range = 30f },
            new DeadlockSound { Id = "greytalon_owl_cast", VpkPrefix = "sounds/abilities/archer/archer_guided_arrow_cast", Range = 60f },
            new DeadlockSound { Id = "greytalon_owl_loop", VpkPrefix = "sounds/abilities/archer/archer_guided_arrow_lp", Volume = 0.7f, Range = 40f },
            new DeadlockSound { Id = "greytalon_owl_explode", VpkPrefix = "sounds/abilities/archer/archer_guided_arrow_explo", Range = 120f },
            new DeadlockSound { Id = "greytalon_footstep", VpkPrefix = "sounds/player/footsteps/orion/greytalon_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            // VO by ability: 1 Charged Shot, 2 Rain of Arrows, 4 Guided Owl (power3 is Spirit Snare: not in the kit).
            new DeadlockSound { Id = "greytalon_vo_power1", VpkPrefix = "sounds/vo/orion/orion_use_power1", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_power2", VpkPrefix = "sounds/vo/orion/orion_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_power4", VpkPrefix = "sounds/vo/orion/orion_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_desperation_power1", VpkPrefix = "sounds/vo/orion/orion_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_desperation_power2", VpkPrefix = "sounds/vo/orion/orion_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_desperation_power4", VpkPrefix = "sounds/vo/orion/orion_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_select", VpkPrefix = "sounds/vo/orion/orion_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_kill", VpkPrefix = "sounds/vo/orion/orion_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_killstreak", VpkPrefix = "sounds/vo/orion/orion_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_pain", VpkPrefix = "sounds/vo/orion/emote/orion_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_pain_small", VpkPrefix = "sounds/vo/orion/emote/orion_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_death", VpkPrefix = "sounds/vo/orion/emote/orion_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_effort", VpkPrefix = "sounds/vo/orion/emote/orion_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "greytalon_vo_efforts", VpkPrefix = "sounds/vo/orion/emote/orion_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Shared ones the other heroes already use (approved).
            new DeadlockSound { Id = "greytalon_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "greytalon_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };

        /// <summary>The shared movement sounds the other heroes already play (registered even before the review).</summary>
        public static readonly HashSet<string> PreApproved = new HashSet<string> { "greytalon_jump", "greytalon_wall_jump" };
    }
}
