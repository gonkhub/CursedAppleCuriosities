using System.Collections.Generic;
using RoR2;

namespace CursedAppleCuriosities.Broker
{
    /// <summary>
    /// What a curse does to each item. A cursed item's copies count as <c>copies × Mult + Add</c> (the game's
    /// "effective" stack count, which every item reads), so an item's own per-stack scaling delivers the buff.
    /// Mult/Add are picked per item so one cursed copy lands near double the item's effect: linear items ×2, items
    /// whose first copy is worth more than the rest (or that scale hyperbolically) get an extra copy or ×3. Items whose
    /// stacking can't double them (Dio's Best Friend, Pluripotent Larva) keep their count and get a custom effect.
    /// <c>tools/gen_curse_audit.py</c> writes a per-item audit table from these rules.
    ///
    /// Keyed by <see cref="ItemDef.name"/> (vanilla: the RoR2Content/DLCxContent field name; ours: ITEM_CURSEDAPPLE_*).
    /// Items not listed (other mods) count double with a generic description.
    /// </summary>
    public readonly struct CurseRule
    {
        public readonly int Mult;
        public readonly int Add;
        public readonly string Improves;

        public CurseRule(int mult, int add, string improves) { Mult = mult; Add = add; Improves = improves; }

        public int Apply(int copies) => copies <= 0 ? copies : (int)System.Math.Min(int.MaxValue, (long)copies * Mult + Add);

        /// <summary>"Counts as 2× copies (+1)", for the tooltip.</summary>
        public string CountText()
        {
            if (Mult == 1 && Add == 0) return "";
            string s = Mult == 1 ? "" : $"Counts as {Mult}× its copies";
            if (Add > 0) s += (s.Length == 0 ? "Counts as " : ", ") + $"+{Add} extra cop{(Add == 1 ? "y" : "ies")}";
            return s + ".";
        }
    }

    public static class CurseRules
    {
        public static readonly CurseRule Default = new CurseRule(2, 0, "Every per-stack effect of this item is doubled.");

        /// <summary>Custom-handled: each copy revives twice (see <see cref="CurseLedger"/>).</summary>
        public const string ExtraLife = "ExtraLife", ExtraLifeVoid = "ExtraLifeVoid";

        // Never offered: no per-stack effect worth cursing, or consumable bookkeeping the count would break.
        private static readonly HashSet<string> Excluded = new HashSet<string>
        {
            "RegeneratingScrap", "RegeneratingScrapConsumed", "MoneyLoan", "MasterBattery", "MasterCore",
            "PowerCube", "PowerOrbSphere", "PowerPyramid", "Junk", "LowHealthOrbSpawnerConsumed",
            "HealingPotion", "HealingPotionConsumed", "FragileDamageBonusConsumed", // consumed on use: extra copies add nothing
        };

