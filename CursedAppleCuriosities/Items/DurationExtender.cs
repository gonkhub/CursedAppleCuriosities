using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Deadlock's Duration Extender (Spirit T2, +20% ability duration) as a white: +15% duration per stack on
    /// everything you cause. Like Mystic Expansion, custom timers call <see cref="Multiplier"/>; vanilla content is
    /// covered by general hooks:
    /// - timed buffs on yourself (not internal cooldowns) last longer;
    /// - while a body is the "source" (on-hit procs, damage it deals, its equipment, its heroes' server hits), timed
    ///   buffs/debuffs, stuns and freezes applied to anyone use the source's multiplier;
    /// - DoTs you inflict (bleed, burn, blight, Spirit Burn...) last longer.
    /// </summary>
    public class DurationExtender : ItemBase
    {
        public override string ItemName => "Duration Extender";
        public override string ItemLangTokenName => "DURATIONEXTENDER";

        public override string ItemPickupDesc => "Your buffs, debuffs, stuns and abilities last longer.";
        public override string ItemFullDescription =>
            $"Everything you cause lasts <style=cIsUtility>{DurationPerStack * 100:0}%</style> " +
            $"<style=cStack>(+{DurationPerStack * 100:0}% per stack)</style> longer: your <style=cIsUtility>buffs</style>, " +
            "the <style=cIsDamage>debuffs, stuns, freezes and damage over time</style> you inflict, and the effects, channels and " +
            "windows of your <style=cIsUtility>items, equipment and abilities</style>.";

        public override string ItemLore => "Time is a flat circle. This one's just a slightly wider circle.";

        public override ItemTier Tier => ItemTier.Tier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_durationextender.png";

        public static float DurationPerStack;
        private static ItemDef? _def;

        public override void Init(ConfigFile config)
        {
            DurationPerStack = config.Bind("White", "Duration Extender - Duration Per Stack", 0.15f,
                "Extra duration per stack (linear; 0.15 = +15%).").Value;
            CreateLang();
            CreateItem();
            _def = ItemDef;
            Hooks();
        }

        /// <summary>Duration multiplier for effects this body causes (1 without the item).</summary>
        public static float Multiplier(CharacterBody? body)
        {
            if (_def == null || body == null || body.inventory == null) return 1f;
            int count = body.inventory.GetItemCountEffective(_def);
            return count > 0 ? 1f + DurationPerStack * count : 1f;
        }

        public static float Multiplier(GameObject? owner) => owner != null ? Multiplier(owner.GetComponent<CharacterBody>()) : 1f;

        // ---------------- source scope ----------------

        [ThreadStatic] private static CharacterBody? _source;
        [ThreadStatic] private static bool _hasSource;
        [ThreadStatic] private static int _exact;

        /// <summary>The multiplier of the source in scope (1 with none): for custom timers started inside a hit or an equipment use.</summary>
        public static float Current => _hasSource ? Multiplier(_source) : 1f;

        /// <summary>Inside, durations are applied as given: for a buff whose duration a caller already extended.</summary>
        public static ExactScope Exact() { _exact++; return default; }

        public readonly struct ExactScope : IDisposable
        {
            public void Dispose() => _exact--;
        }

        /// <summary>
        /// Everything applied inside the scope counts as caused by this body: <c>using (DurationExtender.From(body)) { ... }</c>.
        /// Scopes nest; the innermost wins.
        /// </summary>
        public static Scope From(CharacterBody? source) => new Scope(source);
        public static Scope From(GameObject? source) => new Scope(source != null ? source.GetComponent<CharacterBody>() : null);

        public readonly struct Scope : IDisposable
        {
            private readonly CharacterBody? _prev;
            private readonly bool _prevHas;

            public Scope(CharacterBody? source)
            {
                _prev = _source;
                _prevHas = _hasSource;
                _source = source;
                _hasSource = true;
            }

            public void Dispose()
            {
                _source = _prev;
                _hasSource = _prevHas;
            }
        }

        /// <summary>The multiplier for something applied to <paramref name="target"/> right now.</summary>
        private static float For(CharacterBody? target, bool debuff)
        {
            if (!debuff && (!_hasSource || _source != target)) return Multiplier(target); // your own buffs: your item
            return _hasSource ? Multiplier(_source) : 1f;                              // what someone did to you: theirs
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.AddTimedBuffInternal += (orig, self, buffDef, duration, totalOverride) =>
            {
                if (buffDef != null && !buffDef.isCooldown && duration > 0f && _exact == 0) // never stretch item cooldown buffs
                {
                    float m = For(self, buffDef.isDebuff);
                    duration *= m;
                    if (totalOverride > 0f) totalOverride *= m;
                }
                orig(self, buffDef, duration, totalOverride);
            };
            On.RoR2.SetStateOnHurt.SetStun += (orig, self, duration) => orig(self, _exact == 0 ? duration * Current : duration);
            On.RoR2.SetStateOnHurt.SetFrozen += (orig, self, duration) => orig(self, _exact == 0 ? duration * Current : duration);
            On.RoR2.SetStateOnHurt.SetShock += (orig, self, duration) => orig(self, _exact == 0 ? duration * Current : duration);
            On.RoR2.DotController.InflictDot_refInflictDotInfo += (On.RoR2.DotController.orig_InflictDot_refInflictDotInfo orig, ref InflictDotInfo info) =>
            {
                info.duration *= Multiplier(info.attackerObject);
                orig(ref info);
            };
        }

        /// <summary>
        /// The general source scopes. Called at the very end of plugin Awake, so these hooks wrap every other mod
        /// hook on the same methods (the last hook added runs outermost) and the source is set for all of them.
        /// </summary>
        public static void HookSources()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += (orig, self, info, victim) =>
            {
                using (From(info?.attacker)) orig(self, info, victim);
            };
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                using (From(info?.attacker)) orig(self, info);
            };
            On.RoR2.SetStateOnHurt.OnTakeDamageServer += (orig, self, report) =>
            {
                using (From(report?.attackerBody)) orig(self, report);
            };
        }
    }
}
