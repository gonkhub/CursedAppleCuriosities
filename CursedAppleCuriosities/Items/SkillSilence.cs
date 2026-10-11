using System.Collections.Generic;
using RoR2;

namespace CursedAppleCuriosities.Items
{
    // Shared "can't use skills" lock. Any body carrying one of the registered buffs fails
    // GenericSkill.ExecuteIfReady — player skills run on their owning client and enemy skills on the
    // server, and buffs are networked, so the check holds on whichever side drives the skill.
    // Used by Ethereal Shift (you can't act while shifted) and Focus Lens (silences its target).
    public static class SkillSilence
    {
        private static readonly List<BuffDef> _buffs = new List<BuffDef>();
        private static bool _hooked;

        public static void Register(BuffDef buff)
        {
            if (buff != null && !_buffs.Contains(buff)) _buffs.Add(buff);
            if (_hooked) return;
            _hooked = true;
            On.RoR2.GenericSkill.ExecuteIfReady += GenericSkill_ExecuteIfReady;
        }

        public static bool IsSilenced(CharacterBody? body)
        {
            if (body == null) return false;
            foreach (var buff in _buffs)
                if (buff.buffIndex != BuffIndex.None && body.HasBuff(buff.buffIndex))
                    return true;
            return false;
        }

        private static bool GenericSkill_ExecuteIfReady(On.RoR2.GenericSkill.orig_ExecuteIfReady orig, GenericSkill self)
        {
            if (IsSilenced(self.characterBody)) return false;
            return orig(self);
        }
    }
}
