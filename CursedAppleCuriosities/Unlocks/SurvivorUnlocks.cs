using System;
using System.Collections.Generic;
using R2API;
using RoR2;
using CursedAppleCuriosities.Items;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks
{
    /// <summary>
    /// 3.0 (user): a fresh profile has Abrams and Seven only. Mina, Ivy, Bebop, Paradox and Lash are map secrets (a silhouette in a
    /// Deadlock district, see Stages/DeadlockMap/HeroSilhouette); every other hero unlocks through a challenge of their own.
    /// Everything stays locked for everyone: no unlock-all toggle (players who want it use an Unlockify-style mod).
    /// </summary>
    public static class SurvivorUnlocks
    {
        /// <summary>Unlocked from the start.</summary>
        private static readonly string[] Free = { "Abrams", "Seven" };

        /// <summary>The district each secret hero hides in (the how-to-unlock hint).</summary>
        private static readonly Dictionary<string, string> SecretWhere = new Dictionary<string, string>
        {
            ["Mina"] = "Chinatown",
            ["Ivy"] = "Umbria University",
            ["Bebop"] = "the Haunted Lot",
            ["Paradox"] = "the Sunken Plaza",
            ["Lash"] = "the Sunken Plaza", // on its highest rooftop (user, 2026-10-15)
        };

        private static readonly Dictionary<string, UnlockableDef> _byHero = new Dictionary<string, UnlockableDef>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SurvivorDef> _survivors = new Dictionary<string, SurvivorDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A registered hero's SurvivorDef (free heroes too).</summary>
        public static SurvivorDef? SurvivorOf(string hero) => _survivors.TryGetValue(hero, out SurvivorDef d) ? d : null;

        /// <summary>
        /// Once, before the heroes register: the challenges' names and descriptions (RoR2 reads ACHIEVEMENT_&lt;ID&gt;_NAME /
        /// _DESCRIPTION; the achievements themselves are found by their [RegisterAchievement]) and the shot tracker they use.
        /// </summary>
        public static void Init()
        {
            Challenge(Challenges.VyperRattler.Identifier, "Rattler", $"Fire {Challenges.VyperRattler.Shots} shots within 1 second.");
            Challenge(Challenges.GreyTalonOneShot.Identifier, "Big Game", "Kill a boss with a single hit from full health.");
            Challenge(Challenges.VictorPainBattery.Identifier, "Pain Battery", "In a single run, take 1,000 damage from yourself (not counting Shrines of Blood or falls).");
            ShotTracker.Init();
            RoR2Application.onLoad += UsePortraits;
        }

        private static void Challenge(string id, string name, string description)
        {
            string token = "ACHIEVEMENT_" + id.ToUpperInvariant();
            LanguageAPI.Add(token + "_NAME", name);
            LanguageAPI.Add(token + "_DESCRIPTION", description);
        }

        /// <summary>Each unlock's icon (challenges menu, unlock popup) is the hero's portrait once the catalogs are up.</summary>
        private static void UsePortraits()
        {
            foreach (var pair in _byHero)
            {
                Texture? portrait = SurvivorOf(pair.Key)?.bodyPrefab?.GetComponent<CharacterBody>()?.portraitIcon;
                if (portrait is Texture2D tex)
                    pair.Value.achievementIcon = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }

        public static bool IsSecret(string hero) => SecretWhere.ContainsKey(hero);

        /// <summary>The hero's unlockable (null for a free hero or one that isn't registered).</summary>
        public static UnlockableDef? Of(string hero) => _byHero.TryGetValue(hero, out UnlockableDef d) ? d : null;

        /// <summary>Lock a hero's survivor behind its own unlockable (call once, at load, after the SurvivorDef exists).</summary>
        public static void Lock(string hero, SurvivorDef? survivor)
        {
            if (survivor == null) return;
            _survivors[hero] = survivor;
            if (Array.IndexOf(Free, hero) >= 0) return;
            string id = hero.Replace(" ", "");
            string token = "CURSEDAPPLE_UNLOCK_" + id.ToUpperInvariant();
            var def = ScriptableObject.CreateInstance<UnlockableDef>();
            def.cachedName = "CursedApple.Survivors." + id;
            ((ScriptableObject)def).name = def.cachedName;
            def.nameToken = survivor.displayNameToken;
            def.hidden = false;
            def.sortScore = 200;
            def.achievementIcon = ItemBase.CreatePlaceholderSprite(survivor.primaryColor);

            LanguageAPI.Add(token + "_HOW", SecretWhere.TryGetValue(hero, out string where)
                ? $"Something of theirs waits in {where}, a Deadlock district."
                : "Complete their challenge.");
            def.getHowToUnlockString = () => Language.GetStringFormatted("UNLOCK_VIA_ACHIEVEMENT_FORMAT",
                Language.GetString(survivor.displayNameToken), Language.GetString(token + "_HOW"));
            def.getUnlockedString = () => Language.GetStringFormatted("UNLOCKED_FORMAT", Language.GetString(survivor.displayNameToken));

            ContentAddition.AddUnlockableDef(def);
            survivor.unlockableDef = def;
            _byHero[hero] = def;
        }

        /// <summary>Server: unlock a hero for every player in the run (a secret found, or a challenge done for the lobby).</summary>
        public static void GrantToEveryone(string hero)
        {
            UnlockableDef? def = Of(hero);
            if (def == null || Run.instance == null) return;
            Run.instance.GrantUnlockToAllParticipatingPlayers(def);
        }

        /// <summary>True when every player in the run already has the hero (nothing left to find).</summary>
        public static bool EveryoneHas(string hero)
        {
            UnlockableDef? def = Of(hero);
            return def == null || Run.instance == null || Run.instance.DoesEveryoneHaveThisUnlockableUnlocked(def);
        }
    }
}
