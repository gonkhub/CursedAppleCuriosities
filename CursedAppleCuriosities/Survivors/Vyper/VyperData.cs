using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Vyper's numbers (Deadlock's hero_viper / ability_viper_*, read from client 6774; her icons are named kali_*).
    /// Deadlock values are kept where RoR2 has the same scale (metres, seconds, speeds; 39.37 hu = 1 m); damage becomes
    /// RoR2 damage coefficients and cooldowns RoR2-length. Headline knobs are in the [Vyper] config section.
    /// </summary>
    public static class VyperData
    {
        public const string Hero = "vyper";
        public const string Section = "Vyper";

        // ---- survivor: a close-range assassin (Deadlock 780 health, 2 regen, 6.9 m/s) ----
        public static float BaseHealth = 100f;
        public const float HealthPerLevel = 30f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.9f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 2f;              // Deadlock EBaseHealthRegen
        // ---- Deadlock movement: Deadlock's own 4 stamina (user: the exception, like Lash), 1 air jump, 1 air dash; Slither ----
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = VyperModel.Play,
            StaminaPips = 4,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 6.9f,    // Deadlock ESprintSpeed over her run
            // Slither (ability_viper_snakedash / ability_viper_slide): turns twice as fast, no flat friction for 0.6 s,
            // slides up hills, +15% slide distance.
            SlideTurnDegPerSec = 180f,         // m_flTurnSpeed
            SlideFrictionGrace = 1f,           // Deadlock m_flFlatGroundFrictionGraceTime 0.6; longer by playtest
            SlideUphillLoss = 0f,
            SlideDistanceBonus = 0.15f,        // SlideScale 15
            // Even more slippery than Deadlock (playtest): flat ground and slight inclines barely slow her.
            SlideFriction = 0.4f,              // heroes 1.2 (Lash's playtested value)
            SlideFrictionSlope = 0.1f,         // Deadlock 0.15
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: The Rattler (full-auto; proc coefficient 1, balanced by damage: user) ----
        public static float GunDamage = 0.22f;
        public static float GunCycle = 0.07f;           // Deadlock m_flCycleTime (~14 shots/s)
        public static float GunSlideFireRate = 0.5f;    // +50% fire rate while sliding (user)
        // Spread like Deadlock's (m_flShootSpreadPenaltyPerShot, penalty decay after 0.3 s): tight on the first shot, wider
        // the longer she holds the trigger, back to tight soon after she lets go. Degrees.
        public static float GunSpreadMin = 0.6f;
        public static float GunSpreadPerShot = 0.3f;
        public static float GunSpreadMax = 4.5f;
        public const float GunSpreadRecoverDelay = 0.3f; // Deadlock m_flShootSpreadPenaltyDecayDelay
        public const float GunSpreadRecover = 9f;        // degrees per second once recovering
        public const float GunBloomPerShot = 0.12f;

        // ---- secondary: Screwjab Dagger ----
        public static float DaggerDamage = 2.5f;        // Deadlock Damage 50
        public static float DaggerStackDamage = 1.5f;   // per earlier dagger on the target (Deadlock DamagePerStack 25; 150% by playtest)
        public static float DaggerRecharge = 5f;        // per charge (Deadlock 10 s cooldown, 4 s between charges)
        public const int DaggerCharges = 2;             // Deadlock AbilityCharges
        public const float DaggerSlow = 0.28f;          // Deadlock SlowPercent
        public const float DaggerSlowPerStack = 0.12f;  // Deadlock SlowPercentPerStack
        public const float DaggerSlowSeconds = 2f;      // Deadlock SlowDuration
        public const float DaggerStackSeconds = 10f;    // Deadlock StackDuration
        public const int DaggerMaxStacks = int.MaxValue; // Deadlock MaxStacks 3; no cap (user): stacks while daggers keep landing within 10 s
        public const float DaggerSpeed = 90f;           // Deadlock 4200 hu/s as a bullet; a little slower so it reads as thrown

        // ---- utility: Lethal Venom ----
        public static float VenomMinDamage = 3f;        // at full health (Deadlock VenomMinDamage 20)
        public static float VenomMaxDamage = 21f;       // at 30% health or lower (Deadlock VenomMaxDamage 140)
        public const float VenomMinAt = 1f, VenomMaxAt = 0.3f; // Deadlock VenomMin/MaxDamageHealthPercentage
        public static float VenomCooldown = 10f;        // Deadlock 28
        public const float VenomDelay = 3f;             // Deadlock VenomDuration
        public static float VenomRange = 10f;           // Deadlock AbilityCastRange
        public const float VenomConeDeg = 35f;          // Deadlock's targeting cone is 70 deg wide

        // ---- special: Petrifying Bola (Deadlock's T3 given free: everything in the blast is petrified, user) ----
        public static float BolaDamage = 4f;            // blast (Deadlock Damage 50)
        public static float BolaDirectDamage = 10f;     // direct hit, on top (Deadlock PetrifyDamage 180)
        public static float BolaCooldown = 30f;         // Deadlock 115
        public static float BolaRadius = 12f;           // Deadlock Radius
        public static float PetrifySeconds = 2.2f;      // Deadlock PetrifyDuration
        public const float PetrifyBossSeconds = 1f;     // bosses: a short stun, never immune (user)
        public const float BolaSlow = 0.4f;             // Deadlock SlowPercent
        public const float BolaSlowSeconds = 1.5f;      // Deadlock SlowDuration (after the petrify)
        public const float BolaSpeed = 50.8f;           // Deadlock 2000 hu/s
        public const float BolaLifetime = 2f;           // Deadlock m_flBulletLifetime
        public const float BolaDrop = 4f;               // m/s2: a mostly straight throw with a little drop (~2 m over the first 50 m)

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Vyper max health at level 1 (+30 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet (full-auto, proc coefficient 1).").Value;
            GunCycle = c.Bind(Section, "Gun - Time Between Shots", GunCycle, "Seconds between shots before attack speed.").Value;
            GunSlideFireRate = c.Bind(Section, "Gun - Slide Fire Rate Bonus", GunSlideFireRate, "Extra fire rate while sliding (0.5 = +50%).").Value;
            GunSpreadMin = c.Bind(Section, "Gun - Spread Min", GunSpreadMin, "Degrees of spread on the first shot.").Value;
            GunSpreadPerShot = c.Bind(Section, "Gun - Spread Per Shot", GunSpreadPerShot, "Degrees of spread added by each shot while holding the trigger.").Value;
            GunSpreadMax = c.Bind(Section, "Gun - Spread Max", GunSpreadMax, "Most degrees of spread when holding the trigger.").Value;
            DaggerDamage = c.Bind(Section, "Screwjab Dagger - Damage", DaggerDamage, "Damage coefficient of a dagger.").Value;
            DaggerStackDamage = c.Bind(Section, "Screwjab Dagger - Damage Per Stack", DaggerStackDamage, "Damage coefficient added for each earlier dagger on the same target (no cap).").Value;
            DaggerRecharge = c.Bind(Section, "Screwjab Dagger - Recharge", DaggerRecharge, "Seconds per charge (2 charges).").Value;
            VenomMinDamage = c.Bind(Section, "Lethal Venom - Min Damage", VenomMinDamage, "Damage coefficient against a target at full health.").Value;
            VenomMaxDamage = c.Bind(Section, "Lethal Venom - Max Damage", VenomMaxDamage, "Damage coefficient against a target at 30% health or lower.").Value;
            VenomRange = c.Bind(Section, "Lethal Venom - Range", VenomRange, "Metres.").Value;
            VenomCooldown = c.Bind(Section, "Lethal Venom - Cooldown", VenomCooldown, "Seconds.").Value;
            BolaDamage = c.Bind(Section, "Petrifying Bola - Damage", BolaDamage, "Damage coefficient to everything in the blast.").Value;
            BolaDirectDamage = c.Bind(Section, "Petrifying Bola - Direct Hit Damage", BolaDirectDamage, "Extra damage coefficient to the enemy hit directly.").Value;
            BolaRadius = c.Bind(Section, "Petrifying Bola - Radius", BolaRadius, "Metres.").Value;
            PetrifySeconds = c.Bind(Section, "Petrifying Bola - Petrify Duration", PetrifySeconds, "Seconds enemies are petrified (stunned and immune to everything but Lethal Venom).").Value;
            BolaCooldown = c.Bind(Section, "Petrifying Bola - Cooldown", BolaCooldown, "Seconds.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/viper/viper.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f; // Lash's size
        public const string MuzzleBone = "muzzle_fx";

        public const string IconPortrait = "panorama/images/heroes/kali_sm_psd.vtex_c";
        public const string IconDagger = "panorama/images/hud/abilities/viper/viper_debuffdagger_psd.vtex_c";
        public const string IconVenom = "panorama/images/hud/abilities/viper/viper_venom_psd.vtex_c";
        public const string IconSlither = "panorama/images/hud/abilities/viper/viper_snakedash_psd.vtex_c";
        public const string IconBola = "panorama/images/hud/abilities/viper/viper_petrifybola_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Vyper is an AnimGraph2 hero (models/heroes_staging): viper_abilitymask
        /// plays viper_dagger_ability and viper_bolo_ability; Lethal Venom has no clip of its own (generic_cast_*). Her
        /// clip set has no plain jump start, in-air loop or death: jump_ground, in_air_loop_apex and a knockdown stand in.
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
            ("jump", "jump_ground", false, "in_air", "full"),
            ("air_jump", "jump_ground", false, "in_air", "full"),
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
            ("shoot", "weapon_shoot", false, "locomotion", "additive"),
            ("dagger", "viper_dagger_ability", false, "locomotion", "full"),
            ("venom", "generic_cast_start", false, "locomotion", "full"),
            ("bola", "viper_bolo_ability", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "knockdown_large_start", false, "hold", "full"),
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "death" };

        /// <summary>
        /// Deadlock sounds, approved by the user by ear (2026-10-12). Her gun is layered as in Deadlock: shot_first, shot_mid while
        /// held, then tail + release on letting go (vyper_wpn_last_shot is a clip's last round: n/a, infinite ammo). She has no
        /// hero footstep, dash foley or landing sounds in Deadlock: those stay silent until the user picks some.
        /// </summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "vyper_gun_first", VpkPrefix = "sounds/weapons/viper/viper_weapon_shot_first", Volume = 0.6f, Range = 60f, MinGap = 0.03f, PitchJitter = 0.04f, VolumeJitter = 0.1f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "vyper_gun_mid", VpkPrefix = "sounds/weapons/viper/viper_weapon_shot_mid", Volume = 0.5f, Range = 60f, MinGap = 0.03f, PitchJitter = 0.07f, VolumeJitter = 0.2f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "vyper_gun_tail", VpkPrefix = "sounds/weapons/viper/viper_weapon_tail", Volume = 0.6f, Range = 60f, MinGap = 0.1f },
            new DeadlockSound { Id = "vyper_gun_release", VpkPrefix = "sounds/weapons/viper/viper_weapon_release", Volume = 0.6f, Range = 40f, MinGap = 0.1f },
            new DeadlockSound { Id = "vyper_dagger_cast", VpkPrefix = "sounds/abilities/viper/a1_dagger/viper_dagger_cast", Volume = 0.9f },
            new DeadlockSound { Id = "vyper_dagger_loop", VpkPrefix = "sounds/abilities/viper/a1_dagger/viper_dagger_projectile_lp", Volume = 0.6f, Range = 25f },
            new DeadlockSound { Id = "vyper_dagger_impact", VpkPrefix = "sounds/abilities/viper/a1_dagger/viper_dagger_impact", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "vyper_dagger_miss", VpkPrefix = "sounds/abilities/viper/viper_dagger_miss", Volume = 0.7f, Range = 25f, MinGap = 0.05f },
            new DeadlockSound { Id = "vyper_venom_cast", VpkPrefix = "sounds/abilities/viper/a2_venom/viper_venom_cast", Volume = 0.9f },
            new DeadlockSound { Id = "vyper_venom_loop", VpkPrefix = "sounds/abilities/viper/a2_venom/viper_venom_lp", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "vyper_venom_weak", VpkPrefix = "sounds/abilities/viper/a2_venom/viper_venom_explode_weak", Range = 50f },
            new DeadlockSound { Id = "vyper_venom_medium", VpkPrefix = "sounds/abilities/viper/a2_venom/viper_venom_explode_medium", Range = 50f },
            new DeadlockSound { Id = "vyper_venom_strong", VpkPrefix = "sounds/abilities/viper/a2_venom/viper_venom_explode_strong", Range = 60f },
            new DeadlockSound { Id = "vyper_bola_cast", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_cast", Volume = 0.9f },
            new DeadlockSound { Id = "vyper_bola_loop", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_proj_lp", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "vyper_bola_explode", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_explode", Range = 70f },
            new DeadlockSound { Id = "vyper_bola_direct", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_direct_hit", Range = 60f },
            new DeadlockSound { Id = "vyper_petrify_loop", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_debuff_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "vyper_petrify_end", VpkPrefix = "sounds/abilities/viper/a4_bola/viper_petrifying_bola_debuff_end", Volume = 0.8f, Range = 35f, MinGap = 0.05f },
            // Slither: her own slide sounds, not the generic ones.
            new DeadlockSound { Id = "vyper_slide", VpkPrefix = "sounds/abilities/viper/a3_slide/viper_slide_enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "vyper_slide_loop", VpkPrefix = "sounds/abilities/viper/a3_slide/viper_slide_lp", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "vyper_slide_exit", VpkPrefix = "sounds/abilities/viper/a3_slide/viper_slide_exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            // VO by ability: 1 Screwjab Dagger, 2 Lethal Venom, 4 Petrifying Bola (Slither is passive: no power3).
            new DeadlockSound { Id = "vyper_vo_power1", VpkPrefix = "sounds/vo/viper/viper_use_power1", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_power2", VpkPrefix = "sounds/vo/viper/viper_use_power2", Chance = 0.6f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_power4", VpkPrefix = "sounds/vo/viper/viper_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            // Desperation: the same abilities cast below a quarter health (DeadlockHeroVoice.Power picks them).
            new DeadlockSound { Id = "vyper_vo_desperation_power1", VpkPrefix = "sounds/vo/viper/viper_desperation_power1", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_desperation_power2", VpkPrefix = "sounds/vo/viper/viper_desperation_power2", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_desperation_power4", VpkPrefix = "sounds/vo/viper/viper_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_select", VpkPrefix = "sounds/vo/viper/viper_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_kill", VpkPrefix = "sounds/vo/viper/viper_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_killstreak", VpkPrefix = "sounds/vo/viper/viper_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_pain", VpkPrefix = "sounds/vo/viper/emote/viper_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_pain_small", VpkPrefix = "sounds/vo/viper/emote/viper_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_low_health", VpkPrefix = "sounds/vo/viper/viper_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_death", VpkPrefix = "sounds/vo/viper/emote/viper_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_effort", VpkPrefix = "sounds/vo/viper/emote/viper_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "vyper_vo_efforts", VpkPrefix = "sounds/vo/viper/emote/viper_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Shared ones Lash and Abrams already use.
            new DeadlockSound { Id = "vyper_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "vyper_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
