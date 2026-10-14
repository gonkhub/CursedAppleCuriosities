using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Framework
{
    /// <summary>
    /// Apollo, framed (Bebop and Ivy graduated to their own folders) (user, 2026-10-14: build their frameworks now, design the kits later). Numbers are Deadlock's
    /// (2026-10-06 vdata): hero_bebop, hero_tengu (Ivy), hero_fencer (Apollo). Health is Deadlock's over about 7.5; guns are
    /// Deadlock's cycle times with damage set near the other heroes' damage per second.
    /// </summary>
    public static class NextHeroes
    {
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

        public static IEnumerable<FrameworkHero> All => new[] { Apollo };
    }
}
