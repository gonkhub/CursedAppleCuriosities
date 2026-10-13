# Cursed Apple Curiosities — Project Handoff

A BepInEx plugin for **Risk of Rain 2** (Survivors of the Void, build 912) that adds custom
items and equipment **adapted from Deadlock's item catalogue**. The name is homage: Deadlock's
map is the "Cursed Apple" and items are bought from a "curiosity shop."

**State:** builds clean (0 errors), **31 items + 14 equipment**, 8 survivors (Lash, Abrams, Seven, Vyper, Grey Talon, Victor, Viscous, Celeste). **v1.0.0** released
2026-10-11; **v1.2.5** packaged 2026-10-12 for friends (tag `v1.2.5` on main); **v1.3.0** finished 2026-10-12 on branch `1.3`
(tag `v1.3.0`, not packaged); **v1.4.0** (Cursed Broker, not yet playtested) finished 2026-10-12 on branch `1.4` (tag `v1.4.0`,
not packaged); **v1.5.0** (Viscous, Celeste, Victor) merged to main 2026-10-12 (tag `v1.5.0`, PR #1). New work happens on branch `1.6` (version 1.6.0). The user playtests as we go.

---

## Build / deploy / test / distribute

- **Build (Debug, quick):**
  `dotnet build "CursedAppleCuriosities\CursedAppleCuriosities.csproj"`
  Post-build target auto-deploys the DLL to the r2modman profile.
- **Build (Release, distributable):**
  `dotnet build "CursedAppleCuriosities\CursedAppleCuriosities.csproj" -c Release`
  Also auto-packages the Thunderstore zip to `dist\`.
- **Profile path (dev):** `C:\r2heimman\RiskOfRain2\profiles\Curiosities Dev` — clone of Hallowed Concepts Release
  (the post-DLC profile), made 2026-10-11. Builds replace the DLL in its `Unknown-CursedAppleCuriosities` folder, and
  the csproj compiles against its BepInEx/R2API/MMHOOK. **Hallowed Concepts Release is frozen at 1.0.0: never deploy to it.**
  Alloyed Goonlective 3-24 (the old dev profile, pre-DLC MMHOOK) no longer receives builds.
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

- **Addressables keys are the short address, not the asset path:** `RoR2/Base/GainArmor/ElephantDefense.prefab`, not
  `RoR2/Base/Equipment/GainArmor/...` (no `Equipment/`, `Items/`, `Characters/`, `Skills/` folders). A wrong key fails silently
  until used. 26 such keys were fixed 2026-10-12. Check new keys against the catalog (`StreamingAssets/aa/catalog.json`,
  `m_KeyDataString`) before wiring them.

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

## Roster (31 items + 14 equipment)

### White (Tier 1)
| Item | Mechanic | Art |
|---|---|---|
| Long Range (class `Sharpshooter`) | Bonus damage to enemies 30–60m away, scaling with distance | ✅ |
| Kinetic Dash | Utility skill → +20%/stack fire rate for 6s | ✅ |
| Intensifying Magazine | Sustained basic-attack fire ramps damage up to +45% (+30%/stack) over 3s | ✅ |
| Restorative Shot | Every 3s, next hit heals 3% max HP (+3%/stack) | ✅ |
| Sprint Boots | While sprinting, ramp to +25% move speed (+12%/stack) over 6s; resets on stop; +1 hp/s (+0.5/stack) out of danger, flat (user, 1.5.0) | ✅ |
| Battle Vest | Above 75% HP → +20%/stack bonus damage | ✅ |
| Mystic Shot | Every 3s, next hit deals a 100% (+100%/stack) base-damage bonus burst | ✅ |
| Mystic Expansion | +30%/stack radius AND range (doubled from 15%, 2026-10-12). BlastAttacks (hook `BlastAttack.Fire`; their effects grow to match, projectile explosions via `ProjectileExplosion.DetonateServer`), and everything that calls `MysticExpansion.RadiusMultiplier(body)`: Siphon Life, Seismic Impact, Death Slam (impact, lock-on area, throw distance), Grapple range, Heavy Melee reach, Shoulder Charge width, Torment Pulse, Spirit Burn, Heroic/Hunter's Aura, Colossus slow, Ricochet, Lifestrike/Spirit Snatch "close" range, Long Range window (slides closer), Focus Lens/Cultist reach, Majestic Leap distance. Vanilla (`MysticExpansionReach.cs`): Ukulele, Tesla, N'kuhanna, Razorwire, Focus Crystal (IL), Warbanner (level-up AND teleporter)/Fungus/Rachis/Pauldron/cripple wards, Frost Relic ward, Faraday Spur zone (IL); Focus Crystal ring grown client-side (`MysticIndicatorScale`). Torment Pulse draws a faint red range ring (local player). New custom AoEs/ranges must call it too. Not playtested. | ✅ |
| Duration Extender | Deadlock T2 Spirit (+20%) → white, +15%/stack linear. `DurationExtender.Multiplier(body)` for custom timers, plus general hooks: `CharacterBody.AddTimedBuffInternal` (your own buffs use your item; anything applied inside a `DurationExtender.From(source)` scope uses the source's; `isCooldown` buffs never), `SetStateOnHurt.SetStun/SetFrozen/SetShock`, `DotController.InflictDot`. Source scopes: OnHitEnemy / TakeDamage / SetStateOnHurt.OnTakeDamageServer (hooked LAST in plugin Awake so they wrap every other hook), equipment OnUse, hero server hits. `DurationExtender.Current` = the scoped source's multiplier; `Exact()` scope = no extension (for buffs whose duration a caller already extended). Covers item/equipment timers (Kinetic Dash, Burst Fire, Radiant Regen, Inhibitor, Toxic poison, Colossus, Counterspell, Ethereal Shift, Focus Lens), hero channels/hovers/charge (Death Slam channel, Seismic hover, Shoulder Charge, Seven's Storm), and hero stuns/debuffs (Abrams, Lash, Seven, Vyper). Not playtested. | ✅ |

### Green (Tier 2)
| Item | Mechanic | Art |
|---|---|---|
| Spirit Snatch | Reworked 2026-10-12: "Focus Crystal for barrier". Hits within 13m (Focus Crystal's check: attacker core to hit point; Mystic Expansion scales it) grant 1% max HP barrier per stack × proc coefficient (capped at 1). Armor steal and bonus burst removed. Local player sees a flat yellow ring at the range (`SpiritSnatchRing`, client-side LineRenderer). Not playtested. | ✅ |
| Berserker | Take damage → Rage stacks (+4% dmg each), decays | ✅ |
| Burst Fire | On hit → +10% move / +15% atk speed (per stack) buff | ✅ |
| Heroic Aura | 20m aura: allies get -8% dmg taken / +8% atk speed per stack. **Minions emit too** | ✅ |
| Torment Pulse | Every 1.5s, 200% dmg to enemies in 12m (+10m/stack), 2 targets (+1/stack) | ✅ |
| Hunter's Aura | 15m (+5m/stack) aura: enemies take +25% dmg, tripled if enemies ≤ item count. **Minions excluded** | ✅ |
| Lifestrike | Close (13m) hit: heal 8% of dmg (+4%/stack, cap 4% maxHP/hit) + Slow50 | ✅ |
| Fortitude | +5% max HP/stack. After 10s no damage, regen 1%/s max HP per stack. | ✅ |
| Radiant Regen | Skill use heals 20 HP (5s cd). Unique-enemy skill hits grant Radiance stacks (max 6/stack); each = 2 HP/s (+2/stack). | ✅ |
| Toxic Bullets | 10 hits build Toxin; at max: 1% max HP/s DoT (+1%/stack) for 5s + -50% healing. Buildup and antiheal buffs are hidden (`isHidden`); only the poison shows. | ✅ |
| Tankbuster | 500%+ single-hit → 10% of enemy max HP bonus burst (+5% max HP per extra stack); flat 10s cd | ✅ |

### Red (Tier 3)
| Item | Mechanic | Art |
|---|---|---|
| Lucky Shot | On a CRIT, hyperbolic ~29% chance to strike a second crit | ✅ |
| Siphon Bullets | On hit, steal 1% of damage dealt as bonus max HP (cap 10%×stacks of enemy max HP per enemy). Stolen HP lost when that enemy dies. | ✅ |
| Frenzy | Smooth ramp below 50% HP (+8%/stack window): up to +40% dmg / +50% atk / +25% move at brink; holds its peak 5 s after healing back above (user, 1.5.0) | ✅ |
| Spellslinger | Non-primary skill → a stack (max 10, +10/copy): +10% atk speed and 4% shorter cooldowns each (`cooldownMultAdd`, capped at 50%); decays after 5s. Buffed 2026-10-12 (was 6 stacks, +7%, 3s). | ✅ |
| Spellbreaker | Block 200+ dmg hit by 75%, 30s cd (-50%/stack) | ✅ |
| Healing Tempo | Reworked 2026-10-12 (option B, "overheal → Tempo"): +25% healing received (+10%/stack, regen too). Every 10% max HP of non-regen overheal (the game's own `HealthComponent.onCharacterOverhealServer`, same figure Aegis uses) = 1 Tempo stack: +10% atk / +6% move each, max 4 (+2/stack), decays 1/s after 7s without overheal. Stats read the synced buff count (fixes the old version giving non-host players nothing). Deadlock proc/end sounds + Energized visual. Personal only, the old 30m ally aura is gone. Not playtested. | ✅ |
| Ricochet | Hits (chance = proc coeff) ricochet to 2 (+1/stack) other enemies within 26m (+5m/stack) for 50% of the hit. Each ricochet has a 50% chance to carry the hit's proc coefficient (else 0 = no on-hit effects). Tagged with an R2API ModdedProcType so ricochets (and anything they proc) never ricochet again. Needs R2API_ProcType. | ✅ |
| Inhibitor | On hit: enemy Inhibited 4s, deals 25% less damage (hyperbolic 1-1/(1+⅓·stacks)) + 25% slow. Not playtested. | ✅ |

### Void
| Item | Tier | Mechanic | Art |
|---|---|---|---|
| Spirit Burn | VoidTier2 | Corrupts Tankbusters. 500%+ hit → 15m explosion igniting all enemies; 35% dmg/s (+35%/stack) for 5s. 3s cd. | ✅ |
| Trophy Collector | VoidTier1 | Corrupts Sprint Boots. Killing an elite grants a Trophy (max 1, +1 per item). Each Trophy = $50/min (flat) and +10% sprint speed. Cleared on teleporter charge / each stage. | ✅ |

### Lunar
| Item | Mechanic | Art |
|---|---|---|
| Glass Cannon | +25% dmg/stack; each kill = +1% fire rate (50 cap/stack); +25% dmg TAKEN/stack; charges lost on death/stage | ✅ |
| Curiosity Catalog | When the teleporter event completes (charged **and** boss dead), a lunar droplet launches from the boss drop point and lands as **one combined shop** (CommandCube picker) listing every Curiosity item/equipment, grouped white → green → red. Base costs anchored to base-game chests (white 30, green 75, red 400), difficulty-scaled **live per purchase** (cost = base × coeff^1.25). The menu **stays open between purchases**; options the shopper can't afford are greyed out and refresh after each buy. Gold is only drained to XP when the player leaves via the teleporter, so the whole stage's gold can be spent. **Only catalog holders can shop.** Blocks chests/multishops (cost==Money) but **allows printers and scrappers**. No teleporter → no shop (Bazaar, Void Fields, Moon). | ✅ |

### Equipment
| Item | Mechanic | Art |
|---|---|---|
| Counterspell | 25s cd. Ward 2s → fully negate next non-DoT hit + heal 8% maxHP | ✅ |
| Colossus | 35s cd. 8s: grow 1.75×, 65% dmg resist, slow enemies in 20m, -30% move speed | ✅ |
| Goose Eggs (Grey/Green/Red/Golden/Void) | `GoldenGooseEgg.cs` (`GooseEgg` base). Consumed on use, -10% dmg while held. +1 stack per 5:00 held, uncapped, starts at 0 (activating at 0 does nothing). Per stack: Grey 15 whites, Green 5 greens, Red 1 red, Golden 2 boss items, Void 2 void items (Void Cradle's `dtVoidChest`). Progress per individual egg (1.5.0): `GooseEggProgress` on the inventory per equipment location while held, `GooseEggPickupProgress` on the pickup when swapped out (carried in the `AttemptGrant` hook); a recycled egg starts fresh. Only Grey is droppable; every drop table rolls it into a weighted egg (45/27/12/8/8, config). Holding Void, picking up another egg absorbs it as +1 stack (`GenericPickupController.AttemptGrant`). Icons: golden art recoloured. All eggs priced white in the Catalog. | ✅ |
| Blood Tribute | Toggle (1s cd). While active: drain 5% max HP/s + -50% healing received, but +100% attack speed / +50% move speed. | ✅ |
| Ethereal Shift | 45s cd. 3s invulnerable (HiddenInvincibility) + Cloak, frozen in place (`CharacterMotor.UpdateVelocity` zeroed) and no skills (`SkillSilence`); then Special-source damage +100% for 8s (`DamageSource.Special`). Not playtested. | ✅ |
| Refresher | 80s cd. `SkillLocator.ResetSkills()` (networked to the owning client). Not playtested. | ✅ |
| Arctic Blast | 75s cd. 12m BlastAttack, 300% dmg, `Freeze2s` (frozen <30% HP get executed); unfreezables Slow80; all hit Slow60 4s. Not playtested. | ✅ |
| Cultist Sacrifice | 120s cd (playtest: 30 → 120). Aimed (40m, 20° cone, spherecast fallback, reticle): kill a non-boss/non-champion enemy via `Suicide`; pays 4x its own `goldReward` total (elites/rich enemies pay more). Blessing: +50 max HP for 105s (non-stacking). No target = no cooldown. Not playtested. | ✅ |
| Majestic Leap | 45s cd. Launch forward (22 m/s) and up (38 m/s) via `CharacterMotor.ApplyForceImpulse` (networked to the owner), barrier 50% max HP, no fall damage on the landing (`DeadlockFallGuard.ProtectNextLanding`). Deadlock sounds `sounds/mods/armor/majestic_leap/*`; icon cropped from `Resources\Vitality\MajesticLeap.png`. Not playtested. | ✅ |
| Focus Lens | 45s cd. Aimed (60m, 20° cone, spherecast fallback, reticle; bosses OK): target silenced 4.5s (`SkillSilence`); 50% of ALL damage it took in that window echoes when it ends. Not playtested. | ✅ |

---

### Equipment FX (2026-10-10)
- `Items/EquipmentFx.cs`: `Effect(key, pos, scale)` (vanilla Addressables effect, transmitted), `Sound(id, pos, follow)` (Deadlock
  sound to every client via `SoundMessage`, which now carries an optional follow object so vanilla `Fallback` Wwise events can play
  for players without Deadlock), `RegisterBuffFx(buff, tempVisualPrefabKey, loopSoundId)` (vanilla TemporaryVisualEffect + sound loop
  on any body holding the buff; hooks `CharacterBody.UpdateAllTemporaryVisualEffects`, runs on every client).
- Equipment sounds register in `EquipmentBase.RegisterSounds()` (called by the plugin before `DeadlockInstall.Start`). Paths are
  Deadlock's `sounds/mods/...` (listed read-only from pak01's directory).
- `Items/EquipmentTargeting.cs`: shared crosshair targeting + Trophy Hunter's Tricorn reticle for the local player (Focus Lens, Cultist Sacrifice).
- Slam skills deal no fall damage: `Deadlock/DeadlockFallGuard.cs` (server-side `OnCharacterHitGroundServer` window opened by each slam state; the landing state ends it 1.5 s after impact). Used by Abrams Seismic Impact and Lash Ground Strike.
  Siphon Life: tethers removed, ring is blue.

## Survivors (Deadlock heroes)

The source material is **Deadlock itself**: any hero on its roster is a candidate, and each hero's kit,
numbers, clips and sounds come from Deadlock's own data (heroes/abilities vdata, animgraphs, localization).
Hero model, animations, sounds and icons are read **at runtime from the player's own Deadlock install**
(read-only, never while deadlock.exe runs, no Valve file ever shipped). Without Deadlock the hero plays the
same on a stand-in model.

- `Deadlock/` — shared layer: `DeadlockInstall` (finds Deadlock via Steam libraries, VPK reader, sounds →
  MP3, vtex → icons, runs `vrf\Source2Viewer-CLI.exe` once to convert model + clips into
  `%LocalLow%\Hopoo Games, LLC\Risk of Rain 2\CursedApple\deadlock`), `GlbLoader` (one renderer per
  primitive so `CharacterModel` overlays work), `MiniJson` (RoR2 has no Newtonsoft), `DeadlockSounds`
  (Unity AudioSource, follows RoR2 master×SFX volume).
- **Hero preloading (no freeze on picking a hero):** a "preloader" thread reads every `WantModel` model (`GlbLoader.Parse`:
  plain data, no Unity objects) and its clips as soon as each is converted. The main thread builds them a few ms per frame
  (`DeadlockInstall.Pump` from the plugin's `Update`, via `GlbLoader.Build`). `DeadlockInstall.Model(key)` never blocks.
  It returns null until the model is read (the caller retries; stand-in meanwhile), then finishes the remaining uploads at
  once. `HeroTextures` decodes base-colour PNGs off-thread (its own PNG decoder), mipmaps them and compresses them to
  DXT1/DXT5. The result is cached as `<png>.cac`, so later launches only read it; this is 8× less VRAM than LoadImage's
  RGBA32. Materials come from one recipe per model key, `DeadlockHeroes.HeroMaterial` (the def's tints and glows).
  Background-thread rule: Parse/LoadClips may only use managed code or Unity natives marked `IsThreadSafe`
  (Quaternion.Inverse/Slerp, Matrix4x4.Inverse are).
- `Survivors/Lash/` — **Lash** (`CursedAppleLashBody`). Commando's rig cloned as the stand-in (hurtboxes,
  camera, item display bones); `LashModelSwap` puts the Deadlock model on top and makes its renderers the
  `CharacterModel`'s; `LashAnimator` plays his Deadlock clips (anim table in `LashData`).
  - Primary: 3-round burst gun. Secondary: **Grapple** (needs a
    target, enemy or ally player; refunds air jump; 6 s cd). Utility: **Ground Strike** (300% + 50%/m; Flog removed 2026-10-12). Special: **Death Slam** (channel/lock-on, lift 0.6 s + hang 0.3 s, throw).
    Death Slam (2026-10-12): carried enemies take no fall damage; lift speed capped at 35 m/s and held under ceilings; thrown enemies fly on their line until they hit terrain (sphere-cast ahead), and one with no terrain below after 8 s is TrueKilled (credited to Lash). MapZone also kills monsters leaving the map.
  - Passive "Perfect Cardio, Perfect Form, Perfect Hair": the shared Deadlock movement (see below) with Lash's
    profile `LashData.Movement`: 4 stamina, 2 air jumps + 2 air dashes of 16 m (heroes 8 m; default to 3 / 1 / 1), 0.3 s
    ground dash, slide on Mouse 4. Grapple resets air jumps/dashes and refunds a pip.
- **Lobby intros** (2026-10-12): `Deadlock/DeadlockDisplayIntro.cs` (base on the display prefab root: runs once per pick,
  moves the whole display so the stand-in plays it too; display animators now run one-shots on to their next state).
  Lash: `Survivors/Lash/LashIntro.cs` Ground Strikes onto the pedestal from 30 m (gs_drop/gs_dive, gs_land, impact sound,
  Loader slam effect). The select VO waits for the intro to end (`DeadlockModelSwap.PlaySelectVoice` at the pedestal).
  Seven: `SevenIntro.cs` sinks from 7 m in the Storm Cloud float over 2.6 s, 5 bolts around the pedestal, storm_end on landing.
  His float clips spin his pelvis (storm_end -75 -> -9 deg in 0.4 s: an awkward spin on landing, user): the intro counter-turns the display to
  keep his hip line across the screen through the landing, then eases back.
  Vyper: `VyperIntro.cs` slides in from 10 m back and to screen right (user: behind her final facing), getup turning to face the camera.
  Seven/Vyper not playtested. **Abrams has no intro on purpose** (user: his head clipping off the top of the screen is funny).
- **Shared Deadlock movement** (`Deadlock/DeadlockMovement.cs`) for every Deadlock hero:
  `DeadlockMovementProfile` (numbers, Deadlock defaults; sound ids `{Hero}_jump`, `_dash`, `_vo_effort`, `_slide`...;
  an `Anim` hook) → `DeadlockMovement.Apply(body, profile[, mainStateSubclass])` adds a `DeadlockMover` (stamina),
  sets `DeadlockMain` as the Body main state, and registers `DeadlockMain/Dash/Slide` + the stamina HUD once.
  `profile.Bind(config, section)` exposes the player knobs. Includes stamina pips (4.5 s regen, HUD under the
  crosshair), sprint key = dash, air jumps/dashes (1 pip each), wall jump, slide, out-of-combat sprint (they take fall damage).
  - **Dash-jump** (Deadlock): jump in a window late in the ground dash (a share of the dash's duration; the pips
    flash blue) = 2 stamina total, a fixed launch (`DashJumpSpeed` 30 m/s forward, `DashJumpUp` 10.8). A ground
    dash can't be jumped out of otherwise: a press outside the window does nothing, locks the dash-jump out for
    that dash, and shows "Dash-jump: too early / too late" under the pips. Window = Deadlock's
    0.30-0.55 s (seconds, not a share: a share halved it on Lash's short dash). A ground dash always lasts 0.68 s:
    Lash's 10 m burst takes 0.3 s, then eases to a run (`GroundDashTotalSeconds`, `GroundDashTailDecay`).
    Works off a ledge mid-dash (coyote) without spending air jumps.
  - **Down-dash** (user 2026-10-12): double-tap the slide key in the air (`DownDashDoubleTap` 0.3 s, second tap airborne) =
    half a stamina pip, +10 m/s downward, horizontal kept. Stamina (`DeadlockMover.Pips`) is a float now; a refill adds a whole pip;
    the HUD shows half bars. `DeadlockMover.TryDownDash(motor)` is called by DeadlockMain and Viscous' GooBall state.
  - **Air control boost** (user): any `TrySpend` while airborne (air jump, air dash, down-dash, Grey Talon's glide dash) sets
    `AirBoost` until landing: `AirSteer` accel x`AirControlBoost` (1.35); Goo Ball's air turn rate too.
  - Ground Strike interrupts Grapple and Death Slam's hold (the throw fires at once). Grounded Ground Strike = no hop.
  - Numbers + config in `LashData` (`[Lash]` section).
  - Server work (Flog damage+heal, Death Slam lift/throw/impact) goes through R2API.Networking messages in `LashNet`.
- Build copies the converter (Source2Viewer-CLI, 122 MB) once from `$(VrfSource)` (set in the csproj) into
  `plugins\CursedAppleCuriosities\vrf`. Since 1.0.0 the Thunderstore zip bundles it as `plugins/vrf` (the csproj passes
  `$(VrfSource)` to `package-only.ps1`), with `THIRD_PARTY_NOTICES.md` + `LICENSE-SPIRV-Cross.txt` for its licenses.
- **Playtest profile:** `Lash Playtest` = clone of "Alloyed Goonlective super real" (full modpack) + DebugToolkit.
  The build replaces the DLL in its `Unknown-CursedAppleCuriosities` folder (same folder, so no duplicate GUID).
- Test: pick Lash in the lobby, or `spawn_as CursedAppleLashBody` (DebugToolkit). Log lines start `[Lash]` / `[Deadlock]`.
- **Shared hero layer** (2026-10-10): `Deadlock/DeadlockHeroModel.cs` (`DeadlockHeroDef` + `DeadlockModelSwap` +
  `DeadlockAnimator` + `DeadlockFootsteps`, lifted from Lash's model code; Lash's own rules are now his `LashModelSwap.Def`),
  `Deadlock/DeadlockSurvivorKit.cs` (skills, slots, skin copy, extra state machine, mute stand-in) and
  `Deadlock/DeadlockHeroVoice.cs` (pain / low health / kill / killstreak / death VO by `{hero}_vo_*`). Lash still uses his
  own copies of the kit and voice code (unchanged); Abrams uses the shared ones. Every hero's ability lines go through
  `DeadlockHeroVoice.Power("{hero}_vo_powerN", gameObject)`, which swaps in Deadlock's `{hero}_vo_desperation_powerN`
  line below 25% health (`LowHealth`) when that row exists (Abrams/Vyper 1,2,4; Lash/Seven 1-4).
- `Survivors/Abrams/` — **Abrams** (`CursedAppleAbramsBody`), same shape as Lash: `AbramsData` (numbers, anims, sounds,
  `[Abrams]` config), `AbramsModel` (hero def), `AbramsStates`, `AbramsNet` (server hits, carrying, Siphon/Resilience
  components), `AbramsSurvivor`. Numbers read from Deadlock build 25841406 (`hero_atlas`, `citadel_ability_bull_*`;
  Deadlock calls him "bull"/"atlas": icons `bull_*`, VO `sounds/vo/atlas/`). Clips picked from his animgraphs
  (`abrams_playercontrol_abilities`, `abrams_upperbodyreplace`).
  - Primary: **Shotgun** — 9 pellets every 0.63 s, infinite ammo (no reload, like Lash: reloading was cut by playtest).
  - Secondary: **Siphon Life** (own `Siphon` state machine, runs alongside everything; synced buff + `AbramsSiphon`
    ticks true damage every 0.25 s, heals 35%).
  - Special: **Heavy Melee** (default since 2026-10-12: wind-up lunge, 5 m cone punch, 1500%, 3 s, heals 18% of damage). Alt: **Seismic Impact**.
  - Utility: **Shoulder Charge** (250%) — 1.4 s, 18.75→30 m/s, steers 140°/s, scoops enemies up (server `AbramsCarry`), wall =
    stun 1.1 s (Deadlock 0.3 + its upgrade, the "gift"); bosses bounce it. No shooting mid-charge (playtest); Siphon Life / Seismic Impact can still be cast.
  - Special alt: **Seismic Impact** (2026-10-12: leap 30.5 m/s up = 2x height; enemies within 15 m when the crash starts, or 10 m of its path, ride down in a ring around him and take the impact; carried enemies take no fall damage) — leap, hover ≤3 s with a landing ring, special/primary to crash (100 m/s), enemies on
    the way ride down, 9 m × 6 m impact, 1.6 s stun, 0.7 s planted.
  - Starts every run with 1 Ghor's Tome (server, once per player master: `AbramsStarterGiven`).
  - Passive: **Infernal Resilience** — 50% of damage taken regenerates over 20 s (Deadlock 13%) and +10 armor (on top of 20 base).
    Heavy Melee's 18% lifesteal (Deadlock's upgrade) is described on Heavy Melee now. Shared movement with hero defaults (3 stamina, 1 air jump,
    1 air dash), Lash's playtested controls (slide on Mouse 4, wall jump), and a longer, slower ground dash than Lash's
    (21 m in 1.4 s vs 10 m in 0.3 s; 11 m air dash; +45% out-of-combat sprint, all by playtest 2026-10-12), with his own `dash_ground` clip (from Deadlock's hero_dash graph).
    Pending Infernal Resilience heal is drawn on every health bar after current health (`AbramsResilienceBar`, ember
    tint); the server syncs the amount with `AbramsPendingMessage`.
  - His shotgun is a separate bone chain off root_motion (`weapon`). The clips already pose it in his right hand (grip on
    `hand_R`); `DeadlockHeroDef.HandIK` = ("weapon", "hand_R") records that hand-to-gun offset before the aim bend and
    restores it after, so the gun follows the hand. Don't snap to the `weapon_hand_R` socket: it sits 19 cm / ~98° off
    the clips' grip and turned the gun sideways (playtest 2026-10-10).
    `GlbLoader` also folds a clip's baked root_motion turn into bones under root_motion (his pelvis): newer AnimGraph2
    clips like `dash_ground` have one, which laid him on his side.
  - Coat fix: `GlbLoader.RemoveRootTravel` puts `$cloth_*` tracks in root_motion's frame (his coat ran
    ahead and snapped back each loop). Applies to every hero's clips.
  - **Not playtested yet.** Test: `spawn_as CursedAppleAbramsBody`; log lines `[Abrams]` / `[Deadlock]`.

- `Survivors/Seven/` — **Seven** (`CursedAppleSevenBody`, Deadlock `hero_gigawatt`, model `models/heroes_staging/gigawatt_prisoner`)
  and `Survivors/Vyper/` — **Vyper** (`CursedAppleVyperBody`, Deadlock `hero_viper`, icons `kali_*`). Prototyped 2026-10-12, **built,
  never launched**. Same shape as Abrams (Data / Model / States / Net / Survivor). Both are AnimGraph2 heroes (`heroes_staging`): the
  converter exports their vnmclips by name (test-converted OK). Kit decisions are the user's (memory `seven-vyper-design`).
  - Seven: M1 Cold Calculus 3-round burst · M2 Lightning Ball (Nano-Bomb projectile clone + `SevenBall`) / alt Static Charge (must lock onto an enemy (user): Tricorn reticle while ready, 48 m / 20 deg, no target = no cast; 3.5 s
    delay, 3 s stun, Mystic Expansion radius) · Utility Power Surge (own `Surge` machine; 10 s chain lightning per burst + 3 m/s) ·
    Special Storm Cloud (float 3 m, drift, 7 s; radius 10 m → 1000 m (cubic), damage on the same curve 150%→990%/s (~2520% per full channel, user), line of sight from Seven, bosses ×0.5; M1 once = Lightning Strike).
  - Vyper: M1 The Rattler full-auto 0.07 s, proc 1.0 · M2 Screwjab Dagger (2 charges, stacking dmg/slow; Bandit shiv clone) · Utility
    Lethal Venom (aimed 10 m, 3 s, 300%→2100% by missing health; no target = refund) · Special Petrifying Bola (Commando grenade clone, no
    gravity; everything in 12 m petrified 2.2 s = stunned + immune except Venom; bosses 1 s stun only) · Passive Slither (new profile knobs
    `SlideFrictionGrace`, `SlideUphillLoss`, `SlideDistanceBonus`, 180°/s turning) · 4 stamina.
  - **Sounds** (user picked by ear 2026-10-12 from MP3 exports + a local `review.html` player in
    `%LocalLow%\Hopoo Games, LLC\Risk of Rain 2\CursedApple\deadlock\sound_review`, never the repo): Seven gun = `gigawatt_weapon_fire`
    (not `fire/NN`), Static Charge = cast + loop only, Storm Cloud = `gigawatt_storm_cloud_*` (not `a4_storm_cloud`), never `a3_voltage_drain`
    or `seven_spark`. Vyper all approved; her gun is layered (`VyperGunAudio`: first / mid / tail + release). Vyper has no Deadlock footstep,
    dash or landing sounds: silent for now. **Rule: pitch any new Deadlock sound to the user before wiring it.**
  - Pain Battery ready glow (user): `VictorWireGlow` draws the left-arm barbed wire green while the battery is full. Deadlock: ability
    m_FullyChargedParticle `frank_shock_target_ready_buff` -> `_trace` child -> snapshot `barbed_wire_l_fx_model.vsnap` (76 points, each on one
    arm/hand bone, rope colour 157,203,114). The snapshot is dumped once as text by the converter (`DeadlockInstall.WantDump`/`Dump`, cache
    `dumps/`), points mapped Deadlock (x,y,z) in -> (-y, z, x) * 0.0254 m in GlbLoader's root space, bound by `RestInRoot`.
  - Reanimation charge-up + intro: `VictorReassembly` plays Deadlock's `frank_hover_death_anim_fx_model` (clip _02 = parts fly together,
    then its hover hold) in place of his model while he's down; `ability_reanimation` is only the get-up.
  - Item displays fitted 2026-10-12 (`SevenItemDisplays.cs`, `VyperItemDisplays.cs`; tools/itempass README). Abrams' 9 mirrored rules fixed; the
    user's dev cfg had `[Abrams - Debug] Fitted Item Anchors = false` (old automatic anchors), set back to true.
  - Pending: ping VO + Static Charge detonation sound (pitched: `sound_review/ping_review.html`), Petrify stone visual check, camera/scale playtest,
    Seven's sleeve/waist cloth bones (watch for an Abrams-style coat bug), writing review (subtitles/descriptions are drafts).
  - Seven's runs bake their travel into the pelvis (a sibling of root_motion, not a child): he ran ahead and snapped back.
    `GlbLoader.RemoveRootTravel` now takes the horizontal root travel out of root_motion's siblings when the pelvis follows it
    (`PelvisFollowsRoot`). Lash's matching clips (sprints, dash, wall jump, hard landing) were already pelvis-pinned: unchanged.
  - Vyper playtest fixes (2026-10-12): her gun (`tengu_gun`, borrowed from Deadlock's tengu) is a white texture x `g_vColorTint1`,
    which the converter drops: `DeadlockHeroDef.MaterialTints` restores it. Rattler spread grows per held shot (0.6 -> 4.5 deg, recovers
    0.3 s after release; config). Sounds: `DeadlockSound.PitchJitter/VolumeJitter` + no same take twice in a row (gun shots).
  - Seven's VFX are orange (user): `DeadlockSurvivorKit.Recoloured/Recolour` copies a vanilla effect and swaps its _RemapTex ramp
    (most RoR2 VFX colour from it) plus hue-shifts tints, particles, trails, lights. Lightning Ball shows a see-through range sphere
    (Helfire Tincture's matHelfireRangeIndicator) sized to its real shock radius incl. Mystic Expansion; its orb is orange and 1.8x.
  - Storm Cloud: after the 3 m rise Seven climbs 1 m/s for the channel (user). `SevenStormFx` (every client) draws an orange ground
    ring + see-through dome at the damage edge; radius comes from `SevenData.StormRadius`, the same function the server damages with.
  - Electric Current (Seven's passive, user): `DeadlockMovementProfile.SprintInCombat` lets a hero sprint on RoR2's outOfDanger
    (only taking damage resets it) instead of outOfCombat; Seven sets it while Power Surge is up.
  - Storm Cloud look (user, as Deadlock's animgraph): `SevenStormLook` hides his whole regular model (`gun|outer_costume|shirt_backfaces|headgear_N` renderers) and shows his skeleton mesh
    (`skeleton_N`, material blue_illum) only while channeling; also on the lobby display (skeleton hidden). `DeadlockHeroDef.MaterialGlows`
    makes blue_illum a flat orange self-lit material.
  - Shared: `DeadlockSurvivorKit.TryLoad` (tries both Addressables spellings) and `CloneProjectile` (vanilla projectile, behaviours stripped).

---

- `Survivors/GreyTalon/` — **Grey Talon** (`CursedAppleGreyTalonBody`, Deadlock `hero_orion`, model `models/heroes_staging/archer/archer`,
  icons `archer_*`, VO `sounds/vo/orion`). Built 2026-10-12, **never launched**. Kit is the user's (memory `grey-talon-design`):
  - M1 Bow: 300% hitscan, 0.6 s, 600 m reach; falloff only past 150 m (to 50% at 300 m). During Rain of Arrows: 5-arrow fan, +13% each.
  - M2 Charged Shot: draw 0.5 s, hold with M1 or M2, zoomed over-shoulder camera, walk at Deadlock's 1.5 m/s; release = Lynx Archer arrow
    (DLC4) x2.5, 203 m/s, pierces every enemy (`GreyTalonChargedArrow` sweeps its path), 800%, no falloff. 6 s cd.
  - Utility Rain of Arrows: 0.2 s, launch 27.5 m/s at 60 deg, 4 s glide (fall <= 0.64 m/s, drift 6.6 m/s), jump cancels. 8 s cd.
  - Special Guided Owl: 1.5 s cast, then the owl flies on the owner's client (`GreyTalonOwl`) steered by look (110 deg/s), 15 -> 36 m/s
    while forward is held, bursts on touch or on Primary (12 m, 2500%, 0.75 s stun); camera = `ICameraStateProvider` override that keeps the
    player's look rotation and only moves the position behind the owl. Jump releases it. Grey Talon is HiddenInvincibility + Cloak +
    SkillSilence while steering. Boss kills: +20% owl damage each, whole run (`GreyTalonOwlTrophies` on the master, shown as a buff).
    125 s cd (Deadlock's, user wanted it very long). The owl model is Deadlock's `archer_guided_arrow` (converted as `greytalon_owl`)
    inside the Engi harpoon ghost; others see a ghost from position messages. The perched owl (`owl_bodygroup`) hides while it flies.
  - Sounds approved by the user except Rain of Arrows' cast (silent). Item displays fitted (`GreyTalonItemDisplays.cs`; itempass
    `head_floor` keeps his braids out of the head region). Owl on his arm hidden; blast is green (Captain airstrike + omni, recoloured).
  - Playtest changes (2026-10-12): Rain of Arrows leaps at 45 m/s along 60 deg with no gravity, stops dead at 10 m, then glides;
    jump during the rise keeps the speed (the press is swallowed: `DeadlockMover.SuppressJumpUntil`); air dash works in the glide; one
    fan's repeat hits on an enemy deal 50%. Charged Shot hit radius 1.5 m, Lynx arrow ghost + trail. Owl 2500%, cruises 6 m/s,
    Primary detonates it, 2 s invulnerable grace after. Slide friction 1.2. Bow shot uses the new "delta" overlay layer (shoot_idle
    is a full pose); `GlbLoader` no longer removes root travel twice from cloth (his skirt).
  - Lobby intro `GreyTalonIntro.cs`: the owl dives onto the pedestal and he stands in the blast.
  - Shared change: a held ("hold") one-shot now also asks the hero's LoopRule (his jump pose ends on landing; his set has no in-air loop).
  - Pending: ping VO, Rain of Arrows cast sound, dash pose/sound sync in multiplayer (the in-glide dash plays locally only), writing review.

- `Survivors/Victor/` — **Victor** (`CursedAppleVictorBody`, Deadlock `hero_frank`, model `models/heroes_wip/frank/frank` but AnimGraph2 clips
  in `frank/clips`, icons `frank_*`, VO `sounds/vo/frank`, movement foley under `victor`). Prototyped 2026-10-12 on branch `1.5`, **built, never
  launched**. Kit and rules are the user's (memory `victor-design`); Vyper's file shape (Data / Model / States / Net / Survivor).
  - M1 Memento Mori: 75% hitscan, spins up 0.198 -> 0.171 s between shots (Deadlock spin rates), layered gun sound (windup/first/main/mech; no tail, user).
  - M2 Pain Battery (`VictorBatterySkillDef`): ready only when full. Charge = synced buff stacks 0..100 filled by `onServerDamageDealt` on
    Victor (damage taken to health, shield or barrier, measured in his TakeDamage hook; his own Aura/Jumpstart included;
    20% of max health fills it, user). 0.35 s wind-up, 7 bolts in a 40 deg fan (28 m,
    sphere-cast, walls stop them), each enemy once for 500%, and each enemy hit heals 15% of missing health (Deadlock T3, built in).
  - Utility Jumpstart: costs 15% current health (non-lethal, bypasses armor/block), heals 2x that (at least 10% max) over 4.5 s on a linearly
    fading rate (`VictorJumpstart`), +3 m/s fading in 10 buff steps. 2 charges (user), 12 s each.
  - Special Aura of Suffering (toggle, user): own "Aura" state machine (he keeps shooting); stays on until Special again or he dies (going down
    to Reanimation counts); 2 s cooldown after it ends. Server `VictorAura` ticks every 0.25 s: enemies within 8 m take 100% -> 600%/s over 8 s, then holds (Special source, proc 0.5, crits rolled, `OnHitEnemy`/`OnHitAll`, so on-hit, lifesteal, on-kill, Ethereal Shift's special
    bonus all apply), Victor takes 1.1% -> 5% max health/s (same ramp) and can die of it. Mystic Expansion widens it. Radius shown by `VictorAuraFx` as Abrams' Siphon Life ring (user, after the
    Focus Crystal terrain indicator never rendered): grey, terrain-hugging, Mystic Expansion radius.
  - Passive Shocking Reanimation (`VictorRevive`): while ready, hits on Victor are NonLethal; one that leaves him at 1 health starts it:
    slow wave (125% fading over 3 s, 36 m: user doubled Deadlock's 18), 3 s HiddenInvincibility + Reanimating buff (SkillSilence; the owner's body machine enters
    `Reanimate`), then up at 50% health with a 1000% blast + 1.5 s stun. 90 s cooldown on the master (`VictorReviveClock`, user), shown as a
    cooldown buff; a respawn on the same stage keeps it, a new stage resets it (user).
  - VFX green (user), recoloured with `Kit.Recoloured` at hue 0.33; UI colour grey-green. Sounds picked by me (user: no review pass needed).
  - Lobby intro `VictorIntro.cs` (user: Shocking Reanimation): his parts reassemble on the pedestal (3 s), he gets up; no lightning strikes (user).
  - Item displays fitted 2026-10-12 (`VictorItemDisplays.cs`, `HERO=victor`; items skip his `weapon` mesh). Chance Doll sits in his hip.
  - Not done yet: ping VO, Deadlock's ability upgrades beyond the T3 heal.

- `Survivors/Viscous/` — **Viscous** (`CursedAppleViscousBody`, Deadlock `hero_viscous`, model `models/heroes_staging/viscous/viscous`, Kelvin's
  skeleton, AnimGraph1 clip names like Abrams; in the air and his ability overlays use Kelvin's AnimGraph2 clips by path,
  `models/heroes_staging/kelvin_v2/clip/...`, which his own model exports: Deadlock's live Viscous runs hero_*+kelvin graphs). Goo Ball
  playtested OK 2026-10-12 (user); full kit added the same day and playtested by the user that evening:
  - M1 Goo Gun (0.21 s) / alt **Goo Glob** (Deadlock alt fire: 38 m/s, 0.2 gravity, 350% burst 3.56 m, 1.26 s). M2 **Splatter** (user: 3 bursts,
    500% each, 5 m) / alt **Puddle Punch** (user: 700%, 40 m aim, 4 m, launches enemies, allies (landing guarded) and Viscous himself, who
    gets x3 air control for 2.5 s via `DeadlockMover.BoostAirControl`). Utility **The Cube** (own "Cube" machine: tap = ally under the
    crosshair with Tricorn reticle, nobody = refund; hold 0.3 s = self; 3 s x Duration Extender, invulnerable, held in place (UpdateVelocity
    hook + rootMotion zeroed), SkillSilence, 5%/s heal, jump breaks out after 1 s; then `PostCubeBuff` 8 s: +2.5 m/s and stamina x1/0.7 via
    `DeadlockMover.RegenMultipliers`). Cube drawn by `ViscousCubeHost` with Deadlock's `viscous_cube` model (green box until converted), sized to the
    target (user, 1.6.0): `ViscousCubes.FitSize`, the bounds of its CharacterModel's drawn meshes, tallest/widest side x1.1.
  - Globs = `ViscousGlob` on a CrocoSpit clone; bursts are BlastAttacks so Mystic Expansion's hook widens them (effect spawned at base radius
    so the hook grows it); ghost scaled by Mystic Expansion. Punch radius/range, cube range, ball size all take Mystic Expansion (user).
  - Splatter/Punch/Cube castable while rolling (Deadlock CastWhileRolling). Ball steers with WASD relative to the camera, else the crosshair (user).
  - Lobby intro (user): he stands in the cube for its 3 s, it pops, then his select line. The cube and its sound go away with the display if it is swapped mid-intro.
  - Playtest 2 (user, 2026-10-12): Kelvin clip names must be SHORT in the anim table (`GlbLoader` keys clips by the last path part;
    the converter matches short names too); full paths silently fell back to the run in the air. Goo Glob: no gravity, splats at
    Deadlock range 25.4 m, Splatter's burst effect (Beetle spit burst had a stray ring). Splatter ignores enemies (IgnoreCollision on
    contact), bounces x2 (restitution 1.3, min 12 m/s). Puddle Punch: hold to aim (ring + arrow preview, local LineRenderers), release
    to punch. It goes on any surface the crosshair hits (floor, slope, wall, ceiling; the ring lies flat on it) and throws
    everything straight out of it, along the normal (user); the area is a cylinder standing out of the surface. Cube: slippery (keeps momentum, 0.15/s
    friction, slides downhill, hits shove it). Cube cast cleanses (Util.CleanseBody: debuffs, DoTs, stun); inside, hits deal 0 damage
    but are NOT rejected, so on-hit debuffs land and DoTs tick normally (user); no fall damage in the cube or the Goo Ball
    (OnCharacterHitGroundServer hooks). Out of them he takes fall damage (user, 1.6.0): no landing guard when the ball or his own
    cube ends mid-air, or after punching himself up (allies keep theirs). Goo Ball jump 22 m/s, cooldown 32 s (1.6.0, user; was 20) (user's cfg hand-edited, backup `.bak-viscous`).
  - Goo Ball = Deadlock `viscous_goo_bowling_ball`: 0.55 s curl (`rolling_goo`), then 11 s rolling (own `GooBall` Body state, owner moves it via
    rootMotion): 11.4 m/s, Primary held = 20.3 m/s (both x his move speed over base, sprint divided out; user, 1.6.0) (accel 15.2, decel 1.27 m/s2), steers to the crosshair 110/50 deg/s, walls bounce it
    (sphere cast 0.45 m smaller than the 1.4 m ball, so curbs roll over; 65% speed kept, 0.1 s no steering), jump + 1 double jump (12.7 m/s),
    1.8 m hit radius (Mystic Expansion), 250% + Heavy (user: Loader's rule, +30% per m/s of speed), 0.5 s stun, 10 m/s knock, each enemy every 1.2 s; it rolls through normal enemies.
    Bosses (user): double damage, no stun, the ball freezes dead 0.3 s (`BossFreeze`, roll timer paused) then bounces off as off a wall.
    Down-dash works in the ball too (shared movement). 50% resist (user; Deadlock 35%) = armor via
    the synced `BallBuff`. Special again ends it; cooldown 40 s starts when it ends. Camera pulled back x1.5 and up 1.2 m.
  - The ball is his own model's hidden mesh group `inflated` (renderers `inflated_N`, skinned to `inflated_center`, radius 1.51 model units).
    `ViscousBallLook` (body + display, runs after the animator) swaps body/gun/outline for it while the Body state is `GooBall`, and puts
    `inflated_center` at foot + radius, scaled to the radius (1.4 m x Mystic Expansion), rolled from the body's real motion (works for remote players).
  - Model height quirk: the loader scales by the tallest vertex, which is the ball's helmet (2.94 m) not him (2.45 m): `BallHeightFix`.
  - The hurtbox becomes the ball's sphere while he rolls (`ViscousBallHurtbox`, 2026-10-12; checked with the self-test `probe`). Item displays fitted 2026-10-12 (`ViscousItemDisplays.cs`; the ball and outline
    shell count for height only). Skin: Blue.

- `Survivors/Celeste/` — **Celeste** (`CursedAppleCelesteBody`, Deadlock `hero_unicorn`, model `models/heroes_wip/unicorn/unicorn`,
  AnimGraph2 clips in `unicorn/clips`, icons `unicorn_*`, VO `sounds/vo/unicorn`). Built 2026-10-12 while the user was away,
  then tuned by the user in playtest the same evening (memory `celeste-design`). All numbers are `[Celeste]` config knobs.
  - Movement (user): Lash's extras: 5 stamina, 2 air jumps (1.25x the ground jump's speed, `AirJumpScale`), 2 air dashes of 10 m;
    her innate low gravity (-25%, +44% air control).
    Air-dash pose holds 0.8 s before the in-air loop.
  - M1 Carny Life: a blade every 0.58 s, 120% (user: a light poke), 60 m/s, always drops 18 m/s2; its forward speed eases out from 15 m
    to nothing at 45 m (smoothstep, user: gradual), a blue-violet spark where it bounces, the shiv's own swing rings shrunk and held at size,
    homes gently onto an enemy within 22.5 deg of its flight, one bounce off the world, no ricochet; ghost 0.45x, light blue-violet
    (`CelesteDagger` on a Bandit shiv clone).
  - M2 Light Eater: 0.3 s (she hangs in the air; in the air the flare plays over her in-air pose), 70 deg cone 10 m, 400%, marks 8 s
    (mark shown over the enemy): +180% per blade hit on marked enemies and 18% of all her damage to them heals her. Alt Radiant Daggers:
    hold to preview (ring + beam marker), release to cast, like Puddle Punch; beam onto the aimed spot (30 m), 1.4 s, 600% in 8 m (a tall
    cylinder), +7% damage per enemy hit (Radiance: uncapped, one counter, 30 s from the latest hit).
  - Utility Dazzling Trick: 20% max HP barrier, 4 s window (Lunar Golem shell visual); the hit that takes the barrier to 0 bursts
    it: 500% in 14 m + 1.75 s stun (user; Deadlock silences).
  - Special Shining Wonder: hold to aim (slow fall), release: orb (Artifact shell's seeking flare, given SolarFlareGhost, 0.8x) at
    18 m/s (user: a floating ball of light), chases the nearest enemy through walls, 500% + 32% slow per hit, 24 bounces (user) within 15.5 m (Deadlock's; user, was 7) of
    the enemy it hit (or off her prism, which tops the prism back up), lingers 3.25 s for a target circling what it last hit.
  - Every radius takes Mystic Expansion; mark/slow/prism durations take Duration Extender. Her light is pink (Hue 0.9, her hair);
    projectiles blue-violet (Hue 0.72). Soft purple glow: self-lit horn shell (`unicorn_glow`) and a faint chest light (`CelesteGlow`).
  - VO by Deadlock's mapping: power3 = Shining Wonder, power4 = Radiant Daggers.
  - Her model exports no `shoot_idle`/`ability_luminous_flux`: `shoot_idle_start` and `ability_radiant_daggers_stand_idle` stand in.
  - Lobby intro (user): she ground-dashes in from the back right, as Vyper slides in (`dash_ground`), then turns to the camera. Item displays fitted (`CelesteItemDisplays.cs`). Skin: Mint.
  - Deadlock animator: in root-locked clips her cloth points are pulled back with the pinned pelvis (her skirt used to run ahead in the dash).

## Self-test (playtesting without the user, 2026-10-12)

- `Testing/SelfTest.cs`: launch with `--cac-selftest <script.txt>` and the game plays a script: `run <Body>` (title -> lobby ->
  run, leaves the drop pod), `wait`, `cmd <console command>` (DebugToolkit's too), `hold/tap/press/release <button>`, `move x y [s]`,
  `aim enemy|yaw pitch|off`, `slide 1|0`, `setskill <slot> <SkillDef name>` (live body only, the saved loadout is untouched),
  `shot <name>` (screen), `portrait <name> <yaw> [zoom] [height]` (the body alone on a flat background), `log`, `quit`.
  PNGs land next to the script. Every line is logged `[SelfTest]`, plus a load clock until the title screen. Without the
  argument nothing runs.
- Profile **Curiosities Selftest** (`C:\r2heimman\RiskOfRain2\profiles`): R2API, DebugToolkit, Risk Of Options and the fix mods
  (MiscFixes, VFiX, WolfFixes, GildedChunkSpamFix: without them the game hangs at 33% loading). Copy the built DLL into its
  `Unknown-CursedAppleCuriosities` folder, then `steam.exe -applaunch 632360 --doorstop-enabled true --doorstop-target-assembly
  "<profile>\BepInEx\core\BepInEx.Preloader.dll" --cac-selftest <script>`.
- Load time measured: ~150 s launch to title on that profile, 148 s with the mod removed. The mod costs ~4 s (plugin Awake 3.7 s:
  items 1.0 s, equipment 0.2 s, survivors ~0.2 s each, Risk Of Options page ~0.9 s). The rest is the game and VFiX's dither pass (37 s).
  Icons are now decoded once per file and the pickup card GLB parsed once.
- `spawn_ai Lemurian` matches the allied Devoted Lemurian: use `LemurianMaster`.

## Recent changes (since last handoff)

- **1.6.0 (branch `1.6`, 2026-10-12, user's list):** Cursed Broker guaranteed on stage 4 and in each later loop's first Bazaar;
  Viscous air jump = Kelvin `jump_ground` (`jump_start_short` is posed on Abrams' bigger skeleton: limbs 30-50% long) and ground dash =
  Kelvin's `dash_ground` roll; Puddle Punch windup 0.3 s + Deadlock's 0.45 s enemy portal telegraph (self-punch none), launch 42 m/s held
  for Deadlock's 0.6 s toss (`PunchToss`), then `disableAirControlUntilCollision` (others) / no air drag 2.5 s (himself,
  `DeadlockMover.SuspendAirDrag`); Celeste blade/orb/movement changes (see Survivors). Not playtested yet.
- **Playtest pass (user, 2026-10-12 evening):** Celeste retuned (movement, Carny Life, Light Eater, Radiant Daggers preview, uncapped
  Radiance, Dazzling Trick stuns, slower homing Shining Wonder, purple glow, VO mapping; see Survivors above); Viscous Goo Ball is Heavy
  (250% + 30% per m/s) with 50% resist; Viscous' lobby cube is cleaned up mid-intro; shorter skill descriptions for the six newer heroes;
  projectile clones strip components without the startup 'Can't remove' error; Siphon Bullets' stolen max HP synced to clients.
- **Autonomous session (2026-10-12, user away):** self-test harness (above); Celeste; item displays for Viscous and Celeste
  (`tools/itempass`, `HERO=viscous|celeste`, plus `allview.py`); skins Mint Celeste / Coral Vyper / Blue Viscous; Kinetic Dash
  triggers on dash-jumps (Deadlock's own trigger; `KineticDashJumpMessage` from the dashing client); Abrams' Heavy Melee hits are
  Special-source (were still Secondary from before it moved to the special slot). All eight heroes, every alt skill, all mod items
  at once and all 10 equipment ran in the self-test with no exceptions from the mod.
- **Character select colours (user, 2026-10-12):** each survivor's `Color` (body colour + SurvivorDef primary colour): Lash light blue,
  Abrams dark blue, Seven orange, Vyper yellowish green, Grey Talon dark green, Victor greyish green.
- **Victor (1.5.0, prototyped 2026-10-12, tuned in the user's playtests).** Sixth survivor; see Survivors above and memory `victor-design`.

- **Cursed Broker (1.4.0, built 2026-10-12, NOT playtested).** `Broker/`: `CursedBroker` (Shrine of Order clone with its
  purchase stripped and a `PickupPickerController` + Command picker panel; symbol tinted purple; spawned in
  `SceneDirector.onPostPopulateSceneServer`, 20% per stage from stage 3 or after a loop, always on stage 4 of the first loop, and (1.6.0, user)
  in the first Bazaar visit of each later loop (`Stage.onServerStageBegin`, near the Newt's shop terminal), config `[Cursed Broker]`),
  `CurseRules` (per-item Mult/Add on the effective stack count + improvement text; full table in
  `docs/CursedBrokerAudit.md`, regenerate with `tools/gen_curse_audit.py`; any non-lunar tier incl. white),
  `CursePenalties` (26 narrow penalties, rolled per item when a player first opens a broker, cached per broker+player), `CurseLedger` (per-master curse list, server-owned, mirrored by `CurseLedgerMessage`; IL hook on
  `Inventory.UpdateEffectiveItemStacks`; HUD shows real copy counts, purple glow, curse tooltip; Dio's/Larva revive twice).
  Range/Duration penalties feed `MysticExpansion.RadiusMultiplier` / `DurationExtender.Multiplier`.
  Debug: `cac_broker_stage3 [scene]` (skip to a stage 3 with a guaranteed broker), `cac_broker_spawn` (one next to you);
  registered through `[assembly: HG.Reflection.SearchableAttribute.OptIn]`. Config `Debug - Spawn Every Stage`.

- **Abrams Shoulder Charge (2026-10-12, user):** scoop 250% -> 50%; a wall slam deals 300% (`ChargeWallDamage`) to every carried enemy.
- **2026-10-12 balance/bug pass (built, not playtested).** Arctic Blast 75 s / 12 m; Cultist Sacrifice 120 s; Torment Pulse
  base range 12 m; Toxic Bullets UI; Lash: Flog removed, Ground Strike 300%, Grapple 6 s, Death Slam fixes (above), model
  ×1.25 (camera +0.4 m, ×1.15 distance); Abrams: Heavy Melee → special (1500%), Seismic → special alt, Charge 250%, Resilience 50% + 10 armor, model ×1.5
  (visual only). Goose Egg family + Majestic Leap (see Equipment). Mystic Expansion range pass. Slide key default is
  LeftControl (now a `KeyboardShortcut`, read live); existing cfgs keep their value.
- **Hero audio is positional in multiplayer (built, not playtested).** `DeadlockSounds` hears from the local
  `CameraRigController` (its target body, panned by its sceneCam) instead of `Camera.main`, which could be null and
  made every sound play unattenuated map-wide. Falloff is full within 5 m, squared to silent at each row's `Range`
  (was linear to 1.5× Range). The 1.5 s "don't talk over yourself" voice gate is now per speaker, not global.
  Ping lines keep `Range = 400` on purpose (team-wide, like Deadlock).
  **Settings menu:** `ModSettings.cs` puts every config entry on Risk Of Options' mod page (soft dependency; options are
  restart-required except the slide keys). Config defaults changed but existing cfg files keep old values (user's choice);
  the user's Curiosities Dev cfg had the rebalanced keys reset by hand.
- **Multiplayer stat-sync audit (2026-10-12, built, NOT playtested in multiplayer).** Stat hooks that read a
  server-only tracker gave non-host players nothing, because each client recalculates its own body's
  speed and damage. These now read the synced buff instead (same numbers, same single-player behavior):
  **Spellslinger** (attack speed ← `GetBuffCount`), **Berserker** (damage ← Rage buff count),
  **Burst Fire** and **Kinetic Dash** (← `HasBuff`). The trackers still own the timers and stacks on the server
  and mirror them to the buff; their explicit `RecalculateStats` calls are gone because AddBuff/RemoveBuff mark stats dirty.
  Checked and left alone: Frenzy and Sprint Boots (the tracker runs on every machine from synced health or
  sprint state), Glass Cannon, Heroic Aura, Radiant Regen, Blood Tribute, Trophy Collector, Inhibitor and
  Cultist Sacrifice (already buff-driven), Fortitude and Goose Egg (inventory only), and survivors (they have no stat hooks).
  **Siphon Bullets** (2026-10-12): the stolen max HP travels as a hidden stacking buff (`CursedApple_SiphonStolenHealth`, 1 stack per HP),
  so clients see it too (self-tested single player: 95 -> 99 while hitting golems, back to 95 when they died).
