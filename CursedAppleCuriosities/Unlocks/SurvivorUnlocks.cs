using System;
using System.Collections.Generic;
using R2API;
using RoR2;
using CursedAppleCuriosities.Items;
using UnityEngine;

namespace CursedAppleCuriosities.Unlocks
{
    /// <summary>
    /// 3.0 (user): a fresh profile has Abrams and Seven only. Mina, Bebop, Paradox and Lash are map secrets (a silhouette in a
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
            Challenge(Challenges.ViscousCube.Identifier, "The Cube", "Survive a single hit that would have dealt more than your maximum health.");
            Challenge(Challenges.CelesteShowstopper.Identifier, "Showstopper", "Kill 30 enemies within 5 seconds.");
            Challenge(Challenges.ShivKillingBlow.Identifier, "Killing Blow", "Kill a boss while below 10% health.");
            Challenge(Challenges.ApolloFlawless.Identifier, "Flawless Advance", "Complete a teleporter event without taking any damage.");
            // Alternate skills (each unlocked by its hero's own challenge).
            SkillUnlocks.Register(Challenges.AbramsFreightTrain.Skill);
            Challenge(Challenges.AbramsFreightTrain.Identifier, "Abrams: Freight Train", $"As Abrams, slam {Challenges.AbramsFreightTrain.Enemies} or more enemies into a wall with one Shoulder Charge.");
            SkillUnlocks.Register(Challenges.SevenLiveWire.Skill);
            Challenge(Challenges.SevenLiveWire.Identifier, "Seven: Live Wire", $"As Seven, have one Lightning Ball shock {Challenges.SevenLiveWire.Enemies} different enemies.");
            SkillUnlocks.Register(Challenges.CelesteSpotlight.Skill);
            Challenge(Challenges.CelesteSpotlight.Identifier, "Celeste: Spotlight", $"As Celeste, have {Challenges.CelesteSpotlight.Marked} enemies marked by Light Eater at the same time.");
            SkillUnlocks.Register(Challenges.ViscousSplashZone.Skill);
            Challenge(Challenges.ViscousSplashZone.Identifier, "Viscous: Splash Zone", $"As Viscous, kill {Challenges.ViscousSplashZone.Kills} enemies with a single Splatter.");
            SkillUnlocks.Register(Challenges.ViscousStrike.Skill);
            Challenge(Challenges.ViscousStrike.Identifier, "Viscous: Strike!", $"As Viscous, kill {Challenges.ViscousStrike.Kills} enemies in one Goo Ball roll.");
            SkillUnlocks.Register(Challenges.ShivChainReaction.Skill);
            Challenge(Challenges.ShivChainReaction.Identifier, "Shiv: Chain Reaction", $"As Shiv, chain {Challenges.ShivChainReaction.Chain} Killing Blow executes in a row.");
            SkillUnlocks.Register(Challenges.ParadoxDisplacement.Skill);
            Challenge(Challenges.ParadoxDisplacement.Identifier, "Paradox: Temporal Displacement", "As Paradox, swap places with a boss using Paradoxical Swap, then kill it within 5 seconds.");
            SkillUnlocks.Register(Challenges.BebopBombSquad.Skill);
            Challenge(Challenges.BebopBombSquad.Identifier, "Bebop: Bomb Squad", "As Bebop, build up +200% Sticky Bomb damage without dying.");
            SkillUnlocks.Register(Challenges.ApolloEnGarde.Skill);
            Challenge(Challenges.ApolloEnGarde.Identifier, "Apollo: En Garde", "As Apollo, heal 1,000 health from Flawless Advance's perfect stabs in a single stage.");
            // Mastery skins (beat the game or obliterate on Monsoon as the hero).
            SkinUnlocks.Register(Challenges.Masteries.AbramsSkin, "CURSEDAPPLE_ABRAMS_SKIN_GREEN", "skinCursedAppleAbramsGREEN", "skinCursedAppleAbramsDISPLAYGREEN");
            Challenge("CursedAppleAbramsMastery", "Abrams: Mastery", "As Abrams, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.LashSkin, "CURSEDAPPLE_LASH_SKIN_GILDED", "skinCursedAppleLashGILDED", "skinCursedAppleLashDISPLAYGILDED");
            Challenge("CursedAppleLashMastery", "Lash: Mastery", "As Lash, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.GreyTalonSkin, "CURSEDAPPLE_GREYTALON_SKIN_NIGHTOWL", "skinCursedAppleGreyTalonNIGHTOWL", "skinCursedAppleGreyTalonDISPLAYNIGHTOWL");
            Challenge("CursedAppleGreyTalonMastery", "Grey Talon: Mastery", "As Grey Talon, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.VictorSkin, "CURSEDAPPLE_VICTOR_SKIN_GREENMONSTER", "skinCursedAppleVictorGREENMONSTER", "skinCursedAppleVictorDISPLAYGREENMONSTER");
            Challenge("CursedAppleVictorMastery", "Victor: Mastery", "As Victor, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.ShivSkin, "CURSEDAPPLE_SHIV_SKIN_ALOHA", "skinCursedAppleShivALOHA", "skinCursedAppleShivDISPLAYALOHA");
            Challenge("CursedAppleShivMastery", "Shiv: Mastery", "As Shiv, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.BebopSkin, "CURSEDAPPLE_BEBOP_SKIN_GOLDPLATED", "skinCursedAppleBebopGOLDPLATED", "skinCursedAppleBebopDISPLAYGOLDPLATED");
            Challenge("CursedAppleBebopMastery", "Bebop: Mastery", "As Bebop, beat the game or obliterate on Monsoon.");
            // Chrome: its own challenge (user 3.0), hook an enemy at the end of Grapple Arm's reach.
            SkinUnlocks.Register(Challenges.Masteries.ApolloSkin, "CURSEDAPPLE_APOLLO_SKIN_BLACKMORE", "skinCursedAppleApolloBLACKMORE", "skinCursedAppleApolloDISPLAYBLACKMORE");
            Challenge("CursedAppleApolloMastery", "Apollo: Mastery", "As Apollo, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.BebopChromeSkin, "CURSEDAPPLE_BEBOP_SKIN_CHROME", "skinCursedAppleBebopCHROME", "skinCursedAppleBebopDISPLAYCHROME");
            SkinUnlocks.Register(Challenges.Masteries.SevenSkin, "CURSEDAPPLE_SEVEN_SKIN_OVERCHARGED", "skinCursedAppleSevenOVERCHARGED", "skinCursedAppleSevenDISPLAYOVERCHARGED");
            Challenge("CursedAppleSevenMastery", "Seven: Mastery", "As Seven, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.VyperSkin, "CURSEDAPPLE_VYPER_SKIN_CORAL", "skinCursedAppleVyperCORAL", "skinCursedAppleVyperDISPLAYCORAL");
            Challenge("CursedAppleVyperMastery", "Vyper: Mastery", "As Vyper, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.ViscousSkin, "CURSEDAPPLE_VISCOUS_SKIN_BLUE", "skinCursedAppleViscousBLUE", "skinCursedAppleViscousDISPLAYBLUE");
            Challenge("CursedAppleViscousMastery", "Viscous: Mastery", "As Viscous, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.CelesteSkin, "CURSEDAPPLE_CELESTE_SKIN_MINT", "skinCursedAppleCelesteMINT", "skinCursedAppleCelesteDISPLAYMINT");
            Challenge("CursedAppleCelesteMastery", "Celeste: Mastery", "As Celeste, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.MinaSkin, "CURSEDAPPLE_MINA_SKIN_NIGHTSHADE", "skinCursedAppleMinaNIGHTSHADE", "skinCursedAppleMinaDISPLAYNIGHTSHADE");
            Challenge("CursedAppleMinaMastery", "Mina: Mastery", "As Mina, beat the game or obliterate on Monsoon.");
            SkinUnlocks.Register(Challenges.Masteries.ParadoxSkin, "CURSEDAPPLE_PARADOX_SKIN_TEAL", "skinCursedAppleParadoxTEAL", "skinCursedAppleParadoxDISPLAYTEAL");
            Challenge("CursedAppleParadoxMastery", "Paradox: Mastery", "As Paradox, beat the game or obliterate on Monsoon.");
            Challenge(Challenges.BebopLongArm.Identifier, "Bebop: Long Arm of the Law", "As Bebop, hook an enemy with Grapple Arm within 10 m of its maximum reach.");
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
