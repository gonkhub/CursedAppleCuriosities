using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash's numbers. Deadlock values (HeroBrew's sheets, synced from Deadlock) are kept where RoR2 has the same
    /// scale (metres, seconds, speeds); damage becomes RoR2 damage coefficients and cooldowns RoR2-length.
    /// Headline knobs are in the [Lash] config section.
    /// </summary>
    public static class LashData
    {
        public const string Hero = "lash";
        public const string Section = "Lash";

        // ---- survivor ----
        public static float BaseHealth = 120f;
        public const float HealthPerLevel = 36f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 7.2f;           // Deadlock EMaxMoveSpeed
        public const int JumpCount = 3;                 // a ground jump + Lash's two air jumps (stamina each)

        // ---- Deadlock movement (HeroBrew's movement sheet, synced from Deadlock) ----
        public static int StaminaPips = 5;              // Deadlock EStamina 3; 5 by playtest (as in HeroBrew)
        public const float StaminaRegenSeconds = 4.5f;  // 1 / EStaminaRegenPerSecond
        public static float QuailStaminaRegen = 0.25f;  // Wax Quail: +25% stamina regen speed per stack (Lash never sprints, so no jump boost)
        public const int AirDashesPerAir = 2;
        public static bool GroundDash = true;
        public const float GroundDashMetres = 10f;
        public static float GroundDashSeconds = 0.3f;   // Deadlock 0.68; faster by playtest, quick enough to dodge projectiles
        public const float AirDashMetres = 8f, AirDashSeconds = 0.47f, AirDashEndSpeed = 0.2f;
        // Slide (Deadlock values; HeroBrew's movement sheet)
        public static UnityEngine.KeyCode SlideKey = UnityEngine.KeyCode.Mouse3; // Mouse 4 (back side button), by playtest
        public const float SlideDashSpeed = 16.5f, SlideActivation = 8.89f, SlideSustain = 4.32f;
        public static float SlideFriction = 1.2f;       // Deadlock 0.8; more by playtest
        public const float SlideMaxStart = 16.5f;       // a slide never starts faster than Deadlock's dash-slide (playtest: too fast)
        public const float SlideFrictionSlope = 0.15f, SlideSlopeMinDeg = 8f, SlideSlopeFacingDeg = 75f;
        public const float SlideAccMinSlopeDeg = 5f, SlideAccMaxSlopeDeg = 45f, SlideAccMin = 12.7f, SlideAccMax = 21.59f;
        public const float SlideMaxSpeedMin = 30.48f, SlideMaxSpeed = 38.1f, SlideTurnDegPerSec = 90f;
        // Wall jump
        public static float WallJumpUp = 22f;           // Deadlock 13.3; much stronger by playtest
        public const float WallJumpOutMax = 7.9f, WallJumpOutMin = 5.7f, WallJumpRecharge = 1.25f;
        public const float WallJumpPowerMin = 0.6f;     // a chained wall jump within 1.25 s (Deadlock 0.2)
        // Air movement (playtest: RoR2's air control felt sluggish). Lash keeps his momentum in the air; input
        // steers it at AirAccel (RoR2's own is 20 m/s2 toward walk speed), speed above a run bleeds off slowly.
        public static float AirAccel = 50f;
        // Deadlock's sprint: out of combat (RoR2: 5 s without dealing or taking damage) and moving, Lash sprints for
        // real (so every sprint item works: Energy Drink, Rose Buckler, Little Disciple, Weeping Fungus, modded ones),
        // building up from a run to Deadlock's sprint speed over SprintRampSeconds. The sprint key only dashes.
        public const float OutOfCombatSpeed = 2.1f / 7.2f; // Deadlock ESprintSpeed over EMaxMoveSpeed: +29%
        public static float SprintRampSeconds = 2f;
        public const float AirDrag = 3f;
        public const float DashJumpMetres = 19f, DashJumpVertical = 10.8f, DashJumpWindowStart = 0.3f, DashJumpWindowEnd = 0.55f;

        // ---- primary: Lash's gun (3-round burst) ----
        public static float GunDamage = 0.75f;
        public const int BurstCount = 3;
        public const float BurstInterval = 0.1f;        // Deadlock 0.084; slower by playtest
        public static float BurstCycle = 0.55f;         // Deadlock 0.2625; slower by playtest
        public static float GunSpread = 0.35f;          // share of RoR2's spread bloom (1 = Commando-like)
        public const float GunBloomPerShot = 0.1f;

        // ---- secondary: Ground Strike ----
        public static float GroundStrikeDamage = 5f;
        public static float GroundStrikeDamagePerMetre = 0.5f;
        public const float GroundStrikeDamagePerMetreBeyond = 0.35f;
        public const float FallPrimaryM = 25f;          // Deadlock
        public static float GroundStrikeCooldown = 6f;
        public const float GroundStrikeRadius = 10f;    // Deadlock Radius 10m
        public const float GroundStrikeMinHeight = 3f;  // cast lower and Lash hops up first
        public const float GroundStrikeMinAimDeg = 60f; // dive at most this far from straight down
        public static float GroundStrikeSpeed = 90f;  // Deadlock StrikeVelocity 50; faster by playtest

        // ---- secondary alt: Flog ----
        public static float FlogDamage = 6f;
        public static float FlogHealFraction = 0.25f;
        public static float FlogCooldown = 8f;
        public const float FlogRange = 25f;
        public const float FlogConeDeg = 40f;           // Deadlock TargetingConeAngle

        // ---- utility: Grapple ----
        public static float GrappleCooldown = 5f;
        public static float GrappleRange = 75f;         // Deadlock: 30 m; much longer by playtest
        public const float GrappleConeDeg = 25f;
        public static float GrappleLaunchSpeed = 35f;   // m/s, launched toward the target
        public const float GrappleLaunchAngle = 45f;    // degrees above flat

        // ---- special: Death Slam ----
        public static float DeathSlamDamage = 12f;
        public static float DeathSlamCooldown = 16f;
        // Lock-on area (playtest): a big cylinder around Lash, far above and below him; inside it, an enemy locks
        // when the crosshair stays near it (DeathSlamAimDeg from the camera's aim) for DeathSlamLockOn seconds.
        public static float DeathSlamRange = 30f;       // cylinder radius (Deadlock AbilityCastRange 20)
        public const float DeathSlamHeight = 60f;       // above and below Lash
        public static float DeathSlamAimDeg = 12f;
        public const float DeathSlamChannel = 2.76f;
        public static float DeathSlamLockOn = 0.25f;    // Deadlock 0.7; faster by playtest
        public static int DeathSlamMaxTargets = 6;
        public const float DeathSlamPull = 1f;          // Deadlock BoostTime
        public const float DeathSlamHang = 0.6f;
        public const float DeathSlamThrow = 14f;
        public const float DeathSlamSpeed = 40.6f;
        public const float DeathSlamRadius = 6f;        // Deadlock ImpactRadius
        public const float DeathSlamSlowSeconds = 4f;

        public static void Bind(ConfigFile c)
        {
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Lash max health at level 1 (+36 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet (3-round bursts).").Value;
            BurstCycle = c.Bind(Section, "Gun - Time Between Bursts", BurstCycle, "Seconds after a burst before the next (before attack speed).").Value;
            GunSpread = c.Bind(Section, "Gun - Spread", GunSpread, "Bullet spread: share of the normal spread bloom (0 = pinpoint).").Value;
            GroundStrikeDamage = c.Bind(Section, "Ground Strike - Damage", GroundStrikeDamage, "Damage coefficient on landing, before fall height.").Value;
            GroundStrikeDamagePerMetre = c.Bind(Section, "Ground Strike - Damage Per Metre", GroundStrikeDamagePerMetre, "Extra damage coefficient per metre fallen (first 25 m).").Value;
            GroundStrikeSpeed = c.Bind(Section, "Ground Strike - Dive Speed", GroundStrikeSpeed, "Metres per second.").Value;
            GroundStrikeCooldown = c.Bind(Section, "Ground Strike - Cooldown", GroundStrikeCooldown, "Seconds.").Value;
            FlogDamage = c.Bind(Section, "Flog - Damage", FlogDamage, "Damage coefficient to every enemy in the cone.").Value;
            FlogHealFraction = c.Bind(Section, "Flog - Heal Fraction", FlogHealFraction, "Share of Flog damage dealt healed back.").Value;
            FlogCooldown = c.Bind(Section, "Flog - Cooldown", FlogCooldown, "Seconds.").Value;
            GrappleLaunchSpeed = c.Bind(Section, "Grapple - Launch Speed", GrappleLaunchSpeed, "Metres per second, launched up at 45 degrees toward the target.").Value;
            GrappleRange = c.Bind(Section, "Grapple - Range", GrappleRange, "Metres.").Value;
            StaminaPips = c.Bind(Section, "Stamina", StaminaPips, "Stamina pips for dashes and air jumps.").Value;
            GroundDashSeconds = c.Bind(Section, "Ground Dash - Duration", GroundDashSeconds, "Seconds to cover the 10 m ground dash (lower = faster).").Value;
            SprintRampSeconds = c.Bind(Section, "Sprint - Build Up Time", SprintRampSeconds, "Seconds for the out-of-combat sprint to build up to full speed.").Value;
            QuailStaminaRegen = c.Bind(Section, "Wax Quail - Stamina Regen", QuailStaminaRegen, "Faster stamina refill per Wax Quail (0.25 = +25% per stack).").Value;
            GroundDash = c.Bind(Section, "Ground Dash", GroundDash, "Sprint key on the ground dashes (one stamina). Off = no ground dash (Lash never sprints either way).").Value;
            SlideKey = c.Bind(Section, "Slide Key", SlideKey, "Hold on the ground while fast (sprint, dash) or on a downhill slope to slide; hold in the air to slide on landing. Deadlock's crouch.").Value;
            SlideFriction = c.Bind(Section, "Slide - Friction", SlideFriction, "Share of slide speed lost per second on flat ground.").Value;
            AirAccel = c.Bind(Section, "Air Control", AirAccel, "How hard Lash steers in the air (m/s2). He keeps his momentum either way.").Value;
            DeathSlamLockOn = c.Bind(Section, "Death Slam - Lock On Time", DeathSlamLockOn, "Seconds an enemy must stay in the cone to be locked on.").Value;
            WallJumpUp = c.Bind(Section, "Wall Jump - Up Speed", WallJumpUp, "Upward launch speed off a wall (m/s). Chained wall jumps within 1.25 s start at 60% of it.").Value;
            DeathSlamRange = c.Bind(Section, "Death Slam - Radius", DeathSlamRange, "Radius of the lock-on cylinder around Lash (metres; it reaches 60 m above and below him).").Value;
            DeathSlamAimDeg = c.Bind(Section, "Death Slam - Aim Angle", DeathSlamAimDeg, "How close (degrees) the crosshair must be to an enemy to lock onto it.").Value;
            GrappleCooldown = c.Bind(Section, "Grapple - Cooldown", GrappleCooldown, "Seconds (only spent when there is a target).").Value;
            DeathSlamDamage = c.Bind(Section, "Death Slam - Damage", DeathSlamDamage, "Damage coefficient where thrown enemies land.").Value;
            DeathSlamCooldown = c.Bind(Section, "Death Slam - Cooldown", DeathSlamCooldown, "Seconds.").Value;
            DeathSlamMaxTargets = c.Bind(Section, "Death Slam - Max Targets", DeathSlamMaxTargets, "Most enemies one Death Slam can lift.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/lash/lash.vmdl_c";
        public const float ModelHeightScale = 1.08f;      // Lash stands a little taller than the stand-in's capsule
        public const string MuzzleBone = "muzzle_fx";     // Lash's gun is his glove

        public const string IconPortrait = "panorama/images/heroes/lash_sm_psd.vtex_c";
        // Deadlock's own file names are swapped for Lash's first ability (checked in HeroBrew).
        public const string IconGroundStrike = "panorama/images/hud/abilities/lash/lash_death_slam_psd.vtex_c";
        public const string IconGrapple = "panorama/images/hud/abilities/lash/lash_lash_psd.vtex_c";
        public const string IconFlog = "panorama/images/hud/abilities/lash/lash_flog_psd.vtex_c";
        public const string IconDeathSlam = "panorama/images/hud/abilities/lash/lash_counter_lash_psd.vtex_c";

        /// <summary>State, Deadlock clip, loop, next state ("locomotion", "hold", a state, or "" for loops), overlay kind.</summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "weapon_stand_idle", true, "", "full"),
            ("run_n", "weapon_run_n", true, "", "full"), ("run_ne", "weapon_run_ne", true, "", "full"),
            ("run_e", "weapon_run_e", true, "", "full"), ("run_se", "weapon_run_se", true, "", "full"),
            ("run_s", "weapon_run_s", true, "", "full"), ("run_sw", "weapon_run_sw", true, "", "full"),
            ("run_w", "weapon_run_w", true, "", "full"), ("run_nw", "weapon_run_nw", true, "", "full"),
            ("sprint_n", "primary_sprint_n", true, "", "full"), ("sprint_ne", "primary_sprint_ne", true, "", "full"),
            ("sprint_e", "primary_sprint_e", true, "", "full"), ("sprint_se", "primary_sprint_se", true, "", "full"),
            ("sprint_s", "primary_sprint_s", true, "", "full"), ("sprint_sw", "primary_sprint_sw", true, "", "full"),
            ("sprint_w", "primary_sprint_w", true, "", "full"), ("sprint_nw", "primary_sprint_nw", true, "", "full"),
            ("jump", "jump_ground", false, "in_air", "full"),
            ("air_jump", "jump_in_air_transition", false, "in_air", "full"),
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "dash_air_forward", false, "in_air", "full"),
            ("dash_air_back", "dash_air_back", false, "in_air", "full"),
            ("dash_air_left", "dash_air_left", false, "in_air", "full"),
            ("dash_air_right", "dash_air_right", false, "in_air", "full"),
            ("slide_start", "slide_start", false, "slide", "full"),
            ("slide", "slide_loop", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("wall_jump", "wall_jump", false, "in_air", "full"),
            ("in_air", "in_air_apex_loop", true, "", "full"),
            ("falling", "in_air_down_loop", true, "", "full"),
            ("land", "jump_landing", false, "locomotion", "additive"),
            ("hard_land", "hard_landing", false, "locomotion", "full"),
            ("shoot", "primary_shoot", false, "locomotion", "additive"),
            ("gs_drop", "ability_groundstrike_airdrop", false, "gs_dive", "full"),
            ("gs_dive", "ability_groundstrike_airdrop_loop", true, "", "full"),
            ("gs_land", "ability_groundstrike_legsmash", false, "locomotion", "full"),
            ("grapple_start", "ability_grapple_start", false, "grapple_air", "full"),
            ("grapple_air", "ability_grapple_in_air_loop", true, "", "full"),
            ("grapple_land", "ability_grapple_land", false, "locomotion", "full"),
            ("flog", "ability_flog", false, "locomotion", "full"),
            ("ds_channel_start", "cast_channeling_start", false, "ds_channel", "full"),
            ("ds_channel", "cast_channeling_loop", true, "", "full"),
            ("ds_lift", "ability_deathslam_lift_inair", false, "hold", "full"),
            ("ds_throw", "ability_deathslam_throw_inair", false, "locomotion", "full"),
            ("death", "death_01", false, "hold", "full"),
        };

        /// <summary>
        /// Clips whose own travel is removed (HeroBrew root_lock): Deadlock moves heroes with these animations, RoR2
        /// moves the body itself, so the model would run ahead. Measured pelvis drift: ground dash 13 m, sprints 5.4 m
        /// a loop, hard landing (the roll) 3.8 m, Ground Strike drop 2.8 m, death 1.6 m, air dashes 1.5 m, Death Slam
        /// throw 1.3 m, wall jump 1.3 m.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> Locked = new System.Collections.Generic.HashSet<string>
        { "dash_ground", "hard_land", "gs_drop", "death", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "ds_throw", "wall_jump" };

        public static bool RootLocked(string state) => Locked.Contains(state) || state.StartsWith("sprint_");

        public static string[] ClipNames()
        {
            var l = new System.Collections.Generic.List<string>();
            foreach (var a in Anims) if (!l.Contains(a.Clip)) l.Add(a.Clip);
            return l.ToArray();
        }

        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "lash_gun_fire", VpkPrefix = "sounds/weapons/lash/lash_weapon_fire", Volume = 0.55f, Range = 60f, MinGap = 0.03f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "lash_gs_whoosh", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_downstrike_whoosh", Volume = 0.9f },
            new DeadlockSound { Id = "lash_gs_impact", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_impact", Range = 50f },
            new DeadlockSound { Id = "lash_grapple_cast", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_cast", Volume = 0.9f },
            new DeadlockSound { Id = "lash_grapple_target", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_target", Volume = 0.8f },
            new DeadlockSound { Id = "lash_flog_cast", VpkPrefix = "sounds/abilities/lash/a3_flog/lash_a3_flog_cast", Volume = 0.9f },
            new DeadlockSound { Id = "lash_flog_impact", VpkPrefix = "sounds/abilities/lash/a3_flog/lash_a3_flog_impact", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_cast", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_cast", Range = 50f },
            new DeadlockSound { Id = "lash_ds_lockon", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_lockon", MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_launch", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_launch", Range = 50f },
            new DeadlockSound { Id = "lash_ds_impact", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_impact", Range = 50f, MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_channel", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_channel_lp", Volume = 0.9f },
            new DeadlockSound { Id = "lash_vo_power1", VpkPrefix = "sounds/vo/lash/lash_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power2", VpkPrefix = "sounds/vo/lash/lash_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power3", VpkPrefix = "sounds/vo/lash/lash_use_power3", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power4", VpkPrefix = "sounds/vo/lash/lash_use_power4", MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_select", VpkPrefix = "sounds/vo/lash/lash_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "lash_vo_kill", VpkPrefix = "sounds/vo/lash/lash_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "lash_vo_pain", VpkPrefix = "sounds/vo/lash/emote/lash_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "lash_vo_death", VpkPrefix = "sounds/vo/lash/emote/lash_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "lash_vo_effort", VpkPrefix = "sounds/vo/lash/emote/lash_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "lash_dash", VpkPrefix = "sounds/player/movement/lash/lash_movement_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "lash_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "lash_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
            // Sound pass: the rest of Lash's Deadlock sounds.
            new DeadlockSound { Id = "lash_gs_loop", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_mod_lp", Volume = 0.8f },
            new DeadlockSound { Id = "lash_gs_hit", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_hit", MinGap = 0.05f },
            new DeadlockSound { Id = "lash_grapple_loop", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_self_lp", Volume = 0.7f },
            new DeadlockSound { Id = "lash_grapple_impact", VpkPrefix = "sounds/abilities/lash/a2_grapple/target_impact", Volume = 0.8f },
            new DeadlockSound { Id = "lash_grapple_end", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_end", Volume = 0.8f, MinGap = 0.3f },
            new DeadlockSound { Id = "lash_ds_lockon_victim", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_lockon_victim", Volume = 0.8f, MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_miss", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_miss" },
            new DeadlockSound { Id = "lash_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "lash_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "lash_footstep", VpkPrefix = "sounds/player/footsteps/lash/lash_footstep", Volume = 0.45f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "lash_vo_pain_small", VpkPrefix = "sounds/vo/lash/emote/lash_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "lash_vo_efforts", VpkPrefix = "sounds/vo/lash/emote/lash_efforts", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            new DeadlockSound { Id = "lash_vo_low_health", VpkPrefix = "sounds/vo/lash/lash_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "lash_vo_ground_pound", VpkPrefix = "sounds/vo/lash/lash_massive_ground_pound", Chance = 0.8f, MinGap = 10f, Voice = true },
            new DeadlockSound { Id = "lash_vo_killstreak", VpkPrefix = "sounds/vo/lash/lash_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "lash_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "lash_land", VpkPrefix = "sounds/player/movement/lash/lash_jump_land", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
        };
    }
}
