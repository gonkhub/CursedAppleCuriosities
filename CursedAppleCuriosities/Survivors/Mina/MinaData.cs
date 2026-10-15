using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Mina's numbers (Deadlock's hero_vampirebat, id 63 / citadel_weapon_vampirebat_set / ability_vampirebat_*, read from the
    /// 2026-10-06 vdata: today's client 6782 can't be decoded by the bundled converter). Deadlock calls her "vampirebat"
    /// everywhere (model heroes_wip/vampirebat, VO sounds/vo/vampirebat, foley under "mina"). Deadlock values are kept where
    /// RoR2 has the same scale; damage becomes RoR2 coefficients. The kit is the user's (2026-10-14).
    /// </summary>
    public static class MinaData
    {
        public const string Hero = "mina";
        public const string Section = "Mina";

        // ---- survivor: fragile (Deadlock 660 health, 2 regen, 6.5 m/s) ----
        public static float BaseHealth = 90f;
        public const float HealthPerLevel = 27f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.5f;            // Deadlock EMaxMoveSpeed
        public const float BaseRegen = 2f;              // Deadlock EBaseHealthRegen
        // Deadlock's 2 stamina (user), and her umbrella hover instead (MinaHover).
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = MinaModel.Play,
            StaminaPips = 2,                   // Deadlock EStamina
            OutOfCombatSpeed = 1.6f / 6.5f,    // Deadlock ESprintSpeed over her run
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- passive: the umbrella hover (Deadlock: aiming down sights in the air floats her, ability_vampirebat_steallife's
        // "vampirebat_floating": FallSpeedMax 3, MaxFloatTime 4). User: in RoR2 it's holding jump in the air. ----
        public static float HoverFallSpeed = 3f;        // m/s at most while floating (Deadlock FallSpeedMax)
        public static float HoverSeconds = 4f;          // per time in the air (Deadlock MaxFloatTime)
        public const float HoverHoldDelay = 0.15f;      // jump held this long, falling, before she opens the umbrella (a tap is a jump)

        // ---- primary: her pistol (citadel_weapon_vampirebat_set: 7.31 a shot, 0.252 s, falloff 787 -> 1811 hu to 10%) ----
        public static float GunDamage = 0.9f;           // user: low damage, her damage is Love Bites
        public static float GunCycle = 0.252f;          // Deadlock m_flCycleTime
        public const float GunFalloffStart = 20f, GunFalloffEnd = 46f, GunFalloffMin = 0.1f;
        public const float GunSpread = 0.6f;

        // ---- secondary: Rake (ability_vampirebat_steallife: 10 m, 60 deg, 60 damage + 6% of missing health, 16 s, 25 heal on kill) ----
        public static float RakeDamage = 3f;
        public static float RakeMissingHealth = 0.06f;  // Deadlock MissingHealthDamagePercentage: the target's missing health, as extra damage
        public const float RakeMissingCap = 10f;        // that extra is capped at this many times her damage (bosses' huge health bars)
        public static float RakeKillHeal = 0.1f;        // share of her max health per kill (Deadlock RakeHealPerKill 25)
        public static float RakeCooldown = 6f;          // Deadlock 16
        public static float RakeRange = 10f;            // Deadlock AbilityCastRange
        public const float RakeConeDeg = 60f;           // Deadlock TargetingConeAngle (whole cone)
        public const float RakeHop = 5f;                // Deadlock MiniJumpVelocity 200 hu/s, in the air
        public const float RakeCastDelay = 0.1f;        // Deadlock AbilityCastDelay

        // ---- utility: Sanguine Retreat (ability_vampirebat_batblink: 9 m in 0.65 s, 1 recast within 4 s, 32 s) ----
        public static float RetreatMetres = 14f;        // user: a moderate distance
        public const float RetreatSeconds = 0.45f;
        public static float RetreatRecastWindow = 5f;   // user (Deadlock RecastWindow 4)
        public static float RetreatCooldown = 10f;      // Deadlock 32
        public const float RetreatExitSpeed = 5f;       // Deadlock ExitVelocity 5 m

        // ---- special: Nox Nostra (ability_vampirebat_batswarm: leap 17 m/s at 60 deg, hover, 75 bats at 30/s, 150 s) ----
        public static int Bats = 75;                    // Deadlock BatCount
        public const int BonusBatsMax = 50;             // Deadlock BonusBatsMax
        public const float BatsPerSecond = 30f;         // Deadlock BatPerSecond
        public static float BatMaxHealth = 0.01f;       // user: 1% of max health a bat
        public const float BatBossScale = 0.5f;         // user: half on bosses (Deadlock m_flBossDamageScale 0.5)
        public const float BatProc = 0.2f;              // Deadlock BatEffectiveness
        public const float BatSpeed = 35f;              // Deadlock m_flSpeed 1800 hu/s ~ 46, slower to read as a swarm
        public const float BatLifetime = 3f;            // Deadlock m_flBatLifetime
        public const float BatTurnDegPerSec = 420f;
        public const float BatSeekRange = 40f;          // Deadlock AbilityCastRange
        public const float BatSeekConeDeg = 40f;        // Deadlock LockonConeAngle
        public const float NoxJumpSpeed = 24f;          // Deadlock JumpSpeed 17: higher (user 2.5)
        public const float NoxJumpPitch = 72f;          // Deadlock JumpPitch -60 (up), steeper; leaps partly BACKWARD (user 2.5)
        public const float NoxFallMax = 1f;             // Deadlock FallSpeedMax: hovering on the umbrella
        public const float NoxCastDelay = 0.5f;         // Deadlock AbilityCastDelay
        public static float NoxCooldown = 40f;          // Deadlock 150

        // ---- passive: Love Bites (ability_vampirebat_lovebites: build up 100 -> burst 45 + spirit, 10 s per target) ----
        public static float LoveBitesDamage = 4f;       // user: 400%
        public static float LoveBitesTargetCooldown = 15f; // user (Deadlock PerTargetCooldown 10)
        public const float BuildUpMax = 100f;
        public const float BuildUpPerShot = 18.4f;      // Deadlock BuildUpPerShot, per proc coefficient
        public const float BuildUpPerBat = 20f;         // Deadlock BuildUpPerBat
        public const float BuildUpPerRake = 40f;
        public const float BuildUpDecayDelay = 3f;      // Deadlock m_flBuildUpDecayDelay
        public const float BuildUpDecaySeconds = 2f;    // from full to nothing once decaying

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Mina max health at level 1 (+27 per level).").Value;
            HoverFallSpeed = c.Bind(Section, "Umbrella Hover - Fall Speed", HoverFallSpeed, "Fastest fall (m/s) while holding jump in the air.").Value;
            HoverSeconds = c.Bind(Section, "Umbrella Hover - Duration", HoverSeconds, "Seconds of hover per time in the air.").Value;
            GunDamage = c.Bind(Section, "Pistol - Damage", GunDamage, "Damage coefficient per shot.").Value;
            GunCycle = c.Bind(Section, "Pistol - Time Between Shots", GunCycle, "Seconds between shots before attack speed.").Value;
            RakeDamage = c.Bind(Section, "Rake - Damage", RakeDamage, "Damage coefficient of the swipe.").Value;
            RakeMissingHealth = c.Bind(Section, "Rake - Missing Health Damage", RakeMissingHealth, "Extra damage as a share of the target's missing health.").Value;
            RakeKillHeal = c.Bind(Section, "Rake - Heal Per Kill", RakeKillHeal, "Share of Mina's max health healed per kill.").Value;
            RakeCooldown = c.Bind(Section, "Rake - Cooldown", RakeCooldown, "Seconds.").Value;
            RakeRange = c.Bind(Section, "Rake - Range", RakeRange, "Metres.").Value;
            RetreatMetres = c.Bind(Section, "Sanguine Retreat - Distance", RetreatMetres, "Metres travelled as bats.").Value;
            RetreatRecastWindow = c.Bind(Section, "Sanguine Retreat - Recast Window", RetreatRecastWindow, "Seconds to recast once.").Value;
            RetreatCooldown = c.Bind(Section, "Sanguine Retreat - Cooldown", RetreatCooldown, "Seconds.").Value;
            Bats = c.Bind(Section, "Nox Nostra - Bats", Bats, "Bats per cast, before Love Bites stacks.").Value;
            BatMaxHealth = c.Bind(Section, "Nox Nostra - Bat Max Health Damage", BatMaxHealth, "Share of the target's max health a bat deals (bosses: half).").Value;
            NoxCooldown = c.Bind(Section, "Nox Nostra - Cooldown", NoxCooldown, "Seconds.").Value;
            LoveBitesDamage = c.Bind(Section, "Love Bites - Damage", LoveBitesDamage, "Damage coefficient of the burst.").Value;
            LoveBitesTargetCooldown = c.Bind(Section, "Love Bites - Per Target Cooldown", LoveBitesTargetCooldown, "Seconds before the same enemy can be Love Bitten again.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_wip/vampirebat/vampirebat.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f;
        public const string MuzzleBone = "muzzle_fx";   // checked against her skeleton once the converter works again

        public const string IconPortrait = "panorama/images/heroes/vampirebat_sm_psd.vtex_c";
        public const string IconRake = "panorama/images/hud/abilities/vampirebat/vampirebat_rake_psd.vtex_c";
        public const string IconRetreat = "panorama/images/hud/abilities/vampirebat/vampirebat_sanguine_retreat_psd.vtex_c";
        public const string IconLoveBites = "panorama/images/hud/abilities/vampirebat/vampirebat_love_bites_psd.vtex_c";
        public const string IconNox = "panorama/images/hud/abilities/vampirebat/vampirebat_nox_nostra_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer. Mina is an AnimGraph2 hero (heroes_wip/vampirebat/clips), most clips
        /// prefixed vampirebat_: vampirebat_ability_rake, vampirebat_ability_ult_start/_loop (Nox Nostra's hover). Sanguine Retreat
        /// hides her (she is the bats). No death clip: her large knockdown stands in.
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims =
        {
            ("idle", "vampirebat_weapon_stand_idle", true, "", "full"),
            ("run_n", "vampirebat_weapon_run_n", true, "", "full"), ("run_ne", "vampirebat_weapon_run_ne", true, "", "full"),
            ("run_e", "vampirebat_weapon_run_e", true, "", "full"), ("run_se", "vampirebat_weapon_run_se", true, "", "full"),
            ("run_s", "vampirebat_weapon_run_s", true, "", "full"), ("run_sw", "vampirebat_weapon_run_sw", true, "", "full"),
            ("run_w", "vampirebat_weapon_run_w", true, "", "full"), ("run_nw", "vampirebat_weapon_run_nw", true, "", "full"),
            ("sprint_n", "vampirebat_outofcombat_run_n", true, "", "full"), ("sprint_ne", "vampirebat_outofcombat_run_ne", true, "", "full"),
            ("sprint_e", "vampirebat_outofcombat_run_e", true, "", "full"), ("sprint_se", "vampirebat_outofcombat_run_se", true, "", "full"),
            ("sprint_s", "vampirebat_outofcombat_run_s", true, "", "full"), ("sprint_sw", "vampirebat_outofcombat_run_sw", true, "", "full"),
            ("sprint_w", "vampirebat_outofcombat_run_w", true, "", "full"), ("sprint_nw", "vampirebat_outofcombat_run_nw", true, "", "full"),
            ("jump", "vampirebat_jump_ground", false, "in_air", "full"),
            ("air_jump", "vampirebat_jump_ground", false, "in_air", "full"),
            ("wall_jump", "vampirebat_jump_ground", false, "in_air", "full"),
            ("in_air", "vampirebat_in_air_loop_apex", true, "", "full"),
            ("falling", "vampirebat_in_air_loop_down", true, "", "full"),
            ("land", "vampirebat_jump_landing", false, "locomotion", "delta"), // her wip clip stores the pelvis 2 m off (0,-1.44,1.39): additive threw the model off on landing (user 2.5)
            ("dash_ground", "dash_ground", false, "locomotion", "full"),
            ("dash_air_forward", "vampirebat_dash_air_forward", false, "in_air", "full"),
            ("dash_air_back", "vampirebat_dash_air_backward", false, "in_air", "full"),
            ("dash_air_left", "vampirebat_dash_air_left", false, "in_air", "full"),
            ("dash_air_right", "vampirebat_dash_air_right", false, "in_air", "full"),
            ("slide_start", "vampirebat_slide_intro", false, "slide", "full"),
            ("slide", "vampirebat_slide_forward", true, "", "full"),
            ("slide_getup", "vampirebat_slide_getup", false, "locomotion", "full"),
            ("shoot", "vampirebat_idle_shoot", false, "locomotion", "delta"),
            ("rake", "vampirebat_ability_rake", false, "locomotion", "full"),
            ("retreat_end", "vampirebat_in_air_loop_glide", false, "in_air", "full"), // reforming mid-air under the umbrella
            ("nox", "vampirebat_ability_ult_start", false, "nox_loop", "full"),
            ("nox_loop", "vampirebat_ability_ult_loop", true, "", "full"),
            ("hover.in_air", "vampirebat_in_air_loop_glide", true, "", "full"),   // the umbrella hover (MinaModel.LocoSet)
            ("hover.falling", "vampirebat_in_air_loop_glide", true, "", "full"),
            ("parry", "vampirebat_parry_to_idle", false, "locomotion", "full"),
            ("parry_air", "vampirebat_parry_to_idle", false, "in_air", "full"),
            ("death", "vampirebat_knockdown_large", false, "hold", "full"),
        };

        public static readonly HashSet<string> Locked = new HashSet<string>
        { "dash_ground", "dash_air_forward", "dash_air_back", "dash_air_left", "dash_air_right", "rake", "retreat_end", "nox", "nox_loop", "death" };

        /// <summary>Deadlock sounds (user 2026-10-12: I pick and wire them). Her pistol is layered as Deadlock's: first shot, main, then the tail.</summary>
        public static readonly DeadlockSound[] Sounds =
        {
            new DeadlockSound { Id = "mina_gun_first", VpkPrefix = "sounds/weapons/vampirebat/vampirebat_weapon_fire_first", Volume = 0.6f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.04f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "mina_gun", VpkPrefix = "sounds/weapons/vampirebat/vampirebat_weapon_fire_main", Volume = 0.55f, Range = 60f, MinGap = 0.05f, PitchJitter = 0.05f, VolumeJitter = 0.1f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "mina_gun_mech", VpkPrefix = "sounds/weapons/vampirebat/vampirebat_weapon_fire_mech", Volume = 0.4f, Range = 25f, MinGap = 0.05f },
            new DeadlockSound { Id = "mina_rake_cast", VpkPrefix = "sounds/abilities/vampirebat/a1_rake/vampirebat_rake_cast", Volume = 0.9f },
            new DeadlockSound { Id = "mina_rake_hit", VpkPrefix = "sounds/abilities/vampirebat/a1_rake/vampirebat_rake_hit_confirm", Volume = 0.9f, MinGap = 0.05f },
            new DeadlockSound { Id = "mina_rake_heal", VpkPrefix = "sounds/abilities/vampirebat/a1_rake/vampirebat_rake_kill_confirm_heal", Volume = 0.9f, MinGap = 0.1f },
            new DeadlockSound { Id = "mina_retreat_start", VpkPrefix = "sounds/abilities/vampirebat/a2_batblink/vampirebat_blink_start", Volume = 0.9f },
            new DeadlockSound { Id = "mina_retreat_loop", VpkPrefix = "sounds/abilities/vampirebat/a2_batblink/vampirebat_blink_travel_lp", Volume = 0.7f, Range = 30f },
            new DeadlockSound { Id = "mina_retreat_end", VpkPrefix = "sounds/abilities/vampirebat/a2_batblink/vampirebat_blink_end", Volume = 0.9f },
            new DeadlockSound { Id = "mina_retreat_end_final", VpkPrefix = "sounds/abilities/vampirebat/a2_batblink/vampirebat_blink_end_final", Volume = 0.9f },
            new DeadlockSound { Id = "mina_lovebites_buildup", VpkPrefix = "sounds/abilities/vampirebat/a3_lovebites/love_bites_buildup", Volume = 0.5f, Range = 30f, MinGap = 0.4f },
            new DeadlockSound { Id = "mina_lovebites_proc", VpkPrefix = "sounds/abilities/vampirebat/a3_lovebites/love_bites_proc", Volume = 0.9f, Range = 50f, MinGap = 0.05f },
            new DeadlockSound { Id = "mina_lovebites_gained", VpkPrefix = "sounds/abilities/vampirebat/a3_lovebites/love_bites_bats_gained", Volume = 0.8f, Range = 30f, MinGap = 0.1f },
            new DeadlockSound { Id = "mina_nox_cast", VpkPrefix = "sounds/abilities/vampirebat/a4_nox_nostra/vampirebat_nox_nostra_cast_delay", Range = 70f },
            new DeadlockSound { Id = "mina_nox_loop", VpkPrefix = "sounds/abilities/vampirebat/a4_nox_nostra/vampirebat_nox_nostra_channel_lp", Volume = 0.8f, Range = 70f },
            new DeadlockSound { Id = "mina_nox_end", VpkPrefix = "sounds/abilities/vampirebat/a4_nox_nostra/vampirebat_nox_nostra_channel_end", Range = 60f },
            new DeadlockSound { Id = "mina_bat_hit", VpkPrefix = "sounds/abilities/vampirebat/a4_nox_nostra/vampirebat_nox_nostra_impact", Volume = 0.4f, Range = 40f, MinGap = 0.06f, PitchJitter = 0.08f },
            new DeadlockSound { Id = "mina_bat_whizby", VpkPrefix = "sounds/abilities/vampirebat/a4_nox_nostra/vampirebat_nox_nostra_whizby", Volume = 0.3f, Range = 20f, MinGap = 0.15f },
            new DeadlockSound { Id = "mina_hover_start", VpkPrefix = "sounds/player/movement/mina/mina_umbrella_flight_start", Volume = 0.8f, Range = 30f, MinGap = 0.2f },
            new DeadlockSound { Id = "mina_hover_loop", VpkPrefix = "sounds/player/movement/mina/mina_umbrella_flight_lp", Volume = 0.6f, Range = 30f },
            new DeadlockSound { Id = "mina_hover_end", VpkPrefix = "sounds/player/movement/mina/mina_umbrella_flight_end", Volume = 0.8f, Range = 30f, MinGap = 0.2f },
            new DeadlockSound { Id = "mina_umbrella_open", VpkPrefix = "sounds/player/movement/mina/mina_umbrella_open", Volume = 0.8f, Range = 30f },
            new DeadlockSound { Id = "mina_umbrella_close", VpkPrefix = "sounds/player/movement/mina/mina_umbrella_close", Volume = 0.8f, Range = 30f },
            // VO by ability (Deadlock's signature order): 1 Rake, 2 Sanguine Retreat, 3 Love Bites, 4 Nox Nostra.
            new DeadlockSound { Id = "mina_vo_power1", VpkPrefix = "sounds/vo/vampirebat/vampirebat_use_power1", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_power2", VpkPrefix = "sounds/vo/vampirebat/vampirebat_use_power2", Chance = 0.5f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_power3", VpkPrefix = "sounds/vo/vampirebat/vampirebat_use_power3", Chance = 0.25f, MinGap = 6f, Voice = true },
            new DeadlockSound { Id = "mina_vo_power4", VpkPrefix = "sounds/vo/vampirebat/vampirebat_use_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_desperation_power1", VpkPrefix = "sounds/vo/vampirebat/vampirebat_desperation_power1", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_desperation_power2", VpkPrefix = "sounds/vo/vampirebat/vampirebat_desperation_power2", Chance = 0.7f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_desperation_power3", VpkPrefix = "sounds/vo/vampirebat/vampirebat_desperation_power3", Chance = 0.4f, MinGap = 6f, Voice = true },
            new DeadlockSound { Id = "mina_vo_desperation_power4", VpkPrefix = "sounds/vo/vampirebat/vampirebat_desperation_power4", Range = 60f, MinGap = 4f, Voice = true },
            new DeadlockSound { Id = "mina_vo_select", VpkPrefix = "sounds/vo/vampirebat/vampirebat_select", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "mina_vo_kill", VpkPrefix = "sounds/vo/vampirebat/vampirebat_kill_anyhero", Chance = 0.15f, MinGap = 8f, Voice = true },
            new DeadlockSound { Id = "mina_vo_killstreak", VpkPrefix = "sounds/vo/vampirebat/vampirebat_killstreak_start", MinGap = 30f, Voice = true },
            new DeadlockSound { Id = "mina_vo_pain", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_pain_big", Volume = 0.9f, Chance = 0.35f, MinGap = 3f, Voice = true },
            new DeadlockSound { Id = "mina_vo_pain_small", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_pain_small", Volume = 0.8f, Chance = 0.25f, MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "mina_vo_low_health", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_pain_low_health_warning", MinGap = 20f, Voice = true },
            new DeadlockSound { Id = "mina_vo_death", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_pain_death", MinGap = 2f, Voice = true },
            new DeadlockSound { Id = "mina_vo_effort", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_effort_dash", Volume = 0.8f, Chance = 0.5f, MinGap = 0.5f, Voice = true },
            new DeadlockSound { Id = "mina_vo_efforts", VpkPrefix = "sounds/vo/vampirebat/emote/vampirebat_effort_general", Volume = 0.7f, Chance = 0.4f, MinGap = 1f, Voice = true },
            // Movement: her own foley (Deadlock files it under "mina"), the shared jump/slide sounds.
            new DeadlockSound { Id = "mina_footstep", VpkPrefix = "sounds/player/footsteps/mina/mina_footstep_sprint", Volume = 0.25f, Range = 20f, MinGap = 0.12f },
            new DeadlockSound { Id = "mina_land", VpkPrefix = "sounds/player/footsteps/mina/mina_footstep_jumpland", Volume = 0.7f, Range = 25f, MinGap = 0.2f },
            new DeadlockSound { Id = "mina_dash", VpkPrefix = "sounds/player/movement/mina/mina_foley_cloth_mvmt_short", Volume = 0.8f, Range = 25f, MinGap = 0.1f },
            new DeadlockSound { Id = "mina_slide", VpkPrefix = "sounds/gameplay/slide/enter", Volume = 0.8f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "mina_slide_loop", VpkPrefix = "sounds/gameplay/slide/lp2_texture", Volume = 0.6f, Range = 20f },
            new DeadlockSound { Id = "mina_slide_exit", VpkPrefix = "sounds/gameplay/slide/exit", Volume = 0.7f, Range = 20f, MinGap = 0.2f },
            new DeadlockSound { Id = "mina_jump", VpkPrefix = "sounds/player/movement/generic/generic_jump_start", Volume = 0.5f, Range = 20f },
            new DeadlockSound { Id = "mina_wall_jump", VpkPrefix = "sounds/player/wall_jump", Volume = 0.9f, Range = 25f },
        };
    }
}
