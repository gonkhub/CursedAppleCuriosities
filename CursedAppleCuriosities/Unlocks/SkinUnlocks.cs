using System;
using System.Collections.Generic;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks
{
    /// <summary>
    /// 3.0 (user): each hero's mastery skin is a recolour, locked until that hero beats the game or obliterates on Monsoon
    /// (vanilla's mastery rule). A lock is registered by skin name (body and lobby display copies alike) before the heroes build
    /// their skins; <see cref="Deadlock.DeadlockSurvivorKit.OwnSkin"/> puts it on the SkinDef.
    /// </summary>
    public static class SkinUnlocks
    {
        private static readonly Dictionary<string, UnlockableDef> _bySkin = new Dictionary<string, UnlockableDef>(StringComparer.Ordinal);

        /// <summary>Lock these skins (SkinDef names) behind one unlockable, "CursedApple.Skins.&lt;id&gt;".</summary>
        public static void Register(string id, string nameToken, params string[] skinNames)
        {
            var def = ScriptableObject.CreateInstance<UnlockableDef>();
            def.cachedName = "CursedApple.Skins." + id;
            ((ScriptableObject)def).name = def.cachedName;
            def.nameToken = nameToken;
            def.sortScore = 220;
            ContentAddition.AddUnlockableDef(def);
            foreach (string s in skinNames) _bySkin[s] = def;
        }

        /// <summary>The unlock for a skin (null = always available); its icon becomes the unlock's icon.</summary>
        public static UnlockableDef? For(string skinName, Sprite? icon)
        {
            if (!_bySkin.TryGetValue(skinName, out UnlockableDef def)) return null;
            if (icon != null && def.achievementIcon == null) def.achievementIcon = icon;
            return def;
        }
    }
}
