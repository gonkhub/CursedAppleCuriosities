using System;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Broker
{
    /// <summary>
    /// Every curse carries one harsh penalty to one specific thing, kept for the rest of the run. Never a blanket nerf:
    /// a skill slot's damage, one stat, one cooldown, one kind of incoming damage. Values are rolled when a player opens
    /// a Cursed Broker; reds, bosses and void reds roll toward the harsh end.
    /// </summary>
    public enum CursePenalty : byte
    {
        PrimaryDamage, SecondaryDamage, UtilityDamage, SpecialDamage,
        AttackSpeed, MaxHealth, MoveSpeed, SprintSpeed, JumpHeight, Gravity,
        AllCooldowns, SecondaryCooldown, UtilityCooldown, SpecialCooldown, EquipmentCooldown,
        Healing, Regen, Armor, CritDamage, Luck,
        Range, Duration, Gold, BarrierDecay, DotTaken, EliteTaken,
    }

    public static class CursePenalties
    {
        private readonly struct Def
        {
            public readonly CursePenalty Kind;
            public readonly float Min, Max;
            public readonly Func<float, string> Text;
            public Def(CursePenalty kind, float min, float max, Func<float, string> text) { Kind = kind; Min = min; Max = max; Text = text; }
        }

        private static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        // Min/Max: the rolled magnitude (fractions are percentages; Armor is flat; Luck is fixed).
        private static readonly Def[] Defs =
        {
            new Def(CursePenalty.PrimaryDamage,     0.50f, 0.75f, v => $"-{Pct(v)} damage dealt by your Primary skill"),
            new Def(CursePenalty.SecondaryDamage,   0.50f, 0.75f, v => $"-{Pct(v)} damage dealt by your Secondary skill"),
            new Def(CursePenalty.UtilityDamage,     0.60f, 0.85f, v => $"-{Pct(v)} damage dealt by your Utility skill"),
            new Def(CursePenalty.SpecialDamage,     0.50f, 0.75f, v => $"-{Pct(v)} damage dealt by your Special skill"),
            new Def(CursePenalty.AttackSpeed,       0.30f, 0.45f, v => $"-{Pct(v)} attack speed"),
            new Def(CursePenalty.MaxHealth,         0.30f, 0.45f, v => $"-{Pct(v)} maximum health"),
            new Def(CursePenalty.MoveSpeed,         0.25f, 0.40f, v => $"-{Pct(v)} movement speed"),
            new Def(CursePenalty.SprintSpeed,       0.60f, 0.90f, v => $"Sprinting's speed bonus is {Pct(v)} weaker"),
            new Def(CursePenalty.JumpHeight,        0.40f, 0.55f, v => $"-{Pct(v)} jump power"),
            new Def(CursePenalty.Gravity,           0.60f, 1.00f, v => $"+{Pct(v)} gravity: you fall faster and jumps are shorter"),
            new Def(CursePenalty.AllCooldowns,      0.35f, 0.60f, v => $"+{Pct(v)} cooldown on all skills"),
            new Def(CursePenalty.SecondaryCooldown, 0.80f, 1.30f, v => $"+{Pct(v)} Secondary skill cooldown"),
            new Def(CursePenalty.UtilityCooldown,   0.80f, 1.30f, v => $"+{Pct(v)} Utility skill cooldown"),
            new Def(CursePenalty.SpecialCooldown,   0.70f, 1.10f, v => $"+{Pct(v)} Special skill cooldown"),
            new Def(CursePenalty.EquipmentCooldown, 1.00f, 2.00f, v => $"+{Pct(v)} equipment cooldown"),
            new Def(CursePenalty.Healing,           0.50f, 0.70f, v => $"-{Pct(v)} healing received (all sources but regeneration)"),
            new Def(CursePenalty.Regen,             0.75f, 1.00f, v => $"-{Pct(v)} health regeneration"),
            new Def(CursePenalty.Armor,             40f,   70f,   v => $"-{Mathf.RoundToInt(v)} armor"),
            new Def(CursePenalty.CritDamage,        0.40f, 0.60f, v => $"-{Pct(v)} critical strike damage"),
            new Def(CursePenalty.Luck,              1f,    1f,    v => "-1 luck: chance effects roll twice and keep the worse result"),
            new Def(CursePenalty.Range,             0.30f, 0.50f, v => $"-{Pct(v)} radius on your explosions, auras and area effects"),
            new Def(CursePenalty.Duration,          0.35f, 0.55f, v => $"-{Pct(v)} duration of your buffs and of the debuffs, stuns and damage over time you inflict"),
            new Def(CursePenalty.Gold,              0.35f, 0.55f, v => $"-{Pct(v)} gold gained"),
            new Def(CursePenalty.BarrierDecay,      1.00f, 2.00f, v => $"Barrier decays {Pct(v)} faster"),
            new Def(CursePenalty.DotTaken,          1.00f, 1.50f, v => $"+{Pct(v)} damage taken from burns, bleeds, poisons and other damage over time"),
            new Def(CursePenalty.EliteTaken,        0.40f, 0.70f, v => $"+{Pct(v)} damage taken from elite monsters"),
        };

        private static Def Find(CursePenalty kind)
        {
            foreach (Def d in Defs) if (d.Kind == kind) return d;
            return Defs[0];
        }

        public static string Describe(CursePenalty kind, float value) => Find(kind).Text(value);

        public static int Count => Defs.Length;

        /// <summary>A magnitude for this penalty: whites roll the lower half of the range, greens the lower 70%, reds/bosses the upper 70%.</summary>
        public static float Roll(CursePenalty kind, int severityBand, Xoroshiro128Plus rng)
        {
            Def d = Find(kind);
            float r = rng.nextNormalizedFloat;
            float t = severityBand switch { 0 => 0.5f * r, 1 => 0.7f * r, _ => 0.3f + 0.7f * r };
            float v = Mathf.Lerp(d.Min, d.Max, t);
            return kind == CursePenalty.Armor ? Mathf.Round(v) : Mathf.Round(v * 100f) / 100f;
        }

        public static CursePenalty KindAt(int i) => Defs[i].Kind;

        // ─────────────────────────────── effects ───────────────────────────────

        /// <summary>Product of (1 - v) over this master's penalties of a kind (1 with none).</summary>
        public static float Reduction(CharacterMaster? master, CursePenalty kind)
        {
            float f = 1f;
            CurseLedger? l = CurseLedger.For(master);
            if (l != null) foreach (CurseEntry e in l.Entries) if (e.Penalty == kind) f *= Mathf.Max(0f, 1f - e.Value);
            return f;
        }

        /// <summary>Sum of this master's penalty values of a kind (0 with none).</summary>
        public static float Sum(CharacterMaster? master, CursePenalty kind)
        {
            float s = 0f;
            CurseLedger? l = CurseLedger.For(master);
            if (l != null) foreach (CurseEntry e in l.Entries) if (e.Penalty == kind) s += e.Value;
            return s;
        }

        public static float Reduction(CharacterBody? body, CursePenalty kind) => body != null && CurseLedger.Any ? Reduction(body.master, kind) : 1f;
        public static float Sum(CharacterBody? body, CursePenalty kind) => body != null && CurseLedger.Any ? Sum(body.master, kind) : 0f;

        /// <summary>Multiplier on this body's radii (Mystic Expansion and everything that reads it).</summary>
        public static float RadiusFactor(CharacterBody? body) => Reduction(body, CursePenalty.Range);

        /// <summary>Multiplier on this body's durations (Duration Extender and everything that reads it).</summary>
        public static float DurationFactor(CharacterBody? body) => Reduction(body, CursePenalty.Duration);

        public static void Hook()
        {
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (!CurseLedger.Any || body == null) return;
                CharacterMaster? m = body.master;
                CurseLedger? l = CurseLedger.For(m);
                if (l == null || l.Entries.Count == 0) return;
                args.attackSpeedTotalMult *= Reduction(m, CursePenalty.AttackSpeed);
                args.healthTotalMult      *= Reduction(m, CursePenalty.MaxHealth);
                args.moveSpeedTotalMult   *= Reduction(m, CursePenalty.MoveSpeed);
                args.jumpPowerTotalMult   *= Reduction(m, CursePenalty.JumpHeight);
                args.regenTotalMult       *= Reduction(m, CursePenalty.Regen);
                args.critDamageTotalMult  *= Reduction(m, CursePenalty.CritDamage);
                args.armorAdd -= Sum(m, CursePenalty.Armor);
                args.luckAdd  -= Sum(m, CursePenalty.Luck);
                args.barrierDecayMult *= 1f + Sum(m, CursePenalty.BarrierDecay);

                // Sprint: shrink only the sprint part of the speed (stats are recalculated when sprinting toggles).
                float keep = Reduction(m, CursePenalty.SprintSpeed);
                float sprint = body.sprintingSpeedMultiplier;
                if (keep < 1f && body.isSprinting && sprint > 1f)
                    args.moveSpeedTotalMult *= (1f + (sprint - 1f) * keep) / sprint;
            };

            // Skill-slot damage (attacker) and incoming DoT / elite damage (victim). Server.
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (CurseLedger.Any && info != null && self != null && !Items.ItemBase.FinalDamage(info))
                {
                    CharacterBody? attacker = info.attacker != null ? info.attacker.GetComponent<CharacterBody>() : null;
                    if (attacker != null)
                    {
                        DamageSource src = info.damageType.damageSource;
                        if ((src & DamageSource.Primary) != 0)   info.damage *= Reduction(attacker, CursePenalty.PrimaryDamage);
                        if ((src & DamageSource.Secondary) != 0) info.damage *= Reduction(attacker, CursePenalty.SecondaryDamage);
                        if ((src & DamageSource.Utility) != 0)   info.damage *= Reduction(attacker, CursePenalty.UtilityDamage);
                        if ((src & DamageSource.Special) != 0)   info.damage *= Reduction(attacker, CursePenalty.SpecialDamage);
                        if (attacker.isElite) info.damage *= 1f + Sum(self.body, CursePenalty.EliteTaken);
                    }
                    if (info.dotIndex != DotController.DotIndex.None) info.damage *= 1f + Sum(self.body, CursePenalty.DotTaken);
                }
                orig(self, info);
            };

            On.RoR2.HealthComponent.Heal += (orig, self, amount, procChainMask, nonRegen) =>
            {
                if (CurseLedger.Any && nonRegen && self != null) amount *= Reduction(self.body, CursePenalty.Healing);
                return orig(self, amount, procChainMask, nonRegen);
            };

            // Gravity: on whichever machine simulates the body.
            On.RoR2.CharacterMotor.ModifyGravity += (On.RoR2.CharacterMotor.orig_ModifyGravity orig, CharacterMotor self, ref float verticalVelocity, ref float gravity, float deltaTime) =>
            {
                orig(self, ref verticalVelocity, ref gravity, deltaTime);
                if (CurseLedger.AnyGravity && self != null)
                    gravity *= 1f + Sum(self.GetComponent<CharacterBody>(), CursePenalty.Gravity);
            };

            // Skill cooldowns: the game caps a skill's cooldown at its base (Min(base, base * scale - flat)), so stat
            // cooldown multipliers can only shorten it. Lengthen the final interval after that cap instead; cooldown
            // reduction from items still applies first.
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval += (orig, self) =>
            {
                float interval = orig(self);
                if (!CurseLedger.Any || self == null) return interval;
                CharacterBody? body = self.characterBody;
                if (body == null || body.master == null || CurseLedger.For(body.master) == null) return interval;
                float add = Sum(body, CursePenalty.AllCooldowns);
                SkillLocator? locator = body.skillLocator;
                if (locator != null)
                {
                    switch (locator.FindSkillSlot(self))
                    {
                        case SkillSlot.Secondary: add += Sum(body, CursePenalty.SecondaryCooldown); break;
                        case SkillSlot.Utility:   add += Sum(body, CursePenalty.UtilityCooldown);   break;
                        case SkillSlot.Special:   add += Sum(body, CursePenalty.SpecialCooldown);   break;
                    }
                }
                if (add <= 0f) return interval;
                float temp = self.temporaryCooldownPenalty;
                return (interval - temp) * (1f + add) + temp;
            };

            On.RoR2.Inventory.CalculateEquipmentCooldownScale += (orig, self) =>
            {
                float scale = orig(self);
                if (CurseLedger.Any && self != null) scale *= 1f + Sum(self.GetComponent<CharacterMaster>(), CursePenalty.EquipmentCooldown);
                return scale;
            };

            On.RoR2.CharacterMaster.GiveMoney_uint_bool += (orig, self, amount, bypassStatManager) =>
            {
                if (CurseLedger.Any && self != null && amount > 0)
                    amount = (uint)Mathf.Max(0f, Mathf.Round(amount * Reduction(self, CursePenalty.Gold)));
                orig(self, amount, bypassStatManager);
            };
        }
    }
}
