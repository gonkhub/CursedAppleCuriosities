using System;
using System.Collections.Generic;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks
{
    /// <summary>
    /// 3.0 (user): alternate skills start locked and unlock through a challenge played as that hero. An unlock is registered
    /// by the skill's name before the heroes build their skill families; <see cref="Deadlock.DeadlockSurvivorKit.Slot"/> then puts it on that
    /// skill's variant, which is what greys it out in the loadout.
    /// </summary>
    public static class SkillUnlocks
    {
        private static readonly Dictionary<string, UnlockableDef> _bySkill = new Dictionary<string, UnlockableDef>(StringComparer.Ordinal);

        /// <summary>Lock a skill (its SkillDef.skillName, e.g. "CursedAppleAbramsHeavyMelee"); the unlockable is "CursedApple.Skills.&lt;skillName&gt;".</summary>
        public static void Register(string skillName)
        {
            if (_bySkill.ContainsKey(skillName)) return;
            var def = ScriptableObject.CreateInstance<UnlockableDef>();
            def.cachedName = "CursedApple.Skills." + skillName;
            ((ScriptableObject)def).name = def.cachedName;
            def.sortScore = 210;
            ContentAddition.AddUnlockableDef(def);
            _bySkill[skillName] = def;
        }

        /// <summary>The unlock for a skill variant (null = always available). Takes the skill's name and icon for the challenge menu.</summary>
        public static UnlockableDef? For(SkillDef skill)
        {
            if (skill == null || !_bySkill.TryGetValue(skill.skillName, out UnlockableDef def)) return null;
            def.nameToken = skill.skillNameToken;
            if (skill.icon != null) def.achievementIcon = skill.icon;
            return def;
        }
    }
}
