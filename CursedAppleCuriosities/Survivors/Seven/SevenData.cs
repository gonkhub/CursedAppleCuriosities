using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Seven's numbers (Deadlock's hero_gigawatt / citadel_ability_lightning_ball, _static_charge_v2, ability_power_surge,
    /// citadel_ability_storm_cloud, read from client 6774). Deadlock values are kept where RoR2 has the same scale (metres,
    /// seconds, speeds; 39.37 hu = 1 m); damage becomes RoR2 damage coefficients and cooldowns RoR2-length.
    /// </summary>
    public static class SevenData
    {
        public const string Hero = "seven";
        public const string Section = "Seven";

        // ---- survivor: a ranged caster (Deadlock 730 health, 6.7 m/s; RoR2's casters sit at 110) ----
        public static float BaseHealth = 110f;
        public const float HealthPerLevel = 33f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.7f;            // Deadlock EMaxMoveSpeed
        // ---- Deadlock movement: hero defaults (3 stamina, 1 air jump, 1 air dash) ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = SevenModel.Play,
            OutOfCombatSpeed = 1.8f / 6.7f,    // Deadlock ESprintSpeed over his run
            // Electric Current (user): during Power Surge he sprints through his own shooting; only getting hurt stops it.
            SprintInCombat = body => SevenSurvivor.SurgeBuff != null && body.HasBuff(SevenSurvivor.SurgeBuff),
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Cold Calculus (3-round burst; infinite ammo like the other heroes) ----
        public static float GunDamage = 0.5f;           // 0.7 (~490%/s) was too much next to his crowd control (playtest)
        public const int BurstCount = 3;                // Deadlock m_iBurstShotCount
        public const float BurstInterval = 0.084f;      // Deadlock m_flIntraBurstCycleTime
        public static float BurstCycle = 0.2625f;       // Deadlock m_flCycleTime
        public static float GunSpread = 0.35f;
        public const float GunBloomPerShot = 0.1f;
        // Climbing recoil (user; Deadlock m_VerticallRecoil 0.2, slow recovery): each round of a burst kicks harder than the last.
        public const float GunRecoilKick = 0.6f;        // vertical kick of the first round; round n kicks n times this
        // Mid range (user): full damage further out than Lash, and a gentle falloff.
        public static float GunFalloffStart = 20f;      // Deadlock m_flDamageFalloffStartRange 787 hu
        public static float GunFalloffEnd = 57.5f;      // Deadlock m_flDamageFalloffEndRange 2264 hu
        public const float GunFalloffMin = 0.6f;        // Deadlock eases to 10%; gentle here

        public static float GunFalloff(float distance) => DeadlockSurvivorKit.Falloff(distance, GunFalloffStart, GunFalloffEnd, GunFalloffMin);

        // ---- secondary: Lightning Ball ----
        public static float BallDps = 2.5f;             // damage coefficient per second to each enemy inside (Deadlock 75 DPS)
        public static float BallCooldown = 7f;          // Deadlock 26
        public static float BallRadius = 6.375f;        // Deadlock ShockRadius 4.25; +50% (user, 1.9)
        public const float BallTick = 0.1f;             // Deadlock TickRate (0.1); RoR2 ticks it at 0.25 to keep procs sane
        public const float BallTickRoR2 = 0.25f;
        public const float BallSpeed = 43.2f;           // Deadlock m_flSpeed 1700 hu/s
        public const float BallHitSpeed = 3.5f;         // Deadlock m_flNonHeroHitSpeed 138 hu/s: it crawls while it's shocking something
        public const float BallLifetime = 5f;           // Deadlock MaxLifetime
        public const float BallProc = 0.7f;             // 0.5 until 3.1 (user: in line with the storm)
        public const float BallSlowLinger = 0.5f;      // enemies it shocks are slowed 80% (Slow80) while inside and this long after (user, 1.7)

        // ---- secondary (alt): Static Charge (Deadlock's v2: strike a unit or a spot) ----
        public static float StaticDamage = 4f;
        public static float StaticCooldown = 12f;       // Deadlock 42
        public const float StaticRange = 48f;           // Deadlock AbilityCastRange 16, tripled (user); grows with Mystic Expansion
        public const float StaticConeDeg = 20f;         // Deadlock m_flTargetingConeAngle
        public const float StaticDelay = 3.5f;          // Deadlock ShockDelay (user: keep it)
        public static float StaticStun = 3f;            // Deadlock 0.9 s; 3 s by design (user)
        public static float StaticRadius = 10f;         // Deadlock ShockRadius 5, doubled in 2.0 (user); grows with Mystic Expansion

        // ---- utility: Power Surge ----
        public static float SurgeCooldown = 30f;        // Deadlock 50; 12 s gave near-permanent uptime (playtest)
        public const float SurgeDuration = 10f;         // Deadlock AbilityDuration
        public static float SurgeChainDamage = 0.8f;    // damage coefficient of the first jump (Deadlock DamagePerChain 10)
        public static float SurgeChainGrowth = 0.8f;    // added per jump after that (Deadlock BonusPerChain 10)
        public const int SurgeChainCount = 4;           // Deadlock ChainCount
        public const float SurgeChainRadius = 10f;      // Deadlock ChainRadius
        public static float SurgeMoveSpeed = 3f;        // m/s: Deadlock's T2 upgrade (+3 m), given free (user)

        // ---- special: Storm Cloud (Seven floats and channels; the cloud grows to the whole map, user) ----
        // Damage per second follows the cloud's growth (user): Start + (End - Start) * t^3 over the channel, t = 0..1. A target in
        // the storm the whole 7 s takes 7 * (Start + (End - Start) / 4) = ~1925% (~1575% before 2.0, ~2520% before 1.7: the strikes are weaker
        // but proc at 0.7 since 3.1, user), dealt as a lightning bolt every StormStrikeGap s (25-200% each). Attack speed shortens the gap
        // (more bolts, so more damage and procs; user, 2.0). The ramp
        // always takes the base 7 s: Duration Extender only lengthens the time at full output.
        public static float StormDpsStart = 1.0f;       // (Deadlock 95 DPS flat)
        public static float StormDpsEnd = 8.0f;          // 600% until 2.0 (user)
        public static float StormDps(float t) => StormDpsStart + (StormDpsEnd - StormDpsStart) * t * t * t;

        /// <summary>The storm's radius after channelling for age seconds: 10 m growing (slowly, then fast) to the whole map. Server damage and every client's ring use this.</summary>
        public static float StormRadius(float age, RoR2.CharacterBody? body)
        {
            float t = UnityEngine.Mathf.Clamp01(age / StormChannel); // the damage ramps on the same curve (StormDps)
            return UnityEngine.Mathf.Lerp(StormInitialRadius, StormFinalRadius, t * t * t) * CursedAppleCuriosities.Items.MysticExpansion.RadiusMultiplier(body);
        }
        public static float StormCooldown = 45f;        // Deadlock 205
        public const float StormChannel = 7f;           // Deadlock AbilityChannelTime
        public const float StormInitialRadius = 30f;    // Deadlock InitialRadius 10 m; 30 m from the start (user, 1.7)
        public static float StormFinalRadius = 1000f;   // Deadlock 30 m; the whole map by the end (user)
        public const float StormStrikeGap = 0.25f;      // s between bolts on each enemy at base attack speed (divided by attack speed), jittered 0.8-1.2x (Deadlock DamageInterval 0.3; 0.125 in 1.9, slowed in 2.0 for attack speed scaling, user)
        public const float StormStrayHit = 0.25f;       // s of storm DPS in each stray bolt's blast
        public const float StormProc = 0.7f;            // 3.1 balance (user): a very high proc, not a guaranteed one (1.0 before)
        public const float StormStrayRate = 6f;         // stray bolts per second at random spots under the cloud (user), times attack speed
        public const float StormStrayRadius = 4f;       // m: each stray bolt's blast
        public const float StormStrayReach = 45f;       // m from Seven: strays fall where he can see them, even once the cloud is map-wide
        public const float StormRise = 3f;              // Deadlock CloudHeight 120 hu: he lifts this far
        public const float StormRiseTime = 0.6f;
        public const float StormDrift = 3f;             // Deadlock FlightControlEnabled 1.5 m/s, doubled (user, 2.0)
        public static float StormClimb = 2f;            // m/s: after the rise he keeps climbing for the whole channel (user; doubled in 2.0)
        public const float StormBossScale = 0.5f;       // Deadlock m_flBossDamageScale
        public static float StrikeDamage = 6f;          // Lightning Strike (Deadlock 75)
        public const float StrikeRadius = 7f;           // Deadlock LightningStrikeRadius
        public const float StrikeDelay = 0.25f;         // Deadlock LightningStrikeDelay
        public const float StrikeRange = 120f;
        public const float StrikeKnockback = 3000f;     // Deadlock LightningStrikeKnockBackForce 500 (RoR2 force units)

        public static void Bind(ConfigFile c) // the hero's config section is only its keybind layout (3.0)
        {
            Movement.Bind(c, Section);
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/gigawatt_prisoner/gigawatt_prisoner.vmdl_c";

        // Bunny Seven skin (user 3.0): funnymonkeyman's Bunny Seven, a Deadlock mod (https://gamebanana.com/mods/681692; a fan-made
        // model on his skeleton, not Valve's and not shipped). The player drops its .vpk into a skins folder (or keeps it in Deadlock's
        // addons); it is found by a file only it has and converted once, read-only, like his own model. Credit in the skin's name.
        public const string BunnyKey = "seven_bunny";
        public const string BunnyName = "Bunny Seven (by funnymonkeyman)";
        public const string BunnyMarker = "models/heroes_staging/gigawatt_prisoner/materials/colourlightdarkpurple_vmat_g_tcolor_a9d5fdf5.vtex_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f; // Lash's size, then x1.15 by playtest (heroes read small on the stand-in), then x0.95 (user, 1.7: all but Vyper a little smaller)
        public const string MuzzleBone = "muzzle_fx";

        // Deadlock's internal name for Seven is "gigawatt" (icons giga_*).
        public const string IconPortrait = "panorama/images/heroes/gigawatt_sm_psd.vtex_c";
        // The primary shows the weapon itself (user 3.0, like Celeste's baton): Deadlock's shop image of the hero's gun.
        public const string IconGun = "panorama/images/heroes/guns/gigawatt_gun_psd.vtex_c";
        public const string IconBall = "panorama/images/hud/abilities/giga_ball_psd.vtex_c";
        public const string IconStatic = "panorama/images/hud/abilities/giga_static_psd.vtex_c";
        public const string IconSurge = "panorama/images/hud/abilities/giga_chain_psd.vtex_c";
        public const string IconStorm = "panorama/images/hud/abilities/giga_storm_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Seven is an AnimGraph2 hero (models/heroes_staging): his clips
        /// are the vnmclip names. gigawatt_abilitymask plays ability_lightning_ball(_in_air), ability_power_surge and
        /// ability_storm_cloud_start / _loop / _end; Static Charge has no clip of its own (cast_*).
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "weapon_stand_idle", true, "", "full"),
            ("run_n", "weapon_run_n", true, "", "full"), ("run_ne", "weapon_run_ne", true, "", "full"),
            ("run_e", "weapon_run_e", true, "", "full"), ("run_se", "weapon_run_se", true, "", "full"),
            ("run_s", "weapon_run_s", true, "", "full"), ("run_sw", "weapon_run_sw", true, "", "full"),
            ("run_w", "weapon_run_w", true, "", "full"), ("run_nw", "weapon_run_nw", true, "", "full"),
            // Deadlock has out-of-combat runs only forward: the other sprint directions reuse his runs.
            ("sprint_n", "out_of_combat_run_n", true, "", "full"), ("sprint_ne", "out_of_combat_run_ne", true, "", "full"),
            ("sprint_nw", "out_of_combat_run_nw", true, "", "full"),
            ("sprint_e", "weapon_run_e", true, "", "full"), ("sprint_se", "weapon_run_se", true, "", "full"),
            ("sprint_s", "weapon_run_s", true, "", "full"), ("sprint_sw", "weapon_run_sw", true, "", "full"),
            ("sprint_w", "weapon_run_w", true, "", "full"),
            ("jump", "jump_start", false, "in_air", "full"),
            ("air_jump", "jump_air", false, "in_air", "full"),
            ("wall_jump", "jump_air", false, "in_air", "full"),
            ("in_air", "in_air_loop", true, "", "full"),
            ("falling", "in_air_loop_down", true, "", "full"),
            ("land", "landing_impact_idle", false, "locomotion", "additive"),
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "dash_air_forward", false, "in_air", "full"),
            ("dash_air_back", "dash_air_back", false, "in_air", "full"),
            ("dash_air_left", "dash_air_left", false, "in_air", "full"),
            ("dash_air_right", "dash_air_right", false, "in_air", "full"),
            ("slide_start", "slide_start", false, "slide", "full"),
            ("slide", "slide_loop", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "shoot_idle", false, "locomotion", "additive"),
            ("ball", "ability_lightning_ball", false, "locomotion", "full"),
            ("ball_air", "ability_lightning_ball_in_air", false, "in_air", "full"),
            ("static", "cast_start", false, "locomotion", "full"),
            ("surge", "ability_power_surge", false, "locomotion", "full"),
            ("storm_start", "ability_storm_cloud_start", false, "storm", "full"),
            ("storm", "ability_storm_cloud_loop", true, "", "full"),
            ("storm_end", "ability_storm_cloud_end", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "death_idle_01", false, "hold", "full"),
        };

        /// <summary>Clips that travel on their own (Deadlock moves heroes with them; RoR2 moves the body).</summary>
        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death", "storm_start", "storm", "storm_end" };

        /// <summary>
        /// Deadlock sounds, as the user picked them by ear (2026-10-12): gun = gigawatt_weapon_fire (set B; seven_wpn_last_shot is
        /// the last round of a clip, n/a with infinite ammo); Static Charge = cast + loop only; Storm Cloud = the gigawatt_storm_cloud_*
        /// set (not a4_storm_cloud); never a3_voltage_drain (an old ability) or seven_spark footsteps. Prefixes match by StartsWith.
        /// </summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "seven_gun_fire", VpkPrefix = "sounds/weapons/gigawatt/gigawatt_weapon_fire", Volume = 0.6f, Range = 60f, MinGap = 0.03f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "seven_ball_cast", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_lightning_ball_cast", Volume = 0.9f },
            new DeadlockSound { Id = "seven_ball_loop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_lightning_ball_projectile_lp", Volume = 0.8f },
            new DeadlockSound { Id = "seven_ball_zap", VpkPrefix = "sounds/abilities/gigawatt/a1_lightning_ball/damage", Volume = 0.6f, Range = 35f, MinGap = 0.12f },
            new DeadlockSound { Id = "seven_ball_explode", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_lightning_ball_explode", Volume = 0.9f },
            new DeadlockSound { Id = "seven_static_cast", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_static_charge_cast", Volume = 0.9f },
            new DeadlockSound { Id = "seven_static_loop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_static_charge_lp", Volume = 0.8f },
            new DeadlockSound { Id = "seven_surge_start", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_power_surge_start", Volume = 0.9f },
            new DeadlockSound { Id = "seven_surge_loop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_power_surge_activate_lp", Volume = 0.6f, Range = 25f },
            new DeadlockSound { Id = "seven_surge_fire", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_power_surge_fire", Volume = 0.8f, MinGap = 0.08f },
            new DeadlockSound { Id = "seven_surge_stop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_power_surge_stop", Volume = 0.8f },
            new DeadlockSound { Id = "seven_storm_cast", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_cast", Range = 80f },
            new DeadlockSound { Id = "seven_storm_loop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_lp", Range = 80f },
            new DeadlockSound { Id = "seven_storm_aoe_loop", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_aoe_lp", Volume = 0.8f, Range = 80f },
            new DeadlockSound { Id = "seven_storm_end", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_end", Range = 80f },
            new DeadlockSound { Id = "seven_strike_cast", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_bolt_cast", Volume = 0.7f, Range = 80f },
            new DeadlockSound { Id = "seven_strike_explode", VpkPrefix = "sounds/abilities/gigawatt/gigawatt_storm_cloud_bolt_explo", Volume = 0.4f, Range = 100f, MinGap = 0.05f }, // quieter (user)
            // Deadlock numbers his VO by ability: 1 Lightning Ball, 2 Static Charge, 3 Power Surge, 4 Storm Cloud (captions checked).
            new DeadlockSound { Id = "seven_vo_power1", VpkPrefix = "sounds/vo/gigawatt/gigawatt_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_power2", VpkPrefix = "sounds/vo/gigawatt/gigawatt_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_power3", VpkPrefix = "sounds/vo/gigawatt/gigawatt_use_power3", Chance = 0.6f, MinGap = 4f, Voice = true },
            // "_0": 01-09 and the 01 alt laugh, not the gigawatt_use_power4_as_enemy lines (what an enemy hears).
            new DeadlockSound { Id = "seven_vo_power4", VpkPrefix = "sounds/vo/gigawatt/gigawatt_use_power4_0", Range = 80f, MinGap = 4f, Voice = true },
            // Desperation: the same abilities cast below 35% health (DeadlockHeroVoice.Power picks them).
            new DeadlockSound { Id = "seven_vo_desperation_power1", VpkPrefix = "sounds/vo/gigawatt/gigawatt_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_desperation_power2", VpkPrefix = "sounds/vo/gigawatt/gigawatt_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_desperation_power3", VpkPrefix = "sounds/vo/gigawatt/gigawatt_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_desperation_power4", VpkPrefix = "sounds/vo/gigawatt/gigawatt_desperation_power4", Range = 80f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "seven_vo_select", VpkPrefix = "sounds/vo/gigawatt/gigawatt_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "seven_vo_kill", VpkPrefix = "sounds/vo/gigawatt/gigawatt_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "seven_vo_killstreak", VpkPrefix = "sounds/vo/gigawatt/gigawatt_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "seven_vo_pain", VpkPrefix = "sounds/vo/gigawatt/emote/gigawatt_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "seven_vo_pain_small", VpkPrefix = "sounds/vo/gigawatt/emote/gigawatt_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "seven_vo_low_health", VpkPrefix = "sounds/vo/gigawatt/gigawatt_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "seven_vo_death", VpkPrefix = "sounds/vo/gigawatt/emote/gigawatt_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "seven_vo_effort", VpkPrefix = "sounds/vo/gigawatt/emote/gigawatt_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "seven_vo_efforts", VpkPrefix = "sounds/vo/gigawatt/emote/gigawatt_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            new DeadlockSound { Id = "seven_dash", VpkPrefix = "sounds/player/movement/seven/seven_movement_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "seven_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "seven_land", VpkPrefix = "sounds/player/movement/seven/gigawatt_jump_land", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "seven_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
            new DeadlockSound { Id = "seven_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "seven_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "seven_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "seven_footstep", VpkPrefix = "sounds/player/footsteps/seven/seven_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
        };
    }
}
