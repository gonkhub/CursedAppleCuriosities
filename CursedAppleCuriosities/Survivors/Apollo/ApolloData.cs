using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Framework;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Apollo's numbers (Deadlock's hero_fencer: model heroes_wip/fencer; citadel_weapon_fencer_set "Pride of Ixia"; ability_fencer_throwblade
    /// (Disengaging Sigil), _riposte, _lunge (Flawless Advance), _ultimate (Itani Lo Sahn); 2026-10-06 vdata). The user asked me to design
    /// his kit (2026-10-14): Deadlock's abilities, fitted to RoR2.
    /// </summary>
    public static class ApolloData
    {
        public const string Hero = "apollo";
        public const string Section = "Apollo";

        // ---- survivor (Deadlock 770 health, 1 regen, 7.2 m/s, 3 stamina) ----
        public static float BaseHealth = 100f;
        public const float HealthPerLevel = 30f;
        public const float BaseSpeed = 7.2f;
        public const float BaseRegen = 1f;
        public static readonly DeadlockMovementProfile Movement = new DeadlockMovementProfile
        {
            Hero = Hero,
            Anim = ApolloModel.Play,
            StaminaPips = 3,
            OutOfCombatSpeed = 1.6f / 7.2f,
            WallJumpUp = 22f,
            WallJumpPowerMin = 0.6f,
        };

        // ---- primary: Pride of Ixia, a flying sword wave (18.5 every 0.38 s, 5000 hu/s, no falloff over its 1000 hu reach) ----
        public static float WaveDamage = 2.3f;          // 3.1 balance (user): ~605%/s, ~757%/s with Disengaging Sigil's +25% fire rate (1.3 before)
        public const float WaveProc = 1f;               // the vanilla primary standard (user 3.1: a perfectly average proc chance)
        public static float WaveCycle = 0.38f;          // Deadlock m_flCycleTime
        public static float WaveRange = 25.4f;          // Deadlock falloff ranges 1000 hu: the wave's reach
        public const float WaveRadius = 0.6f;           // the little blade's reach either side of its line
        public const float WaveSpeed = 60f;             // m/s: Celeste's blade speed (user 2.5: 127 read as instant, 35 far too slow)

        // ---- secondary: Disengaging Sigil (ability_fencer_throwblade: 0.5 s cast, 6.5 m sigil 300 hu ahead, 85 damage, 24% slow 4 s, leap back 16 m/s, 12 s) ----
        public static float SigilDamage = 3f;           // user 2.5 (was 500%)
        public static float SigilRadius = 6.5f;
        public const float SigilAhead = 7.6f;           // Deadlock m_flForwardPlacementDistance 300 hu
        public const float SigilCast = 0.5f;            // Deadlock AbilityCastDelay
        public const float SigilSlow = 0.24f, SigilSlowSeconds = 4f;
        public const float SigilLeap = 26f;             // Deadlock JumpVelocityHidden 16: much higher (user 2.5), straight away from the sigil
        public const float SigilLift = 8f;              // and a little up on top, so a level cast still flips him high
        public static float SigilHaste = 0.25f;         // +25% fire rate after casting it (user 2.5)
        public const float SigilHasteSeconds = 5f;      // (my number; Duration Extender lengthens it)
        public static float SigilCooldown = 6f;

        // ---- utility: Flawless Advance (ability_fencer_lunge: hold 0.25-1.1 s, perfect 0.525-0.775 s; 25 -> 40 / perfect 65; a 13 m stab; 3 stabs
        // within 5 s; 26 s) ----
        public static float StabMin = 2.5f, StabMax = 4f, StabPerfect = 6.5f;
        public static float PerfectHeal = 0.2f;         // user 2.5: a perfect stab that lands heals 20% of max health
        public const float HoldMin = 0.25f, PerfectStart = 0.525f, PerfectLength = 0.25f, HoldMax = 1.1f; // Deadlock's timings
        public const float StabLength = 20f;            // Deadlock SlashLength 13 m; longer reach (user 3.1)
        public const float StabRadius = 1.6f;           // Deadlock SlashRadius
        public const float StabDash = 6f, StabDashSpeed = 56f; // Deadlock AttackDashRange 3 m (6 since 3.1, user: more range) / AttackingDashSpeed 2200 hu/s: the stab's own lunge, where he aims, up and down too
        public const float ReadyDash = 4f, ReadyDashTime = 0.2f; // the hop as he readies it, the way he's moving (user 2.5; Deadlock's lunge_start_<dir>)
        public const int Stabs = 3;                     // Deadlock MaxStabs
        public const float StabRecastWindow = 5f;       // Deadlock RecastTime
        public static float AdvanceCooldown = 9f;
        public static float AdvanceDamageResist = 0.15f; // less damage taken while readying and running a stab (user 3.0)

        // ---- utility alt: Riposte (ability_fencer_riposte: 0.8 s stance, parry the next attack; dash 35 m to the attacker; 6 m slash, 0.8 s
        // stun, 40% slow 4 s, 22 s) ----
        public const float RiposteWindow = 0.8f;        // Deadlock AbilityChannelTime
        public static float RiposteDamage = 2.5f;       // user 2.5 (was 600%)
        public static float RiposteKickDamage = 4f;     // user 3.1: hold Riposte through the counter: after the slash, a full heavy melee kick
        public const float RiposteKickAfter = 0.3f;     // the slash's beat before the kick's wind-up starts (my number)
        public const float RiposteKickWindup = 0.5f;    // melee_start: the heavy melee's wind-up (my number; Abrams' is 0.4)
        public const float RiposteKickImpact = 0.15f, RiposteKickDuration = 0.6f; // the kick lands this far into melee_hit; he's free after (my numbers)
        public const float RiposteKickHold = 0.15f;     // held at least this long counts as holding (a tap on a close enemy is the slash)
        public const float RiposteDashSpeed = 76f;      // Deadlock DashSpeed 3000 hu/s
        public const float RiposteCounterRange = 50f;   // user 3.0: after a parry, pick the counter's target near the crosshair within 50 m
        public const float RiposteCounterConeDeg = 20f;
        public const float RiposteCounterWindow = 3f;   // seconds to press Riposte again after a parry (my number, 3.0)
        public const float RiposteSlashRadius = 6f;     // Deadlock SlashRadius
        public const float RiposteStun = 2.5f;          // user 2.5 (Deadlock 0.8 s)
        public const float RiposteSlow = 0.4f, RiposteSlowSeconds = 4f;
        public const float RiposteInvulnerable = 1f;
        public static float RiposteCooldown = 8f;
        public static float RiposteLifesteal = 0.4f;    // user 2.5: lifesteal after a parry (my number)
        public const float RiposteLifestealSeconds = 5f; // x Duration Extender

        // ---- special: Itani Lo Sahn (ability_fencer_ultimate: 1.5 s charge, a 27 m dash-slash 7 m wide; 70 impact, then 200 after 1.8 s of slow
        // motion, +60% under half health; Apollo invulnerable meanwhile; 145 s) ----
        public static float UltImpact = 5f;
        public static float UltDelayed = 20f;
        public const float UltLowHealthBonus = 0.6f, UltLowHealth = 0.5f; // Deadlock BonusDamagePercent / LowHealthEnemyThresholdPct
        public const float UltCharge = 1.5f;            // Deadlock AbilityCastDelay
        public const float UltDash = 27f;               // Deadlock DashRange
        public const float UltRadius = 7f;              // Deadlock DashRadius
        public const float UltLock = 1.8f;              // Deadlock DebuffDuration / CasterLockDuration
        public const float UltMaxHold = 6f;             // seconds ready before he strikes by himself (Deadlock's channel has no end: 9999)
        public const float UltDamageTaken = 0.3f;       // while masked: Deadlock IncomingDamageReductionPercent 70
        public const string MaskKey = "apollo_mask";
        public const string DlMaskModel = "models/abilities/fencer_mask_model_fx.vmdl_c"; // the mask he dons for Itani Lo Sahn
        public static float UltCooldown = 40f;

        public static void Bind(ConfigFile c) // the hero's config section is only its keybind layout (3.0)
        {
            Movement.Bind(c, Section);
        }

        // ---- Deadlock assets ----
        public const string DlModel = "models/heroes_wip/fencer/fencer.vmdl_c";
        public const float ModelHeightScale = 1.08f * 1.25f * 1.15f * 0.95f;
        public const string MuzzleBone = "muzzle_fx";
        public const string IconPortrait = "panorama/images/heroes/fencer_sm_psd.vtex_c";
        // The primary shows the weapon itself (user 3.0, like Celeste's baton): Deadlock's shop image of the hero's gun.
        public const string IconGun = "panorama/images/heroes/guns/fencer_rapier_psd.vtex_c";
        public const string IconSigil = "panorama/images/hud/abilities/fencer/fencer_sigil_psd.vtex_c";
        public const string IconRiposte = "panorama/images/hud/abilities/fencer/fencer_riposte_psd.vtex_c";
        public const string IconAdvance = "panorama/images/hud/abilities/fencer/fencer_lungingstab_psd.vtex_c";
        public const string IconUlt = "panorama/images/hud/abilities/fencer/fencer_ult_psd.vtex_c";

        public static readonly (string State, string Clip, bool Loop, string Next, string Layer)[] Anims = FrameworkAnims.Table(
            new FrameworkAnims.Names { AirJump = "jump_air", Land = "land_impact_idle", Parry = "parry_idle" },
            ("shoot_left", "shoot_left_idle", false, "locomotion", "delta"),
            ("sigil", "ability_sigil_start", false, "locomotion", "full"),
            ("sigil_jump", "ability_sigil_jump", false, "in_air", "full"),
            // Flawless Advance as Deadlock plays it: a start clip per direction of the hop he makes as he readies (held until the stab),
            // then the attack. (ability_lunge_aim is only an additive layer: it turned him sideways and isn't the readying pose.)
            ("lunge_start_n", "ability_lunge_start_n", false, "hold", "full"),
            ("lunge_start_e", "ability_lunge_start_e", false, "hold", "full"),
            ("lunge_start_s", "ability_lunge_start_s", false, "hold", "full"),
            ("lunge_start_w", "ability_lunge_start_w", false, "hold", "full"),
            ("lunge", "ability_lunge_attack_n", false, "locomotion", "full"),
            ("riposte", "ability_riposte", false, "hold", "full"),
            ("riposte_dash", "ability_riposte_counterattack_dash", false, "riposte_hit", "full"),
            ("riposte_hit", "ability_riposte_counterattack_hit", false, "locomotion", "full"),
            ("riposte_kick_start", "melee_start", false, "hold", "full"),             // the counter held: a full heavy melee after the slash (user 3.1)
            ("riposte_kick_start_air", "melee_in_air_start", false, "hold", "full"),
            ("riposte_kick", "melee_hit", false, "locomotion", "full"),
            ("riposte_kick_air", "melee_in_air_hit", false, "in_air", "full"),
            ("ult_start", "ability_superslash_ult_start", false, "ult_hold", "full"),
            ("ult_hold", "ability_superslash_ult_hold", true, "", "full"),
            ("ult_dash", "ability_superslash_ult_dash", false, "ult_finale", "full"),
            ("ult_finale", "ability_superslash_ult_finale", false, "locomotion", "full"));

        public static readonly HashSet<string> Locked = FrameworkAnims.Locked("sigil", "lunge", "riposte_dash", "riposte_hit", "riposte_kick_start", "riposte_kick_start_air", "riposte_kick", "riposte_kick_air", "ult_dash", "ult_finale", "lunge_start_n", "lunge_start_e", "lunge_start_s", "lunge_start_w");

        public static readonly DeadlockSound[] Sounds = BuildSounds();

        private static DeadlockSound[] BuildSounds()
        {
            var l = FrameworkAnims.CommonSounds(Hero, "fencer", "sounds/player/footsteps/fencer/fencer_footstep", "sounds/player/footsteps/fencer/fencer_footstep_jumpland", "sounds/player/movement/apollo/apollo_dash_sweetener", lowHealthInEmote: true);
            void Add(string id, string path, float volume = 0.9f, float range = 40f, float gap = 0.1f, string? fallback = null) =>
                l.Add(new DeadlockSound { Id = "apollo_" + id, VpkPrefix = path, Volume = volume, Range = range, MinGap = gap, Fallback = fallback });
            Add("wave", "sounds/weapons/fencer/fencer_wpn_fire", 0.7f, 60f, 0.08f, "Play_merc_sword_swing"); // his gun's own fire (Fencer.Wpn.Fire; user 2.5)
            Add("wave_mech", "sounds/weapons/fencer/fencer_weapon_mech", 0.4f, 25f, 0.08f);
            Add("sigil_cast", "sounds/abilities/fencer/sigil/fencer_sigil_cast");
            Add("sigil_charge", "sounds/abilities/fencer/sigil/fencer_sigil_cast_delay");
            Add("sigil_blast", "sounds/abilities/fencer/sigil/fencer_sigil_aoe_start", 1f, 60f);
            Add("sigil_hit", "sounds/abilities/fencer/sigil/fencer_sigil_hit_confirm", 0.8f, 40f, 0.08f);
            Add("lunge_charge", "sounds/abilities/fencer/lunge/fencer_lunge_charge", 0.8f);
            Add("lunge_glint", "sounds/abilities/fencer/lunge/fencer_lunge_charge_glint");
            Add("lunge_cast", "sounds/abilities/fencer/lunge/fencer_lunge_cast");
            Add("lunge_hit", "sounds/abilities/fencer/lunge/fencer_lunge_hit_confirm", 0.9f, 40f, 0.05f);
            Add("lunge_perfect", "sounds/abilities/fencer/lunge/fencer_lunge_perfect_hit", 1f, 50f);
            Add("riposte_cast", "sounds/abilities/fencer/riposte/fencer_riposte_cast");
            Add("riposte_success", "sounds/abilities/fencer/riposte/fencer_riposte_success", 1f, 50f);
            Add("riposte_dash", "sounds/abilities/fencer/riposte/fencer_riposte_counterattack_cast", 1f, 50f);
            Add("riposte_hit", "sounds/abilities/fencer/riposte/fencer_riposte_counterattack_impact", 1f, 50f);
            Add("kick_charge", "sounds/player/melee/shared/charged_melee_full", 0.9f, 30f);
            Add("kick_swing", "sounds/player/melee/shared/swing_charged", 0.9f, 30f);
            Add("kick_impact", "sounds/player/melee/shared/melee_charged_kick", 1f, 40f, 0.05f);
            Add("ult_voice", "sounds/vo/fencer/fencer_use_power4_start_02", 1f, 80f); // "Itani lo sahn..." (Deadlock's captions)
            Add("ult_cast", "sounds/abilities/fencer/super_slash/fencer_super_slash_cast", 1f, 80f);
            Add("ult_charge", "sounds/abilities/fencer/super_slash/fencer_super_slash_cast_delay", 1f, 80f);
            Add("ult_hit", "sounds/abilities/fencer/super_slash/fencer_super_slash_hit_confirm", 1f, 70f, 0.05f);
            Add("ult_timer", "sounds/abilities/fencer/super_slash/fencer_super_slash_victim_mod_timer", 0.8f, 60f, 0.3f);
            Add("ult_end", "sounds/abilities/fencer/super_slash/fencer_super_slash_victim_mod_end", 1f, 80f, 0.05f);
            Add("ult_lock", "sounds/abilities/fencer/super_slash/fencer_super_slash_caster_arrival_mod_lp", 0.7f, 50f);
            return l.ToArray();
        }
    }
}
