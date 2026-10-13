using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Celeste's numbers (Deadlock's hero_unicorn / ability_unicorn_* and citadel_weapon_unicorn_set, read 2026-10-12; her
    /// files are all named "unicorn"). Deadlock values are kept where RoR2 has the same scale (metres, seconds, speeds;
    /// 39.37 hu = 1 m); damage becomes RoR2 damage coefficients and cooldowns RoR2-length (about a third, as Vyper's).
    /// Kit picked while the user was away (2026-10-12): every number is a [Celeste] config knob for their review.
    /// </summary>
    public static class CelesteData
    {
        public const string Hero = "celeste";
        public const string Section = "Celeste";

        // ---- survivor: a ranged spirit caster (Deadlock 690 health, 1 regen, 6.2 m/s, 4 stamina) ----
        public static float BaseHealth = 95f;
        public const float HealthPerLevel = 28f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.2f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 1f;              // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: Lash's extras (user): 5 stamina, 2 air jumps, 2 air dashes, plus her low gravity ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = CelesteModel.Play,
            StaminaPips = 5,                   // user (Deadlock EStamina 4)
            AirJumps = 2,                      // like Lash (user)
            AirDashesPerAir = 2,
            AirDashMetres = 14f,               // a couple of metres short of Lash's 16 (user)
            // Her innate modifier_hero_gravity (on citadel_weapon_unicorn_set): gravity -25%, air control +44%, air accel +10%.
            GravityScale = 0.75f,
            AirAccel = 50f * 1.44f,
            OutOfCombatSpeed = 1.6f / 6.2f,    // Deadlock ESprintSpeed over her run
            WallJumpUp = 22f,                  // Lash's playtested controls, kept for every hero
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Carny Life (Deadlock: a bouncing, homing thrown blade every 0.58 s, 18 damage, gravity 0.2) ----
        public static float DaggerDamage = 1.2f;        // a light poke (user): her damage is Shining Wonder and blades on marked enemies
        public const float DaggerCycle = 0.58f;          // Deadlock m_flCycleTime
        public const float DaggerSpeed = 50f;            // Deadlock m_flBulletSpeed 1968.5 hu/s
        public const float DaggerDrop = 9f;              // m/s2, homing or not: a shallow arc (user; Deadlock gravity scale 0.2)
        public const float DaggerHomeConeDeg = 22.5f;    // Deadlock: homes onto an enemy within ~45 deg of its flight
        public const float DaggerHomeRange = 45f;
        public const float DaggerTurnDegPerSec = 150f;  // gentle, so the arc shows
        public const float DaggerBounceKeep = 0.7f;      // speed kept by its one bounce off the world
        public const float DaggerLifetime = 3f;

        // ---- secondary: Light Eater (Deadlock: 70 deg cone, 10 m, 0.3 s delay, 40 damage; marked 8 s; 15 per bullet; 18% lifesteal) ----
        public static float FlareDamage = 4f;
        public static float FlareRange = 10f;            // Deadlock AbilityCastRange (Mystic Expansion widens it)
        public const float FlareConeDeg = 35f;           // half of Deadlock's 70 deg TargetingConeAngle
        public const float FlareDelay = 0.3f;            // Deadlock AbilityCastDelay
        public static float FlareCooldown = 7f;          // Deadlock 20
        public static float MarkSeconds = 8f;            // Deadlock AbilityDuration
        public static float MarkDaggerBonus = 1.8f;      // extra damage coefficient on each Carny Life hit on a marked enemy (Deadlock Damage 15)
        public static float MarkLifesteal = 0.18f;       // share of her damage to marked enemies she heals (Deadlock AbilityLifestealPercentHero)
        public const float FlareHangFall = 1f;           // m/s: she hangs in the air while casting (Deadlock m_flJumpFallSpeedMax)

        // ---- secondary alt: Radiant Daggers (Deadlock: 30 m, 1.4 s to form, 8 m, 55 damage, a stacking spirit buff per hero hit) ----
        public static float BeamDamage = 6f;
        public static float BeamRange = 30f;             // Deadlock AbilityCastRange
        public static float BeamRadius = 8f;             // Deadlock ExplosionRadius
        public const float BeamDelay = 1.4f;             // Deadlock PreExplosionDuration
        public static float BeamCooldown = 10f;          // Deadlock 33
        public static float RadianceDamage = 0.07f;      // per stack (Deadlock MagicIncreasePerStack 7%)
        public const int RadianceMaxStacks = 6;          // Deadlock BuffMaxStacks
        public const float RadianceSeconds = 30f;        // Deadlock BuffDuration

        // ---- utility: Dazzling Trick (Deadlock: a 100 barrier for 4 s; broken = 14 m burst, half the barrier as damage, a 1.75 s silence: a stun here, user) ----
        public static float PrismBarrier = 0.2f;         // share of max health (Deadlock CombatBarrier 100 of 690 health)
        public const float PrismSeconds = 4f;            // Deadlock MaxLifetime
        public static float PrismDamage = 5f;            // the burst when it breaks
        public static float PrismRadius = 14f;           // Deadlock ExplodeRadius
        public static float PrismStun = 1.75f;           // Deadlock DebuffDuration (a silence there; a stun here: user)
        public static float PrismCooldown = 12f;         // Deadlock 38

        // ---- special: Shining Wonder (Deadlock: hold to aim, release; 140 damage, 32% slow 1.5 s, 8 bounces within 15.5 m) ----
        public static float OrbDamage = 10f;
        public static int OrbBounces = 8;                // Deadlock MaxBounces (hits after the first)
        public static float OrbBounceRange = 7f;         // from the enemy it just hit (user: close; Deadlock BounceRadius 15.5)
        public static float OrbCooldown = 40f;           // Deadlock 160
        public const float OrbSpeed = 18f;               // much slower than Deadlock's 50.8 m/s (user): a floating ball of light
        public const float OrbRadius = 1.65f;            // Deadlock m_flBulletRadius 65 hu
        public const float OrbSlow = 0.32f;              // Deadlock SlowPercent
        public const float OrbSlowSeconds = 1.5f;        // Deadlock SlowDuration
        public const float OrbOrbitDegPerSec = 240f;     // while it waits, it circles what it last hit (user)
        public const float OrbLinger = 3.25f;            // Deadlock BounceGrace: no target, it waits this long for one
        public const float OrbHomeConeDeg = 25f;
        public const float OrbFlightSeconds = 3f;        // without a target it flies on this long, then lingers where it is
        public const float OrbMinHold = 0.35f;           // Deadlock AbilityCastDelay 0.75 is the wind-up; she can release after this
        public const float OrbMaxHold = 6f;              // held this long, it fires by itself
        public const float OrbAimFall = 3f;              // m/s: aiming in the air slows her fall (Deadlock: "slows Celeste's glide")

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Celeste max health at level 1 (+28 per level).").Value;
            DaggerDamage = c.Bind(Section, "Carny Life - Damage", DaggerDamage, "Damage coefficient of each thrown blade.").Value;
            FlareDamage = c.Bind(Section, "Light Eater - Damage", FlareDamage, "Damage coefficient of the flare.").Value;
            FlareRange = c.Bind(Section, "Light Eater - Range", FlareRange, "Metres (a 70 degree cone).").Value;
            FlareCooldown = c.Bind(Section, "Light Eater - Cooldown", FlareCooldown, "Seconds.").Value;
            MarkSeconds = c.Bind(Section, "Light Eater - Mark Duration", MarkSeconds, "Seconds an enemy stays marked.").Value;
            MarkDaggerBonus = c.Bind(Section, "Light Eater - Blade Bonus", MarkDaggerBonus, "Extra damage coefficient of each Carny Life hit on a marked enemy.").Value;
            MarkLifesteal = c.Bind(Section, "Light Eater - Lifesteal", MarkLifesteal, "Share of Celeste's damage to marked enemies that heals her.").Value;
            BeamDamage = c.Bind(Section, "Radiant Daggers - Damage", BeamDamage, "Damage coefficient of the beam's explosion.").Value;
            BeamRange = c.Bind(Section, "Radiant Daggers - Range", BeamRange, "Metres to the aimed spot.").Value;
            BeamRadius = c.Bind(Section, "Radiant Daggers - Radius", BeamRadius, "Metres.").Value;
            BeamCooldown = c.Bind(Section, "Radiant Daggers - Cooldown", BeamCooldown, "Seconds.").Value;
            RadianceDamage = c.Bind(Section, "Radiant Daggers - Damage Per Stack", RadianceDamage, "Damage bonus per Radiance stack (one per enemy hit, up to 6, 30 s).").Value;
            PrismBarrier = c.Bind(Section, "Dazzling Trick - Barrier", PrismBarrier, "Barrier as a share of max health.").Value;
            PrismDamage = c.Bind(Section, "Dazzling Trick - Damage", PrismDamage, "Damage coefficient of the burst when the barrier breaks.").Value;
            PrismRadius = c.Bind(Section, "Dazzling Trick - Radius", PrismRadius, "Metres.").Value;
            PrismStun = c.Bind(Section, "Dazzling Trick - Stun", PrismStun, "Seconds enemies caught are stunned.").Value;
            PrismCooldown = c.Bind(Section, "Dazzling Trick - Cooldown", PrismCooldown, "Seconds.").Value;
            OrbDamage = c.Bind(Section, "Shining Wonder - Damage", OrbDamage, "Damage coefficient of each hit.").Value;
            OrbBounces = c.Bind(Section, "Shining Wonder - Bounces", OrbBounces, "Bounces after the first hit.").Value;
            OrbBounceRange = c.Bind(Section, "Shining Wonder - Bounce Range", OrbBounceRange, "Metres from the enemy it just hit to the next target (the nearest enemy, or Celeste's prism).").Value;
            OrbCooldown = c.Bind(Section, "Shining Wonder - Cooldown", OrbCooldown, "Seconds.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/unicorn/unicorn.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f; // Lash's size
        public const string MuzzleBone = "hand_R";           // she throws from her right hand

        public const string IconPortrait = "panorama/images/heroes/unicorn_sm_psd.vtex_c";
        public const string IconFlare = "panorama/images/hud/abilities/unicorn/unicorn_flare_psd.vtex_c";
        public const string IconShield = "panorama/images/hud/abilities/unicorn/unicorn_shield_psd.vtex_c";
        public const string IconBeam = "panorama/images/hud/abilities/unicorn/unicorn_luminousflux_psd.vtex_c";
        public const string IconOrb = "panorama/images/hud/abilities/unicorn/unicorn_orb_psd.vtex_c";
        public const string IconGun = "panorama/images/heroes/guns/unicorn_baton_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Celeste is an AnimGraph2 hero (models/heroes_wip/unicorn/clips), like
        /// Victor. Her set has in_air_apex rather than in_air_loop_apex. Ability clips by Deadlock's names: radiant_blast = Light
        /// Eater, prismaticguard = Dazzling Trick, luminous_flux = Radiant Daggers, dazzlingorb = Shining Wonder.
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
            ("jump", "jump_ground", true, "", "full"),          // whole-jump clips: CelesteModel.LoopRule ends them (as Victor's)
            ("air_jump", "jump_air", true, "", "full"),
            ("wall_jump", "jump_ground", true, "", "full"),
            ("in_air", "in_air_apex", true, "", "full"),
            ("falling", "in_air_loop_down", true, "", "full"),
            ("land", "landing_impact_idle", false, "locomotion", "delta"),
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "dash_air_forward", true, "", "full"),
            ("dash_air_back", "dash_air_back", true, "", "full"),
            ("dash_air_left", "dash_air_left", true, "", "full"),
            ("dash_air_right", "dash_air_right", true, "", "full"),
            ("slide_start", "slide_start", false, "slide", "full"),
            ("slide", "slide_loop", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "shoot_idle_start", false, "locomotion", "delta"), // her model exports no shoot_idle
            ("flare", "ability_unicorn_radiant_blast", false, "locomotion", "full"),
            ("prism", "ability_unicorn_prismaticguard", false, "locomotion", "delta"),
            ("beam", "ability_radiant_daggers_stand_idle", false, "locomotion", "delta"), // ability_luminous_flux isn't in her model
            ("orb_start", "ability_unicorn_dazzlingorb_start", false, "orb_loop", "full"),
            ("orb_loop", "ability_unicorn_dazzlingorb_loop", true, "", "full"),
            ("orb_end", "ability_unicorn_dazzlingorb_end", false, "locomotion", "full"),
            ("orb_end_air", "ability_unicorn_dazzlingorb_inair_end", false, "in_air", "full"),
            ("select", "ui_hero_select", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry_inair", false, "in_air", "full"),
            ("death", "knockdown_large_start", false, "hold", "full"),
        };

        /// <summary>Seconds her jump and air-dash poses play before the in-air loop takes over (see CelesteModel.LoopRule).</summary>
        public const float JumpPose = 0.8f, AirDashPose = 0.8f; // her air-dash clip is 0.83 s: it plays (nearly) through (user: it snapped back too soon)

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death", "select" };

        /// <summary>
        /// Deadlock sounds from her own folders (picked by name while the user was away: review pending). VO by ability:
        /// 1 Light Eater, 2 Dazzling Trick, 3 Radiant Daggers, 4 Shining Wonder (Deadlock's slot order).
        /// </summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "celeste_gun", VpkPrefix = "sounds/weapons/unicorn/unicorn_weapon_fire_main", Volume = 0.6f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.04f, VolumeJitter = 0.1f, Fallback = "Play_bandit2_m2_slash" },
            new DeadlockSound { Id = "celeste_gun_tail", VpkPrefix = "sounds/weapons/unicorn/unicorn_weapon_fire_tail", Volume = 0.4f, Range = 40f, MinGap = 0.05f },
            new DeadlockSound { Id = "celeste_gun_impact", VpkPrefix = "sounds/weapons/unicorn/impacts/unicorn_weapon_impact", Volume = 0.6f, Range = 30f, MinGap = 0.04f },
            new DeadlockSound { Id = "celeste_gun_loop", VpkPrefix = "sounds/weapons/unicorn/unicorn_weapon_lp", Volume = 0.35f, Range = 20f },
            new DeadlockSound { Id = "celeste_flare_cast", VpkPrefix = "sounds/abilities/unicorn/a1/unicorn_radiant_flare_cast_delay", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "celeste_flare_blast", VpkPrefix = "sounds/abilities/unicorn/a1/unicorn_radiant_flare_cast", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "celeste_flare_hit", VpkPrefix = "sounds/abilities/unicorn/a1/unicorn_radiant_flare_hit", Volume = 0.7f, Range = 40f, MinGap = 0.05f },
            new DeadlockSound { Id = "celeste_mark_loop", VpkPrefix = "sounds/abilities/unicorn/a1/unicorn_radiant_flare_debuff_lp", Volume = 0.4f, Range = 25f },
            new DeadlockSound { Id = "celeste_prism_cast", VpkPrefix = "sounds/abilities/unicorn/a2/unicorn_prismatic_guard_cast", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "celeste_prism_loop", VpkPrefix = "sounds/abilities/unicorn/a2/unicorn_prismatic_guard_lp", Volume = 0.5f, Range = 25f },
            new DeadlockSound { Id = "celeste_prism_crack", VpkPrefix = "sounds/abilities/unicorn/a2/unicorn_prismatic_guard_cracking", Volume = 0.7f, Range = 30f, MinGap = 0.3f },
            new DeadlockSound { Id = "celeste_prism_explode", VpkPrefix = "sounds/abilities/unicorn/a2/unicorn_prismatic_guard_explode", Range = 70f },
            new DeadlockSound { Id = "celeste_prism_expire", VpkPrefix = "sounds/abilities/unicorn/a2/unicorn_prismatic_guard_expire", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "celeste_beam_cast", VpkPrefix = "sounds/abilities/unicorn/a3/unicorn_luminous_cast", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "celeste_beam_warning", VpkPrefix = "sounds/abilities/unicorn/a3/unicorn_luminous_aoe_warning", Volume = 0.8f, Range = 50f },
            new DeadlockSound { Id = "celeste_beam_explode", VpkPrefix = "sounds/abilities/unicorn/a3/unicorn_luminous_explode", Range = 80f },
            new DeadlockSound { Id = "celeste_beam_buff", VpkPrefix = "sounds/abilities/unicorn/a3/unicorn_luminous_buff", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "celeste_beam_buff_max", VpkPrefix = "sounds/abilities/unicorn/a3/unicorn_luminous_buff_max", Volume = 0.8f, Range = 25f, MinGap = 1f },
            new DeadlockSound { Id = "celeste_orb_cast", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_cast_delay", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "celeste_orb_channel", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_channel_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "celeste_orb_fire", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_cast", Volume = 0.9f, Range = 60f },
            new DeadlockSound { Id = "celeste_orb_loop", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_proj_lp", Volume = 0.6f, Range = 40f },
            new DeadlockSound { Id = "celeste_orb_bounce", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_bounce", Volume = 0.9f, Range = 60f, MinGap = 0.05f },
            new DeadlockSound { Id = "celeste_orb_final", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_final_hit", Range = 70f },
            new DeadlockSound { Id = "celeste_orb_linger", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_grace_lp", Volume = 0.6f, Range = 40f },
            new DeadlockSound { Id = "celeste_orb_expire", VpkPrefix = "sounds/abilities/unicorn/a4/unicorn_dazzling_orb_expire", Volume = 0.8f, Range = 50f },
            new DeadlockSound { Id = "celeste_vo_power1", VpkPrefix = "sounds/vo/unicorn/unicorn_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_power2", VpkPrefix = "sounds/vo/unicorn/unicorn_use_power2_on_self", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_power3", VpkPrefix = "sounds/vo/unicorn/unicorn_use_power3", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_power4", VpkPrefix = "sounds/vo/unicorn/unicorn_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_desperation_power1", VpkPrefix = "sounds/vo/unicorn/unicorn_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_desperation_power2", VpkPrefix = "sounds/vo/unicorn/unicorn_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_desperation_power3", VpkPrefix = "sounds/vo/unicorn/unicorn_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_desperation_power4", VpkPrefix = "sounds/vo/unicorn/unicorn_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_select", VpkPrefix = "sounds/vo/unicorn/unicorn_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_kill", VpkPrefix = "sounds/vo/unicorn/unicorn_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_killstreak", VpkPrefix = "sounds/vo/unicorn/unicorn_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_pain", VpkPrefix = "sounds/vo/unicorn/emote/unicorn_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_pain_small", VpkPrefix = "sounds/vo/unicorn/emote/unicorn_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_death", VpkPrefix = "sounds/vo/unicorn/emote/unicorn_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_effort", VpkPrefix = "sounds/vo/unicorn/emote/unicorn_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "celeste_vo_efforts", VpkPrefix = "sounds/vo/unicorn/emote/unicorn_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Her own movement foley and footsteps.
            new DeadlockSound { Id = "celeste_dash", VpkPrefix = "sounds/player/movement/unicorn/unicorn_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "celeste_land", VpkPrefix = "sounds/player/footsteps/unicorn/unicorn_footstep_jumpland", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "celeste_footstep", VpkPrefix = "sounds/player/footsteps/unicorn/unicorn_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "celeste_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "celeste_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "celeste_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "celeste_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "celeste_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
