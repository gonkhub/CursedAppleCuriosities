# Bug hunt report — 2026-10-16

Autonomous pass over the mod (main checkout, 3.0 + the uncommitted 3.0 batch). Fixes are code-only; none have been
playtested in game yet. Each entry: what was wrong, how it showed up, what changed.

## Fixed

### 1. Item hooks rescaled Shiv's Bloodletting ticks a second time
- **Files:** `Items/GlassCannon.cs`, `Items/HeroicAura.cs`, `Items/HuntersAura.cs`, `Items/Inhibitor.cs`, `Items/Spellbreaker.cs`
- **Problem:** Bloodletting ticks are "final" damage (`BypassDamageCalculations`): the share was already priced with every
  multiplier when the hit landed. Four hooks (Battle Vest, Colossus, Ethereal Shift, Long Range) skip such damage via
  `ItemBase.FinalDamage`, but these five didn't, so the ticks were multiplied again.
- **Symptom:** Shiv with Glass Cannon took +25%/stack extra on every tick; with Heroic Aura nearby, or hit by an Inhibited
  enemy, the ticks came out smaller than the yellow bar. Either way the drain stopped matching the bar (the thing fixed in
  ec62629). A low-health tick could also set off Spellbreaker and spend its 30 s cooldown on Shiv's own drain.
- **Fix:** the same one-line `FinalDamage` guard at the top of each hook.

### 2. Seven's gun hit-spark loaded from the wrong asset key
- **File:** `Survivors/Seven/SevenSurvivor.cs`
- **Problem:** `RoR2/Base/Common/VFX/Stylized(Omni)/OmniImpactVFXLightning.prefab` isn't an Addressables key (the real
  one is `RoR2/Base/Common/VFX/OmniImpactVFXLightning.prefab`). The tracer's first key was wrong too, but its second
  spelling worked.
- **Symptom:** an `InvalidKeyException` in the log on every launch, and Seven's bullet impacts quietly used Commando's
  hitspark instead of the lightning sparks the comment describes. **Visual change:** his hits will now show the orange
  lightning impact.
- **Fix:** correct key first; the dead tracer spelling removed.

### 3. Shiv's full-Rage glow never appeared, and its load retried every frame
- **File:** `Survivors/Shiv/ShivRageFx.cs`
- **Problem:** the overlay key (Predatory Instincts' wolf-hat material) was `RoR2/Base/Items/AttackSpeedOnCrit/...`; the real key has no `Items/`
  (the same mistake as the 26 keys fixed on 2026-10-12).
- **Symptom:** at full Rage the red overlay never showed, and because the result stayed null, `Update` called
  `LoadAssetAsync(...).WaitForCompletion()` with the bad key again every frame while Rage was full: a logged error
  per frame and a synchronous load stall each time. (The red light still worked.)
- **Fix:** correct key.

### 4. Mystic Expansion never grew Captain's supply-drop range
- **File:** `Items/MysticExpansionSkills.cs`
- **Problem:** `SetupSupplyDrop.GetPlacementInfo` is a static method; the lookup only searched instance methods, so
  the hook was never added (log: "GetPlacementInfo not found"). For a static method the hook would also have read the
  wrong argument as "self".
- **Fix:** static methods are found too, and a static reader uses its `GameObject` argument as the owner. The separate
  "UpdateTrajectoryInfo: no read of maxDistance" warning was noise (its `ref` wrapper overload doesn't read the field,
  the real overload is hooked fine); it now only warns when no overload reads the field.

### 5. Spellslinger's cooldown reduction never worked
- **File:** `Items/Spellslinger.cs`
- **Problem:** it subtracted from R2API's legacy `args.cooldownMultAdd`. The installed R2API (RecalculateStats) adds that
  field as `Math.Max(0f, cooldownMultAdd)`, so any negative value is thrown away.
- **Symptom:** the stacks gave their attack speed, but skill cooldowns never got shorter (the "4% shorter cooldowns
  per stack, up to 50%" in the tooltip did nothing).
- **Fix:** `args.allSkills.cooldownMultiplier *= 1 - min(stacks × 4%, 50%)`, the current API's multiplier on the final
  cooldown. **Heads-up:** this makes Spellslinger noticeably stronger than what you've been playing (it now does what
  it says). The cap and per-stack values are unchanged. The mod has no other negative cooldown values (checked).

### 6. Berserker gained Rage from blocked hits
- **File:** `Items/Berserker.cs`
- **Problem:** the stack was added after any hit with damage > 0, including rejected ones (Tougher Times blocks,
  Ethereal Shift, Vyper's Petrify, Apollo's parry), which keep their damage number.
- **Fix:** rejected hits don't count. (Judgment call: the tooltip says "each time you take damage", and a blocked hit
  deals none. Easy to revert if you liked it.)

### 7. Counterspell's ward was wasted on Bloodletting ticks
- **File:** `Items/Counterspell.cs`
- **Problem:** the ward skips damage over time so it saves a real hit, but it only checked vanilla DoTs (`dotIndex`).
  The mod's own ticks (Bloodletting, Toxic Bullets poison) carry the `DoT` flag with no index.
