# Cursed Apple Curiosities — Project Handoff

A BepInEx plugin for **Risk of Rain 2** (Survivors of the Void, build 912) that adds custom
items and equipment **adapted from Deadlock's item catalogue**. The name is homage: Deadlock's
map is the "Cursed Apple" and items are bought from a "curiosity shop."

**State:** builds clean (0 errors), **28 items + 4 equipment**, v0.9.1.
Last verified: 2026-06-23.

---

## Build / deploy / test / distribute

- **Build (Debug, quick):**
  `dotnet build "CursedAppleCuriosities\CursedAppleCuriosities.csproj"`
  Post-build target auto-deploys the DLL to the r2modman profile.
- **Build (Release, distributable):**
  `dotnet build "CursedAppleCuriosities\CursedAppleCuriosities.csproj" -c Release`
  Also auto-packages the Thunderstore zip to `dist\`.
- **Profile path:** `C:\r2heimman\RiskOfRain2\profiles\Alloyed Goonlective 3-24`
- **Game Managed DLLs:** `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed`
- **Log (read after each launch):** `<profile>\BepInEx\LogOutput.log`
- **Testing in-game (DebugToolkit console, Ctrl+Alt+`):**
  - Items: `give_item <LangToken>` (e.g. `give_item Berserker`, `give_item CURIOSITYCATALOG`)
  - Equipment: `give_equip <name>` (NOT `give_item`); `list_equip` lists all
  - Lunars don't appear in normal chests — use `give_item GlassCannon` or a Bazaar run
- **Distribute:** the `dist\*.zip` is Thunderstore-format. Friends import via r2modman →
  Settings → Import local mod. **Do NOT also import into the active dev profile** — duplicate-GUID conflict.
  ⚠️ Bump `packaging\manifest.json` `version_number` before sharing (multiplayer requires matching versions).

---

## Architecture (one item per file, auto-discovered)

- **`ItemBase`** (Items/ItemBase.cs) — abstract base. Each item overrides name/tokens/tier/tags,
  optional `IconFileName` (embedded PNG). Handles lang tokens, ItemDef creation, registration via
  `ItemAPI.Add`. Auto-discovered by reflection in `CursedAppleCuriosities.cs`.
- **`EquipmentBase`** (Items/EquipmentBase.cs) — sibling for actives. `OnUse(EquipmentSlot)` runs
  on activation; the plugin dispatches via an `EquipmentSlot.PerformEquipmentAction` hook.
- **Buffs:** registered with `ContentAddition.AddBuffDef()` in each item's `Init()`. Buff icons
  use `ItemBase.BuffIcon(IconFileName, color)`. **Never use `IContentPackProvider` for BuffDefs** (silently fails).
- **Tiers:** resolved in an `On.ItemTierCatalog.Init` hook (NOT at Awake — the white-tier bug).
  Lunar tier works the same way.
