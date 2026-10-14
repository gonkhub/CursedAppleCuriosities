using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Framework
{
    /// <summary>
    /// Bebop, Ivy and Apollo, framed (user, 2026-10-14: build their frameworks now, design the kits later). Numbers are Deadlock's
    /// (2026-10-06 vdata): hero_bebop, hero_tengu (Ivy), hero_fencer (Apollo). Health is Deadlock's over about 7.5; guns are
    /// Deadlock's cycle times with damage set near the other heroes' damage per second.
    /// </summary>
    public static class NextHeroes
    {
        // ---------------- Bebop (hero_bebop: 880 health, 2.5 regen, 6.45 m/s, 3 stamina) ----------------
        public static readonly FrameworkHero Bebop = Make(new FrameworkHero
        {
            Hero = "bebop", Name = "Bebop", Vo = "bebop", Subtitle = "The Wrecking Ball", SortPosition = 111f,
            Description = "Bebop pulls enemies in, launches them up and blasts them away.",
            Color = new Color(0.85f, 0.55f, 0.15f),
            BaseHealth = 115f, HealthPerLevel = 34f, BaseSpeed = 6.45f, BaseRegen = 2.5f,
            // citadel_weapon_bebop_set: 4.98 a bullet every 0.084 s, falloff 787 -> 2000 hu to 10%
            GunDamage = 0.28f, GunCycle = 0.084f, GunSpread = 0.8f, FalloffStart = 20f, FalloffEnd = 50.8f,
            GunSound = "bebop_gun",
            IconPortrait = "panorama/images/heroes/bebop_sm_psd.vtex_c",
            Abilities = new[]
            {
                new FrameworkAbility { Name = "Exploding Uppercut", Cooldown = 8f, Anim = "uppercut", Sound = "bebop_uppercut", Vo = "bebop_vo_power1",
                    Icon = "panorama/images/hud/abilities/bebop/bebop_uppercut_psd.vtex_c", Deadlock = "Deadlock: knock nearby enemies into the air; they explode on landing, hurting enemies around them (22 s)." },
                new FrameworkAbility { Name = "Sticky Bomb", Cooldown = 8f, Anim = "throw", Sound = "bebop_sticky_cast", Vo = "bebop_vo_power2",
                    Icon = "panorama/images/hud/abilities/bebop/bebop_sticky_bomb_psd.vtex_c", Deadlock = "Deadlock: attach a bomb that explodes after a delay; hero hits grow its damage for good (18 s)." },
                new FrameworkAbility { Name = "Grapple Arm", Cooldown = 8f, Anim = "hook", Sound = "bebop_hook_shoot", Vo = "bebop_vo_power3",
                    Icon = "panorama/images/hud/abilities/bebop/bebop_hook_psd.vtex_c", Deadlock = "Deadlock: launch a mechanical hand that hooks the first character it hits and reels them in (23 s)." },
                new FrameworkAbility { Name = "Hyper Beam", Cooldown = 40f, Anim = "hyperbeam", Sound = "bebop_hyperbeam_start", Vo = "bebop_vo_power4",
                    Icon = "panorama/images/hud/abilities/bebop/bebop_hyper_beam_psd.vtex_c", Deadlock = "Deadlock: channel a powerful beam of energy (120 s)." },
            },
            Def = new DeadlockHeroDef
            {
                DlModel = "models/heroes_staging/bebop/bebop.vmdl_c",
                ModelHeightScale = 1.2f * 1.15f * 0.95f, // a big robot, Viscous-sized
                Anims = FrameworkAnims.Table(new FrameworkAnims.Names { InAir = "in_air_loop_apex", Falling = "in_air_down", Land = "landing_impact", Death = "knockdown_large_intro" },
                    ("uppercut", "ability_uppercut", false, "locomotion", "full"),
                    ("throw", "throw", false, "locomotion", "delta"),
                    ("hook", "ability_hook_cast", false, "locomotion", "delta"),
                    ("hyperbeam", "hyperbeam_charge", false, "locomotion", "full")),
                RootLocked = FrameworkAnims.Locked("uppercut"),
            },
            Movement = new DeadlockMovementProfile { Hero = "bebop", StaminaPips = 3, OutOfCombatSpeed = 1.6f / 6.45f, WallJumpUp = 22f, WallJumpPowerMin = 0.6f },
        }, "sounds/player/footsteps/bebop/bebop_footstep", "sounds/player/movement/bebop/bebop_jumpland", "sounds/player/movement/bebop/bebop_foley", false,
            new DeadlockSound { Id = "bebop_gun", VpkPrefix = "sounds/weapons/bebop/bebop_primaryweapon_start", Volume = 0.5f, Range = 60f, MinGap = 0.25f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "bebop_uppercut", VpkPrefix = "sounds/abilities/bebop/bebop_uppercut", Volume = 0.9f },
            new DeadlockSound { Id = "bebop_sticky_cast", VpkPrefix = "sounds/abilities/bebop/bebop_stickybomb_prepare", Volume = 0.9f },
            new DeadlockSound { Id = "bebop_hook_shoot", VpkPrefix = "sounds/abilities/bebop/bebop_hook_shoot", Volume = 0.9f },
            new DeadlockSound { Id = "bebop_hyperbeam_start", VpkPrefix = "sounds/abilities/bebop/bebop_hyperbeam_prepstart", Range = 70f });

        // ---------------- Ivy (hero_tengu: 755 health, 2 regen, 7.2 m/s, 4 stamina) ----------------
        public static readonly FrameworkHero Ivy = Make(new FrameworkHero
        {
            Hero = "ivy", Name = "Ivy", Vo = "tengu", Subtitle = "The Gargoyle", SortPosition = 112f,
            Description = "Ivy binds her allies to her, turns to stone and carries friends into the fight.",
            Color = new Color(0.45f, 0.62f, 0.4f),
            BaseHealth = 100f, HealthPerLevel = 30f, BaseSpeed = 7.2f, BaseRegen = 2f,
            // citadel_weapon_tengu_set: 4.45 a bullet every 0.0735 s, falloff 787 -> 2264 hu to 10%
            GunDamage = 0.24f, GunCycle = 0.0735f, GunSpread = 0.7f, FalloffStart = 20f, FalloffEnd = 57.5f,
            GunSound = "ivy_gun",
            IconPortrait = "panorama/images/heroes/tengu_sm_psd.vtex_c",
            Abilities = new[]
            {
                new FrameworkAbility { Name = "Entangling Thorns", Cooldown = 10f, Anim = "cast", Sound = "ivy_thorns", Vo = "ivy_vo_power1",
                    Icon = "panorama/images/hud/abilities/tengu/tengu_storm_flask_psd.vtex_c", Deadlock = "Deadlock: summon a patch of choking thorns that damages and slows enemies in it (32 s)." },
                new FrameworkAbility { Name = "Kudzu Connection", Cooldown = 10f, Anim = "cast", Sound = "ivy_tether", Vo = "ivy_vo_power2",
                    Icon = "panorama/images/hud/abilities/tengu/tengu_tether_psd.vtex_c", Deadlock = "Deadlock: tether to a nearby ally to share bonuses and healing (37 s)." },
                new FrameworkAbility { Name = "Stone Form", Cooldown = 12f, Anim = "stone", Sound = "ivy_stone", Vo = "ivy_vo_power3",
                    Icon = "panorama/images/hud/abilities/tengu/tengu_stone_form_psd.vtex_c", Deadlock = "Deadlock: turn to impervious stone and smash down, stunning and damaging nearby enemies, healing a share of max health (40 s)." },
                new FrameworkAbility { Name = "Air Drop", Cooldown = 40f, Anim = "cast", Sound = "ivy_airdrop", Vo = "ivy_vo_power4",
                    Icon = "panorama/images/hud/abilities/tengu/tengu_lightning_crash_psd.vtex_c", Deadlock = "Deadlock: grab an ally (or a bomb) and fly; dropping it explodes (100 s)." },
            },
            Def = new DeadlockHeroDef
            {
                DlModel = "models/heroes_wip/ivy/ivy.vmdl_c", // her clips live under heroes_staging/tengu/tengu_v2/clips
                ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f,
                Anims = FrameworkAnims.Table(new FrameworkAnims.Names
                    {
                        Idle = "weapon_idle", Land = "land_impact_idle", DashGround = "ground_dash", DashAir = "air_dash_", Shoot = "weapon_shoot",
                        Death = "knockdown_large_start",
                    },
                    ("cast", "generic_cast_start", false, "locomotion", "full"),
                    ("stone", "stone_form_cast", false, "locomotion", "full")),
                RootLocked = FrameworkAnims.Locked("stone"),
            },
            Movement = new DeadlockMovementProfile { Hero = "ivy", StaminaPips = 4, OutOfCombatSpeed = 1.6f / 7.2f, WallJumpUp = 22f, WallJumpPowerMin = 0.6f },
        }, "sounds/player/footsteps/ivy/ivy_footstep", "sounds/player/movement/ivy/ivy_wingflap_small", "sounds/player/movement/ivy/ivy_wingflap", false,
            new DeadlockSound { Id = "ivy_gun", VpkPrefix = "sounds/weapons/tengu/ivy_weapon_fire_mid", Volume = 0.5f, Range = 60f, MinGap = 0.03f, PitchJitter = 0.05f, Fallback = "Play_commando_M1" },
            new DeadlockSound { Id = "ivy_thorns", VpkPrefix = "sounds/abilities/tengu/ivy_a1_vines_extend", Volume = 0.9f },
            new DeadlockSound { Id = "ivy_tether", VpkPrefix = "sounds/abilities/tengu/ivy_tether_cast", Volume = 0.9f },
            new DeadlockSound { Id = "ivy_stone", VpkPrefix = "sounds/abilities/tengu/a3_stone_form/ivy_a3_stone_form_cast", Volume = 0.9f },
            new DeadlockSound { Id = "ivy_airdrop", VpkPrefix = "sounds/abilities/tengu/ivy_airdrop_cast", Range = 60f });

        // ---------------- Apollo (hero_fencer: 770 health, 1 regen, 7.2 m/s, 3 stamina) ----------------
        public static readonly FrameworkHero Apollo = Make(new FrameworkHero
        {
            Hero = "apollo", Name = "Apollo", Vo = "fencer", Subtitle = "The Duelist", SortPosition = 113f,
            Description = "Apollo fights up close with a blade that cuts at range, parries what comes at him and finishes with one great slash.",
            Color = new Color(0.85f, 0.8f, 0.6f),
            BaseHealth = 100f, HealthPerLevel = 30f, BaseSpeed = 7.2f, BaseRegen = 1f,
            // citadel_weapon_fencer_set: 18.5 a slash every 0.38 s, flying 5000 hu/s, no falloff (a 25 m reach)
            GunDamage = 1.3f, GunCycle = 0.38f, GunSpread = 0f, FalloffStart = 25f, FalloffEnd = 26f, FalloffMin = 0f,
            GunSound = "apollo_gun",
            IconPortrait = "panorama/images/heroes/fencer_sm_psd.vtex_c",
            Abilities = new[]
            {
                new FrameworkAbility { Name = "Disengaging Sigil", Cooldown = 6f, Anim = "sigil", Sound = "apollo_sigil", Vo = "apollo_vo_power1",
                    Icon = "panorama/images/hud/abilities/fencer/fencer_sigil_psd.vtex_c", Deadlock = "Deadlock: draw a sigil in front of you and leap back as it explodes, damaging and slowing (12 s)." },
                new FrameworkAbility { Name = "Riposte", Cooldown = 8f, Anim = "riposte", Sound = "apollo_riposte", Vo = "apollo_vo_power2",
                    Icon = "panorama/images/hud/abilities/fencer/fencer_riposte_psd.vtex_c", Deadlock = "Deadlock: deflect the next attack; then dash to an enemy and stun it (22 s)." },
                new FrameworkAbility { Name = "Flawless Advance", Cooldown = 10f, Anim = "lunge", Sound = "apollo_lunge", Vo = "apollo_vo_power3",
                    Icon = "panorama/images/hud/abilities/fencer/fencer_lungingstab_psd.vtex_c", Deadlock = "Deadlock: a series of lunges in any direction; hold to time each stab, a perfect release hits hardest (26 s)." },
                new FrameworkAbility { Name = "Itani Lo Sahn", Cooldown = 40f, Anim = "ult", Sound = "apollo_ult", Vo = "apollo_vo_power4",
                    Icon = "panorama/images/hud/abilities/fencer/fencer_ult_psd.vtex_c", Deadlock = "Deadlock: charge a long-range slash; struck enemies freeze in slow motion, then take devastating damage (145 s)." },
            },
            Def = new DeadlockHeroDef
            {
                DlModel = "models/heroes_wip/fencer/fencer.vmdl_c",
                ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f,
                Anims = FrameworkAnims.Table(new FrameworkAnims.Names { AirJump = "jump_air", Land = "land_impact_idle", Parry = "parry_idle", Death = "knockdown_large_start" },
                    ("sigil", "ability_sigil_start", false, "locomotion", "full"),
                    ("riposte", "ability_riposte", false, "locomotion", "full"),
                    ("lunge", "ability_lunge_start_n", false, "locomotion", "full"),
                    ("ult", "ability_superslash_ult_start", false, "locomotion", "full")),
                RootLocked = FrameworkAnims.Locked("sigil", "lunge", "ult"),
            },
            Movement = new DeadlockMovementProfile { Hero = "apollo", StaminaPips = 3, OutOfCombatSpeed = 1.6f / 7.2f, WallJumpUp = 22f, WallJumpPowerMin = 0.6f },
        }, "sounds/player/footsteps/fencer/fencer_footstep", "sounds/player/footsteps/fencer/fencer_footstep_jumpland", "sounds/player/movement/apollo/apollo_dash_sweetener", true,
            new DeadlockSound { Id = "apollo_gun", VpkPrefix = "sounds/abilities/fencer/weapon_swipe", Volume = 0.6f, Range = 50f, MinGap = 0.1f, PitchJitter = 0.05f, Fallback = "Play_merc_sword_swing" },
            new DeadlockSound { Id = "apollo_sigil", VpkPrefix = "sounds/abilities/fencer/sigil/fencer_sigil_cast", Volume = 0.9f },
            new DeadlockSound { Id = "apollo_riposte", VpkPrefix = "sounds/abilities/fencer/riposte/fencer_riposte_cast", Volume = 0.9f },
            new DeadlockSound { Id = "apollo_lunge", VpkPrefix = "sounds/abilities/fencer/lunge/fencer_lunge_cast", Volume = 0.9f },
            new DeadlockSound { Id = "apollo_ult", VpkPrefix = "sounds/abilities/fencer/super_slash/fencer_super_slash_cast", Range = 70f });

        /// <summary>Fills in what follows from the hero key: the model def's names, the movement's animation hook, the sound list.</summary>
        private static FrameworkHero Make(FrameworkHero h, string footstep, string land, string dash, bool lowHealthInEmote, params DeadlockSound[] own)
        {
            h.Def.Hero = h.Hero;
            h.Def.DisplayName = h.Name;
            h.Def.BaseSpeed = h.BaseSpeed;
            h.Def.MuzzleBone = "muzzle_fx";
            h.Def.MuzzleName = h.MuzzleName;
            h.Movement.Anim = DeadlockModelSwap.Play;
            var sounds = FrameworkAnims.CommonSounds(h.Hero, h.Vo, footstep, land, dash, lowHealthInEmote);
            sounds.AddRange(own);
            h.Sounds = sounds.ToArray();
            return h;
        }

        public static IEnumerable<FrameworkHero> All => new[] { Bebop, Ivy, Apollo };
    }
}
