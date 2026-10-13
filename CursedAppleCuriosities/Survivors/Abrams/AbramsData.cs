using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Abrams
{
    /// <summary>
    /// Abrams' numbers (Deadlock's hero_atlas / citadel_*_bull*, read from build 25841406). Deadlock values are kept where RoR2 has the same scale (metres, seconds, speeds); damage becomes RoR2
    /// damage coefficients and cooldowns RoR2-length. Headline knobs are in the [Abrams] config section.
    /// </summary>
    public static class AbramsData
    {
        public const string Hero = "abrams";
        public const string Section = "Abrams";

        // ---- survivor: the tank (Deadlock 800 health, Lash 120 here: RoR2's bruisers sit at 160) ----
        public static float BaseHealth = 160f;
        public const float HealthPerLevel = 48f;
        public const float BaseArmor = 20f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.4f;            // Deadlock EMaxMoveSpeed (Lash 7.2)
        public const float BaseRegen = 1.5f;            // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: hero defaults (3 stamina, 1 air jump, 1 air dash) plus Abrams' own dash timings ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = AbramsModel.Play,
            GroundDashMetres = 21f,            // farther than Lash's 10 m (Deadlock 10): 14 m, then 1.5x for his bigger model (playtest)
            GroundDashSeconds = 1.4f,          // but slower: 15 m/s against Lash's 33 (playtest: 0.6, then 0.85 s were too fast; 1.2 s at 14 m)
            GroundDashTotalSeconds = 1.5f,     // a short ease to a run after the burst
            AirDashMetres = 11f,               // heroes 8 m; longer for his size (playtest)
            AirDashSeconds = 0.51f,            // Deadlock EAirDashDuration
            OutOfCombatSpeed = 0.45f,          // Deadlock ESprintSpeed over his run was +25%; +45% (Lash's 9.3 m/s) by playtest
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: the shotgun (9 pellets; no reload: infinite ammo, like Lash) ----
        public static float PelletDamage = 0.45f;
        public static int Pellets = 9;                  // Deadlock m_iBullets
        public const float ShotCycle = 0.63f;           // Deadlock m_flCycleTime
        public const float SpreadDeg = 4.5f;            // Deadlock 2 deg spread x 2.1 pellet scatter

        // ---- secondary: Siphon Life ----
        public static float SiphonDps = 1.5f;           // damage coefficient per second to each enemy (Deadlock 22 DPS)
        public static float SiphonHeal = 0.35f;         // Deadlock NonHeroHealingFactor 35% (70% from heroes)
        public static float SiphonCooldown = 12f;       // Deadlock 42
        public const float SiphonDuration = 4f;         // Deadlock AbilityDuration
        public static float SiphonRadius = 8f;          // Deadlock Radius 8m
        public const float SiphonTick = 0.25f;          // Deadlock TickRate

        // ---- special: Heavy Melee (Abrams' Deadlock melee: 116 heavy, 5 m, 30 deg half-angle) ----
        public static float MeleeDamage = 15f;      // 600% as a secondary alt; big buff on moving to special (playtest)
        public static float MeleeCooldown = 3f;
        public const float MeleeWindup = 0.4f;          // heavy wind-up
        public const float MeleeRange = 5f;             // Deadlock MeleeAttackLength, measured to the enemy's surface
        public const float MeleeHalfAngle = 60f;        // Deadlock's 30 deg is too thin for RoR2's third-person camera
        public const float MeleePointBlank = 2f;        // enemies this close (surface) are hit whatever the angle
        public const float MeleeLunge = 4f;             // m/s forward drift during the wind-up
        public const float MeleeLungeSpeed = 32f;       // m/s: the punch itself lunges...
        public const float MeleeLungeTime = 0.3f;       // ...for this long (~10 m; 0.2 s / ~6 m before 1.8, user), stopping at the first enemy it reaches
        public const float MeleeRecover = 0.3f;

        // ---- utility: Shoulder Charge ----
        public static float ChargeDamage = 0.5f;        // the scoop (user 2026-10-12: 250% -> 50%, the wall slam does the damage)
        public static float ChargeWallDamage = 3f;      // to every enemy carried into a wall (user)
        public static float ChargeCooldown = 8f;        // Deadlock 33
        public const float ChargeDuration = 1.4f;       // Deadlock AbilityDuration
        public static float ChargeSpeed = 18.75f;       // Deadlock SpeedInitial
        public static float ChargeSpeedMax = 30f;       // Deadlock ChargeSpeedMax
        public const float ChargeTurnDegPerSec = 140f;  // Deadlock TurnRateMax
        public const float ChargeRadius = 2.2f;         // Deadlock ChargeRadius
        public const float ChargeCarryAhead = 1.6f;     // carried enemies ride this far ahead (Deadlock forward offset 120 hu = 3 m from his origin)
        public const float ChargeBumpStun = 0.3f;       // Deadlock StunDuration (charge ends without a wall)
        public static float ChargeWallStun = 1.1f;      // Deadlock 0.3 + its 0.8 s upgrade, given free (Lash got his Grapple upgrade)
        public const float ChargeWallLookAhead = 2.5f;  // Deadlock m_flWallStunLookAheadDist 100 hu, plus Abrams' own radius

        // ---- passive: Infernal Resilience ----
        public static float ResilienceFraction = 0.5f;  // Deadlock 13% (22% fully upgraded); RoR2 hits much harder (25%, then 50% by playtest)
        public static float ResilienceArmor = 10f;      // on top of BaseArmor: the passive's inherent toughness (playtest)
        public const float ResilienceSeconds = 20f;     // Deadlock RegenIncomingDamageDuration
        public const float ResilienceInterval = 1f;     // Deadlock RegenDamageInterval
        public static float MeleeLifesteal = 0.18f;     // Deadlock's Infernal Resilience upgrade MeleeLifesteal 18%, given free: melee hits heal

        // ---- special alt: Seismic Impact ----
        public static float SlamDamage = 22f;            // 1200% at first; buffed by playtest
        public static float SlamCooldown = 33f;         // Deadlock 215; 28, then +5 s with the damage buff (playtest)
        public const float SlamUp = 30.5f;              // Deadlock m_flUpBoostSpeed 850 hu/s (21.6 m/s); x1.41 = twice the height (playtest)
        public const float SlamForward = 15.88f;        // Deadlock m_flForwardBoostSpeed 625 hu/s
        public const float SlamBoostTime = 1.5f;        // Deadlock m_flBoostTime (rising at most this long)
        public const float SlamHover = 3f;              // Deadlock m_flHoverTime
        public const float SlamHoverDrift = 5.08f;      // Deadlock m_flHoverInputSpeedMax 200 hu/s
        public const float SlamHoverAccel = 20f;
        public const float SlamCrashBuffer = 0.5f;      // Deadlock m_flCrashDownInputBuffer: no crash before this
        public const float SlamMinAimDeg = 30f;         // Deadlock m_flMinAimAngle: the crash aims at least this far below flat
        public static float SlamCrashSpeed = 100f;      // Deadlock 6000 hu/s (152 m/s); Lash's 90 m/s dive was right in playtest
        public const float SlamCollide = 10f;           // Deadlock m_flCollideRadius 160 hu (4 m); much wider by playtest: enemies on the way down ride along
        public static float SlamPullRadius = 15f;       // enemies this near Abrams when the crash starts are dragged down with him (playtest)
        public static float SlamRadius = 9f;            // Deadlock ImpactRadius
        public const float SlamHeight = 6f;             // Deadlock ImpactHeight
        public static float SlamStun = 1.6f;            // Deadlock StunDuration
        public const float SlamLanding = 0.7f;          // Deadlock m_flLandingTime: planted after the impact

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Abrams max health at level 1 (+48 per level).").Value;
            PelletDamage = c.Bind(Section, "Shotgun - Pellet Damage", PelletDamage, "Damage coefficient per pellet.").Value;
            Pellets = c.Bind(Section, "Shotgun - Pellets", Pellets, "Pellets per shot.").Value;
            SiphonDps = c.Bind(Section, "Siphon Life - Damage Per Second", SiphonDps, "Damage coefficient per second to every enemy in range.").Value;
            SiphonHeal = c.Bind(Section, "Siphon Life - Heal Fraction", SiphonHeal, "Share of Siphon Life damage healed back.").Value;
            SiphonRadius = c.Bind(Section, "Siphon Life - Radius", SiphonRadius, "Metres.").Value;
            SiphonCooldown = c.Bind(Section, "Siphon Life - Cooldown", SiphonCooldown, "Seconds.").Value;
            MeleeDamage = c.Bind(Section, "Heavy Melee - Damage", MeleeDamage, "Damage coefficient.").Value;
            MeleeCooldown = c.Bind(Section, "Heavy Melee - Cooldown", MeleeCooldown, "Seconds.").Value;
            ChargeDamage = c.Bind(Section, "Shoulder Charge - Damage", ChargeDamage, "Damage coefficient to each enemy scooped up.").Value;
            ChargeWallDamage = c.Bind(Section, "Shoulder Charge - Wall Damage", ChargeWallDamage, "Damage coefficient to each carried enemy slammed into a wall.").Value;
            ChargeSpeed = c.Bind(Section, "Shoulder Charge - Start Speed", ChargeSpeed, "Metres per second at the start of the charge.").Value;
            ChargeSpeedMax = c.Bind(Section, "Shoulder Charge - End Speed", ChargeSpeedMax, "Metres per second the charge builds up to.").Value;
            ChargeWallStun = c.Bind(Section, "Shoulder Charge - Wall Stun", ChargeWallStun, "Seconds carried enemies are stunned when slammed into a wall.").Value;
            ChargeCooldown = c.Bind(Section, "Shoulder Charge - Cooldown", ChargeCooldown, "Seconds.").Value;
            ResilienceFraction = c.Bind(Section, "Infernal Resilience - Regen Fraction", ResilienceFraction, $"Share of damage taken that heals back over {ResilienceSeconds:0} s.").Value;
            ResilienceArmor = c.Bind(Section, "Infernal Resilience - Armor", ResilienceArmor, "Bonus armor from the passive.").Value;
            MeleeLifesteal = c.Bind(Section, "Heavy Melee - Lifesteal", MeleeLifesteal, "Share of Heavy Melee damage dealt healed back.").Value;
            SlamDamage = c.Bind(Section, "Seismic Impact - Damage", SlamDamage, "Damage coefficient around the landing.").Value;
            SlamRadius = c.Bind(Section, "Seismic Impact - Radius", SlamRadius, "Metres.").Value;
            SlamStun = c.Bind(Section, "Seismic Impact - Stun", SlamStun, "Seconds enemies hit are stunned.").Value;
            SlamCrashSpeed = c.Bind(Section, "Seismic Impact - Crash Speed", SlamCrashSpeed, "Metres per second on the way down.").Value;
            SlamCooldown = c.Bind(Section, "Seismic Impact - Cooldown", SlamCooldown, "Seconds.").Value;
            SlamPullRadius = c.Bind(Section, "Seismic Impact - Pull Radius", SlamPullRadius, "Enemies within this many metres when the crash starts are dragged down to the landing.").Value;
            CameraHeight = c.Bind(Section, "Camera - Height", CameraHeight, "Metres the camera is raised above the default survivor camera.").Value;
            CameraDistance = c.Bind(Section, "Camera - Distance", CameraDistance, "Camera distance behind Abrams, as a multiple of the default survivor camera.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/abrams/abrams.vmdl_c";
        // Camera: his model is 1.5x, so the stand-in's camera put the crosshair behind his head (playtest).
        public static float CameraHeight = 1.4f;        // metres the camera pivot is raised over Commando's
        public static float CameraDistance = 1.45f;     // times Commando's camera distance
        public const float ModelHeightScale = 1.2f * 1.5f * 0.95f; // Deadlock 2.3 m tall, then 1.5x by playtest (visual only: hitbox unchanged), then x0.95 (user, 1.7: all but Vyper a little smaller)
        public const string MuzzleBone = "muzzle_fx";

        // Deadlock's internal name for Abrams is "bull" (hero_atlas).
        public const string IconPortrait = "panorama/images/heroes/bull_sm_psd.vtex_c";
        public const string IconSiphon = "panorama/images/hud/abilities/bull_drain_psd.vtex_c";
        public const string IconCharge = "panorama/images/hud/abilities/bull_charge_psd.vtex_c";
        public const string IconResilience = "panorama/images/hud/abilities/bull_beef_psd.vtex_c";
        public const string IconSlam = "panorama/images/hud/abilities/bull_jump_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state ("locomotion", "hold", a state, or "" for loops), layer. Clip names are
        /// the model's own; Deadlock's animgraph (abrams_playercontrol_abilities, abrams_upperbodyreplace) picks
        /// ability_leap_upwards / _hover / _dive (intro, loop) / _impact, ability_charge (precast, loop, in air, toss)
        /// and the syphon locomotion set, which are these.
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "primary_stand_idle", true, "", "full"),
            ("run_n", "primary_run_n", true, "", "full"), ("run_ne", "primary_run_ne", true, "", "full"),
            ("run_e", "primary_run_e", true, "", "full"), ("run_se", "primary_run_se", true, "", "full"),
            ("run_s", "primary_run_s", true, "", "full"), ("run_sw", "primary_run_sw", true, "", "full"),
            ("run_w", "primary_run_w", true, "", "full"), ("run_nw", "primary_run_nw", true, "", "full"),
            ("sprint_n", "primary_sprint_n", true, "", "full"), ("sprint_ne", "primary_sprint_ne", true, "", "full"),
            ("sprint_e", "primary_sprint_e", true, "", "full"), ("sprint_se", "primary_sprint_se", true, "", "full"),
            ("sprint_s", "primary_sprint_s", true, "", "full"), ("sprint_sw", "primary_sprint_sw", true, "", "full"),
            ("sprint_w", "primary_sprint_w", true, "", "full"), ("sprint_nw", "primary_sprint_nw", true, "", "full"),
            // Siphon Life: Abrams keeps moving with his drain pose.
            ("siphon.run_n", "primary_run_syphon_n", true, "", "full"), ("siphon.run_ne", "primary_run_syphon_ne", true, "", "full"),
            ("siphon.run_e", "primary_run_syphon_e", true, "", "full"), ("siphon.run_se", "primary_run_syphon_se", true, "", "full"),
            ("siphon.run_s", "primary_run_syphon_s", true, "", "full"), ("siphon.run_sw", "primary_run_syphon_sw", true, "", "full"),
            ("siphon.run_w", "primary_run_syphon_w", true, "", "full"), ("siphon.run_nw", "primary_run_syphon_nw", true, "", "full"),
            ("siphon.sprint_n", "primary_run_syphon_fast_n", true, "", "full"), ("siphon.sprint_ne", "primary_run_syphon_fast_ne", true, "", "full"),
            ("siphon.sprint_e", "primary_run_syphon_fast_e", true, "", "full"), ("siphon.sprint_nw", "primary_run_syphon_fast_nw", true, "", "full"),
            ("siphon.sprint_w", "primary_run_syphon_fast_w", true, "", "full"),
            ("jump", "jump_start", false, "in_air", "full"),
            ("air_jump", "jump_start_short", false, "in_air", "full"),
            ("wall_jump", "jump_start_short", false, "in_air", "full"),
            ("in_air", "jump_upwards_loop", true, "", "full"),
            ("falling", "jump_downwards_loop", true, "", "full"),
            ("land", "jump_landing", false, "locomotion", "additive"),
            ("dash_ground", "dash_ground", false, "locomotion", "full"), // Deadlock's hero_dash graph (+abrams)
            ("dash_air_forward", "primary_airdash", false, "in_air", "full"),
            ("dash_air_back", "primary_airdash_bkward", false, "in_air", "full"),
            ("dash_air_left", "primary_airdash_left", false, "in_air", "full"),
            ("dash_air_right", "primary_airdash_right", false, "in_air", "full"),
            ("slide_start", "slide_intro", false, "slide", "full"),
            ("slide", "slide_forward", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "primary_hip_fire", false, "locomotion", "additive"),
            ("melee_start", "melee_start", false, "hold", "full"),
            ("melee_hit", "melee_hit", false, "locomotion", "full"),
            ("charge_start", "ability_charge_precast", false, "charge", "full"),
            ("charge", "ability_charge", true, "", "full"),
            ("charge_air", "ability_charge_inair_center", true, "", "full"),
            ("charge_hit", "ability_charge_post_hit", false, "locomotion", "full"),
            ("charge_wall", "wall_impact", false, "locomotion", "additive"), // Deadlock stores this one as offsets
            ("leap_launch", "ability_leap_upwards", false, "leap_hover", "full"),
            ("leap_hover", "ability_leap_hover_loop", true, "", "full"),
            ("leap_dive", "ability_leap_dive", false, "leap_brace", "full"),
            ("leap_brace", "ability_leap_dive_brace_loop", true, "", "full"),
            ("leap_smash", "ability_leap_smash", false, "locomotion", "full"),
            ("parry", "parry_to_idle", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry_to_idle", false, "in_air", "full"),
            ("death", "death_generic", false, "hold", "full"),
        };

        /// <summary>Clips that travel on their own (Deadlock moves heroes with them; RoR2 moves the body).</summary>
        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "charge", "charge_air", "charge_hit", "melee_hit", "leap_smash", "death", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right" };

        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "abrams_gun_fire", VpkPrefix = "sounds/weapons/abrams/abrams_wpn_fire", Volume = 0.7f, Range = 60f, MinGap = 0.03f, Fallback = "Play_bandit2_m1_shotgun" },
            new DeadlockSound { Id = "abrams_siphon_cast", VpkPrefix = "sounds/abilities/abrams/abrams_a1_siphonlife_cast", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_siphon_loop", VpkPrefix = "sounds/abilities/abrams/abrams_a1_siphonlife_loop", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_siphon_end", VpkPrefix = "sounds/abilities/abrams/abrams_a1_siphonlife_end", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_charge_cast", VpkPrefix = "sounds/abilities/abrams/abrams_a2_charge_cast", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_charge_step", VpkPrefix = "sounds/abilities/abrams/abrams_a2_charge_step", Volume = 0.7f, MinGap = 0.2f },
            new DeadlockSound { Id = "abrams_charge_impact", VpkPrefix = "sounds/abilities/abrams/a2_charge/abrams_charge_trooper_impact", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "abrams_charge_wall", VpkPrefix = "sounds/abilities/abrams/a2_charge/abrams_a2_charge_wall_impact", Range = 50f },
            new DeadlockSound { Id = "abrams_slam_cast", VpkPrefix = "sounds/abilities/abrams/a4_leap/abrams_a4_slam_cast", Range = 50f },
            new DeadlockSound { Id = "abrams_slam_float", VpkPrefix = "sounds/abilities/abrams/a4_leap/abrams_a4_slam_float_lp", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_slam_descend", VpkPrefix = "sounds/abilities/abrams/a4_leap/abrams_a4_slam_descend", Volume = 0.9f },
            new DeadlockSound { Id = "abrams_slam_impact", VpkPrefix = "sounds/abilities/abrams/a4_leap/abrams_a4_slam_impact", Range = 60f, MinGap = 0.05f },
            new DeadlockSound { Id = "abrams_melee_swing", VpkPrefix = "sounds/player/melee/shared/swing_charged", Volume = 0.9f, Range = 30f },
            new DeadlockSound { Id = "abrams_melee_impact", VpkPrefix = "sounds/player/melee/shared/melee_impact_default", Range = 30f, MinGap = 0.05f },
            // Deadlock numbers his VO by an older ability order: use_power1 is Shoulder Charge, use_power2 Siphon Life (playtest).
            new DeadlockSound { Id = "abrams_vo_power1", VpkPrefix = "sounds/vo/atlas/atlas_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_power2", VpkPrefix = "sounds/vo/atlas/atlas_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_power4", VpkPrefix = "sounds/vo/atlas/atlas_use_power4", Range = 50f, MinGap = 4f, Voice = true },
            // Desperation: the same abilities cast below a quarter health (DeadlockHeroVoice.Power picks them).
            new DeadlockSound { Id = "abrams_vo_desperation_power1", VpkPrefix = "sounds/vo/atlas/atlas_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_desperation_power2", VpkPrefix = "sounds/vo/atlas/atlas_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_desperation_power4", VpkPrefix = "sounds/vo/atlas/atlas_desperation_power4", Range = 50f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_melee", VpkPrefix = "sounds/vo/atlas/emote/atlas_effort_melee_big", Volume = 0.9f, Chance = 0.7f, MinGap = 1f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_select", VpkPrefix = "sounds/vo/atlas/atlas_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_kill", VpkPrefix = "sounds/vo/atlas/atlas_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_killstreak", VpkPrefix = "sounds/vo/atlas/atlas_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_pain", VpkPrefix = "sounds/vo/atlas/emote/atlas_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_pain_small", VpkPrefix = "sounds/vo/atlas/emote/atlas_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_low_health", VpkPrefix = "sounds/vo/atlas/atlas_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_death", VpkPrefix = "sounds/vo/atlas/emote/atlas_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_effort", VpkPrefix = "sounds/vo/atlas/emote/atlas_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "abrams_vo_efforts", VpkPrefix = "sounds/vo/atlas/emote/atlas_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            new DeadlockSound { Id = "abrams_dash", VpkPrefix = "sounds/player/movement/abrams/abrams_movement_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "abrams_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "abrams_land", VpkPrefix = "sounds/player/movement/abrams/jump_land", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "abrams_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
            new DeadlockSound { Id = "abrams_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "abrams_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "abrams_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "abrams_footstep", VpkPrefix = "sounds/player/footsteps/abrams/abrams_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
        };
    }
}