- **Icons:** item PNGs live in `CursedAppleCuriosities\Resources\` (128px), embedded via the
  csproj `<EmbeddedResource>` list. New art needs both the file AND a csproj entry.

- **Config (v0.9.0+):** each item/equipment binds its **headline balance knob(s)** from the
  `ConfigFile config` passed to `Init`, **before `CreateLang()`** (descriptions interpolate the
  bound values). Sections are grouped by tier: `[White] [Green] [Red] [Void] [Lunar] [Equipment]`
  plus `[Curiosity Catalog]`. Keys are prefixed with the item name (e.g. `Frenzy - Max Damage At
  Brink`) so they're unique within a section. The exposed field is `public static` (was `const`);
  secondary scaling/window/timing consts stay `const`. Config file:
  `BepInEx/config/com.cursedapple.curiosities.cfg`. To make a value tunable: change its `const`
  to `static`, add a `config.Bind(section, key, default, desc).Value` line before `CreateLang()`.
  Equipment cooldowns are exposed by backing the `Cooldown` property with a static field bound
  before `CreateEquipment()`.

See the project memory file (`memory/project_ror2_mod.md`) for the full list of API gotchas —
read it before touching tiers, buffs, pickups, or minion interactions.

---

## Roster (28 items + 4 equipment)

### White (Tier 1)
| Item | Mechanic | Art |
|---|---|---|
| Long Range (class `Sharpshooter`) | Bonus damage to enemies 30–60m away, scaling with distance | ✅ |
| Kinetic Dash | Utility skill → +20%/stack fire rate for 6s | ✅ |
| Intensifying Magazine | Sustained basic-attack fire ramps damage up to +45% (+30%/stack) over 3s | ✅ |
| Restorative Shot | Every 3s, next hit heals 3% max HP (+3%/stack) | ✅ |
| Sprint Boots | While sprinting, ramp to +25% move speed (+12%/stack) over 6s; resets on stop | ✅ |
| Battle Vest | Above 75% HP → +20%/stack bonus damage | ✅ |
| Mystic Shot | Every 3s, next hit deals +3% (+3%/stack) bonus burst | ✅ |
| Trophy Collector | Killing an elite grants a Trophy (max 1, +1 per item). Each Trophy = $50/min (flat — NO per-stack gold scaling). Cleared each stage. | ✅ |

### Green (Tier 2)
| Item | Mechanic | Art |
|---|---|---|
| Spirit Snatch | Close (10m) hit: +40% burst (+10%/stack), STEALS 20 (+15/stack) armor onto you for 5s | ✅ |
| Berserker | Take damage → Rage stacks (+4% dmg each), decays | ✅ |
| Burst Fire | On hit → +10% move / +15% atk speed (per stack) buff | ✅ |
| Heroic Aura | 20m aura: allies get -8% dmg taken / +8% atk speed per stack. **Minions emit too** | ✅ |
| Torment Pulse | Every 1.5s, 200% dmg to enemies in 20m (+10m/stack), 2 targets (+2/stack) | ✅ |
| Hunter's Aura | 15m (+5m/stack) aura: enemies take +25% dmg, tripled if enemies ≤ item count. **Minions excluded** | ✅ |
| Lifestrike | Close (13m) hit: heal 8% of dmg (+4%/stack, cap 4% maxHP/hit) + Slow50 | ✅ |
| Fortitude | +5% max HP/stack. After 10s no damage, regen 1%/s max HP per stack. | ✅ |
| Radiant Regen | Skill use heals 20 HP (5s cd). Unique-enemy skill hits grant Radiance stacks (max 6/stack); each = 2 HP/s (+2/stack). | ✅ |
| Toxic Bullets | 10 hits build Toxin; at max: 1% max HP/s DoT (+1%/stack) for 5s + -50% healing | ✅ |
| Tankbuster | 500%+ single-hit → 10% of enemy HP bonus burst; 20s cd (-50%/stack, min 2s) | ✅ |

### Red (Tier 3)
| Item | Mechanic | Art |
|---|---|---|
| Lucky Shot | On a CRIT, hyperbolic ~29% chance to strike a second crit | ✅ |
| Siphon Bullets | On hit, steal 1% of damage dealt as bonus max HP (cap 10%×stacks of enemy max HP per enemy). Stolen HP lost when that enemy dies. | ✅ |
| Frenzy | Smooth ramp below 50% HP (+8%/stack window): up to +40% dmg / +50% atk / +25% move at brink | ✅ |
| Spellslinger | Non-primary skill → stacking atk speed (+7% each), decays | ✅ |
| Spellbreaker | Block 200+ dmg hit by 75%, 30s cd (-50%/stack) | ✅ |
| Healing Tempo | +25% team active healing; on heal, allies gain +20% atk/move PER STACK for 8s (+6s/stack) | ✅ |

### Void
| Item | Tier | Mechanic | Art |
|---|---|---|---|
| Spirit Burn | VoidTier2 | Corrupts Tankbusters. 500%+ hit → 15m explosion igniting all enemies; 35% dmg/s (+35%/stack) for 5s. 3s cd. | ✅ |

### Lunar
| Item | Mechanic | Art |
|---|---|---|
| Glass Cannon | +25% dmg/stack; each kill = +1% fire rate (50 cap/stack); +25% dmg TAKEN/stack; charges lost on death/stage | ✅ |
| Curiosity Catalog | At stage start, spawns a personal 3-tier shop ~10m off to the side of the teleporter. Base costs anchored to base-game chests (white 30 ≈ Small Chest, green 75 ≈ Large Chest, red 400 = Legendary Chest); price is difficulty-scaled **live per purchase** (cost = base × coeff^1.25). **Only the catalog holder can shop** the cubes. Blocks chests/multishops (cost==Money) but **allows printers and scrappers**. | ❌ |

### Equipment
| Item | Mechanic | Art |
|---|---|---|
| Counterspell | 25s cd. Ward 2s → fully negate next non-DoT hit + heal 8% maxHP | ✅ |
| Colossus | 35s cd. 8s: grow 1.75×, 65% dmg resist, slow enemies in 20m, -30% move speed | ✅ |
| Golden Goose Egg | Consumed on use. -10% dmg while held. Hatches into 1–4 items over time (white 0–10 min, green 10–20, red 20–30). Progress persists. | ✅ |
| Blood Tribute | Toggle (1s cd). While active: drain 5% max HP/s + -50% healing received, but +100% attack speed / +50% move speed. | ✅ |

---

## Survivors (Deadlock heroes, ported from HeroBrew)

HeroBrew (`Documents\GitHub\HeroBrew`) is the Valheim sibling: Deadlock heroes as potion
transformations. The RoR2 port reuses its pipeline: hero model, animations, sounds and icons are read
**at runtime from the player's own Deadlock install** (read-only, never while deadlock.exe runs, no Valve
file ever shipped — same rule as HeroBrew). Without Deadlock the hero plays the same on a stand-in model.

- `Deadlock/` — shared layer: `DeadlockInstall` (finds Deadlock via Steam libraries, VPK reader, sounds →
  MP3, vtex → icons, runs `vrf\Source2Viewer-CLI.exe` once to convert model + clips into
  `%LocalLow%\Hopoo Games, LLC\Risk of Rain 2\CursedApple\deadlock`), `GlbLoader` (one renderer per
  primitive so `CharacterModel` overlays work), `MiniJson` (RoR2 has no Newtonsoft), `DeadlockSounds`
  (Unity AudioSource, follows RoR2 master×SFX volume).
- `Survivors/Lash/` — **Lash** (`CursedAppleLashBody`). Commando's rig cloned as the stand-in (hurtboxes,
  camera, item display bones); `LashModelSwap` puts the Deadlock model on top and makes its renderers the
  `CharacterModel`'s; `LashAnimator` plays his Deadlock clips (anim table in `LashData`).
  - Primary: 3-round burst gun. Secondary: **Ground Strike** (alt: **Flog**). Utility: **Grapple** (needs a
    target, enemy or ally player; refunds air jump). Special: **Death Slam** (channel/lock-on, lift, throw).
  - Passive "Perfect Cardio, Perfect Form, Perfect Hair": Deadlock movement in `LashMovement.cs` —
    3 stamina pips (4.5 s each, HUD bars under the crosshair), sprint key = dash (ground 10 m / air 8 m, 1 pip),
    2 air jumps + 2 air dashes (1 pip each), dash-jump, wall jump, slide (Left Ctrl, configurable), no fall damage.
    `LashMain` replaces the Body machine's main state. Grapple resets air jumps/dashes and refunds a pip.
  - Ground Strike interrupts Grapple and Death Slam's hold (the throw fires at once). Grounded Ground Strike = no hop.
  - Numbers + config in `LashData` (`[Lash]` section).
  - Server work (Flog damage+heal, Death Slam lift/throw/impact) goes through R2API.Networking messages in `LashNet`.
- Build copies the converter (122 MB) once from HeroBrew's playtest profile into
  `plugins\CursedAppleCuriosities\vrf`. The Thunderstore zip does NOT include it yet — friends get the stand-in model.
- **Playtest profile:** `Lash Playtest` = clone of "Alloyed Goonlective super real" (full modpack) + DebugToolkit.
  The build replaces the DLL in its `Unknown-CursedAppleCuriosities` folder (same folder, so no duplicate GUID).
- Test: pick Lash in the lobby, or `spawn_as CursedAppleLashBody` (DebugToolkit). Log lines start `[Lash]` / `[Deadlock]`.
- Next heroes follow the same shape (HeroBrew has Abrams): data table + states + `ModelRequest`.

---

## Recent changes (since last handoff)

- **v0.9.1 — Critical config-crash fix.** A config key with an apostrophe
  (`"Hunter's Aura - ..."`) threw `ArgumentException` (BepInEx forbids `= \n \t \ " ' [ ]` in
  section/key names) **inside `HuntersAura.Init`**, which aborted the whole plugin `Awake`:
  `ModItemDefs`/`ModEquipmentDefs` never populated → catalog `BuildOptions` empty → **shop never
  spawned**, and every item iterated after Hunter's Aura failed to register. Fix: removed the
  apostrophe (`"Hunters Aura - ..."`). Also hardened `InitItems/InitEquipments/InitArtifacts`
  with per-item try/catch so one failed item can no longer take down the entire mod.
  **Lesson:** never put `= \n \t \ " ' [ ]` in a `config.Bind` section or key.
  Also: shop teleporter offset reduced 10m → 6m (configurable: `[Curiosity Catalog] Teleporter Offset (m)`).
