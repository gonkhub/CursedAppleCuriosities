using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Paradox's numbers (Deadlock's hero_chrono, id 10 / citadel_weapon_chrono_set / citadel_ability_chrono_*, read from the
    /// 2026-10-06 vdata: today's client 6782 can't be decoded by the bundled converter). Deadlock calls her "chrono" (model
    /// heroes_staging/chrono, VO sounds/vo/chrono, sounds under chrono and paradox_). The kit is the user's (2026-10-14).
    /// </summary>
    public static class ParadoxData
    {
        public const string Hero = "paradox";
        public const string Section = "Paradox";

        // ---- survivor (Deadlock 730 health, 1 regen, 6.7 m/s) ----
        public static float BaseHealth = 95f;
        public const float HealthPerLevel = 28f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.7f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 1f;              // Deadlock EBaseHealthRegen
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = ParadoxModel.Play,
            StaminaPips = 3,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 6.7f,    // Deadlock ESprintSpeed over her run
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: the burst rifle (citadel_weapon_chrono_set: 5-round bursts, 0.0735 s apart, 0.294 s between, falloff 20 -> 57.5 m) ----
        public static float GunDamage = 0.4f;
        // Her rifle is her only proc source (user 3.0), middle of the pack: ~8.5 shots/s x 0.45 = ~3.8 procs/s, about Commando's.
        public const float GunProc = 0.45f;
        public const int BurstCount = 5;                // Deadlock m_iBurstShotCount
        public const float BurstInterval = 0.0735f;     // Deadlock m_flIntraBurstCycleTime
        public static float BurstCycle = 0.294f;        // Deadlock m_flCycleTime
        public const float GunFalloffStart = 20f, GunFalloffEnd = 57.5f, GunFalloffMin = 0.6f; // Seven's mid-range easing (user, Seven's playtest)
        public const float GunSpread = 0.3f;            // Deadlock 0.2 deg

        // ---- secondary: Pulse Bomb (citadel_ability_chrono_pulse_grenade: 3.2 s, a pulse every 0.8 s, 5.5 m +1 m a pulse, +4% a stack for 8 s) ----
        public static float PulseDamage = 1.5f;         // Deadlock PulseDamage 35
        public const float PulseBossScale = 0.5f;       // Deadlock m_flBossDamageScale
        public static float PulseInterval = 0.8f;       // Deadlock PulseInterval; shorter with attack speed (user)
        public static float PulseDuration = 3.2f;       // Deadlock AbilityDuration (Duration Extender lengthens it)
        public const float PulseRadius = 5.5f;          // Deadlock Radius
        public const float PulseRadiusGrowth = 1f;      // Deadlock RadiusIncreasePerPulse
        public static float PulseAmpPerStack = 0.10f;   // Paradox's damage to the target (Deadlock DamageAmplificationPerStack 4%; 10% since 3.0, user: not more, stacks go well past 4 with attack speed, Backup Mags, Duration Extender)
        public const float PulseAmpSeconds = 8f;        // Deadlock DebuffDuration
        public const float PulseSlow = 0.2f, PulseSlowSeconds = 0.4f; // Deadlock SlowPercent / MovementSlowDuration 0.2 (a pulse apart, a little longer)
        public static float PulseCooldown = 8f;         // Deadlock 32
        public const float PulseThrowSpeed = 40.6f;     // Deadlock m_flBulletSpeed 1600 hu/s
        public const float PulseThrowUp = 10f;          // Deadlock m_flVerticalAimBias

        // ---- secondary alt: Time Wall (citadel_ability_chrono_time_wall: 8 x 4 x 0.5 m, 5.5 s, 5 m ahead, +30% friendly bullets) ----
        public static float WallDamage = 15f;           // user: 500% to enemies that pass through, +200% (user 2.5)
        public static float WallDuration = 5.5f;        // Deadlock AbilityDuration
        public const float WallWidth = 8f, WallHeight = 4f, WallDepth = 0.5f; // Deadlock TimeWallWidth/Height/Depth
        public const float WallDistance = 5f;           // Deadlock AbilityCastRange 200 hu
        public static float WallFriendlyBonus = 0.3f;   // Deadlock FriendlyBulletDamageBonus (3.0, user: briefly 50%, back to 30%: the wall already hits hard on swaps and helps the whole team)
        public const float WallSlow = 0.64f, WallSlowSeconds = 2f; // Deadlock MovementSlowPct / AuraEffectDuration
        public const float WallFormation = 0.5f;        // Deadlock TimeWallFormationTime
        public static float WallCooldown = 12f;         // Deadlock 25

        // ---- utility: Kinetic Carbine (citadel_ability_chrono_kinetic_carbine: tap to charge, left click fires (user 2.5); charge 2.5 s, +25% speed for 3.5 s, time-slow on hit) ----
        public static float CarbineMinDamage = 4f;
        public static float CarbineMaxDamage = 15f;     // fully charged (Deadlock MaxBonusBulletDamage scales hardest of her kit); 2000% until 3.0 (user)
        public const float CarbineChargeSeconds = 2.5f; // Deadlock MaxChargeDuration
        public const float CarbineHoldSeconds = 5f;     // seconds full charge waits for her click, x Duration Extender; then wasted (user 2.5)
        public static float CarbineSpeed = 0.25f;       // Deadlock SpeedChange: builds while charging, held for 3.5 s after the shot
        public const float CarbineSpeedSeconds = 3.5f;  // Deadlock SpeedBoostDuration
        public const float CarbineSlow = 0.9f;          // Deadlock TimeScaleDebuff 90: a massive slow...
        public const float CarbineSlowSeconds = 1.5f;   // ...fading out quickly (user)
        public static float CarbineCooldown = 10f;      // Deadlock 28
        public const float CarbineRadius = 0.4f;        // Deadlock BulletRadiusOverride 16 hu

        // ---- special: Paradoxical Swap (citadel_ability_chrono_swap: 25 m, 3500 hu/s, 150 damage, swap 0.6-1 s over 30 m, 110 s) ----
        public static float SwapDamage = 3f;            // halved (user 2.5; was 6)
        public static float SwapRange = 35f;            // user: limited range (Deadlock AbilityCastRange 25)
        public const float SwapSpeed = 89f;             // Deadlock m_flBulletSpeed 3500 hu/s
        public const float SwapRadius = 1.5f;           // a big shot, like Grey Talon's Charged Shot (user); Deadlock 32 hu
        public const float SwapMinTime = 1f, SwapMaxTime = 1.6f, SwapTimeDistance = 30f; // slower (user 2.5); Deadlock MinSwapTime 0.6 / SwapTime 1 / DistanceToMaxTime
        public const float SwapArc = 3f;                // metres up at the middle of the swap
        public const float SwapPass = 1.5f;             // metres each side of the line: they pass by each other (user)
        public const float SwapSlow = 0.9f, SwapSlowSeconds = 2.5f; // user: a fading slow when the swap ends
        public const float SwapCastDelay = 0.15f;       // Deadlock AbilityCastDelay
        public const float SwapGroupRadius = 4f;        // enemies this close to the one hit are swapped along with it (user 2.5; Mystic Expansion: wider)
        public static float SwapCooldown = 30f;         // Deadlock 110

        public static void Bind(ConfigFile c) // the hero's config section is only its keybind layout (3.0)
        {
            Movement.Bind(c, Section);
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/chrono/chrono.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f;
        public const string MuzzleBone = "muzzle_fx";   // checked against her skeleton once the converter works again

        public const string IconPortrait = "panorama/images/heroes/chrono_sm_psd.vtex_c";

        // The primary shows the weapon itself (user 3.0, like Celeste's baton): Deadlock's shop image of the hero's gun.

        public const string IconGun = "panorama/images/heroes/guns/chrono_gun_psd.vtex_c";
        public const string IconPulse = "panorama/images/hud/abilities/chrono/chrono_time_bomb_psd.vtex_c";
        public const string IconWall = "panorama/images/hud/abilities/chrono/chrono_time_wall_psd.vtex_c";
        public const string IconCarbine = "panorama/images/hud/abilities/duo/duo_attack_psd.vtex_c"; // Kinetic Carbine's m_strAbilityImage
        public const string IconSwap = "panorama/images/hud/abilities/chrono/chrono_swap_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Paradox is an AnimGraph2 hero (heroes_staging/chrono/clips):
        /// ability_pulse_grenade_cast, ability_time_wall, ability_kinetic_carbine_charging/_shoot, ability_paradoxical_swap_*.
        /// Her model exports no out_of_combat_run_* or death_*: she sprints gun-up (primary_sprint_gun_up_*), a knockdown stands in for death.
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "weapon_stand_idle", true, "", "full"),
            ("run_n", "weapon_run_n", true, "", "full"), ("run_ne", "weapon_run_ne", true, "", "full"),
            ("run_e", "weapon_run_e", true, "", "full"), ("run_se", "weapon_run_se", true, "", "full"),
            ("run_s", "weapon_run_s", true, "", "full"), ("run_sw", "weapon_run_sw", true, "", "full"),
            ("run_w", "weapon_run_w", true, "", "full"), ("run_nw", "weapon_run_nw", true, "", "full"),
            ("sprint_n", "primary_sprint_gun_up_n", true, "", "full"), ("sprint_ne", "primary_sprint_gun_up_ne", true, "", "full"),
            ("sprint_e", "primary_sprint_gun_up_e", true, "", "full"), ("sprint_se", "primary_sprint_gun_up_se", true, "", "full"),
            ("sprint_s", "primary_sprint_gun_up_s", true, "", "full"), ("sprint_sw", "primary_sprint_gun_up_sw", true, "", "full"),
            ("sprint_w", "primary_sprint_gun_up_w", true, "", "full"), ("sprint_nw", "primary_sprint_gun_up_nw", true, "", "full"),
            ("jump", "jump_ground", false, "in_air", "full"),
            ("air_jump", "jump_air", false, "in_air", "full"),
            ("wall_jump", "jump_ground", false, "in_air", "full"),
            ("in_air", "in_air_loop_apex", true, "", "full"),
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
            ("shoot", "shoot_idle", false, "locomotion", "delta"),
            ("pulse", "ability_pulse_grenade_cast", false, "locomotion", "delta"),
            ("wall", "ability_time_wall", false, "locomotion", "delta"),
            ("carbine_charge", "ability_kinetic_carbine_charging", false, "hold", "delta"),
            ("carbine_shoot", "ability_kinetic_carbine_shoot", false, "locomotion", "delta"),
            ("swap_cast", "ability_paradoxical_swap_cast", false, "locomotion", "delta"),
            ("swap", "ability_paradoxical_swap_exchange_positions", false, "hold", "full"),
            ("swap_end", "ability_paradoxical_swap_end", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "knockdown_large_start", false, "hold", "full"), // her death_* clips aren't in her model's list
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "swap", "swap_end", "death" };

        /// <summary>Deadlock sounds (user 2026-10-12: I pick and wire them).</summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "paradox_gun", VpkPrefix = "sounds/weapons/chrono/chrono_weapon_fire", Volume = 0.55f, Range = 60f, MinGap = 0.03f, PitchJitter = 0.04f, VolumeJitter = 0.1f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "paradox_pulse_cast", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_cast", Volume = 0.9f },
            new DeadlockSound { Id = "paradox_pulse_loop", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_proj_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "paradox_pulse_land", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_proj_impact", Volume = 0.8f, Range = 40f },
            new DeadlockSound { Id = "paradox_pulse_armed", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_armed", Volume = 0.9f, Range = 50f, MinGap = 0.1f },
            new DeadlockSound { Id = "paradox_pulse_hit", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_hit", Volume = 0.6f, Range = 40f, MinGap = 0.08f },
            new DeadlockSound { Id = "paradox_pulse_expire", VpkPrefix = "sounds/abilities/chrono/paradox_pulse_grenade_expire", Volume = 0.8f, Range = 40f },
            new DeadlockSound { Id = "paradox_wall_cast", VpkPrefix = "sounds/abilities/chrono/paradox_time_wall_cast", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "paradox_wall_form", VpkPrefix = "sounds/abilities/chrono/paradox_time_wall_cast_delay", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "paradox_wall_hit", VpkPrefix = "sounds/abilities/chrono/paradox_time_wall_hit", Volume = 0.8f, Range = 50f, MinGap = 0.08f },
            new DeadlockSound { Id = "paradox_wall_block", VpkPrefix = "sounds/abilities/chrono/paradox_time_wall_projectile_impact", Volume = 0.6f, Range = 40f, MinGap = 0.05f },
            new DeadlockSound { Id = "paradox_wall_expire", VpkPrefix = "sounds/abilities/chrono/paradox_time_wall_expire", Volume = 0.8f, Range = 50f },
            new DeadlockSound { Id = "paradox_carbine_loop", VpkPrefix = "sounds/abilities/chrono/chrono_kinetic_carbine_charge_lp", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "paradox_carbine_full", VpkPrefix = "sounds/abilities/chrono/chrono_kinetic_carbine_charge_expire", Volume = 0.8f, Range = 30f },
            new DeadlockSound { Id = "paradox_carbine_fire", VpkPrefix = "sounds/abilities/chrono/chrono_kinetic_carbine_fire", Range = 90f },
            new DeadlockSound { Id = "paradox_carbine_hit", VpkPrefix = "sounds/abilities/chrono/paradox_kinetic_carbine_hit", Range = 60f },
            new DeadlockSound { Id = "paradox_carbine_slow", VpkPrefix = "sounds/abilities/chrono/paradox_kinetic_carbine_slow_down", Volume = 0.8f, Range = 40f },
            new DeadlockSound { Id = "paradox_swap_cast", VpkPrefix = "sounds/abilities/chrono/paradox_swap_cast", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "paradox_swap_loop", VpkPrefix = "sounds/abilities/chrono/a2_chrono_swap/proj_lp", Volume = 0.7f, Range = 40f },
            new DeadlockSound { Id = "paradox_swap_hit", VpkPrefix = "sounds/abilities/chrono/paradox_swap_hit", Range = 70f },
            new DeadlockSound { Id = "paradox_swap_go", VpkPrefix = "sounds/abilities/chrono/paradox_swap_activate", Range = 70f },
            new DeadlockSound { Id = "paradox_swap_end", VpkPrefix = "sounds/abilities/chrono/paradox_swap_expire", Volume = 0.8f, Range = 50f },
            // VO by ability: 1 Pulse Bomb, 2 Time Wall, 3 Kinetic Carbine, 4 Paradoxical Swap.
            new DeadlockSound { Id = "paradox_vo_power1", VpkPrefix = "sounds/vo/chrono/chrono_use_power1", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_power2", VpkPrefix = "sounds/vo/chrono/chrono_use_power2", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_power3", VpkPrefix = "sounds/vo/chrono/chrono_use_power3", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_power4", VpkPrefix = "sounds/vo/chrono/chrono_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_desperation_power1", VpkPrefix = "sounds/vo/chrono/chrono_desperation_power1", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_desperation_power2", VpkPrefix = "sounds/vo/chrono/chrono_desperation_power2", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_desperation_power3", VpkPrefix = "sounds/vo/chrono/chrono_desperation_power3", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_desperation_power4", VpkPrefix = "sounds/vo/chrono/chrono_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_select", VpkPrefix = "sounds/vo/chrono/chrono_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_kill", VpkPrefix = "sounds/vo/chrono/chrono_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_killstreak", VpkPrefix = "sounds/vo/chrono/chrono_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_pain", VpkPrefix = "sounds/vo/chrono/emote/chrono_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_pain_small", VpkPrefix = "sounds/vo/chrono/emote/chrono_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_low_health", VpkPrefix = "sounds/vo/chrono/chrono_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_death", VpkPrefix = "sounds/vo/chrono/emote/chrono_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_effort", VpkPrefix = "sounds/vo/chrono/emote/chrono_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "paradox_vo_efforts", VpkPrefix = "sounds/vo/chrono/emote/chrono_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Movement: her own footsteps, foley and landing; the shared jump/slide sounds.
            new DeadlockSound { Id = "paradox_footstep", VpkPrefix = "sounds/player/footsteps/chrono/paradox_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "paradox_land", VpkPrefix = "sounds/player/movement/chrono/chrono_jump_land", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "paradox_dash", VpkPrefix = "sounds/player/movement/chrono/chrono_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "paradox_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "paradox_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "paradox_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "paradox_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "paradox_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };

        /// <summary>Pulse Bomb's blast radius at a pulse (0-based), before Mystic Expansion.</summary>
        public static float PulseRadiusAt(int pulse) => PulseRadius + PulseRadiusGrowth * pulse;

        /// <summary>How long a swap across this distance takes (Deadlock: MinSwapTime, up to SwapTime at DistanceToMaxTime).</summary>
        public static float SwapTime(float distance) => Mathf.Lerp(SwapMinTime, SwapMaxTime, Mathf.Clamp01(distance / SwapTimeDistance));
    }
}
