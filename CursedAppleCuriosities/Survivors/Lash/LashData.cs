using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash's numbers. Deadlock values (from Deadlock's hero data) are kept where RoR2 has the same
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
        // ---- Deadlock movement (shared: Deadlock/DeadlockMovement.cs). Lash's overrides of the hero defaults ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = LashModelSwap.Play,
            StaminaPips = 4,              // heroes have 3; Lash is unique in having more (nerfed to 4)
            AirJumps = 2,                 // heroes have 1
            AirDashesPerAir = 2,          // heroes have 1
            AirDashMetres = 16f,          // heroes 8 m; doubled by playtest
            GroundDashSeconds = 0.3f,     // the 10 m burst (Deadlock 0.68): quick enough to dodge projectiles, by playtest;
                                          // then it eases to a run until 0.68 s, keeping Deadlock's 0.3-0.55 s dash-jump window
            WallJumpUp = 22f,             // Deadlock 13.3; much stronger by playtest
            WallJumpPowerMin = 0.6f,      // each wall jump within 1 s of the last is 60% of the one before (Deadlock 0.2)
        };

        // ---- primary: Lash's gun (3-round burst) ----
        public static float GunDamage = 0.75f;
        public const int BurstCount = 3;
        public const float BurstInterval = 0.084f;      // Deadlock
        public static float BurstCycle = 0.2625f;       // Deadlock
        public static float GunSpread = 0.15f;          // share of RoR2's spread bloom (1 = Commando-like); tight, a close-range gun (user)
        public const float GunBloomPerShot = 0.05f;     // no recoil either: Deadlock's Lash gun has none (Seven's climbs)
        // Short range (user): full damage to Deadlock's falloff start, easing to GunFalloffMin at its end (Deadlock eases to 10%).
        public static float GunFalloffStart = 16f;      // Deadlock m_flDamageFalloffStartRange 629.92 hu
        public static float GunFalloffEnd = 48f;        // Deadlock m_flDamageFalloffEndRange 1889.76 hu
        public const float GunFalloffMin = 0.35f;

        public static float GunFalloff(float distance) => DeadlockSurvivorKit.Falloff(distance, GunFalloffStart, GunFalloffEnd, GunFalloffMin);

        // ---- utility: Ground Strike ----
        public static float GroundStrikeDamage = 3f;
        public static float GroundStrikeDamagePerMetre = 0.5f;
        public const float GroundStrikeDamagePerMetreBeyond = 0.35f;
        public const float FallPrimaryM = 25f;          // Deadlock
        public static float GroundStrikeCooldown = 6f;
        public const float GroundStrikeRadius = 10f;    // Deadlock Radius 10m
        public const float GroundStrikeMinHeight = 3f;  // cast lower and Lash hops up first
        public const float GroundStrikeMinAimDeg = 60f; // dive at most this far from straight down
        public static float GroundStrikeSpeed = 90f;  // Deadlock StrikeVelocity 50; faster by playtest
        // Airborne enemies the dive runs into are dragged down beside Lash and let go in his strike (user)
        public const float GroundStrikeCarryMax = 5f;      // seconds: safety, the dive ends long before
        public const float GroundStrikeCarrySpeed = 160f;  // m/s an enemy may be pulled at to keep up with him

        // ---- secondary: Grapple ----
        public static float GrappleCooldown = 6f;
        public static float GrappleRange = 75f;         // Deadlock: 30 m; much longer by playtest
        public const float GrappleConeDeg = 25f;
        public static float GrappleLaunchSpeed = 35f;   // m/s, launched toward the target
        public static float GrappleLaunchAngle = 55f;   // degrees above flat (45 at first; more vertical by playtest)

        // ---- special: Death Slam ----
        public static float DeathSlamDamage = 12f;
        public static float DeathSlamCooldown = 48f;
        // Lock-on area (playtest): a big cylinder around Lash, far above and below him; inside it, an enemy locks
        // when the crosshair stays near it (DeathSlamAimDeg from the camera's aim) for DeathSlamLockOn seconds.
        public static float DeathSlamRange = 30f;       // cylinder radius (Deadlock AbilityCastRange 20)
        public const float DeathSlamHeight = 60f;       // above Lash; below him it is uncapped (playtest: grappling high lost targets underneath)
        public static float DeathSlamAimDeg = 25f;   // 12 at first; widened by playtest
        public const float DeathSlamChannel = 2.76f;
        public static float DeathSlamChannelMaxSpeed = 6f; // horizontal drift cap while channeling (playtest: grapple speed carried him too far)
        public static float DeathSlamLockOn = 0.25f;    // Deadlock 0.7; faster by playtest
        public static int DeathSlamMaxTargets = 6;
        public const float DeathSlamPull = 0.6f;        // Deadlock BoostTime 1 s; faster by playtest
        public const float DeathSlamHang = 0.3f;        // 0.6 at first; faster by playtest
        // The throw always waits for every lifted enemy to reach Lash (user, 1.7); the pull now gets them there in time anyway.
        public const float DeathSlamArrived = 1.5f;     // m from its hold spot (server)
        public const float DeathSlamWaitMax = 5f;       // s: a backstop for an enemy that can't get there (stuck on terrain)
        public const float DeathSlamHoldSpeed = 35f;    // m/s: once up, held beside Lash as he drifts. The pull itself is uncapped (1.7, user):
                                                        // it covers any distance in DeathSlamPull; the hold spot is kept under ceilings
        public const float DeathSlamFlightMax = 8f;     // s: a throw that never hits terrain by then is checked for the void
        public static float DeathSlamThrow = 28f;       // max throw distance, m (Deadlock 14; doubled by playtest)
        public const float DeathSlamSpeed = 40.6f;
        public const float DeathSlamRadius = 6f;        // Deadlock ImpactRadius
        public const float DeathSlamSlowSeconds = 4f;

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Lash max health at level 1 (+36 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet (3-round bursts).").Value;
            BurstCycle = c.Bind(Section, "Gun - Time Between Bursts", BurstCycle, "Seconds after a burst before the next (before attack speed).").Value;
            GunSpread = c.Bind(Section, "Gun - Spread", GunSpread, "Bullet spread: share of the normal spread bloom (0 = pinpoint).").Value;
            GunFalloffStart = c.Bind(Section, "Gun - Falloff Start", GunFalloffStart, "Metres of full damage.").Value;
            GunFalloffEnd = c.Bind(Section, "Gun - Falloff End", GunFalloffEnd, "Metres where damage reaches its floor (35%).").Value;
            GroundStrikeDamage = c.Bind(Section, "Ground Strike - Damage", GroundStrikeDamage, "Damage coefficient on landing, before fall height.").Value;
            GroundStrikeDamagePerMetre = c.Bind(Section, "Ground Strike - Damage Per Metre", GroundStrikeDamagePerMetre, "Extra damage coefficient per metre fallen (first 25 m).").Value;
            GroundStrikeSpeed = c.Bind(Section, "Ground Strike - Dive Speed", GroundStrikeSpeed, "Metres per second.").Value;
            GroundStrikeCooldown = c.Bind(Section, "Ground Strike - Cooldown", GroundStrikeCooldown, "Seconds.").Value;
            GrappleLaunchSpeed = c.Bind(Section, "Grapple - Launch Speed", GrappleLaunchSpeed, "Metres per second, launched up toward the target.").Value;
            GrappleLaunchAngle = c.Bind(Section, "Grapple - Launch Angle", GrappleLaunchAngle, "Degrees above flat Lash is launched at toward the target (90 = straight up).").Value;
            DeathSlamThrow = c.Bind(Section, "Death Slam - Throw Distance", DeathSlamThrow, "Farthest (metres, flat) Death Slam can throw enemies toward your crosshair.").Value;
            DeathSlamChannelMaxSpeed = c.Bind(Section, "Death Slam - Channel Speed Limit", DeathSlamChannelMaxSpeed, "Most horizontal speed (m/s) Lash keeps while channeling Death Slam.").Value;
            GrappleRange = c.Bind(Section, "Grapple - Range", GrappleRange, "Metres.").Value;
            DeathSlamLockOn = c.Bind(Section, "Death Slam - Lock On Time", DeathSlamLockOn, "Seconds an enemy must stay in the cone to be locked on.").Value;
            DeathSlamRange = c.Bind(Section, "Death Slam - Radius", DeathSlamRange, "Radius of the lock-on cylinder around Lash (metres; it reaches 60 m above him and all the way down below).").Value;
            DeathSlamAimDeg = c.Bind(Section, "Death Slam - Aim Angle", DeathSlamAimDeg, "How close (degrees) the crosshair must be to an enemy to lock onto it.").Value;
            GrappleCooldown = c.Bind(Section, "Grapple - Cooldown", GrappleCooldown, "Seconds (only spent when there is a target).").Value;
            DeathSlamDamage = c.Bind(Section, "Death Slam - Damage", DeathSlamDamage, "Damage coefficient where thrown enemies land.").Value;
            CameraHeight = c.Bind(Section, "Camera - Height", CameraHeight, "Metres the camera is raised above the default survivor camera.").Value;
            CameraDistance = c.Bind(Section, "Camera - Distance", CameraDistance, "Camera distance behind Lash, as a multiple of the default survivor camera.").Value;
            DeathSlamCooldown = c.Bind(Section, "Death Slam - Cooldown", DeathSlamCooldown, "Seconds.").Value;
            DeathSlamMaxTargets = c.Bind(Section, "Death Slam - Max Targets", DeathSlamMaxTargets, "Most enemies one Death Slam can lift.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/lash/lash.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f; // a little taller than the stand-in's capsule, then x1.25 (playtest: x1.1 still too small), then x1.15 to match Celeste (user), then x0.95 (user, 1.7: all but Vyper a little smaller)
        // Camera: lifted and pulled back a little for the bigger model (Abrams' needed it at x1.5).
        public static float CameraHeight = 0.6f;        // metres the camera pivot is raised over Commando's (0.4; raised with the x1.15 model, like Celeste)
        public static float CameraDistance = 1.25f;     // times Commando's camera distance (1.15; pulled back with the x1.15 model, like Celeste)
        public const string MuzzleBone = "muzzle_fx";     // Lash's gun is his glove

        public const string IconPortrait = "panorama/images/heroes/lash_sm_psd.vtex_c";
        // Deadlock's own file names are swapped for Lash's first ability.
        public const string IconGroundStrike = "panorama/images/hud/abilities/lash/lash_death_slam_psd.vtex_c";
        public const string IconGrapple = "panorama/images/hud/abilities/lash/lash_lash_psd.vtex_c";
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
            // Death Slam's own targeting pose (cast_channeling_* is the generic item channel every hero shares).
            // Death Slam, as Lash's Deadlock ability graph (lash_abilitymask) has it: the lift IS the channel (he
            // raises them while locking on, then holds the pose), then the throw. Ground and in-air variants.
            // (Not cast_channeling_*: the item cast graph; not lift_loop: a hero being lifted by someone else.)
            ("ds_channel", "ability_deathslam_lift_inair", false, "hold", "full"),
            ("ds_channel_ground", "ability_deathslam_lift", false, "hold", "full"),
            ("ds_throw", "ability_deathslam_throw_inair", false, "locomotion", "full"),
            ("ds_throw_ground", "ability_deathslam_throw", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry_in_air", false, "in_air", "full"),
            ("death", "death_01", false, "hold", "full"),
        };

        /// <summary>
        /// Clips whose own travel is removed (root lock): Deadlock moves heroes with these animations, RoR2
        /// moves the body itself, so the model would run ahead. Measured pelvis drift: ground dash 13 m, sprints 5.4 m
        /// a loop, hard landing (the roll) 3.8 m, Ground Strike drop 2.8 m, death 1.6 m, air dashes 1.5 m, Death Slam
        /// throw 1.3 m, wall jump 1.3 m.
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> Locked = new System.Collections.Generic.HashSet<string>
        { "dash_ground", "hard_land", "gs_drop", "death", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "ds_throw", "ds_throw_ground", "wall_jump" };


        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "lash_gun_fire", VpkPrefix = "sounds/weapons/lash/lash_weapon_fire", Volume = 0.55f, Range = 60f, MinGap = 0.03f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "lash_gs_whoosh", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_downstrike_whoosh", Volume = 0.9f },
            new DeadlockSound { Id = "lash_gs_impact", VpkPrefix = "sounds/abilities/lash/a1_ground_strike/lash_ground_strike_impact", Range = 50f },
            new DeadlockSound { Id = "lash_grapple_cast", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_cast", Volume = 0.9f },
            new DeadlockSound { Id = "lash_grapple_target", VpkPrefix = "sounds/abilities/lash/a2_grapple/lash_a2_grapple_target", Volume = 0.8f },
            new DeadlockSound { Id = "lash_ds_cast", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_cast", Range = 50f },
            new DeadlockSound { Id = "lash_ds_lockon", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_lockon", MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_launch", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_launch", Range = 50f },
            new DeadlockSound { Id = "lash_ds_impact", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_impact", Range = 50f, MinGap = 0.05f },
            new DeadlockSound { Id = "lash_ds_channel", VpkPrefix = "sounds/abilities/lash/a4_death_slam/lash_death_slam_channel_lp", Volume = 0.9f },
            new DeadlockSound { Id = "lash_vo_power1", VpkPrefix = "sounds/vo/lash/lash_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power2", VpkPrefix = "sounds/vo/lash/lash_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power3", VpkPrefix = "sounds/vo/lash/lash_use_power3", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_power4", VpkPrefix = "sounds/vo/lash/lash_use_power4", MinGap = 4f, Voice = true },
            // Desperation: the same abilities cast below 35% health (DeadlockHeroVoice.Power picks them).
            new DeadlockSound { Id = "lash_vo_desperation_power1", VpkPrefix = "sounds/vo/lash/lash_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_desperation_power2", VpkPrefix = "sounds/vo/lash/lash_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_desperation_power3", VpkPrefix = "sounds/vo/lash/lash_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "lash_vo_desperation_power4", VpkPrefix = "sounds/vo/lash/lash_desperation_power4", MinGap = 4f, Voice = true },
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
            new DeadlockSound { Id = "lash_footstep", VpkPrefix = "sounds/player/footsteps/lash/lash_footstep", Volume = 0.27f, Range = 20f, MinGap = 0.12f },
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