- **Symptom:** Shiv casting Counterspell with any Bloodletting pending lost the ward to his next tick (a fraction of a
  second later), so it never blocked the hit it was cast for. Apollo's parry already checked the flag correctly.
- **Fix:** the flag counts too.

### 8. Thirteen items procced on hits the target blocked
- **Files:** Burst Fire, Inhibitor, Intensifying Magazine, Lifestrike, Lucky Shot, Mystic Shot, Radiant Regen,
  Restorative Shot, Ricochet, Siphon Bullets, Spirit Burn, Tankbuster, Toxic Bullets
- **Problem:** vanilla attacks call `GlobalEventManager.OnHitEnemy` even for a rejected hit; vanilla's "no procs on
  a blocked hit" check sits inside the original method, which these hooks call first and then carry on regardless.
- **Symptom:** shooting something that blocks the hit still triggered the items: Vyper's Petrified enemies, bosses in
  an invulnerable phase (Mithrix, False Son, Voidling), monsters with Tougher Times (Evolution, Void Fields,
  Simulacrum). E.g. Siphon Bullets stole max HP, Toxic built up, Tankbuster/Mystic Shot/Spirit Burn/Lucky Shot fired their
  bonus hits, Restorative Shot and Lifestrike healed, Burst Fire and Radiant Regen gave their buffs.
- **Fix:** each hook returns on a rejected hit. (Lightning Scroll and Bebop's hook already did. The heroes' own hit
  helpers that skip the check are now covered, since every proc hook checks it.)

### 9. The A–Z roster put marked-up names out of order
- **File:** `Survivors/RosterOrder.cs` (part of the uncommitted 3.0 batch from another session; edited in place, not committed)
- **Problem:** names were compared raw. From the dev log: `<color=#2150AF>Nero</color>, <color=#B70027>Dante</color>, Abrams,
  ... Vyper, Wanderer, Warlock, 「V??oid Fiend』`: the two colour-tagged mod survivors sorted first, vanilla Void Fiend
  (its English name really is 「V??oid Fiend』) last.
- **Fix:** the sort key drops rich-text tags and anything that isn't a letter, digit or space (Nero under N, Dante under
  D, Void Fiend between Viscous and Vyper).

## Status
- All fixes compile (0 errors). **Not deployed, not committed, not playtested.** The checkout is shared with other
  sessions' uncommitted work (the "Bug fixes and survivor updates" session is editing Apollo, Mina, Abrams, the map,
  unlocks and Lifestrike/Spirit Snatch/Sprint Boots/Fortitude right now; we've agreed who owns which files), so a
  Debug build would deploy their work-in-progress too.
- Files only I touched: the items in #1, #5–#8, `MysticExpansionSkills.cs`, `ShivRageFx.cs`. Mixed with other
  sessions' changes: `SevenSurvivor.cs`, `RosterOrder.cs`, `Lifestrike.cs`.

## Your decisions, applied (compiled, not playtested)
- **Soul Urn** (`Interactables/SoulUrn.cs`): only hits from enemies drop it. Self-damage (Blood Tribute, Victor's
  payments), falls, allies and Shiv's Bloodletting ticks no longer count. The ticks needed their own exclusion: they
  name the enemy who dealt the original hit as their attacker.
- **Curiosity shop** (`Stages/DeadlockMap/MapShop.cs`): the "Not enough gold" message is gone, since unaffordable
  items are already greyed out.
- **Focus Lens** (`Items/FocusLens.cs`): records the health, shield and barrier each hit actually took (after crits,
  armour and every bonus), and the echo lands as exactly 50% of that. Before, the echo hit went through armour again,
  which would have shaved it below 50%.

## Checked, no bug found
- Kill-triggered features use the obsolete `onCharacterDeathGlobal`: it's still raised (it forwards to
  `onDeathProcServer`), so voice lines, challenges and Bebop's stacks still work.
- Every other Addressables key in the code was checked against the game's catalog: all other calls have a valid key
  (the misses are deliberate second spellings). Ivy's (shelved) Golem stone material key is wrong, left alone.
- Read through: Colossus, Trophy Collector, Toxic Bullets, Heroic Aura, Healing Tempo, Frenzy, Torment Pulse, Siphon
  Bullets, Kinetic Dash, Radiant Regen, Duration Extender, Mystic Expansion; the unlock framework and the Abrams, Vyper,
  Grey Talon, Victor, Viscous, Celeste, Shiv and Apollo challenges.
- Also read: Cursed Broker (offers, curse penalties, ledger), Soul Urn, the map shop and stage picking, every network
  message (all 61 write and read the same fields in the same order), the hero hit helpers, Abrams' server code,
  Shiv's execute, Victor's revive/payments, Celeste's mark, the uncommitted model-skin, Rose Buckler and Petrify-look code.
- The compiler's nullable warnings were all false alarms.