- **v0.9.0 — Config + balance reworks.**
  - **Config system** added (tier-grouped, headline knobs only) — see Architecture › Config.
  - **Blood Tribute** nerfed: move speed +100% → **+50%**, and now also **-50% healing received**
    while active (new `On.HealthComponent.Heal` hook gated on the active buff).
  - **Trophy Collector** reworked: gold is **flat per Trophy** (dropped the per-item-count gold
    multiplier); each copy now raises the **Trophy cap** instead (`TrophyCapBase` 1 +
    `TrophyCapPerStack` 1 per item). Payout clamps to the cap if copies are dropped.
  - **Curiosity Catalog**: shopping is now **holder-only** (gated in `GetContextString`,
    `OnInteractionBegin`, `SubmitChoice` via `HoldsCatalog`); block predicate now keys off
    `costType == Money` so **printers/scrappers are allowed** while multishops are still blocked.
- **Siphon Bullets** — fully reworked. Old: timer-based HP from damage dealt. New: steal 1% of
  enemy max HP per hit; per-enemy cap = 10% × stacks; stolen HP is removed when that enemy dies.
  Per-enemy tracking via `Dictionary<HealthComponent, float>` on `SiphonBulletsTracker`; 2s
  cleanup pass removes despawned enemies.
- **Juggernaut** — deleted entirely (file, csproj icon entry, CuriosityCatalog red-price override).
- **Toxic Bullets** — nerfed DotDpsPercent 0.02f → 0.01f (2% → 1% max HP/s).
- **Trophy Collector** — fixed: trophy cap is always `MaxStacksPerItem` (14), NOT `14 × itemCount`.
- **Curiosity Catalog** — new Lunar item. Spawns 3 CommandCube-based persistent terminals at the
  teleporter each stage. Prices locked at stage-start (`GetDifficultyScaledCost` called once,
  stored in `CuriosityPickerMarker`). Blocks chests and multishops; allows lunar pods.
