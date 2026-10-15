using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Framework;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// Bebop's numbers (Deadlock's hero_bebop / citadel_weapon_bebop_set "Humble Pie" / citadel_ability_uppercut, _sticky_bomb, _hook,
    /// _bebop_laser_beam, read from the 2026-10-06 vdata). Deadlock values are kept where RoR2 has the same scale; damage becomes RoR2
    /// coefficients. The kit is the user's (2026-10-14).
    /// </summary>
    public static class BebopData
    {
        public const string Hero = "bebop";
        public const string Section = "Bebop";

        // ---- survivor: a big robot (Deadlock 880 health, 2.5 regen, 6.45 m/s, 3 stamina) ----
        public static float BaseHealth = 115f;
        public const float HealthPerLevel = 34f;
        public const float BaseArmor = 0f;
        public const float BaseDamage = 12f;
        public const float DamagePerLevel = 2.4f;
        public const float BaseSpeed = 6.45f;
        public const float BaseRegen = 2.5f;
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = BebopModel.Play,
            StaminaPips = 3,
            OutOfCombatSpeed = 1.6f / 6.45f,
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Humble Pie, a continuous beam (EBulletHandler_Beam: no travel; 4.98 a tick every 0.084 s; 1259.84 hu reach;
        // 0.3 s wind-up, 0.05 s when picked straight back up; falloff 787 -> 2000 hu to 10%) ----
        public static float BeamDamage = 0.28f;          // per tick (Deadlock 59 DPS, about the other guns' damage per second)
        public static float BeamTick = 0.084f;           // Deadlock m_flCycleTime
        public static float BeamProc = 0.6f;             // 12 ticks a second
        public static float BeamRange = 32f;             // Deadlock m_flRange 1259.84 hu
        public const float BeamWindUp = 0.3f;            // Deadlock WindUpTime
        public const float BeamQuickWindUp = 0.05f;      // Deadlock NoWindUpTime: firing again soon after letting go
        public const float BeamQuickWindow = 0.6f;       // how soon counts as "straight back"
        public const float BeamFalloffStart = 20f, BeamFalloffEnd = 50.8f, BeamFalloffMin = 0.1f;
        public const float BeamRadius = 0.3f;

        // ---- secondary: Sticky Bomb (citadel_ability_sticky_bomb: cast 6 m, 3.5 s fuse, 8 m blast, 18 s; +1% per hero hit / +2.5% per kill).
        // User: Seven's Static Charge flow with a very short reach and no stun; 400%; every enemy damaged +1% for good, a kill +5%, elites
        // and minibosses x1.5; the carrier dying sets it off at once; all of it lost when Bebop dies. ----
        public static float StickyDamage = 4f;
        public static float StickyRange = 6f;            // Deadlock AbilityCastRange
        public const float StickyConeDeg = 45f;          // Deadlock m_flTargetingConeAngle 90 (whole cone)
        public static float StickyFuse = 3.5f;           // Deadlock FuseTime
        public static float StickyRadius = 8f;           // Deadlock Radius
        public static float StickyCooldown = 6f;         // Deadlock 18
        public static float StickyPerHit = 1f;           // % per enemy damaged (user)
        public static float StickyPerKill = 5f;          // % per kill (user)
        public const float StickyEliteScale = 1.5f;      // elites and minibosses (user)

        // ---- secondary alt: Grapple Arm / hook (citadel_ability_hook: 30 m, 2850 hu/s, 33 hu wide, 23 s). User: pulls in the first
        // enemy it hits, which then takes more from his primary; press again to cancel; works with Backup Magazine (charges). ----
        public static float HookRange = 30f;             // Deadlock AbilityCastRange
        public const float HookSpeed = 72.4f;            // Deadlock m_flBulletSpeed 2850 hu/s
        public const float HookRadius = 0.84f;           // Deadlock m_flBulletRadius 33 hu
        public const float HookReturnSpeed = 50.8f;      // Deadlock m_flReturnSpeed 2000 hu/s
        public static float HookDamage = 2f;             // the latch (Deadlock Damage scales 0.7 with spirit)
        public static float HookAmp = 0.5f;              // +50% primary damage to the hooked enemy (user; Deadlock's BulletAmp upgrade)
        public static float HookAmpSeconds = 6f;
        public const float HookPullSpeed = 30f;          // m/s the hooked enemy is reeled in
        public const float HookPullStop = 3f;            // metres from Bebop where the pull ends
        public static float HookCooldown = 8f;           // Deadlock 23

        // ---- utility: Exploding Uppercut (citadel_ability_uppercut: 60 deg half-cone, 6 m, toss 25 m/s, landing 75 damage in 14 m, 22 s).
        // User: a wide cone that launches whole groups along his look direction and pitch; landing blasts; landing an uppercut (once a
        // cast) gives a primary damage buff and heals missing health. ----
        public static float UppercutDamage = 3f;
        public static float UppercutLandDamage = 4f;
        public const float UppercutHalfAngle = 60f;      // Deadlock MeleeHalfAngle
        public static float UppercutReach = 6f;          // Deadlock MeleeAttackLength
        public const float UppercutToss = 25f;           // Deadlock TossVelocity
        public const float UppercutMinPitch = 30f, UppercutMaxPitch = 80f, UppercutLift = 40f; // launch angle = look pitch + lift, clamped
        public static float UppercutLandRadius = 8f;     // Deadlock OnLandDamageRadius 14 m: smaller, RoR2 packs are tighter
        public static float UppercutBuffDamage = 0.4f;   // +40% primary damage...
        public static float UppercutBuffSeconds = 6f;    // ...for this long (Deadlock ExplodeDebuffDuration 5)
        public static float UppercutHeal = 0.3f;         // share of missing health healed
        public static float UppercutCooldown = 8f;       // Deadlock 22

        // ---- special: Hyper Beam (citadel_ability_bebop_laser_beam: 1 s cast, 11 s channel, 160 DPS, 70 m x 2.9 m, ticks 0.1 s, turns 55 deg/s,
        // hovers (FallSpeedMax 1), 1.8 m/s, 20% slow, bosses x0.5, 120 s) ----
        public static float HyperDps = 6f;               // damage coefficient per second
        public const float HyperTick = 0.1f;             // Deadlock Interval
        public static float HyperDuration = 11f;         // Deadlock AbilityChannelTime
        public const float HyperCast = 1f;               // Deadlock AbilityCastDelay
        public static float HyperLength = 70f;           // Deadlock BeamLength
        public const float HyperRadius = 2.9f;           // Deadlock BeamWidth
        public const float HyperTurn = 55f;              // Deadlock TrackingSpeed (deg/s)
        public const float HyperMove = 1.8f;             // Deadlock ChannelMoveSpeed
        public const float HyperFall = 1f;               // Deadlock FallSpeedMax
        public const float HyperSlow = 0.2f, HyperSlowSeconds = 0.5f;
        public const float HyperBossScale = 0.5f;        // Deadlock m_flBossDamageScale
        public const float HyperProc = 0.5f;
        public const float HyperCancelAfter = 0.5f;
        public static float HyperCooldown = 40f;         // Deadlock 120

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Bebop max health at level 1 (+34 per level).").Value;
            BeamDamage = c.Bind(Section, "Beam - Damage Per Tick", BeamDamage, "Damage coefficient of each beam tick.").Value;
            BeamTick = c.Bind(Section, "Beam - Tick Interval", BeamTick, "Seconds between beam ticks before attack speed.").Value;
            BeamProc = c.Bind(Section, "Beam - Proc Coefficient", BeamProc, "Proc coefficient of each tick.").Value;
            BeamRange = c.Bind(Section, "Beam - Range", BeamRange, "Metres.").Value;
            StickyDamage = c.Bind(Section, "Sticky Bomb - Damage", StickyDamage, "Damage coefficient of the blast, before its stacks.").Value;
            StickyRange = c.Bind(Section, "Sticky Bomb - Range", StickyRange, "Metres to stick it on an enemy.").Value;
            StickyFuse = c.Bind(Section, "Sticky Bomb - Fuse", StickyFuse, "Seconds before it blows.").Value;
            StickyRadius = c.Bind(Section, "Sticky Bomb - Radius", StickyRadius, "Metres.").Value;
            StickyPerHit = c.Bind(Section, "Sticky Bomb - Bonus Per Hit", StickyPerHit, "Permanent % damage per enemy the blast damages (lost on death).").Value;
            StickyPerKill = c.Bind(Section, "Sticky Bomb - Bonus Per Kill", StickyPerKill, "Permanent % damage per enemy the blast kills (lost on death).").Value;
            StickyCooldown = c.Bind(Section, "Sticky Bomb - Cooldown", StickyCooldown, "Seconds.").Value;
            HookRange = c.Bind(Section, "Hook - Range", HookRange, "Metres.").Value;
            HookAmp = c.Bind(Section, "Hook - Primary Damage Bonus", HookAmp, "Extra beam damage to the hooked enemy (0.5 = +50%).").Value;
            HookAmpSeconds = c.Bind(Section, "Hook - Bonus Duration", HookAmpSeconds, "Seconds.").Value;
            HookCooldown = c.Bind(Section, "Hook - Cooldown", HookCooldown, "Seconds.").Value;
            UppercutDamage = c.Bind(Section, "Uppercut - Damage", UppercutDamage, "Damage coefficient of the punch.").Value;
            UppercutLandDamage = c.Bind(Section, "Uppercut - Landing Damage", UppercutLandDamage, "Damage coefficient of each landing blast.").Value;
            UppercutLandRadius = c.Bind(Section, "Uppercut - Landing Radius", UppercutLandRadius, "Metres.").Value;
            UppercutBuffDamage = c.Bind(Section, "Uppercut - Primary Damage Bonus", UppercutBuffDamage, "Beam damage bonus after landing an uppercut.").Value;
            UppercutHeal = c.Bind(Section, "Uppercut - Missing Health Heal", UppercutHeal, "Share of missing health healed after landing an uppercut.").Value;
            UppercutCooldown = c.Bind(Section, "Uppercut - Cooldown", UppercutCooldown, "Seconds.").Value;
            HyperDps = c.Bind(Section, "Hyper Beam - Damage Per Second", HyperDps, "Damage coefficient per second (bosses: half).").Value;
            HyperDuration = c.Bind(Section, "Hyper Beam - Duration", HyperDuration, "Seconds of beam.").Value;
            HyperLength = c.Bind(Section, "Hyper Beam - Length", HyperLength, "Metres.").Value;
            HyperCooldown = c.Bind(Section, "Hyper Beam - Cooldown", HyperCooldown, "Seconds.").Value;
        }

        // ---- Deadlock assets (read from the player's own install, never shipped) ----

        public const string DlModel = "models/heroes_staging/bebop/bebop.vmdl_c";
        public const float ModelHeightScale = 1.2f * 1.4f * 0.95f; // almost as tall as Abrams (user 2.5; Abrams 1.2 x 1.5 x 0.95). Visual only: hitbox unchanged
        public const string MuzzleBone = "muzzle_fx";

        public const string IconPortrait = "panorama/images/heroes/bebop_sm_psd.vtex_c";
        public const string IconUppercut = "panorama/images/hud/abilities/bebop/bebop_uppercut_psd.vtex_c";
        public const string IconSticky = "panorama/images/hud/abilities/bebop/bebop_sticky_bomb_psd.vtex_c";
        public const string IconHook = "panorama/images/hud/abilities/bebop/bebop_hook_psd.vtex_c";
        public const string IconHyper = "panorama/images/hud/abilities/bebop/bebop_hyper_beam_psd.vtex_c";

        /// <summary>
        /// State, Deadlock clip, loop, next state, layer (heroes_staging/bebop/clips). The arm clips (weapon_arm_*) and the hyperbeam set
        /// are his own; hook_cast/throw are overlays so he keeps moving.
        /// </summary>
        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims = FrameworkAnims.Table(
            new FrameworkAnims.Names { InAir = "in_air_loop_apex", Falling = "in_air_down", Land = "landing_impact", Death = "knockdown_large_intro" },
            ("uppercut", "ability_uppercut", false, "locomotion", "full"),
            ("sticky", "throw", false, "locomotion", "delta"),
            ("hook", "ability_hook_cast", false, "locomotion", "delta"),
            ("hyper_charge", "hyperbeam_charge", false, "hyper_fire", "full"),
            ("hyper_fire", "hyperbeam_fire_loop", true, "", "full"),
            ("hyper_end", "hyperbeam_fire", false, "locomotion", "full"));

        public static readonly HashSet<string> Locked = FrameworkAnims.Locked("uppercut", "hyper_charge", "hyper_fire", "hyper_end");

        // Before Sounds: static fields initialise in order, and BuildSounds reads this.
        public static readonly string[] Jazz = { "a", "b", "c", "d", "e", "f", "g", "h" };

        /// <summary>Deadlock sounds (user 2026-10-12: I pick and wire them). Humble Pie layers its beam loop with one of eight jazz loops.</summary>
        public static readonly DeadlockSound[] Sounds = BuildSounds();

        private static DeadlockSound[] BuildSounds()
        {
            var l = FrameworkAnims.CommonSounds(Hero, "bebop", "sounds/player/footsteps/bebop/bebop_footstep", "sounds/player/movement/bebop/bebop_jumpland", "sounds/player/movement/bebop/bebop_foley");
            void Add(string id, string path, float volume = 0.9f, float range = 40f, float gap = 0.1f, string? fallback = null) =>
                l.Add(new DeadlockSound { Id = "bebop_" + id, VpkPrefix = path, Volume = volume, Range = range, MinGap = gap, Fallback = fallback });
            Add("beam_windup", "sounds/weapons/bebop/bebop_wpn_windup", 0.7f, 40f);
            Add("beam_start", "sounds/weapons/bebop/bebop_primaryweapon_start", 0.7f, 60f, 0.1f, "Play_engi_R_turret_beam_start");
            Add("beam_loop", "sounds/weapons/bebop/bebop_primaryweapon_loop", 0.55f, 60f);
            Add("beam_end", "sounds/weapons/bebop/bebop_primaryweapon_end", 0.7f, 60f);
            foreach (string j in Jazz)
            {
                Add("beam_jazz_" + j, $"sounds/weapons/bebop/bebop_primaryweapon_jazz_{j}_loop", 0.35f, 40f);
                Add("beam_jazz_end_" + j, $"sounds/weapons/bebop/bebop_primaryweapon_jazz_{j}_end", 0.35f, 40f);
            }
            Add("beam_impact", "sounds/weapons/bebop/impact/lp1", 0.35f, 30f);
            Add("sticky_cast", "sounds/abilities/bebop/bebop_stickybomb_prepare");
            Add("sticky_attach", "sounds/abilities/bebop/bebop_stickybomb_attach");
            Add("sticky_alarm", "sounds/abilities/bebop/bebop_stickybomb_alarm_loop", 0.7f, 40f);
            Add("sticky_urgent", "sounds/abilities/bebop/bebop_stickybomb_alarm_urgent", 0.8f, 40f);
            Add("sticky_explode", "sounds/abilities/bebop/a2_sticky_bomb/explode", 1f, 80f);
            Add("hook_shoot", "sounds/abilities/bebop/bebop_hook_shoot");
            Add("hook_loop", "sounds/abilities/bebop/a3_hook/proj_mono_lp", 0.6f, 40f);
            Add("hook_hit", "sounds/abilities/bebop/a3_hook/impact_success", 1f, 60f);
            Add("hook_latched", "sounds/abilities/bebop/bebop_hook_latched_victim", 0.9f, 50f);
            Add("hook_yank", "sounds/abilities/bebop/bebop_hook_yank", 0.9f, 50f);
            Add("hook_reel", "sounds/abilities/bebop/bebop_hook_retracting_lp", 0.6f, 40f);
            Add("hook_return", "sounds/abilities/bebop/bebop_hook_retract_end", 0.8f, 40f);
            Add("hook_wall", "sounds/abilities/bebop/a3_hook/impact_geo", 0.8f, 40f);
            Add("hook_miss", "sounds/abilities/bebop/a3_hook/miss", 0.8f, 40f);
            Add("uppercut", "sounds/abilities/bebop/bebop_uppercut", 1f, 50f);
            Add("uppercut_hit", "sounds/abilities/bebop/bebop_uppercut_hit_confirm", 1f, 50f);
            Add("uppercut_fly", "sounds/abilities/bebop/bebop_uppercut_travel", 0.7f, 40f);
            Add("uppercut_land", "sounds/abilities/bebop/bebop_uppercut_explode_hit", 1f, 60f, 0.05f);
            Add("hyper_prep", "sounds/abilities/bebop/bebop_hyperbeam_prepstart", 1f, 80f);
            Add("hyper_prep_jazz", "sounds/abilities/bebop/bebop_hyperbeam_prepare_jazz", 0.6f, 80f);
            Add("hyper_launch", "sounds/abilities/bebop/bebop_hyperbeam_launch", 1f, 100f);
            Add("hyper_launch_jazz", "sounds/abilities/bebop/bebop_hyperbeam_launch_jazz", 0.6f, 100f);
            Add("hyper_loop", "sounds/abilities/bebop/bebop_hyperbeam_loop", 0.8f, 100f);
            Add("hyper_end", "sounds/abilities/bebop/bebop_hyperbeam_end", 1f, 100f);
            Add("hyper_end_jazz", "sounds/abilities/bebop/bebop_hyperbeam_end_jazz", 0.6f, 100f);
            Add("hyper_hit", "sounds/abilities/bebop/a4_hyper_beam/hit", 0.4f, 50f, 0.15f);
            return l.ToArray();
        }
    }
}
