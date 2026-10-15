using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// Shiv's numbers (Deadlock's hero_shiv, id 19 / citadel_weapon_shiv_set + _alt / citadel_ability_shiv_*, read from the
    /// 2026-10-06 vdata: today's client 6782 can't be decoded by the bundled converter). Deadlock values are kept where RoR2
    /// has the same scale (metres, seconds, speeds; 39.37 hu = 1 m); damage becomes RoR2 damage coefficients and cooldowns
    /// RoR2-length. The kit is the user's (2026-10-14). Headline knobs are in the [Shiv] config section.
    /// </summary>
    public static class ShivData
    {
        public const string Hero = "shiv";
        public const string Section = "Shiv";

        // ---- survivor: a brawler (Deadlock 830 health, 2 regen, 6.5 m/s) ----
        public static float BaseHealth = 110f;
        public const float HealthPerLevel = 33f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.5f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 2f;              // Deadlock EBaseHealthRegen
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = ShivModel.Play,
            StaminaPips = 3,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 6.5f,    // Deadlock ESprintSpeed over his run
            // Lash's playtested controls, kept for every hero
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: the shotgun (citadel_weapon_shiv_set: 6 pellets x 4.8, 0.551 s cycle, falloff 779 -> 1620 hu to 10%) ----
        public static float PelletDamage = 0.6f;
        public static int Pellets = 6;                  // Deadlock m_iBullets
        public static float ShotCycle = 0.55125f;       // Deadlock m_flCycleTime
        public const float SpreadDeg = 3.5f;            // Deadlock 1.1 deg spread x pellet scatter
        public const float FalloffStart = 19.8f, FalloffEnd = 41.1f, FalloffMin = 0.1f; // Deadlock m_flDamageFalloffStart/EndRange, EndScale
        public const float PelletProc = 0.6f;

        // ---- primary alt: the alt fire (citadel_weapon_shiv_alt: 12 pellets, 1.26 s cycle). User: double damage, half the fire
        // rate, a harsher falloff, and a strong knockback on Shiv himself to chain with slides ----
        public static float AltPelletDamage = 0.6f;     // 12 pellets: twice the shotgun's shot
        public static int AltPellets = 12;              // Deadlock m_iBullets
        public static float AltCycle = 0.55125f * 2f;   // half the shotgun's fire rate (user)
        public const float AltSpreadDeg = 5.5f;         // Deadlock m_flPelletScatterFactor 1.5
        public static float AltFalloffStart = 8f, AltFalloffEnd = 22f; // harsher than the shotgun's (user)
        public static float AltKnockback = 22f;         // m/s pushed away from the aim (user: strong, a movement tool)
        public const float AltKnockbackUpBonus = 0.35f; // aiming down lifts a little more: a rocket-jump-like hop

        // ---- secondary: Serrated Knife (citadel_ability_shiv_dagger: 2 charges, 16 s, bleed 10/s for 5 s, 28% slow) ----
        public static float KnifeDamage = 1f;           // the hit itself (Deadlock ImpactDamage 0: RoR2 wants a hit to proc)
        public static int KnifeBleeds = 3;              // vanilla Bleed stacks (Tri-Tip's), user: 3, Duration Extender lengthens them
        public static float KnifeRecharge = 6f;         // per charge
        public const int KnifeCharges = 3;              // user (Deadlock 2)
        public const float KnifeSlow = 0.28f;           // Deadlock MovementSlow
        public const float KnifeSlowSeconds = 3f;       // as long as a vanilla bleed (Deadlock: the bleed's 5 s)
        public const float KnifeSpeed = 114f;           // Deadlock m_flBulletSpeed 4500 hu/s
        public const float KnifeGravity = 0.2f;         // Deadlock m_flBulletGravityScale
        public const float BleedSeconds = 3f;           // vanilla Bleed (Tri-Tip Dagger)

        // ---- utility: Slice'n'Dice (citadel_ability_shiv_dash: 12 m at 2400 hu/s, 2.5 m wide, 16 s) ----
        public static float DashDamage = 6f;            // user: 600%
        public static float DashCooldown = 8f;          // Deadlock 16
        public static float DashCooldownPerHit = 1f;    // user: -1 s per enemy struck
        public const float DashMetres = 12f;            // Deadlock DashRange
        public const float DashSpeed = 61f;             // Deadlock DashSpeed 2400 hu/s
        public const float AirAimPitch = 80f;           // in the air, Slice'n'Dice and Killing Blow follow the aim this far up or down (user 2.5)
        public const float DashRadius = 2.5f;           // Deadlock DashRadius
        public const float DashCastDelay = 0.1f;        // Deadlock AbilityCastDelay 0.25: shortened, RoR2 is faster

        // ---- special: Killing Blow (citadel_ability_shiv_killing_blow: 12 m, 30 m/s, 200, execute 18%, 145 s) ----
        public static float LungeDamage = 2f;           // user: 200% to everything in the path (Deadlock Damage 200)
        public static float LungeMetres = 32f;          // user: a great distance
        public const float LungeSpeed = 55f;            // m/s (Deadlock MoveSpeedToTarget 30 m/s over 12 m)
        public const float LungeHop = 2.8f;             // Deadlock m_flJumpHeight 110 hu
        public const float LungeRadius = 2.2f;
        public static float ExecuteThreshold = 0.10f;   // user (Deadlock EnemyHealthPercent 18)
        public static float ExecuteThresholdRage = 0.35f; // user: at full rage
        public const float BossThresholdScale = 0.5f;   // user: bosses at half the threshold
        public static float ExecuteRage = 60f;          // rage granted by an execute (user: a large amount)
        public static float KillingBlowCooldown = 30f;  // Deadlock 145; refunded by an execute (user)

        // ---- passive: Bloodletting (citadel_ability_shiv_defer_damage: 25% deferred over 6 s) + Rage ----
        public static float DeferredShare = 0.5f;       // user (Deadlock DamagePctDeferred 25)
        public const float DeferredSeconds = 6f;        // Deadlock DeferredDamageDuration (user)
        public const float DeferredTick = 0.25f;
        public const float RageMax = 100f;
        public static float RageFillDamage = 20f;       // full rage after dealing this many times his damage stat (user)
        public static float RageDrainDelay = 2f;        // user (Deadlock RageDrainDelayDuration 12)
        public static float RageDrainSeconds = 4f;      // from full to empty once draining (Deadlock RageDrainRate 0.25)

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Shiv max health at level 1 (+33 per level).").Value;
            PelletDamage = c.Bind(Section, "Shotgun - Pellet Damage", PelletDamage, "Damage coefficient per pellet.").Value;
            Pellets = c.Bind(Section, "Shotgun - Pellets", Pellets, "Pellets per shot.").Value;
            ShotCycle = c.Bind(Section, "Shotgun - Time Between Shots", ShotCycle, "Seconds between shots before attack speed.").Value;
            AltPelletDamage = c.Bind(Section, "Alt Fire - Pellet Damage", AltPelletDamage, "Damage coefficient per pellet of the alt fire.").Value;
            AltPellets = c.Bind(Section, "Alt Fire - Pellets", AltPellets, "Pellets per alt fire shot.").Value;
            AltCycle = c.Bind(Section, "Alt Fire - Time Between Shots", AltCycle, "Seconds between alt fire shots before attack speed.").Value;
            AltFalloffStart = c.Bind(Section, "Alt Fire - Falloff Start", AltFalloffStart, "Metres of full damage before the alt fire falls off.").Value;
            AltFalloffEnd = c.Bind(Section, "Alt Fire - Falloff End", AltFalloffEnd, "Metres where the alt fire is down to 10%.").Value;
            AltKnockback = c.Bind(Section, "Alt Fire - Self Knockback", AltKnockback, "m/s Shiv is pushed away from where he aims.").Value;
            KnifeDamage = c.Bind(Section, "Serrated Knife - Damage", KnifeDamage, "Damage coefficient of the knife hit.").Value;
            KnifeBleeds = c.Bind(Section, "Serrated Knife - Bleed Stacks", KnifeBleeds, "Bleed stacks per knife (Tri-Tip Dagger's bleed).").Value;
            KnifeRecharge = c.Bind(Section, "Serrated Knife - Recharge", KnifeRecharge, "Seconds per charge (3 charges).").Value;
            DashDamage = c.Bind(Section, "Slice n Dice - Damage", DashDamage, "Damage coefficient to enemies in the dash's path.").Value;
            DashCooldown = c.Bind(Section, "Slice n Dice - Cooldown", DashCooldown, "Seconds.").Value;
            DashCooldownPerHit = c.Bind(Section, "Slice n Dice - Cooldown Per Hit", DashCooldownPerHit, "Seconds taken off the cooldown per enemy struck.").Value;
            LungeDamage = c.Bind(Section, "Killing Blow - Damage", LungeDamage, "Damage coefficient to enemies in the lunge's path.").Value;
            LungeMetres = c.Bind(Section, "Killing Blow - Distance", LungeMetres, "Metres the lunge covers.").Value;
            ExecuteThreshold = c.Bind(Section, "Killing Blow - Execute Threshold", ExecuteThreshold, "Health fraction under which an enemy is executed (bosses: half).").Value;
            ExecuteThresholdRage = c.Bind(Section, "Killing Blow - Execute Threshold At Full Rage", ExecuteThresholdRage, "Execute threshold at full rage (bosses: half).").Value;
            ExecuteRage = c.Bind(Section, "Killing Blow - Rage On Execute", ExecuteRage, "Rage gained by an execute (100 = full).").Value;
            KillingBlowCooldown = c.Bind(Section, "Killing Blow - Cooldown", KillingBlowCooldown, "Seconds (refunded by an execute).").Value;
            DeferredShare = c.Bind(Section, "Bloodletting - Deferred Share", DeferredShare, "Share of damage taken that is dealt over 6 s instead.").Value;
            RageFillDamage = c.Bind(Section, "Rage - Damage To Fill", RageFillDamage, "Full rage after dealing this many times Shiv's damage stat.").Value;
            RageDrainDelay = c.Bind(Section, "Rage - Drain Delay", RageDrainDelay, "Seconds without dealing damage before rage drains.").Value;
            RageDrainSeconds = c.Bind(Section, "Rage - Drain Time", RageDrainSeconds, "Seconds for full rage to drain to nothing.").Value;
        }

        /// <summary>Execute threshold for a target (bosses at half: user).</summary>
        public static float Threshold(bool fullRage, bool boss) =>
            (fullRage ? ExecuteThresholdRage : ExecuteThreshold) * (boss ? BossThresholdScale : 1f);

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/shiv/shiv.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f; // Lash's size (every hero but Vyper, user 1.7)
        public const string MuzzleBone = "muzzle_fx";  // checked against his skeleton once the converter works again

        public const string IconPortrait = "panorama/images/heroes/shiv_sm_psd.vtex_c";
        public const string IconKnife = "panorama/images/hud/abilities/shiv/shiv_toss_psd.vtex_c";           // dagger's m_strAbilityImage
        public const string IconDash = "panorama/images/hud/abilities/shiv/shiv_flash_psd.vtex_c";           // dash's
        public const string IconBloodletting = "panorama/images/hud/abilities/shiv/shiv_bloodletting_psd.vtex_c";
        public const string IconKillingBlow = "panorama/images/hud/abilities/shiv/shiv_killing_blow_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Shiv is an AnimGraph2 hero (models/heroes_staging/shiv/clips):
        /// ability_shiv_dash, ability_shiv_killingblow_leap/_slash, shiv_dagger, reload_idle (the intro's shotgun flick).
        /// shoot_idle is a full pose, played as a delta overlay (as Grey Talon's). No death clip: a knockdown stands in.
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
            ("shoot", "shoot_idle", false, "locomotion", "delta"),
            ("shoot_alt", "shoot_alt_idle", false, "locomotion", "delta"),
            ("knife", "shiv_dagger", false, "locomotion", "full"),
            ("slice", "ability_shiv_dash", false, "locomotion", "full"),
            ("lunge", "ability_shiv_killingblow_leap", false, "hold", "full"),
            ("slash", "ability_shiv_killingblow_slash", false, "locomotion", "full"),
            ("reload", "reload_idle", false, "locomotion", "full"),
            ("parry", "parry", false, "locomotion", "full"),          // Counterspell (DeadlockMover plays it)
            ("parry_air", "parry", false, "in_air", "full"),
            ("death", "knockdown_large_start", false, "hold", "full"),
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "slice", "lunge", "slash", "death" };

        /// <summary>Deadlock sounds (user 2026-10-12: I pick and wire them, no review pass).</summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "shiv_gun", VpkPrefix = "sounds/weapons/shiv/shiv_weapon_fire", Volume = 0.7f, Range = 70f, MinGap = 0.05f, PitchJitter = 0.04f, VolumeJitter = 0.1f, Fallback = "Play_bandit2_m1_rifle" },
            new DeadlockSound { Id = "shiv_gun_alt", VpkPrefix = "sounds/weapons/shiv/shiv_weapon_fire_alt", Volume = 0.8f, Range = 80f, MinGap = 0.1f, PitchJitter = 0.03f, Fallback = "Play_bandit2_m1_shotgun" },
            new DeadlockSound { Id = "shiv_reload_start", VpkPrefix = "sounds/weapons/shiv/reload/shiv_wpn_reload_start", Volume = 0.8f, Range = 25f },
            new DeadlockSound { Id = "shiv_reload_mid", VpkPrefix = "sounds/weapons/shiv/reload/shiv_wpn_reload_mid", Volume = 0.8f, Range = 25f },
            new DeadlockSound { Id = "shiv_reload_end", VpkPrefix = "sounds/weapons/shiv/reload/shiv_wpn_reload_end", Volume = 0.8f, Range = 25f },
            new DeadlockSound { Id = "shiv_knife_cast", VpkPrefix = "sounds/abilities/shiv/shiv_a1_dagger_cast", Volume = 0.9f },
            new DeadlockSound { Id = "shiv_knife_whizby", VpkPrefix = "sounds/abilities/shiv/a1_thrown_shiv/whizby", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "shiv_knife_hit", VpkPrefix = "sounds/abilities/shiv/shiv_a1_dagger_hit", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "shiv_knife_bleed", VpkPrefix = "sounds/abilities/shiv/a1_thrown_shiv/debuff_start", Volume = 0.7f, Range = 30f, MinGap = 0.05f },
            new DeadlockSound { Id = "shiv_knife_miss", VpkPrefix = "sounds/abilities/shiv/a1_thrown_shiv/surf_imp_stab", Volume = 0.7f, Range = 25f, MinGap = 0.05f },
            new DeadlockSound { Id = "shiv_dash_cast", VpkPrefix = "sounds/abilities/shiv/shiv_a1_dash_cast", Volume = 0.9f },
            new DeadlockSound { Id = "shiv_dash_hit", VpkPrefix = "sounds/abilities/shiv/shiv_dash_hit", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "shiv_lunge_cast", VpkPrefix = "sounds/abilities/shiv/shiv_a4_killingblow_cast", Range = 60f },
            new DeadlockSound { Id = "shiv_lunge_hit", VpkPrefix = "sounds/abilities/shiv/shiv_a4_killingblow_hit", Range = 50f, MinGap = 0.05f },
            new DeadlockSound { Id = "shiv_lunge_kill", VpkPrefix = "sounds/abilities/shiv/shiv_a4_killingblow_hit_kill", Range = 70f },
            new DeadlockSound { Id = "shiv_cleanse", VpkPrefix = "sounds/abilities/shiv/shiv_a3_bloodletting_cast", Volume = 0.9f },
            new DeadlockSound { Id = "shiv_rage_full", VpkPrefix = "sounds/abilities/shiv/shiv_rage_full", Volume = 0.9f, MinGap = 1f },
            new DeadlockSound { Id = "shiv_rage_end", VpkPrefix = "sounds/abilities/shiv/shiv_rage_end", Volume = 0.8f, MinGap = 1f },
            // VO by ability: 1 Serrated Knife, 2 Slice'n'Dice, 3 Bloodletting (the execute's cleanse), 4 Killing Blow.
            new DeadlockSound { Id = "shiv_vo_power1", VpkPrefix = "sounds/vo/shiv/shiv_use_power1", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_power2", VpkPrefix = "sounds/vo/shiv/shiv_use_power2", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_power3", VpkPrefix = "sounds/vo/shiv/shiv_use_power3", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_power4", VpkPrefix = "sounds/vo/shiv/shiv_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_desperation_power1", VpkPrefix = "sounds/vo/shiv/shiv_desperation_power1", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_desperation_power2", VpkPrefix = "sounds/vo/shiv/shiv_desperation_power2", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_desperation_power3", VpkPrefix = "sounds/vo/shiv/shiv_desperation_power3", Chance = 0.8f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_desperation_power4", VpkPrefix = "sounds/vo/shiv/shiv_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_select", VpkPrefix = "sounds/vo/shiv/shiv_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_kill", VpkPrefix = "sounds/vo/shiv/shiv_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_killstreak", VpkPrefix = "sounds/vo/shiv/shiv_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_pain", VpkPrefix = "sounds/vo/shiv/emote/shiv_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_pain_small", VpkPrefix = "sounds/vo/shiv/emote/shiv_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_low_health", VpkPrefix = "sounds/vo/shiv/shiv_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_death", VpkPrefix = "sounds/vo/shiv/emote/shiv_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_effort", VpkPrefix = "sounds/vo/shiv/emote/shiv_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "shiv_vo_efforts", VpkPrefix = "sounds/vo/shiv/emote/shiv_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Movement: his own footsteps and landing, the shared jump/slide sounds.
            new DeadlockSound { Id = "shiv_footstep", VpkPrefix = "sounds/player/footsteps/shiv/shiv_footstep", Volume = 0.3f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "shiv_land", VpkPrefix = "sounds/player/movement/shiv/shiv_jump_land", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "shiv_dash", VpkPrefix = "sounds/player/movement/shiv/shiv_movement_foley_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "shiv_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "shiv_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "shiv_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "shiv_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "shiv_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
