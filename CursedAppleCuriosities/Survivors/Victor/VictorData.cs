using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor's numbers (Deadlock's hero_frank / ability_frank_*, read 2026-10-12). Deadlock values are kept where RoR2 has
    /// the same scale (metres, seconds, speeds; 39.37 hu = 1 m); damage becomes RoR2 damage coefficients and cooldowns
    /// RoR2-length. Kit and rules are the user's (memory victor-design). Headline knobs are in the [Victor] config section.
    /// </summary>
    public static class VictorData
    {
        public const string Hero = "victor";
        public const string Section = "Victor";

        // ---- survivor: a self-harming bruiser (Deadlock 800 health, 1.5 regen, 6.3 m/s, 3 stamina) ----
        public static float BaseHealth = 120f;
        public const float HealthPerLevel = 36f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.3f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 1.5f;            // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: hero defaults (3 stamina, 1 air jump, 1 air dash) ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = VictorModel.Play,
            StaminaPips = 3,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.1f / 6.3f,    // Deadlock ESprintSpeed over his run
            SlideFriction = 1.2f,              // the heroes' playtested value
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Memento Mori (Deadlock: one fast bullet, spins up from 0.198 s to 0.171 s between shots) ----
        public static float GunDamage = 0.75f;
        public const float GunCycleSlow = 0.198f;       // Deadlock m_flCycleTime
        public const float GunCycleFast = 0.171f;       // Deadlock m_flMaxSpinCycleTime
        public const float GunSpinUp = 0.6f;            // Deadlock m_flSpinIncreaseRate (spin per second of firing)
        public const float GunSpinDecay = 1f;           // Deadlock m_flSpinDecayRate
        public const float GunSpread = 0.6f;            // Deadlock m_Spread (degrees)
        public const float GunRadius = 0.13f;           // Deadlock m_flBulletRadius 5 hu

        // ---- secondary: Pain Battery (user: charges ONLY from damage taken; Deadlock's T3 missing-health heal built in) ----
        public static float BatteryDamage = 5f;         // per target, once (Deadlock Damage 100)
        public static float BatteryFillFraction = 0.2f; // damage taken to fill it, as a share of max health (user; Deadlock StoredDamageHealthPercentRequired 40)
        public static float BatteryHealMissing = 0.15f; // per enemy hit: heal this share of missing health (T3 MissingHealthPercentHeal)
        public const int BatteryBolts = 7;              // Deadlock BoltCount
        public const float BatterySpreadDeg = 40f;      // Deadlock SpreadAngle (whole fan)
        public static float BatteryRange = 28f;         // Deadlock AbilityCastRange
        public const float BatteryCastDelay = 0.35f;    // Deadlock AbilityCastDelay
        public const float BatteryCooldown = 2f;        // Deadlock AbilityCooldown (after the battery empties)
        public const float BatteryBoltRadius = 0.6f;
        public const int BatteryFull = 100;             // charge is shown as buff stacks, 0..100 %

        // ---- utility: Jumpstart (user: costs HP, then speed + healing that totals more than he spent) ----
        public static float JumpstartCost = 0.15f;      // share of current health (Deadlock CurrentHealthPercentDamage)
        public static float JumpstartHealRatio = 2f;    // heals this many times what it cost, over the duration
        public static float JumpstartMinHeal = 0.1f;    // ...and at least this share of max health
        public static float JumpstartSpeed = 3f;        // m/s at the start, decaying (Deadlock BonusMoveSpeed)
        public const float JumpstartSeconds = 4.5f;     // Deadlock AbilityDuration
        public static float JumpstartCooldown = 12f;    // per charge (Deadlock 30)
        public const int JumpstartCharges = 2;          // user: starts with 2 (Deadlock 1, +1 at T3)
        public const float JumpstartCast = 0.35f;       // Deadlock AbilityChannelTime
        public const int JumpstartSpeedSteps = 10;      // the speed fades in steps (buff stacks)

        // ---- special: Aura of Suffering (ramps up the longer it runs; hurts him too, and can kill him: user) ----
        public static float AuraRadius = 8f;            // Deadlock Radius
        public static float AuraRampSeconds = 8f;       // seconds of uptime to reach max damage, then it holds (user; Deadlock's 8 s)
        public static float AuraMinDamage = 1f;         // damage coefficient per second at the start (Deadlock MinDPS 13)
        public static float AuraMaxDamage = 6f;         // per second once fully ramped: 600% at 8 s (user; Deadlock MaxDPS 58)
        public static float AuraSelfMin = 0.011f;       // share of his max health per second at the start (Deadlock: 70% of the enemy DPS)
        public static float AuraSelfMax = 0.05f;        // ...and once ramped
        public const float AuraTick = 0.25f;            // Deadlock TickRate
        public static float AuraProc = 0.5f;            // per tick (4 ticks a second), like Torment Pulse
        public static float AuraCooldown = 2f;          // after it ends (Deadlock AbilityCooldown); it stays on until toggled off or he dies (user)
        public const float AuraEndGrace = 0.3f;         // special again after this ends it early (Deadlock "End Aura")

        // ---- passive: Shocking Reanimation (Deadlock ultimate, here automatic: user; 240 s cooldown: user) ----
        public static float ReviveCooldown = 90f;       // user (Deadlock 240); also ready again on every new stage
        public const float ReviveChannel = 3f;          // Deadlock AbilityChannelTime
        public const float RevivePost = 0.66f;          // Deadlock AbilityPostCastDuration
        public static float ReviveHealth = 0.5f;        // Deadlock RespawnHealthPercent
        public static float ReviveDamage = 10f;         // Deadlock Damage 200
        public static float ReviveRadius = 36f;         // the wave and the blast (user: double Deadlock's 18 m Radius)
        public const float ReviveStun = 1.5f;           // Deadlock StunDuration
        public const float ReviveSlowSeconds = 3f;      // Deadlock SlowDuration: the wave when he goes down
        public const float ReviveSlowPerStep = 0.25f;   // 5 steps = 125% slow at first, fading (Deadlock SlowPercent 120, diminishing)
        public const int ReviveSlowSteps = 5;

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Victor max health at level 1 (+36 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet.").Value;
            BatteryDamage = c.Bind(Section, "Pain Battery - Damage", BatteryDamage, "Damage coefficient to each enemy the bolts hit (once per enemy).").Value;
            BatteryFillFraction = c.Bind(Section, "Pain Battery - Damage To Fill", BatteryFillFraction, "Damage Victor must take (to health, shield or barrier) to fill the battery, as a share of his max health.").Value;
            BatteryHealMissing = c.Bind(Section, "Pain Battery - Heal Per Hit", BatteryHealMissing, "Share of missing health healed for each enemy hit.").Value;
            BatteryRange = c.Bind(Section, "Pain Battery - Range", BatteryRange, "Metres.").Value;
            JumpstartCost = c.Bind(Section, "Jumpstart - Health Cost", JumpstartCost, "Share of current health Jumpstart costs.").Value;
            JumpstartHealRatio = c.Bind(Section, "Jumpstart - Heal Ratio", JumpstartHealRatio, "Healing over time as a multiple of the health it cost (above 1: always more than he spent).").Value;
            JumpstartMinHeal = c.Bind(Section, "Jumpstart - Minimum Heal", JumpstartMinHeal, "Least healing, as a share of max health.").Value;
            JumpstartSpeed = c.Bind(Section, "Jumpstart - Move Speed", JumpstartSpeed, "Bonus move speed in m/s at the start, fading over 4.5 s.").Value;
            JumpstartCooldown = c.Bind(Section, "Jumpstart - Cooldown", JumpstartCooldown, "Seconds.").Value;
            AuraRadius = c.Bind(Section, "Aura of Suffering - Radius", AuraRadius, "Metres.").Value;
            AuraRampSeconds = c.Bind(Section, "Aura of Suffering - Ramp Time", AuraRampSeconds, "Seconds of continuous uptime to reach max damage (and max self damage), which then holds.").Value;
            AuraMinDamage = c.Bind(Section, "Aura of Suffering - Min Damage", AuraMinDamage, "Damage coefficient per second at the start.").Value;
            AuraMaxDamage = c.Bind(Section, "Aura of Suffering - Max Damage", AuraMaxDamage, "Damage coefficient per second once fully ramped.").Value;
            AuraSelfMin = c.Bind(Section, "Aura of Suffering - Self Damage Min", AuraSelfMin, "Share of Victor's max health lost per second at the start.").Value;
            AuraSelfMax = c.Bind(Section, "Aura of Suffering - Self Damage Max", AuraSelfMax, "Share of Victor's max health lost per second once ramped.").Value;
            AuraProc = c.Bind(Section, "Aura of Suffering - Proc Coefficient", AuraProc, "Proc coefficient of each tick (4 ticks a second).").Value;
            AuraCooldown = c.Bind(Section, "Aura of Suffering - Cooldown", AuraCooldown, "Seconds after it is toggled off.").Value;
            ReviveCooldown = c.Bind(Section, "Shocking Reanimation - Cooldown", ReviveCooldown, "Seconds. It is also ready again whenever a new stage starts.").Value;
            ReviveHealth = c.Bind(Section, "Shocking Reanimation - Health", ReviveHealth, "Share of max health he comes back with.").Value;
            ReviveDamage = c.Bind(Section, "Shocking Reanimation - Damage", ReviveDamage, "Damage coefficient of the blast when he gets up.").Value;
            ReviveRadius = c.Bind(Section, "Shocking Reanimation - Radius", ReviveRadius, "Metres.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/frank/frank.vmdl_c";
        public const float ModelHeightScale = 1.15f * 1.25f; // a head taller than Lash (Deadlock's Victor is a big man)
        public const string MuzzleBone = "muzzle_fx";        // under weapon, off root_motion (HandIK keeps it in his hand)

        // Shocking Reanimation's charge-up: Deadlock's separate "body parts" model and its two clips (user: use this, not
        // the knockdown). _02 flies the scattered parts together (2.47 s); the first holds his stance hovering (1.5 s loop).
        public const string FxKey = "victor_reassemble";
        public const string DlFxModel = "models/abilities/frank_hover_death_anim_fx_model.vmdl_c";
        public const string FxTogether = "frank_hover_death_anim_fx_anim_02";
        public const string FxHover = "frank_hover_death_anim_fx_anim";

        public const string IconPortrait = "panorama/images/heroes/frank_sm_psd.vtex_c";
        public const string IconBattery = "panorama/images/hud/abilities/frank/frank_pain_battery_psd.vtex_c";
        public const string IconJumpstart = "panorama/images/hud/abilities/frank/frank_jump_start_psd.vtex_c";
        public const string IconAura = "panorama/images/hud/abilities/frank/frank_aura_of_suffering_psd.vtex_c";
        public const string IconRevive = "panorama/images/hud/abilities/frank/frank_shocking_reanimation_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Victor is an AnimGraph2 hero (models/heroes_wip/frank/clips):
        /// frank_abilitymask plays ability_pain_battery, ability_jumpstart and ability_reanimation; Aura of Suffering has no
        /// clip of its own (Deadlock's generic cast_channeling set). No in_air_loop_down: the apex loop stands in.
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
            ("jump", "jump_ground", true, "", "full"),          // 3.1 s clips covering the whole jump: VictorModel.LoopRule ends them
            ("air_jump", "jump_air", true, "", "full"),
            ("wall_jump", "jump_ground", true, "", "full"),
            ("in_air", "in_air_loop_apex", true, "", "full"),
            ("falling", "in_air_loop_apex", true, "", "full"),
            ("land", "landing_impact_idle", false, "locomotion", "delta"),     // his clip holds a constant 1.74 m pelvis offset: as an
                                                                                 // additive it threw his model up and aside on landing (playtest)
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "dash_air_forward", true, "", "full"), // 1.33 s: ended by the same rule
            ("dash_air_back", "dash_air_back", true, "", "full"),
            ("dash_air_left", "dash_air_left", true, "", "full"),
            ("dash_air_right", "dash_air_right", true, "", "full"),
            ("slide_start", "slide_start", false, "slide", "full"),
            ("slide", "slide_loop", true, "", "full"),
            ("slide_getup", "slide_getup", false, "locomotion", "full"),
            ("shoot", "shoot_idle", false, "locomotion", "delta"),   // a full pose clip (no additive version): played as its motion only
            ("battery", "ability_pain_battery", false, "locomotion", "full"),
            ("jumpstart", "ability_jumpstart", false, "locomotion", "full"),
            ("aura", "cast_channeling_start", false, "locomotion", "delta"), // he keeps moving and shooting while it runs
            ("aura_end", "cast_end", false, "locomotion", "delta"),
            ("revive_down", "knockdown_large", false, "revive_hold", "full"),
            ("revive_hold", "knockdown_large_loop", true, "", "full"),
            ("revive_up", "ability_reanimation", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "knockdown_large", false, "hold", "full"),
        };

        /// <summary>Seconds his jump and air-dash poses play before the in-air loop takes over (see VictorModel.LoopRule).</summary>
        public const float JumpPose = 0.9f, AirDashPose = 0.6f;

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death", "revive_down", "revive_hold", "revive_up" };

        /// <summary>
        /// Deadlock sounds, picked from his own folders (user 2026-10-12: pick them without a review pass). His gun is layered
        /// as in Deadlock: windup + fire_first on the first shot, fire_main + mech after. No tail on letting go: its reverb
        /// sounded wrong on the last shot (user). VO by ability:
        /// 1 Pain Battery, 2 Jumpstart, 3 Aura of Suffering, 4 Shocking Reanimation (checked against Deadlock's captions).
        /// </summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "victor_gun_windup", VpkPrefix = "sounds/weapons/frank/frank_weapon_fire_windup", Volume = 0.5f, Range = 40f, MinGap = 0.2f },
            new DeadlockSound { Id = "victor_gun_first", VpkPrefix = "sounds/weapons/frank/frank_weapon_fire_first", Volume = 0.6f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.03f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "victor_gun_main", VpkPrefix = "sounds/weapons/frank/frank_weapon_fire_main", Volume = 0.55f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.05f, VolumeJitter = 0.1f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "victor_gun_mech", VpkPrefix = "sounds/weapons/frank/frank_weapon_fire_mech", Volume = 0.35f, Range = 25f, MinGap = 0.05f, PitchJitter = 0.05f },
            new DeadlockSound { Id = "victor_battery_cast", VpkPrefix = "sounds/abilities/frank/frank_pain_battery_cast", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "victor_battery_hit", VpkPrefix = "sounds/abilities/frank/frank_pain_battery_hit", Volume = 0.8f, Range = 40f, MinGap = 0.04f },
            new DeadlockSound { Id = "victor_jumpstart_cast", VpkPrefix = "sounds/abilities/frank/frank_jumpstart_cast", Volume = 0.9f, Range = 40f },
            new DeadlockSound { Id = "victor_jumpstart_loop", VpkPrefix = "sounds/abilities/frank/frank_jumpstart_buff_active", Volume = 0.5f, Range = 25f },
            new DeadlockSound { Id = "victor_jumpstart_end", VpkPrefix = "sounds/abilities/frank/frank_jumpstart_buff_expire", Volume = 0.7f, Range = 25f, MinGap = 0.3f },
            new DeadlockSound { Id = "victor_aura_cast", VpkPrefix = "sounds/abilities/frank/frank_pain_aura_cast", Volume = 0.9f, Range = 50f },
            new DeadlockSound { Id = "victor_aura_loop", VpkPrefix = "sounds/abilities/frank/frank_pain_aura_rampup_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "victor_aura_end", VpkPrefix = "sounds/abilities/frank/frank_pain_aura_deactivate", Volume = 0.8f, Range = 40f, MinGap = 0.3f },
            new DeadlockSound { Id = "victor_revive_start", VpkPrefix = "sounds/abilities/frank/frank_reanimation_start", Range = 70f },
            new DeadlockSound { Id = "victor_revive_blast", VpkPrefix = "sounds/abilities/frank/frank_reanimation_explosion", Range = 90f },
            new DeadlockSound { Id = "victor_vo_power1", VpkPrefix = "sounds/vo/frank/frank_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_power2", VpkPrefix = "sounds/vo/frank/frank_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_power3", VpkPrefix = "sounds/vo/frank/frank_use_power3", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_power4", VpkPrefix = "sounds/vo/frank/frank_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            // Desperation: the same abilities cast below a quarter health (DeadlockHeroVoice.Power picks them; he has 1-3).
            new DeadlockSound { Id = "victor_vo_desperation_power1", VpkPrefix = "sounds/vo/frank/frank_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_desperation_power2", VpkPrefix = "sounds/vo/frank/frank_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_desperation_power3", VpkPrefix = "sounds/vo/frank/frank_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "victor_vo_select", VpkPrefix = "sounds/vo/frank/frank_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "victor_vo_kill", VpkPrefix = "sounds/vo/frank/frank_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "victor_vo_killstreak", VpkPrefix = "sounds/vo/frank/frank_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "victor_vo_pain", VpkPrefix = "sounds/vo/frank/emote/frank_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "victor_vo_pain_small", VpkPrefix = "sounds/vo/frank/emote/frank_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "victor_vo_low_health", VpkPrefix = "sounds/vo/frank/emote/frank_pain_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "victor_vo_death", VpkPrefix = "sounds/vo/frank/emote/frank_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "victor_vo_effort", VpkPrefix = "sounds/vo/frank/emote/frank_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "victor_vo_efforts", VpkPrefix = "sounds/vo/frank/emote/frank_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // His movement foley (Deadlock files these under "victor", not "frank"). Dash = his movement_foley_short equivalent
            // (victor_foley_gun_short), as every hero's dash; victor_dash_sweetener is only a layer on top (wrong alone: playtest).
            new DeadlockSound { Id = "victor_dash", VpkPrefix = "sounds/player/movement/victor/victor_foley_gun_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "victor_land", VpkPrefix = "sounds/player/footsteps/victor/victor_footstep_jumpland", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "victor_footstep", VpkPrefix = "sounds/player/footsteps/victor/victor_footstep_sprint", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "victor_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "victor_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "victor_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            // Shared ones the other heroes already use.
            new DeadlockSound { Id = "victor_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "victor_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
