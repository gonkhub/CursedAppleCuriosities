using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Framework;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Ivy's numbers (Deadlock's hero_tengu: model heroes_wip/ivy, clips heroes_staging/tengu/tengu_v2; citadel_weapon_tengu_set
    /// "Protector's Hand"; citadel_ability_tengu_urn / tangotether / tengu_stone_form / tengu_airlift; 2026-10-06 vdata). The user asked
    /// me to design her kit (2026-10-14): Deadlock's abilities, fitted to RoR2 (few allies, many enemies).
    /// </summary>
    public static class IvyData
    {
        public const string Hero = "ivy";
        public const string Section = "Ivy";

        // ---- survivor (Deadlock 755 health, 2 regen, 7.2 m/s, 4 stamina) ----
        public static float BaseHealth = 100f;
        public const float HealthPerLevel = 30f;
        public const float BaseSpeed = 7.2f;
        public const float BaseRegen = 2f;
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = IvyModel.Play,
            StaminaPips = 4,                 // Deadlock EStamina (like Vyper's, Deadlock's own)
            OutOfCombatSpeed = 1.6f / 7.2f,
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Protector's Hand (4.45 a bullet every 0.0735 s, falloff 787 -> 2264 hu to 10%) ----
        public static float GunDamage = 0.24f;
        public static float GunCycle = 0.0735f;
        public const float GunSpread = 0.8f;
        public const float GunFalloffStart = 20f, GunFalloffEnd = 57.5f, GunFalloffMin = 0.1f;

        // ---- secondary: Kudzu Bomb (citadel_ability_tengu_urn: hold to aim, an arcing bomb (1800 hu/s, x1.5 gravity), a 6 m patch for 4 s, 40 DPS, 28% slow, 32 s) ----
        public static float ThornsDps = 2.5f;
        public static float ThornsDuration = 4f;
        public static float ThornsRadius = 6f;
        public const float ThornsTick = 0.25f;
        public const float ThornsSlow = 0.28f;
        public const float ThornsThrowSpeed = 45.7f;    // Deadlock m_flBulletSpeed 1800 hu/s
        public const float ThornsThrowUp = 10f;         // Deadlock m_flVerticalAimBias
        public static float ThornsCooldown = 8f;
        public const float ThornsMaxAim = 10f;          // seconds she can hold the aim before it throws by itself

        // ---- passive: Kudzu Connection (citadel_ability_tangotether: 16 m, +10% fire rate, 15% lifesteal, 35% shared healing; half the
        // bonuses with nobody connected). A passive, always bound to the nearest ally (user 2.5): players, drones, turrets, minions. ----
        public static float TetherRange = 16f;
        public static float TetherAttackSpeed = 0.1f;
        public static float TetherLifesteal = 0.15f;
        public static float TetherSharedHeal = 0.35f;
        public const float TetherBreakRange = 30f;

        // ---- utility: Stone Form (citadel_ability_tengu_stone_form: lift 180 hu in 1 s, slam: 75 damage in 5.75 m, 0.75 s stun, heals, 40 s) ----
        public static float StoneDamage = 4f;
        public static float StoneRadius = 5.75f;
        public const float StoneStun = 0.75f;
        public const float StoneCastDelay = 0.25f;       // Deadlock AbilityCastDelay: she hangs a moment as she turns to stone
        public const float StoneLift = 4.6f, StoneLiftTime = 1f; // Deadlock LiftHeight 180 hu over LiftTime 1 s (eased: fast, then slowing)
        public const float StoneFallAccel = 75f;         // m/s^2 down: a falling statue (Deadlock's drag modifier pulls her down hard)
        public const float StoneFallMax = 70f;
        public const float StoneSteer = 8f;              // Deadlock MoveSpeedMax 8 m
        public const float StoneDamping = 0.25f;         // Deadlock DampingFactor: the momentum she keeps going in
        public static float StoneHeal = 0.06f;           // max health per second while stone (Deadlock MaxHealthRegen 6)
        public const float StoneHoldAfterLanding = 0.5f; // a statue this long after the slam, then she breaks out (user 2.5: 3 s was much too long)
        public static float StoneCooldown = 10f;

        // ---- special: Air Drop (citadel_ability_tengu_airlift: fly 10-22 m/s carrying a bomb, drop it: 115 damage in 16-20 m, +20% damage 8 s, 100 s) ----
        public static float AirDropDamage = 10f;
        public static float AirDropRadius = 24f;         // user 2.5: bigger (Deadlock 16-20 m)
        public static float AirDropSeconds = 8f;         // most time in the air before the bomb drops by itself
        public const float FlySpeed = 10f, FlyFastSpeed = 22f; // Deadlock m_flTargetAirSpeedBase / Fast (393.7 / 866 hu/s)
        public const float FlyAccel = 9f;                // Deadlock m_flAcceleration
        public const float FlyClimb = 8f;                // m/s up at take-off
        public const float BombSpeed = 30f;              // Deadlock m_flBombReleaseSpeed 1200 hu/s
        public const float BombToss = 38f;               // user 2.5: tossed much farther, along her aim, on top of her own speed
        public static float AirDropBuff = 0.2f;          // Deadlock AirDropOutgoingDamagePercent
        public const float AirDropBuffSeconds = 8f;      // Deadlock BuffDuration
        public static float AirDropCooldown = 30f;

        public static void Bind(ConfigFile c)
        {
            Movement.Bind(c, Section);
            BaseHealth = c.Bind(Section, "Base Health", BaseHealth, "Ivy max health at level 1 (+30 per level).").Value;
            GunDamage = c.Bind(Section, "Gun - Damage", GunDamage, "Damage coefficient per bullet.").Value;
            ThornsDps = c.Bind(Section, "Kudzu Bomb - Damage Per Second", ThornsDps, "Damage coefficient per second in the patch.").Value;
            ThornsDuration = c.Bind(Section, "Kudzu Bomb - Duration", ThornsDuration, "Seconds.").Value;
            ThornsRadius = c.Bind(Section, "Kudzu Bomb - Radius", ThornsRadius, "Metres.").Value;
            ThornsCooldown = c.Bind(Section, "Kudzu Bomb - Cooldown", ThornsCooldown, "Seconds.").Value;
            TetherAttackSpeed = c.Bind(Section, "Kudzu Connection - Attack Speed", TetherAttackSpeed, "Attack speed bonus for both (half alone).").Value;
            TetherLifesteal = c.Bind(Section, "Kudzu Connection - Lifesteal", TetherLifesteal, "Share of damage dealt healed, for both (half alone).").Value;
            TetherSharedHeal = c.Bind(Section, "Kudzu Connection - Shared Healing", TetherSharedHeal, "Share of healing one receives that the other gets too.").Value;
            StoneDamage = c.Bind(Section, "Stone Form - Damage", StoneDamage, "Damage coefficient of the slam.").Value;
            StoneHeal = c.Bind(Section, "Stone Form - Heal", StoneHeal, "Share of max health healed per second while stone.").Value;
            StoneCooldown = c.Bind(Section, "Stone Form - Cooldown", StoneCooldown, "Seconds.").Value;
            AirDropDamage = c.Bind(Section, "Air Drop - Damage", AirDropDamage, "Damage coefficient of the bomb.").Value;
            AirDropRadius = c.Bind(Section, "Air Drop - Radius", AirDropRadius, "Metres.").Value;
            AirDropCooldown = c.Bind(Section, "Air Drop - Cooldown", AirDropCooldown, "Seconds.").Value;
        }

        // ---- Deadlock assets ----
        public const string DlModel = "models/heroes_wip/ivy/ivy.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f;
        public const string MuzzleBone = "muzzle_fx";
        public const string IconPortrait = "panorama/images/heroes/tengu_sm_psd.vtex_c";
        public const string IconThorns = "panorama/images/hud/abilities/tengu/tengu_storm_flask_psd.vtex_c";
        public const string IconTether = "panorama/images/hud/abilities/tengu/tengu_tether_psd.vtex_c";
        // Her Stone Form statue: its own model with its own clip (the gargoyle pose), as Deadlock's tengu_stone_form_model particle shows it.
        public const string StatueKey = "ivy_statue";
        public const string DlStatueModel = "models/particle/tengu_stone_form_model_fx.vmdl_c";
        public const string StatueClip = "ability_stone_form";
        public const string IconStone = "panorama/images/hud/abilities/tengu/tengu_stone_form_psd.vtex_c";
        public const string IconAirDrop = "panorama/images/hud/abilities/tengu/tengu_lightning_crash_psd.vtex_c";

        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims = FrameworkAnims.Table(
            new FrameworkAnims.Names { Idle = "weapon_idle", Falling = "in_air_loop_apex", Land = "land_impact_idle", DashGround = "ground_dash", DashAir = "air_dash_", Shoot = "weapon_shoot" },
            ("cast", "generic_cast_start", false, "locomotion", "full"),
            ("stone", "stone_form_cast", false, "hold", "full"),
            ("stone_air", "stone_form_cast_in_air", false, "hold", "full"),
            ("stone_hold", "ability_stone_form", true, "", "full"),       // the statue pose (Deadlock's ability_stone_form)
            ("fly_start", "falling_grace_intro", false, "fly", "full"),
            ("fly", "falling_grace_level_flight", true, "", "full"),
            ("fly_drop", "falling_grace_release", false, "in_air", "full"));

        public static readonly HashSet<string> Locked = FrameworkAnims.Locked("stone", "stone_air", "stone_hold", "fly_start", "fly", "fly_drop");

        public static readonly DeadlockSound[] Sounds = BuildSounds();

        private static DeadlockSound[] BuildSounds()
        {
            var l = FrameworkAnims.CommonSounds(Hero, "tengu", "sounds/player/footsteps/ivy/ivy_footstep", "sounds/player/movement/ivy/ivy_wingflap_small", "sounds/player/movement/ivy/ivy_wingflap");
            void Add(string id, string path, float volume = 0.9f, float range = 40f, float gap = 0.1f, string? fallback = null) =>
                l.Add(new DeadlockSound { Id = "ivy_" + id, VpkPrefix = path, Volume = volume, Range = range, MinGap = gap, Fallback = fallback });
            Add("gun_first", "sounds/weapons/tengu/ivy_weapon_fire_first", 0.55f, 60f, 0.03f, "Play_commando_M1");
            Add("gun", "sounds/weapons/tengu/ivy_weapon_fire_mid", 0.45f, 60f, 0.03f, "Play_commando_M1");
            Add("gun_tail", "sounds/weapons/tengu/ivy_weapon_fire_tail", 0.5f, 50f);
            Add("gun_release", "sounds/weapons/tengu/ivy_weapon_fire_release", 0.5f, 40f);
            // Kudzu Bomb is "stone squall" in Deadlock's files (tengu_stone_squall_* particles, Ivy.Kudzu.Bomb.* sound events).
            Add("bomb_select", "sounds/abilities/tengu/ivy_stonessquall_prep_01", 0.8f);
            Add("bomb_aim_loop", "sounds/abilities/tengu/ivy_stonessquall_prep_loop", 0.5f, 25f);
            Add("bomb_throw", "sounds/abilities/tengu/ivy_stonessquall_throw", 0.9f, 50f);
            Add("bomb_proj_loop", "sounds/abilities/tengu/ivy_stonessquall_projectile_loop", 0.6f, 40f);
            Add("bomb_impact", "sounds/abilities/tengu/ivy_stonessquall_aoe_impact", 1f, 60f);
            Add("bomb_aoe_loop", "sounds/abilities/tengu/ivy_stonessquall_aoe_loop", 0.55f, 40f);
            Add("thorns_end", "sounds/abilities/tengu/ivy_a1_vines_retract", 0.8f);
            Add("tether_cast", "sounds/abilities/tengu/ivy_tether_cast");
            Add("tether_attach", "sounds/abilities/tengu/ivy_tether_attach");
            Add("tether_loop", "sounds/abilities/tengu/ivy_tether_attached_lp", 0.5f, 30f);
            Add("tether_end", "sounds/abilities/tengu/ivy_tether_buff_expire", 0.8f);
            Add("stone_cast", "sounds/abilities/tengu/a3_stone_form/ivy_a3_stone_form_cast");
            Add("stone_impact", "sounds/abilities/tengu/a3_stone_form/ivy_a3_stone_form_impact", 1f, 70f);
            Add("stone_cast_layer", "sounds/abilities/tengu/a3_stone_form/cast_lyr", 0.8f);
            Add("stone_explosion", "sounds/abilities/tengu/a3_stone_form/explosion", 1f, 80f);
            Add("stone_hit", "sounds/abilities/tengu/ivy_stoneform_hit", 0.8f, 50f, 0.05f);
            Add("airdrop_cast", "sounds/abilities/tengu/ivy_airdrop_cast", 1f, 60f);
            Add("airdrop_loop", "sounds/abilities/tengu/ivy_airdrop_lp", 0.6f, 60f);
            Add("airdrop_launch", "sounds/abilities/tengu/ivy_airdrop_bomb_launch", 1f, 60f);
            Add("airdrop_fall", "sounds/abilities/tengu/ivy_airdrop_bomb_proj_lp", 0.7f, 50f);
            Add("airdrop_explode", "sounds/abilities/tengu/ivy_airdrop_bomb_explode", 1f, 100f);
            Add("airdrop_end", "sounds/abilities/tengu/ivy_airdrop_expire", 0.8f, 50f);
            return l.ToArray();
        }
    }
}