        private static readonly Dictionary<string, CurseRule> Rules = new Dictionary<string, CurseRule>
        {
            // ── Vanilla white ─────────────────────────────────────────────────────────────────────────────
            ["Syringe"]                   = new CurseRule(2, 0, "Soldier's Syringe's attack speed is doubled."),
            ["Bear"]                      = new CurseRule(2, 1, "Tougher Times blocks roughly twice as often."),
            ["Tooth"]                     = new CurseRule(2, 0, "Monster Tooth orbs heal twice as much."),
            ["CritGlasses"]               = new CurseRule(2, 0, "Lens-Maker's Glasses' crit chance is doubled."),
            ["Hoof"]                      = new CurseRule(2, 0, "Paul's Goat Hoof's speed is doubled."),
            ["Crowbar"]                   = new CurseRule(2, 0, "Crowbar's bonus damage is doubled."),
            ["Mushroom"]                  = new CurseRule(2, 0, "Bustling Fungus heals twice as much in a wider zone."),
            ["BleedOnHit"]                = new CurseRule(2, 0, "Tri-Tip Dagger's bleed chance is doubled."),
            ["WardOnLevel"]               = new CurseRule(3, 0, "Warbanner's banner is far larger."),
            ["IgniteOnKill"]              = new CurseRule(2, 1, "Gasoline's burst burns harder in a wider radius."),
            ["StunChanceOnHit"]           = new CurseRule(2, 1, "Stun Grenade stuns about twice as often."),
            ["Firework"]                  = new CurseRule(2, 0, "Bundle of Fireworks launches twice as many."),
            ["SprintBonus"]               = new CurseRule(2, 0, "Energy Drink's sprint speed is doubled."),
            ["SecondarySkillMagazine"]    = new CurseRule(2, 0, "Backup Magazine gives twice the Secondary charges."),
            ["StickyBomb"]                = new CurseRule(2, 0, "Sticky Bomb's attach chance is doubled."),
            ["TreasureCache"]             = new CurseRule(2, 0, "Rusted Key lockboxes roll rarer items."),
            ["BossDamageBonus"]           = new CurseRule(2, 0, "Armor-Piercing Rounds' boss damage is doubled."),
            ["NearbyDamageBonus"]         = new CurseRule(2, 0, "Focus Crystal's close-range damage is doubled."),
            ["Medkit"]                    = new CurseRule(2, 0, "Medkit heals twice as much."),
            ["FlatHealth"]                = new CurseRule(2, 0, "Bison Steak's health is doubled."),
            ["PersonalShield"]            = new CurseRule(2, 0, "Personal Shield Generator's shield is doubled."),
            ["HealWhileSafe"]             = new CurseRule(2, 0, "Cautious Slug's out-of-combat regeneration is doubled."),
            ["ArmorPlate"]                = new CurseRule(2, 0, "Repulsion Armor Plate blocks twice the damage per hit."),
            ["BarrierOnKill"]             = new CurseRule(2, 0, "Topaz Brooch's barrier per kill is doubled."),
            ["AttackSpeedAndMoveSpeed"]   = new CurseRule(2, 0, "Mocha's speed bonuses are doubled."),
            ["FragileDamageBonus"]        = new CurseRule(2, 0, "Delicate Watch's damage bonus is doubled."),
            ["OutOfCombatArmor"]          = new CurseRule(2, 0, "Oddly-shaped Opal's armor is doubled."),
            ["GoldOnHurt"]                = new CurseRule(2, 0, "Roll of Pennies pays twice the gold."),
            ["AttackSpeedPerNearbyAllyOrEnemy"] = new CurseRule(2, 0, "Bolstering Lantern's per-target bonus is doubled."),
            ["SpeedBoostPickup"]          = new CurseRule(2, 0, "Elusive Antlers' shards grant twice the speed."),
            ["IncreaseDamageOnMultiKill"] = new CurseRule(2, 0, "Chronic Expansion's damage per stack is doubled."),
            ["DelayedDamage"]             = new CurseRule(2, 0, "Warped Echo delays twice the damage."),
            ["BearVoid"]                  = new CurseRule(2, 0, "Safer Spaces recharges twice as fast."),
            ["BleedOnHitVoid"]            = new CurseRule(2, 0, "Needletick's collapse chance is doubled."),
            ["CritGlassesVoid"]           = new CurseRule(2, 0, "Lost Seer's Lenses' instakill chance is doubled."),
            ["MushroomVoid"]              = new CurseRule(2, 0, "Weeping Fungus heals twice as much while sprinting."),
            ["TreasureCacheVoid"]         = new CurseRule(2, 0, "Encrusted Key caches roll rarer items."),

            // ── Vanilla green ─────────────────────────────────────────────────────────────────────────────
            ["Missile"]                   = new CurseRule(2, 0, "AtG missiles deal double damage."),
            ["ExplodeOnDeath"]            = new CurseRule(2, 1, "Wisp explosions deal ~2.6× damage in a wider blast."),
            ["Feather"]                   = new CurseRule(2, 0, "Twice as many extra jumps."),
            ["ChainLightning"]            = new CurseRule(3, 0, "Ukulele arcs to 3× as many targets with much longer reach (proc chance unchanged)."),
            ["Seed"]                      = new CurseRule(2, 0, "Leeching Seed heals twice as much per hit."),
            ["AttackSpeedOnCrit"]         = new CurseRule(2, 1, "Predatory Instincts stacks more than twice as high (7 frenzy stacks from one copy)."),
            ["SprintOutOfCombat"]         = new CurseRule(2, 0, "Red Whip's out-of-combat speed is doubled."),
            ["Phasing"]                   = new CurseRule(2, 0, "Stealthkit recharges far faster."),
            ["HealOnCrit"]                = new CurseRule(2, 1, "Critical strikes heal twice as much."),
            ["EquipmentMagazine"]         = new CurseRule(2, 0, "Twice the extra equipment charges and cooldown reduction."),
            ["Infusion"]                  = new CurseRule(2, 0, "Twice the health per kill and twice the cap."),
            ["Bandolier"]                 = new CurseRule(2, 2, "Ammo packs drop about twice as often."),
            ["WarCryOnMultiKill"]         = new CurseRule(2, 1, "Pauldron's frenzy lasts over twice as long."),
            ["Squid"]                     = new CurseRule(2, 0, "Squid Polyps attack twice as fast."),
            ["Thorns"]                    = new CurseRule(2, 1, "Razorwire hits twice as many targets at longer range."),
            ["ExecuteLowHealthElite"]     = new CurseRule(2, 1, "Old Guillotine's execute threshold is roughly doubled."),
            ["EnergizedOnEquipmentUse"]   = new CurseRule(2, 1, "War Horn's attack speed lasts twice as long."),
            ["TPHealingNova"]             = new CurseRule(2, 0, "Lepton Daisy releases twice as many healing novas."),
            ["SlowOnHit"]                 = new CurseRule(2, 0, "Chronobauble's slow lasts twice as long."),
            ["JumpBoost"]                 = new CurseRule(2, 0, "Wax Quail's boost is doubled."),
            ["DeathMark"]                 = new CurseRule(2, 0, "Death Mark lasts twice as long."),
            ["FireRing"]                  = new CurseRule(2, 0, "Kjaro's Band deals double damage."),
            ["IceRing"]                   = new CurseRule(2, 0, "Runald's Band deals double damage."),
            ["BonusGoldPackOnKill"]       = new CurseRule(2, 0, "Ghor's Tome drops treasure twice as often."),
            ["SprintArmor"]               = new CurseRule(2, 0, "Rose Buckler gives double armor while sprinting."),
            ["MoveSpeedOnKill"]           = new CurseRule(2, 0, "Hunter's Harpoon's speed burst lasts twice as long."),
            ["StrengthenBurn"]            = new CurseRule(2, 0, "Ignition Tank's burn bonus is doubled."),
            ["PrimarySkillShuriken"]      = new CurseRule(2, 1, "Three shurikens per copy at higher damage."),
            ["FreeChest"]                 = new CurseRule(2, 0, "Shipping Request deliveries roll rarer items."),
            ["TeleportOnLowHealth"]       = new CurseRule(2, 0, "Unstable Transmitter's blast is stronger and wider."),
            ["ExtraShrineItem"]           = new CurseRule(2, 0, "Chance Doll's upgrade chance is doubled."),
            ["LowerPricedChests"]         = new CurseRule(2, 0, "Sale Star gives twice the extra items."),
            ["IncreasePrimaryDamage"]     = new CurseRule(2, 0, "Luminous Shot's charged damage is doubled."),
            ["TriggerEnemyDebuffs"]       = new CurseRule(2, 0, "Noxious Thorn spreads debuffs twice as far."),
            ["ExtraStatsOnLevelUp"]       = new CurseRule(2, 0, "Prayer Beads store twice the stats."),
            ["KnockBackHitEnemies"]       = new CurseRule(2, 0, "Breaching Fin relaunches enemies twice as many times."),

            // ── Vanilla red ───────────────────────────────────────────────────────────────────────────────
            ["Behemoth"]                  = new CurseRule(2, 1, "Brilliant Behemoth's explosions are over twice as wide."),
            ["Dagger"]                    = new CurseRule(2, 0, "Ceremonial Daggers deal double damage."),
            ["Icicle"]                    = new CurseRule(2, 0, "Frost Relic's storm radius is doubled."),
            ["GhostOnKill"]               = new CurseRule(2, 0, "Happiest Mask ghosts last twice as long."),
            ["Clover"]                    = new CurseRule(2, 0, "57 Leaf Clover's luck is doubled."),
            ["HeadHunter"]                = new CurseRule(2, 0, "Stolen elite powers last twice as long."),
            ["KillEliteFrenzy"]           = new CurseRule(2, 0, "Brainstalks' no-cooldown window lasts twice as long."),
            ["UtilitySkillMagazine"]      = new CurseRule(2, 0, "Hardlight Afterburner gives twice the utility charges."),
            ["BounceNearby"]              = new CurseRule(2, 1, "Meat Hook's chance and hook count are roughly doubled."),
            ["NovaOnHeal"]                = new CurseRule(2, 0, "N'kuhana's Opinion stores twice the soul energy."),
            ["ShockNearby"]               = new CurseRule(2, 0, "Tesla Coil zaps twice as many enemies."),
            ["Talisman"]                  = new CurseRule(2, 0, "Soulbound Catalyst refunds twice the cooldown per kill."),
            ["AlienHead"]                 = new CurseRule(2, 0, "Alien Head's cooldown reduction applies twice."),
            ["IncreaseHealing"]           = new CurseRule(2, 0, "Rejuvenation Rack's healing bonus is doubled."),
            ["BarrierOnOverHeal"]         = new CurseRule(2, 0, "Aegis converts twice the overheal into barrier."),
            ["FallBoots"]                 = new CurseRule(2, 0, "H3AD-5T v2 recharges twice as fast."),
            ["LaserTurbine"]              = new CurseRule(2, 0, "Resonance Disc deals double damage."),
            ["Plant"]                     = new CurseRule(2, 1, "Desk Plant fruit heals twice as much."),
            ["ArmorReductionOnHit"]       = new CurseRule(2, 0, "Shattering Justice's armor shred lasts twice as long."),
            ["CaptainDefenseMatrix"]      = new CurseRule(2, 0, "Microbots shoot down twice as many projectiles."),
            ["CritDamage"]                = new CurseRule(2, 0, "Laser Scope's crit damage bonus is doubled."),
            ["MoreMissile"]               = new CurseRule(2, 0, "Pocket I.C.B.M.'s missile damage bonus is doubled."),
            ["ImmuneToDebuff"]            = new CurseRule(2, 0, "Ben's Raincoat blocks twice as many debuffs."),
            ["RandomEquipmentTrigger"]    = new CurseRule(2, 0, "Bottled Chaos triggers twice as many equipment effects."),
            ["PermanentDebuffOnHit"]      = new CurseRule(2, 0, "Symbiotic Scorpion shreds twice the armor per hit."),
            ["DroneWeapons"]              = new CurseRule(2, 0, "Spare Drone Parts' drone bonuses are doubled."),
            ["StunAndPierce"]             = new CurseRule(2, 0, "Electric Boomerang deals double damage."),
            ["BoostAllStats"]             = new CurseRule(2, 0, "Growth Nectar's buff cap is doubled."),
            ["MeteorAttackOnHighDamage"]  = new CurseRule(2, 0, "Runic Lens meteors are twice as likely and stronger."),
            ["ItemDropChanceOnKill"]      = new CurseRule(2, 0, "Sonorous Whispers' item drop chance is doubled."),
            ["BarrageOnBoss"]             = new CurseRule(2, 0, "War Bonds' barrage is doubled."),
            [ExtraLife]                   = new CurseRule(1, 0, "Each copy revives you twice before it is consumed."),

            // ── Vanilla boss ──────────────────────────────────────────────────────────────────────────────
            ["Knurl"]                     = new CurseRule(2, 0, "Titanic Knurl's health and regeneration are doubled."),
            ["BeetleGland"]               = new CurseRule(2, 0, "Queen's Gland summons twice as many guards."),
            ["Pearl"]                     = new CurseRule(2, 0, "Pearl's max health bonus is doubled."),
            ["ShinyPearl"]                = new CurseRule(2, 0, "Irradiant Pearl's stat bonus is doubled."),
            ["TitanGoldDuringTP"]         = new CurseRule(2, 0, "Aurelionite is twice as strong."),
            ["SprintWisp"]                = new CurseRule(2, 0, "Little Disciple's wisps deal double damage."),
            ["SiphonOnLowHealth"]         = new CurseRule(2, 0, "Mired Urn hits twice as many targets."),
            ["FireballsOnHit"]            = new CurseRule(2, 0, "Molten Perforator deals double damage."),
            ["LightningStrikeOnHit"]      = new CurseRule(2, 0, "Charged Perforator deals double damage."),
            ["BleedOnHitAndExplode"]      = new CurseRule(2, 0, "Shatterspleen's explosions are doubled."),
            ["RoboBallBuddy"]             = new CurseRule(2, 0, "Empathy Cores are twice as strong."),
            ["ParentEgg"]                 = new CurseRule(2, 0, "Planula heals twice as much when hit."),
            ["NovaOnLowHealth"]           = new CurseRule(2, 0, "Genesis Loop recharges twice as fast."),
            ["MinorConstructOnKill"]      = new CurseRule(2, 0, "Defense Nucleus constructs are doubled."),

            // ── Vanilla void ──────────────────────────────────────────────────────────────────────────────
            ["MissileVoid"]               = new CurseRule(2, 0, "Plasma Shrimp missiles deal double damage."),
            ["ChainLightningVoid"]        = new CurseRule(2, 0, "Polylute strikes twice as many times."),
            ["SlowOnHitVoid"]             = new CurseRule(2, 1, "Tentabauble roots more often and for longer."),
            ["EquipmentMagazineVoid"]     = new CurseRule(2, 0, "Lysate Cell gives twice the special charges."),
            ["ElementalRingVoid"]         = new CurseRule(2, 0, "Singularity Band deals double damage."),
            ["ExplodeOnDeathVoid"]        = new CurseRule(2, 0, "Voidsent Flame deals double damage."),
            ["CloverVoid"]                = new CurseRule(2, 0, "Benthic Bloom upgrades twice as many items."),
            [ExtraLifeVoid]               = new CurseRule(1, 0, "Each copy revives you twice before it is consumed."),
            ["VoidMegaCrabItem"]          = new CurseRule(2, 0, "Newly Hatched Zoea grows twice the allies."),

            // ── Alloyed Collective / later DLC (offered only if their tier is green or rarer) ────────────
            ["DronesDropDynamite"]        = new CurseRule(2, 0, "Box of Dynamite's blasts are much stronger."),
            ["JumpDamageStrike"]          = new CurseRule(2, 0, "Faraday Spur's discharge is stronger and wider."),
            ["ShieldBooster"]             = new CurseRule(2, 0, "Kinetic Dampener's armor and pulse are doubled."),
            ["ExtraEquipment"]            = new CurseRule(2, 0, "Twice the extra equipment slots."),
            ["Duplicator"]                = new CurseRule(2, 0, "Duplicated items last twice as long."),
            ["PhysicsProjectile"]         = new CurseRule(2, 0, "Orphaned Core hits for double damage."),
            ["SharedSuffering"]           = new CurseRule(2, 0, "Networked Suffering infects twice as many enemies."),
            ["ShockDamageAura"]           = new CurseRule(2, 0, "Faulty Conductor pulses much more often."),
            ["SpeedOnPickup"]             = new CurseRule(2, 0, "Collector's Compulsion stacks twice as high."),
            ["CritAtLowerElevation"]      = new CurseRule(2, 0, "Hiker's Boots grant double crit per stack."),
            ["BarrierOnCooldown"]         = new CurseRule(2, 0, "Eclipse Lite grants double barrier."),
            ["RechargeableJump"]          = new CurseRule(2, 0, "Earthbound Vessel's extra jumps are doubled."),
            ["MushShot"]                  = new CurseRule(2, 0, "Mycelium Cap's spores hit much harder."),
            ["WispOnKill"]                = new CurseRule(2, 0, "Whispering Fragment holds more spirits."),
            ["BlightZone"]                = new CurseRule(2, 0, "Corpse Lily's cloud is stronger and grows faster."),
            ["SprintDamage"]              = new CurseRule(2, 0, "Ritual Helmet's skulls hit harder and charge faster."),
            ["GhostBlades"]               = new CurseRule(2, 0, "Stained Scimitar's lifesteal and blade cap are doubled."),
            ["CausticPheromones"]         = new CurseRule(2, 0, "Infernal Mark's soul-brand lasts twice as long."),
            ["DreamDemon"]                = new CurseRule(2, 0, "Nightmare Demon explosions deal double damage."),
            ["SanguineChalice"]           = new CurseRule(2, 0, "Sanguine Chalice's bleed healing is doubled."),
            ["VengefulSpirit"]            = new CurseRule(2, 0, "Twice as many protective spirits."),
            ["VengefulSpiritRageMode"]    = new CurseRule(2, 0, "Dormant Spirit's rage lasts twice as long."),
            ["LowHealthOrbSpawner"]       = new CurseRule(2, 0, "Dream Crucible's intercept chance is doubled."),
            ["GoldenGoose"]               = new CurseRule(2, 0, "Totem of Avarice's radius and gold bonus are doubled."),
            ["VileCenser"]                = new CurseRule(2, 0, "Vile Censer's tendrils hit harder and reach further."),

            // ── Cursed Apple Curiosities (white) ──────────────────────────────────────────────────────────
            ["ITEM_CURSEDAPPLE_LONGRANGE"]         = new CurseRule(2, 0, "Long Range's distance bonus is doubled."),
            ["ITEM_CURSEDAPPLE_KINETICDASH"]       = new CurseRule(2, 0, "Kinetic Dash's attack speed is doubled."),
            ["ITEM_CURSEDAPPLE_INTENSIFYINGMAGAZINE"] = new CurseRule(2, 1, "Intensifying Magazine ramps to about double damage."),
            ["ITEM_CURSEDAPPLE_RESTORATIVESHOT"]   = new CurseRule(2, 0, "Restorative Shot heals twice as much."),
            ["ITEM_CURSEDAPPLE_SPRINTBOOTS"]       = new CurseRule(2, 1, "Sprint Boots ramp to about double the speed."),
            ["ITEM_CURSEDAPPLE_BATTLEVEST"]        = new CurseRule(2, 0, "Battle Vest's bonus damage is doubled."),
            ["ITEM_CURSEDAPPLE_MYSTICSHOT"]        = new CurseRule(2, 0, "Mystic Shot's burst deals double damage."),
            ["ITEM_CURSEDAPPLE_MYSTICEXPANSION"]   = new CurseRule(2, 0, "Mystic Expansion's radius and range bonus is doubled."),
            ["ITEM_CURSEDAPPLE_DURATIONEXTENDER"]  = new CurseRule(2, 0, "Duration Extender's bonus duration is doubled."),
            ["ITEM_CURSEDAPPLE_TROPHYCOLLECTOR"]   = new CurseRule(2, 0, "Trophy Collector holds twice as many Trophies."),

            // ── Cursed Apple Curiosities ──────────────────────────────────────────────────────────────────
            ["ITEM_CURSEDAPPLE_SPIRITSNATCH"]  = new CurseRule(2, 0, "Spirit Snatch grants double barrier per hit."),
            ["ITEM_CURSEDAPPLE_BERSERKER"]     = new CurseRule(2, 1, "Berserker's Rage cap is doubled."),
            ["ITEM_CURSEDAPPLE_BURSTFIRE"]     = new CurseRule(2, 0, "Burst Fire's speed bonuses are doubled."),
            ["ITEM_CURSEDAPPLE_HEROICAURA"]    = new CurseRule(2, 0, "Heroic Aura's damage reduction and attack speed are doubled."),
            ["ITEM_CURSEDAPPLE_TORMENTPULSE"]  = new CurseRule(2, 1, "Torment Pulse hits twice the targets at much longer range."),
            ["ITEM_CURSEDAPPLE_HUNTERSAURA"]   = new CurseRule(2, 0, "Hunter's Aura's damage amplification is doubled."), // linear per copy; range doesn't stack
            ["ITEM_CURSEDAPPLE_LIFESTRIKE"]    = new CurseRule(2, 1, "Lifestrike heals twice as much per hit."),
            ["ITEM_CURSEDAPPLE_FORTITUDE"]     = new CurseRule(2, 0, "Fortitude's health and regeneration are doubled."),
            ["ITEM_CURSEDAPPLE_RADIANTREGEN"]  = new CurseRule(2, 0, "Radiant Regen's Radiance cap is doubled."),
            ["ITEM_CURSEDAPPLE_TOXICBULLETS"]  = new CurseRule(2, 0, "Toxic Bullets' poison deals double damage."),
            ["ITEM_CURSEDAPPLE_TANKBUSTER"]    = new CurseRule(2, 1, "Tankbuster's burst deals double damage."),
            ["ITEM_CURSEDAPPLE_LIGHTNINGSCROLL"] = new CurseRule(2, 1, "Lightning Scroll's strike deals double damage."),
            ["ITEM_CURSEDAPPLE_LUCKYSHOT"]     = new CurseRule(3, 0, "Lucky Shot's second-crit chance is roughly doubled."),
            ["ITEM_CURSEDAPPLE_SIPHONBULLETS"] = new CurseRule(2, 0, "Siphon Bullets can steal twice the health per enemy."),
            ["ITEM_CURSEDAPPLE_FRENZY"]        = new CurseRule(2, 1, "Frenzy kicks in much earlier (a far wider health window)."),
            ["ITEM_CURSEDAPPLE_SPELLSLINGER"]  = new CurseRule(2, 0, "Spellslinger's stack cap is doubled."),
            ["ITEM_CURSEDAPPLE_SPELLBREAKER"]  = new CurseRule(2, 0, "Spellbreaker's cooldown is halved."),
            ["ITEM_CURSEDAPPLE_HEALINGTEMPO"]  = new CurseRule(3, 0, "Healing Tempo's healing bonus and Tempo cap are nearly doubled."),
            ["ITEM_CURSEDAPPLE_RICOCHET"]      = new CurseRule(2, 1, "Ricochet bounces to twice as many enemies, further."),
            ["ITEM_CURSEDAPPLE_INHIBITOR"]     = new CurseRule(3, 0, "Inhibitor's damage reduction on enemies is doubled."),
            ["ITEM_CURSEDAPPLE_SPIRITBURN"]    = new CurseRule(2, 0, "Spirit Burn's ignite deals double damage."),
        };