- **Blood Tribute** — new Equipment. Toggle: drains HP/s, doubles atk+move speed.
- **Curiosity Catalog repricing** — base costs re-anchored to base-game guaranteed-tier
  chests (white 30, green 75, red 400 vs. Legendary Chest's 400) so early reds aren't a
  piñata. Pricing changed from locked-at-stage-start to **live per-purchase scaling**
  (`ScaledCost` → `Run.GetDifficultyScaledCost`, cost = base × coeff^1.25) — lingering to
  farm a cleared stage steadily raises prices, rewarding a teleporter rush. Marker stores
  `BaseCost` (was `Cost`). Counterspell routed to the green tab.

---

## Pending / next work

**Art (the main gap):**
- **Curiosity Catalog is the only item still iconless.** Its `IconFileName` already points at
  `icon_curiositycatalog.png`, but that PNG doesn't exist in `Resources\` and its csproj
  `<EmbeddedResource>` line is still commented out — drop the 128px PNG in and uncomment to finish.
  Every other item and equipment now has a wired, embedded 128px icon.
- To add art: drop `icon_x.png` (128px) in `Resources\`, add `<EmbeddedResource>` line to csproj,
  add `protected override string IconFileName => "icon_x.png";` to the item class.
- Stray asset: `Resources\icon_juggernaut.png` is an orphan (Juggernaut was deleted; not embedded,
  not referenced). Safe to delete whenever — kept for now in case the art gets reused.

**Curiosity Catalog tab mapping:**
- All items naturally go to their tier (Tier1→white, Tier2→green, Tier3/Lunar/unknown→red).
- `Spirit Burn` (VoidTier2) is in `_redPriceOverrides` → prices as red.
- Equipment tabs: Golden Goose Egg → white, Blood Tribute → green, Counterspell → green, everything else → red.

**Shelved — do NOT retry from scratch:**
- **Custom world pickup model.** Script-built flat card cannot be forced opaque in RoR2's deferred
  pipeline. Correct path is a Unity AssetBundle (Thunderkit). Items have NO world model
  (pickupModelPrefab null) — that's the clean state.

---

## Design direction

Adapt Deadlock's catalogue while filling **under-served RoR2 niches** (avoid more raw
damage/attack-speed — the early roster was gun-heavy). Use Deadlock's three trees (Weapon /
Vitality / Spirit) and **actives → RoR2 equipment**. The "cursed" theme maps naturally onto
RoR2 lunar items (power + downside), e.g. Glass Cannon. When researching Deadlock items, web
search is available; adapt mechanics faithfully but write original RoR2-style descriptions.
