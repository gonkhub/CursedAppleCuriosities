using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using RoR2;

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// Viscous' numbers (Deadlock's hero_viscous / viscous_goo_bowling_ball, read 2026-10-12). Deadlock distances are
    /// hammer units (39.37 hu = 1 m) and are kept as metres; damage becomes RoR2 damage coefficients (~5% per point, as
    /// Victor) and cooldowns RoR2-length. A Goo Ball prototype first (user: get the ball right before the rest of him):
    /// his gun is a placeholder and he has no secondary or utility yet. Headline knobs in the [Viscous] config section.
    /// </summary>
    public static class ViscousData
    {
        public const string Hero = "viscous";
        public const string Section = "Viscous";

        // ---- survivor (Deadlock 780 health, 2 regen, 7.2 m/s, 3 stamina) ----
        public static float BaseHealth = 115f;
        public const float HealthPerLevel = 34f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 7.2f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 2f;              // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: hero defaults (3 stamina, 1 air jump, 1 air dash) ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = ViscousModel.Play,
            StaminaPips = 3,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 7.2f,    // Deadlock ESprintSpeed over his run
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: his goo gun (placeholder until the rest of the kit: Deadlock 0.21 s between shots, 20 m falloff start) ----
        public static float GunDamage = 0.85f;
        public const float GunCycle = 0.21f;            // Deadlock m_flCycleTime
        public const float GunSpread = 0.2f;            // Deadlock m_Spread (degrees)
        public const float GunRadius = 0.2f;            // Deadlock m_flBulletRadius 8 hu

        // ---- special: Goo Ball (Deadlock viscous_goo_bowling_ball) ----
        public static float BallDuration = 11f;         // Deadlock AbilityDuration
        public static float BallCooldown = 32f;         // Deadlock 150 (user: 40 -> 20 -> 32)
        public const float BallCastDelay = 0.55f;       // Deadlock AbilityCastDelay: he curls up, then is the ball
        public const float BallCancelGrace = 0.3f;      // Special again after this ends it early (Deadlock: ability button cancels)
        public static float BallSpeed = 11.43f;         // Deadlock m_flDefaultBallSpeed 450 hu/s
        public static float BallFastSpeed = 20.32f;     // Deadlock m_flFastBallSpeed 800 hu/s, while Primary is held ("Go Fast")
        public const float BallAccel = 15.24f;          // Deadlock m_flSpeedAccel 600 hu/s2: up to the speed it should have
        public const float BallDecel = 1.27f;           // Deadlock m_flSpeedDeccel 50 hu/s2: a fast ball sheds speed slowly (while Primary is held)
        public const float BallBrake = 14f;             // user: let go of Primary and it drops back to its slow roll quickly
        public const float BallReboundHold = 1.5f;      // an impact rebound keeps all its speed this long (no brake, no decel, no stall bounce)
        public const float BallTurn = 110f;             // Deadlock m_flTurnRatio, deg/s toward where he looks
        public const float BallAirTurn = 50f;           // Deadlock m_flAirTurnRatio
        public const float BallElasticity = 0.65f;      // Deadlock m_flElasticity: speed kept off a wall
        public const float BallWallPause = 0.1f;        // Deadlock m_flWallPauseTime: no steering just after a bounce
        public static float BallJump = 22f;             // user: much higher than Deadlock JumpForce 500 hu/s (12.7)
        public static float BallAirJump = 22f;          // the same for the one double jump (Deadlock AirJumpForce 500 hu/s)
        public const float BallRadius = 1.4f;           // Deadlock BallRadius: the ball is drawn this big
        public static float BallHitRadius = 1.8f;       // Deadlock BallHitRadius: enemies this close to its centre are hit
        public static float BallDamage = 2.5f;          // base per hit (user: lowered from 550% when it went Heavy)
        public static float BallHeavyDamage = 0.3f;     // Heavy (Loader's punch, velocityDamageCoefficient 0.3): + this x speed in m/s
        public const float BallProc = 1f;
        public static float BallStun = 0.5f;            // Deadlock StunDuration
        public const float BallKnock = 10.16f;          // Deadlock KnockForce 400 hu/s, as m/s given to the enemy
        public const float BallKnockUp = 6f;            // ...plus a little lift so they leave the ground
        public const float BallRehit = 1.2f;            // Deadlock viscous_ball_damage_prevention: the same enemy again after this
        public static float BossFreeze = 0.3f;          // user: a boss stops the ball dead this long (hit-stop), then it bounces off as off a wall
        public static float BossDamageMult = 2f;        // user: bosses (and anything it can't stun) take double, and aren't stunned
        public const float ImpactRebound = 1.35f;       // user: it gains speed off an impact: 1.35x the speed it hit with, at least Go Fast's
        public const float ImpactReboundCap = 3f;       // ...but never above this many times Go Fast speed (chained impacts)
        public static float BallResist = 0.5f;         // damage taken while rolling (Deadlock BulletResist/TechResist 35%; 50% by the user)

        /// <summary>The ball's radius on this body: Mystic Expansion makes it bigger (user); it is drawn, bounced and hit with this.</summary>
        public static float Radius(CharacterBody? body) => BallRadius * MysticExpansion.RadiusMultiplier(body);

        // ---- alt primary: his alt fire (Deadlock citadel_weapon_viscous_set_2): a slow glob that bursts on impact ----
        public static float AltDamage = 3.5f;           // Deadlock 42 per glob (4x his bullet)
        public const float AltCycle = 1.26f;            // Deadlock m_flCycleTime
        public const float AltSpeed = 38.1f;            // Deadlock m_flBulletSpeed 1500 hu/s
        public const float AltRange = 25.4f;            // Deadlock m_flRange 1000 hu: it splats here if it hits nothing (no gravity: user)
        public static float AltRadius = 3.56f;          // Deadlock m_flExplosionRadius 140 hu (Mystic Expansion grows it)

        // ---- secondary: Splatter (Deadlock viscous_goo_grenade; user: 3 bounces, 500% each, Mystic Expansion) ----
        public static float SplatterDamage = 5f;        // per bounce (user)
        public static int SplatterBounces = 3;          // bursts before it's spent (user; Deadlock 2, 3 with its upgrade)
        public static float SplatterRadius = 5f;        // Deadlock Radius 5 m (Mystic Expansion grows it)
        public const float SplatterSpeed = 32f;
        public const float SplatterLob = 0.12f;         // thrown a little above the crosshair
        public const float SplatterRestitution = 1.3f;  // speed off each bounce (user: twice the first 0.65)
        public const float SplatterMinPop = 12f;       // at least this fast off a surface (was 6)
        public const float SplatterRearm = 0.12f;       // Deadlock DetonateCooldown: a contact this soon after a burst doesn't burst again
        public const float SplatterLifetime = 8f;
        public static float SplatterCooldown = 7f;      // Deadlock 26

        // ---- secondary alt: Puddle Punch (Deadlock viscous_telepunch; user: 700%, launches everything, Viscous too) ----
        public static float PunchDamage = 7f;           // user
        public static float PunchRange = 40f;           // Deadlock AbilityCastRange (Mystic Expansion: farther)
        public static float PunchRadius = 5f;           // Deadlock Radius 4 m, x1.25 (user; Mystic Expansion: wider)
        public const float PunchHalfHeight = 5.5f;      // Deadlock PunchHalfHeight
        public static float PunchUp = 38f;              // user: sends most things flying, straight out of the surface (was 26, 34, 42 (a touch much); Deadlock TossSpeed 625 hu/s = 16 m/s)
        public const float PunchUpBias = 0.15f;         // user: a little upward tilt on every punch (a wall throws ~8.5 deg up)
        public const float PunchWindup = 0.3f;          // Deadlock m_flWindupTime 0.2 + m_flAttackTime 0.1
        public const float PunchTelegraph = 0.45f;      // Deadlock m_flEnemyPortalTelegraphTime: the portal shows this long first (m_flSelfPortalTelegraphTime 0: a self-punch is quick)
        public static float PunchToss = 0.6f;           // Deadlock TossDuration: the launch speed is held this long (no gravity, no braking), then flies on
        public static float PunchCooldown = 7f;         // Deadlock 24
        public static float PunchSelfAirControl = 3f;   // user: much more air control after punching himself (Deadlock PunchFriendlyAirControl)
        public const float PunchSelfAirSeconds = 2.5f;

        // ---- utility: The Cube (Deadlock viscous_restorative_goo; user: tap = ally under the crosshair, hold = himself) ----
        public static float CubeDuration = 3f;          // Deadlock AbilityDuration (Duration Extender lengthens it)
        public static float CubeHeal = 0.05f;           // share of max health per second (Deadlock BonusHealthRegen 40/s of ~800)
        public static float CubeRange = 20f;            // Deadlock AbilityCastRange (Mystic Expansion: farther)
        public const float CubeAngle = 30f;             // Deadlock m_flTargetingConeAngle 60 (whole cone)
        public const float CubeHold = 0.3f;             // held this long: cube himself
        public const float CubeFriction = 0.15f;        // user: slippery, nearly no friction (share of speed lost per second; Deadlock Friction -80%)
        public const float CubeShove = 1f;              // hits push the cube by their force (Deadlock BulletForce/MeleeForce)
        public const float CubeBreakout = 1f;          // Deadlock BreakoutTime: jump breaks out after this
        public static float CubeCooldown = 14f;         // Deadlock 42
        public static float PostCubeSeconds = 8f;       // Deadlock PostCubeBuffDuration (Duration Extender lengthens it)
        public static float PostCubeSpeed = 2.5f;       // Deadlock BonusMoveSpeed upgrade, m/s
        public static float PostCubeStamina = 0.3f;     // Deadlock StaminaCooldownReduction 30%: stamina refills this much faster

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Viscous max health at level 1 (+34 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet.").Value;
            BallDuration = c.Bind(Section, "Goo Ball - Duration", BallDuration, "Seconds rolling (Special again ends it early).").Value;
            BallCooldown = c.Bind(Section, "Goo Ball - Cooldown", BallCooldown, "Seconds.").Value;
            BallSpeed = c.Bind(Section, "Goo Ball - Speed", BallSpeed, "Rolling speed in m/s.").Value;
            BallFastSpeed = c.Bind(Section, "Goo Ball - Fast Speed", BallFastSpeed, "Rolling speed in m/s while Primary is held.").Value;
            BallJump = c.Bind(Section, "Goo Ball - Jump", BallJump, "Upward speed of the ball's jump and double jump, m/s.").Value;
            BallAirJump = BallJump;
            BallHitRadius = c.Bind(Section, "Goo Ball - Hit Radius", BallHitRadius, "Metres from the ball's centre.").Value;
            BallDamage = c.Bind(Section, "Goo Ball - Damage", BallDamage, "Base damage coefficient per hit, before Heavy (each enemy at most once every 1.2 s).").Value;
            BallHeavyDamage = c.Bind(Section, "Goo Ball - Heavy Damage Per Speed", BallHeavyDamage, "Heavy: extra damage coefficient per m/s of the ball's speed (Loader's punch uses 0.3).").Value;
            BallStun = c.Bind(Section, "Goo Ball - Stun", BallStun, "Seconds.").Value;
            BossFreeze = c.Bind(Section, "Goo Ball - Boss Freeze", BossFreeze, "Seconds the ball stops dead on hitting a boss, before bouncing off it.").Value;
            BossDamageMult = c.Bind(Section, "Goo Ball - Boss Damage Multiplier", BossDamageMult, "Bosses take this many times the ball's damage (and are not stunned).").Value;
            AltDamage = c.Bind(Section, "Alt Fire - Damage", AltDamage, "Damage coefficient of each glob's burst.").Value;
            AltRadius = c.Bind(Section, "Alt Fire - Radius", AltRadius, "Metres (Mystic Expansion widens it).").Value;
            SplatterDamage = c.Bind(Section, "Splatter - Damage", SplatterDamage, "Damage coefficient of each burst (one per bounce).").Value;
            SplatterBounces = c.Bind(Section, "Splatter - Bounces", SplatterBounces, "Bursts before the glob is spent.").Value;
            SplatterRadius = c.Bind(Section, "Splatter - Radius", SplatterRadius, "Metres (Mystic Expansion widens it).").Value;
            SplatterCooldown = c.Bind(Section, "Splatter - Cooldown", SplatterCooldown, "Seconds.").Value;
            PunchDamage = c.Bind(Section, "Puddle Punch - Damage", PunchDamage, "Damage coefficient to enemies punched.").Value;
            PunchRange = c.Bind(Section, "Puddle Punch - Range", PunchRange, "Metres to where the fist comes up (Mystic Expansion: farther).").Value;
            PunchRadius = c.Bind(Section, "Puddle Punch - Radius", PunchRadius, "Metres (Mystic Expansion widens it).").Value;
            PunchUp = c.Bind(Section, "Puddle Punch - Launch", PunchUp, "Speed everything punched is thrown at, straight out of the surface, m/s (bosses don't move).").Value;
            PunchToss = c.Bind(Section, "Puddle Punch - Toss Duration", PunchToss, "Seconds everything punched keeps the full launch speed (no gravity, no braking) before it falls; after that it keeps its momentum until it hits something.").Value;
            PunchCooldown = c.Bind(Section, "Puddle Punch - Cooldown", PunchCooldown, "Seconds.").Value;
            PunchSelfAirControl = c.Bind(Section, "Puddle Punch - Self Air Control", PunchSelfAirControl, "Air control multiplier for a moment after punching himself into the air.").Value;
            CubeDuration = c.Bind(Section, "The Cube - Duration", CubeDuration, "Seconds (Duration Extender lengthens it). Jump breaks out after 1 s.").Value;
            CubeHeal = c.Bind(Section, "The Cube - Healing", CubeHeal, "Share of max health healed per second inside.").Value;
            CubeRange = c.Bind(Section, "The Cube - Range", CubeRange, "Metres to an ally (Mystic Expansion: farther).").Value;
            CubeCooldown = c.Bind(Section, "The Cube - Cooldown", CubeCooldown, "Seconds.").Value;
            PostCubeSpeed = c.Bind(Section, "The Cube - Move Speed After", PostCubeSpeed, "Bonus move speed (m/s) after leaving the cube.").Value;
            PostCubeStamina = c.Bind(Section, "The Cube - Stamina Regen After", PostCubeStamina, "Stamina refills this much faster after leaving the cube (0.3 = 30%).").Value;
            BallResist = c.Bind(Section, "Goo Ball - Damage Resist", BallResist, "Share of incoming damage blocked while rolling.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/viscous/viscous.vmdl_c";
        public const float ModelHeightScale = 1.2f;
        /// <summary>
        /// His model holds the ball too (mesh group "inflated", 2.94 m to the top of the helmet on it), and the model
        /// height the loader scales by is its tallest vertex: 2.94 instead of his own 2.45. Scaled back up by that much.
        /// </summary>
        public const float BallHeightFix = 2.94f / 2.45f;
        public const string MuzzleBone = "muzzle_fx";   // under weapon_bone_R, in his hand
        public const string BallBone = "inflated_center";
        public const float BallModelRadius = 1.51f;     // the inflated mesh around inflated_center, model units (median vertex distance)

        public const string IconPortrait = "panorama/images/heroes/viscous_sm_psd.vtex_c";
        public const string IconBall = "panorama/images/hud/abilities/viscous/viscous_goo_sphere_psd.vtex_c";
        public const string IconSplatter = "panorama/images/hud/abilities/viscous/viscous_goo_ball_psd.vtex_c";
        public const string IconPunch = "panorama/images/hud/abilities/viscous/viscous_goo_punch_psd.vtex_c";
        public const string IconCube = "panorama/images/hud/abilities/viscous/viscous_restorative_goo_psd.vtex_c";
        public const string IconAlt = "panorama/images/hud/abilities/viscous/viscous_goo_puddle_psd.vtex_c";

        /// <summary>The Cube's model (Deadlock's own, 2.3 m across, 2.14 m tall), converted on its own like Grey Talon's owl.</summary>
        public const string CubeKey = "viscous_cube";
        public const string DlCubeModel = "models/abilities/viscous_cube.vmdl_c";
        public const float CubeModelHeight = 2.14f;
        public const float CubeMargin = 1.1f;           // the cube's side over the target's tallest or widest extent (user: proportionate)

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Viscous uses Kelvin's skeleton and AnimGraph1 clip names (as
        /// Abrams). Goo Ball: rolling_goo is the curl-up (the same motion as AnimGraph2's ability_goo_ball_intro); while
        /// rolling his body is hidden and ViscousBallLook drives the ball bone, so the clip under it doesn't matter.
        /// </summary>
        // Kelvin's AnimGraph2 clips: his model exports them under their paths (models/heroes_staging/kelvin_v2/clip/...), but the
        // loader keys clips by the last path part and the converter matches that short name too. A full path here was never found
        // (playtest: he ran in the air).
        private const string K = "";

        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "primary_stand_idle", true, "", "full"),
            ("run_n", "primary_run_n", true, "", "full"), ("run_ne", "primary_run_ne", true, "", "full"),
            ("run_e", "primary_run_e", true, "", "full"), ("run_se", "primary_run_se", true, "", "full"),
            ("run_s", "primary_run_s", true, "", "full"), ("run_sw", "primary_run_sw", true, "", "full"),
            ("run_w", "primary_run_w", true, "", "full"), ("run_nw", "primary_run_nw", true, "", "full"),
            ("sprint_n", "sprint_run_n", true, "", "full"), ("sprint_ne", "sprint_run_ne", true, "", "full"),
            ("sprint_e", "sprint_run_e", true, "", "full"), ("sprint_se", "sprint_run_se", true, "", "full"),
            ("sprint_s", "sprint_run_s", true, "", "full"), ("sprint_sw", "sprint_run_sw", true, "", "full"),
            ("sprint_w", "sprint_run_w", true, "", "full"), ("sprint_nw", "sprint_run_nw", true, "", "full"),
            ("jump", "jump_start", false, "in_air", "full"),
            // jump_start_short (Abrams' air jump) is posed on a bigger skeleton: limbs 30-50% longer, pelvis 0.5 m higher (user: he stretched).
            ("air_jump", K + "jump_ground", false, "in_air", "full"),
            ("wall_jump", "wall_jump", false, "in_air", "full"),
            // In the air: Kelvin's AnimGraph2 loops, which Deadlock's live Viscous plays (his graphs are hero_*+kelvin). The
            // jump_upwards_loop / jump_downwards_loop in his own list are the ones Abrams uses (playtest: "Abrams' in-air animation").
            ("in_air", K + "in_air_loop_apex", true, "", "full"),
            ("falling", K + "in_air_down", true, "", "full"),
            ("land", "jump_landing", false, "locomotion", "additive"),
            ("dash_ground", K + "dash_ground", false, "locomotion", "full"),                   // Kelvin's ground roll, as Deadlock's Viscous (user)
            ("dash_air_forward", "primary_airdash", false, "in_air", "full"),
            ("dash_air_back", "primary_airdash_bkward", false, "in_air", "full"),
            ("dash_air_left", "primary_airdash_left", false, "in_air", "full"),
            ("dash_air_right", "primary_airdash_right", false, "in_air", "full"),
            ("slide_start", "slide_intro", false, "slide", "full"),
            ("slide", "slide_forward", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "primary_hip_fire", false, "locomotion", "additive"),
            ("alt_shoot", "primary_large_shoot", false, "locomotion", "additive"),                   // alt fire glob
            ("throw", K + "throw", false, "locomotion", "delta"),                                     // Splatter
            ("punch", "ability_puddle_punch", false, "locomotion", "delta"), // Puddle Punch
            ("cube", K + "cast_start", false, "locomotion", "delta"),                                 // The Cube
            ("ball_cast", "rolling_goo", false, "ball", "full"),
            ("ball", "rolling_goo", true, "", "full"),
            ("parry", "parry_to_idle", false, "locomotion", "full"),   // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry_to_idle", false, "in_air", "full"),
            ("death", "death_static_1", false, "hold", "full"),
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death", "ball_cast", "ball" };

        /// <summary>
        /// Deadlock sounds (picked without a review pass, as Victor's). Goo Ball: cast, the rolling loop, wall and hero
        /// impacts, the end pop. VO: use_power4 is Goo Ball (his 4th ability).
        /// </summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "viscous_gun", VpkPrefix = "sounds/weapons/viscous/viscous_wpn_fire_main", Volume = 0.55f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.04f, VolumeJitter = 0.1f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "viscous_ball_cast", VpkPrefix = "sounds/abilities/viscous/viscous_a4_ball_cast", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "viscous_ball_loop", VpkPrefix = "sounds/abilities/viscous/viscous_a4_ball_lp_01", Volume = 0.55f, Range = 35f },
            new DeadlockSound { Id = "viscous_ball_wall", VpkPrefix = "sounds/abilities/viscous/viscous_a4_ball_impact_wall", Volume = 0.8f, Range = 40f, MinGap = 0.08f, PitchJitter = 0.05f },
            new DeadlockSound { Id = "viscous_ball_hit", VpkPrefix = "sounds/abilities/viscous/viscous_a4_ball_impact_hero", Volume = 0.9f, Range = 50f, MinGap = 0.05f, PitchJitter = 0.05f },
            new DeadlockSound { Id = "viscous_ball_splat", VpkPrefix = "sounds/abilities/viscous/viscous_a4_gooball_hit", Volume = 0.6f, Range = 40f, MinGap = 0.1f },
            new DeadlockSound { Id = "viscous_ball_end", VpkPrefix = "sounds/abilities/viscous/viscous_a4_ball_end", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "viscous_alt_fire", VpkPrefix = "sounds/weapons/viscous/viscous_weapon_alt_fire", Volume = 0.7f, Range = 60f, MinGap = 0.1f, PitchJitter = 0.04f, Fallback = "Play_commando_M2_grenade_throw" },
            new DeadlockSound { Id = "viscous_alt_loop", VpkPrefix = "sounds/weapons/viscous/viscous_weapon_alt_proj_lp", Volume = 0.4f, Range = 25f },
            new DeadlockSound { Id = "viscous_alt_burst", VpkPrefix = "sounds/abilities/viscous/viscous_a1_gooball_impact", Volume = 0.6f, Range = 50f, MinGap = 0.05f, PitchJitter = 0.06f },
            new DeadlockSound { Id = "viscous_splatter_cast", VpkPrefix = "sounds/abilities/viscous/a1_goo_ball/viscous_a1_goo_ball_cast", Volume = 0.8f, Range = 50f },
            new DeadlockSound { Id = "viscous_splatter_loop", VpkPrefix = "sounds/abilities/viscous/viscous_a1_goo_ball_proj_lp", Volume = 0.5f, Range = 30f },
            new DeadlockSound { Id = "viscous_splatter_burst", VpkPrefix = "sounds/abilities/viscous/viscous_a1_gooball_impact", Volume = 0.9f, Range = 60f, MinGap = 0.05f },
            new DeadlockSound { Id = "viscous_punch_portal", VpkPrefix = "sounds/abilities/viscous/viscous_a3_telepunch_portal", Volume = 0.8f, Range = 50f },
            new DeadlockSound { Id = "viscous_punch_hit", VpkPrefix = "sounds/abilities/viscous/viscous_a3_telepunch_hit", Range = 70f, MinGap = 0.1f },
            new DeadlockSound { Id = "viscous_cube_cast", VpkPrefix = "sounds/abilities/viscous/viscous_a2_cube_cast", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "viscous_cube_loop", VpkPrefix = "sounds/abilities/viscous/viscous_a2_cube_lp", Volume = 0.5f, Range = 25f },
            new DeadlockSound { Id = "viscous_cube_end", VpkPrefix = "sounds/abilities/viscous/viscous_a2_cube_end", Volume = 0.9f, Range = 50f, MinGap = 0.2f },
            // VO by ability (Deadlock order): 1 Splatter, 2 The Cube, 3 Puddle Punch, 4 Goo Ball.
            new DeadlockSound { Id = "viscous_vo_power1", VpkPrefix = "sounds/vo/viscous/viscous_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_power2", VpkPrefix = "sounds/vo/viscous/viscous_use_power2", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_power3", VpkPrefix = "sounds/vo/viscous/viscous_use_power3", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_desperation_power1", VpkPrefix = "sounds/vo/viscous/viscous_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_desperation_power2", VpkPrefix = "sounds/vo/viscous/viscous_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_desperation_power3", VpkPrefix = "sounds/vo/viscous/viscous_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_power4", VpkPrefix = "sounds/vo/viscous/viscous_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_desperation_power4", VpkPrefix = "sounds/vo/viscous/viscous_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_select", VpkPrefix = "sounds/vo/viscous/viscous_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_kill", VpkPrefix = "sounds/vo/viscous/viscous_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_killstreak", VpkPrefix = "sounds/vo/viscous/viscous_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_pain", VpkPrefix = "sounds/vo/viscous/emote/viscous_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_pain_small", VpkPrefix = "sounds/vo/viscous/emote/viscous_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_low_health", VpkPrefix = "sounds/vo/viscous/viscous_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_death", VpkPrefix = "sounds/vo/viscous/emote/viscous_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "viscous_vo_effort", VpkPrefix = "sounds/vo/viscous/emote/viscous_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "viscous_footstep", VpkPrefix = "sounds/player/footsteps/viscous/viscous_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "viscous_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "viscous_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "viscous_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "viscous_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "viscous_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