        public static CurseRule Get(ItemIndex index)
        {
            ItemDef def = ItemCatalog.GetItemDef(index);
            return def != null && Rules.TryGetValue(def.name, out CurseRule r) ? r : Default;
        }

        public static bool IsListed(ItemDef def) => Rules.ContainsKey(def.name);

        /// <summary>Any normal tier but lunar, a real item the player can hold and lose, and not on the excluded list.</summary>
        public static bool IsEligible(ItemDef? def)
        {
            if (def == null || def.hidden || !def.canRemove || Excluded.Contains(def.name)) return false;
            if (def.ContainsTag(ItemTag.Scrap) || def.ContainsTag(ItemTag.WorldUnique)) return false;
            switch (def.tier)
            {
                case ItemTier.Tier1: case ItemTier.Tier2: case ItemTier.Tier3: case ItemTier.Boss:
                case ItemTier.VoidTier1: case ItemTier.VoidTier2: case ItemTier.VoidTier3: case ItemTier.VoidBoss:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Penalty severity band: 0 white, 1 green, 2 red/boss/void red (rarer items roll harsher penalties).</summary>
        public static int SeverityBand(ItemDef def)
        {
            switch (def.tier)
            {
                case ItemTier.Tier1: case ItemTier.VoidTier1: return 0;
                case ItemTier.Tier2: case ItemTier.VoidTier2: return 1;
                default: return 2;
            }
        }
    }
}
