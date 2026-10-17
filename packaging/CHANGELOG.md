# Changelog

## 3.1.0
Fixes and balance, no new content.
- **Apollo, Flawless Advance:** longer reach (20 m stab, 6 m lunge), and it now stabs at any angle, up or down, where you aim.
  His blade flashes bright red for the perfect window: let go while it's lit.
- **Apollo, Disengaging Sigil:** its ring and burst are red.
- **Apollo, Riposte:** keep holding Riposte through the counter and, after the slash, he winds up a full heavy melee kick (400%).
- **Paradox, Kinetic Carbine:** the charge now glows blue from her gun instead of turning her white.
- **Abrams:** Seismic Impact drags flying bosses down with him, and Shoulder Charge carries them along. Ground bosses still stop him.
- **Celeste:** Carny Life 200% -> 250%.
- **Shiv:** Bloodletting defers 50% of damage taken (was 75%), 75% at full Rage. Its drain no longer stops his sprint (real hits still do).
- **Shiv, alt fire:** much less lift (55% of the push upward, was 135%), and shots in the air no longer stack lift. During a slide it pushes 60% harder. Throwing a knife no longer resets the gun's fire cycle (together these allowed endless flight).
- **Shiv's unlock "Bloodletting":** 50 Bleed stacks on one enemy (was 100).
- **Mystic Expansion:** fixed the bonus range on vanilla survivors' abilities (Huntress' and Engineer's targeting, REX,
  Merc's Eviscerate, Loader's grapple, aimed throws, ...), which 3.0 had stopped applying. Bullets were unaffected.

## 3.0.0
- **Settings cleaned up**: the config now only holds each hero's key layout, audio volumes and your Deadlock folder/converter.
  Every balance number is built in, and the old tuning entries are removed from your config file the first time you start
  the game, so everyone plays the same numbers. You no longer need to reset or delete your config after updating.
- Five new survivors from Deadlock: **Shiv**, **Mina**, **Paradox**, **Bebop** and **Apollo** (thirteen in all). The
  character select is sorted A-Z, vanilla survivors included.

### Unlockables
- **Every Deadlock hero starts locked.** Mina, Bebop, Paradox and Lash are hidden as silhouettes in the Deadlock
  districts (the Challenges menu gives a hint for each); every other hero unlocks through their own challenge, playable
  as any survivor (e.g. Abrams: Infernal Resilience, Seven: Electric Chair).
- **Alt skills** for Abrams, Seven, Celeste, Viscous, Shiv, Paradox, Bebop and Apollo start locked behind challenges.
- **Mastery skins** for all thirteen heroes (beat the game or obliterate on Monsoon).
- **Challenge skins**: Abrams Crimson, Mina Blackmore Colors, Apollo Sapphire, Viscous Pisscous and Bebop Chrome.
- **Fan-made model skins**: Celeste's **Spirit Guardian** (TSelky's Spirit Guardian Celeste, challenge **Celeste: Encore**)
  and Seven's **Bunny Seven** (funnymonkeyman's Bunny Seven, challenge **Seven: Hare Trigger**). They read your own copy
  of the mod's `.vpk` (see the README); without it the skin shows the hero's own model.

### Deadlock district stages
- If you own Deadlock, four districts of its map can replace a stage: **Haunted Lot** (stage 1), **Umbria University** (2),
  **Sunken Plaza** (3) and **Chinatown** (4), each at a 10% chance and only when every player in the lobby has the same map
  converted.
- Their shops sell items at chest prices plus a 20% Shop Tax, and every purchase raises that shop's prices by another 25%
  for the stage. The climb ropes launch you like geysers, and Sunken Plaza's pit holds a hostile Bebop.

### Balance
- **Cursed Broker**: 2% chance on any stage (was 10%); stage 3 and Bazaar chances unchanged. Chinatown's souls dealer
  garage always has one.
- **Spirit Snatch**: barrier equal to 10% (+10% per stack) of the damage you deal to enemies within 13 m, uncapped (was 1%
  of max health per hit).
- **Toxic Bullets**: hits fill a Toxin meter on the enemy (1000% of your damage fills it), which drains if you stop
  hitting. Full: poison for 0.5% of max health per second per stack (was 1%) and -50% healing.
- **Intensifying Magazine**: +45% at one stack, +15% per extra stack (was +30% each). The ramp now shows as a buff.
- **Mystic Expansion**: +30% at one stack, +15% per extra stack (was +30% each).
- **Duration Extender**: +15% at one stack, +7.5% per extra stack (was +15% each).
- **Tankbuster** and **Spirit Burn** trigger on hits of 400% or more (was 500%).
- **Fortitude**: +15% max health per stack and nothing else.
- **Lifestrike**: no cap on healing per hit. **Sprint Boots**: +1 health/s per stack out of danger (was +0.5).
- **Trophy Collector**: $10 per minute per Trophy, +50% for each Trophy beyond the first.
- **Abrams**: Seismic Impact is his default special, Heavy Melee the alternate.
- **Apollo**'s Riposte only parries; press it again within 3 s to dash-counter the enemy you aim at.
- **Bebop**'s Sticky Bomb can go on allies, and Humble Pie is drawn as a red ray to the crosshair.
- **Celeste**: each Radiant Daggers cast's Radiance stacks expire on their own 10 s timer, +10% damage each (was 7%).
  Light Eater's mark makes all her damage to the enemy +125%.
- **Mina** can cast Sanguine Retreat during Nox Nostra and pick the ult back up where she reforms.
- **Shiv**: Bloodletting defers 75% of damage taken (was 50%) and drains exactly what its yellow bar shows. Full Rage holds
  6 s before draining. Slice'n'Dice always keeps at least 1 s of cooldown.
- **Victor**: Shocking Reanimation cooldown 120 s (was 90 s).
- **Vyper**'s petrified enemies turn to stone and freeze in place.

### Fixes
- **Spellslinger**'s shorter cooldowns now actually apply (they never did).
- Mod on-hit items no longer trigger on hits the target blocks (Petrified enemies, invulnerable bosses, Tougher Times).
- **Berserker** no longer gains Rage from blocked hits; **Counterspell**'s ward is no longer used up by damage-over-time
  ticks; **Ethereal Shift** blocks every kind of damage.
- **Soul Urn** only drops when an enemy hits you (not self-damage, falls or allies).
- **Mystic Expansion** grows Captain's supply-drop range. Seven's hits show his lightning sparks; Shiv's full-Rage glow
  shows.
- **Focus Lens** echoes exactly 50% of the damage the target took.
- Lobby intros clean up their sounds and effects when you switch heroes mid-intro; Paradox's Pulse Bomb no longer
  cancels her Kinetic Carbine; Mina's ult pose no longer sticks.

## 2.0.0
- Updating from an older version: your config file keeps the values it already has, so balance changes from past
  updates only apply to settings you reset to their defaults, or if you delete
  `BepInEx/config/com.cursedapple.curiosities.cfg` before starting the game.
- Five new survivors from Deadlock: **Vyper**, **Grey Talon**, **Victor**, **Viscous** and **Celeste**.
- New: the **Soul Urn**. On 30% of stages an urn waits at least 180 m from the teleporter. Pick it up and carry it to the
  teleporter before its timer runs out (30 s or more, longer the further away it spawned) without taking damage. Getting
  hit, running out of time or dying sends it back. A delivery drops 3 items, +1 for every stage already cleared.
- **Seven**: **Storm Cloud** ramps up to 800% damage per second (was 600%), drifts and climbs twice as fast and follows your
  move speed. Its bolts come every 0.25 s, faster with attack speed. **Lightning Ball** is 50% bigger. **Static Charge**'s
  blast radius is doubled (5 -> 10 m).
- **Mystic Expansion** also extends cast ranges: vanilla survivors' bullets, aim arcs, target searches and Loader's grapple,
  plus the new heroes' ranged skills.
- The **Curiosity Catalog** item is gone (its after-teleporter shop and chest lockout with it).
- **Artifact of the Curio** also filters what Command's picks and printers offer.
- **Goose Eggs**: each stack now hatches more than the last. Grey yields 4 whites at 1 stack, 10 at 2, 18 at 3; Green 2/5/9;
  Red 1/2/4; Golden 1/3/6; Void 2/5/9. New config keys (**First Stack Items**, **Extra Items Per Stack**) replace
  **Items Per Stack**.
- **Goose Eggs** only mature while their holder is alive and the run timer is actually counting up (they now also stop
  during trial gongs and anywhere else the timer is held).

## 1.9.0
- Updating from an older version: your config file keeps the values it already has, so the Goose Egg change below only
  applies if you reset **Goose Eggs - Damage Penalty** to its default (or delete the config file).
- **Seven**: **Storm Cloud** always reaches full strength in 7 seconds. Extra duration (e.g. from duration-extending items)
  now only adds time at full damage and size, instead of stretching the ramp-up. Its lightning strikes each enemy twice as
  often, for half as much per bolt.
- **Goose Eggs**: holding an egg now costs -25% damage (was -10%).

## 1.8.0
- Updating from an older version: your config file keeps the values it already has, so balance changes from past
  updates (including the ones below) only apply to settings you reset to their defaults, or if you delete
  `BepInEx/config/com.cursedapple.curiosities.cfg` before starting the game.
- **Abrams**: **Heavy Melee** lunges about 10 m (was 6).
- New red item: **Lightning Scroll**. The first time your special skill hits an enemy, a purple bolt of lightning strikes it
  3 seconds later for 500% damage (+250% per stack) and stuns it for 2 seconds.
- The mod's content is now an **expansion** you can switch off in the lobby, next to the base game's expansions. Off: none of
  its items, equipment or artifacts drop or can be given, and no Cursed Broker appears. Its survivors can still be played.
- **Toxic Bullets**: the poison is now credited to you, so poison kills pay gold and XP and trigger on-kill effects. It deals
  exactly 1% of the enemy's max health per second per stack, ignoring armor.
- **Colossus**: other players now see you grow (the growth only showed on the host).
- **Hunter's Aura**: holders no longer interfere with each other; an enemy takes the strongest aura on it.
- **Cursed Broker**: a cursed **Hunter's Aura** now doubles its damage amplification (it was tripling it, and its tooltip
  promised a wider aura that never happened).
- **Counterspell**: a blocked hit is fully rejected, so its debuffs and on-hit effects don't land either.
- **Trophy Collector**: gold per Trophy scales with difficulty (like chest prices). Trophies are cleared whenever you leave a
  stage, not only when the teleporter charges, so leftover gold can always be turned into XP.
- **Burst Fire**: only hits from your primary skill trigger it.
- **Radiant Regen**: only secondary, utility and special skills count. The cast heal is 5% of max health (5 s cooldown) and each
  Radiance stack regenerates 1% of max health per second.
- **Long Range**: boosts the hit itself instead of dealing a separate hit.
- **Tankbuster**: 5% of the enemy's max health (+2.5% per stack), was 10% (+5%).
- **Spirit Burn**: burns for 60% damage per second per stack. It was described as 35% but actually dealt about 9%.

## 1.7.0
- Survivors now release one at a time: this package has **Lash**, **Abrams** and **Seven**. The other heroes from
  earlier notes come back in later releases.
- **Seven**: his gun's tracer is a thin beam of lightning instead of a thick laser.
- **Seven**: **Storm Cloud** starts 30 m wide (was 10) and strikes every enemy it can see with a bolt of lightning about
  four times a second, each proccing fully (was 0.3), for less damage overall (100-600% per second, was 150-990%). Stray bolts
  also fall around Seven, hitting anything near them.
- Wall jumps (every Deadlock hero) lose height when spammed: each one within a second of the last is 60% as high as the one
  before. Waiting a full second, an air jump, an air dash or landing restores full height.
- Wall jumps: the first is free, each one after it costs half a stamina until you land or air jump/air dash.
- Deadlock heroes start sprinting 1 second after last taking damage or attacking (was RoR2's 5 seconds out of combat).
- **Seven**: sprinting while shooting during **Power Surge**, he runs with his gun up instead of his out-of-combat sprint.
- **Seven**: **Lightning Ball** slows the enemies it shocks by 80% while they're inside it (and half a second after).
- **Majestic Leap**: leaps 50% higher, and its cooldown is halved (22.5 s, was 45).
- **Lash**: **Death Slam** pulls enemies up to him in the same time from any distance (from the skybox too), and always waits
  until every lifted enemy is up beside him before throwing.
- Every Deadlock hero is 5% smaller.
- **Cursed Broker** spawns: 10% on any stage, 50% on stage 3 of every loop, and 50% each visit to the Bazaar Between Time
  (once one has appeared there, not again until the next loop). No longer guaranteed on stage 4.
- **Goose Eggs** don't mature while the run timer is stopped (the Bazaar Between Time and other places it pauses).

## 1.6.0
- Fixed: **Cursed Broker** cooldown penalties (all skills, Secondary, Utility, Special) now actually lengthen your cooldowns.
- Cursed items have a dark-red outline in your inventory.
- **Cursed Broker**: always appears on stage 4, and after looping in your first visit to the Bazaar Between Time each loop.
- **Viscous**: his air jump no longer stretches him out, and his ground dash is his own roll.
- **Viscous**: **Puddle Punch** winds up like Deadlock's (its portal shows for a moment before the fist comes out), throws
  harder over a 25% wider area (5 m), and the throw carries: everything punched keeps its full speed for a moment, then flies on without slowing.
- **Viscous**: **The Cube** fits whoever is inside it (much smaller around Viscous himself).
- **Viscous**: **Goo Ball** cooldown 32 s (was 20). Anything it can't stun now gets the boss treatment (double damage, a
  dramatic freeze), and the ball rebounds off them faster than it hit, keeping that speed for a moment.
- **Viscous**: the **Goo Ball** drops back to its slow roll quickly when you let go of Primary.
- **Viscous**: move speed items (and slows) now change how fast the **Goo Ball** rolls.
- **Viscous** takes fall damage again whenever he isn't in the Goo Ball or The Cube (landing after popping out mid-air,
  or after Puddle Punching himself).
- **Viscous**: cubing yourself while in the **Goo Ball** stops the ball's control: the cube slides on with the ball's speed,
  and the ball picks up from there when the cube ends.
- Fixed: damage over time (burn, poison...) no longer hurts anyone inside **The Cube**; it still sticks and resumes after.
- Fixed: with a spare **Goo Ball** charge, ending the ball early no longer rolls the next one at once; the charge is kept.
- **Celeste**: blades are a little slower and ease out gradually toward the end of their range; they flash where they bounce
  off a surface, and their rings no longer grow in flight.
- **Celeste**: **Shining Wonder** deals half damage (500%) but bounces three times as often (24), and reaches 15.5 m to its next target again (was 7).
- **Celeste**: shorter air dashes (10 m, was 14) and higher air jumps.
- **Lash**: **Ground Strike** drags airborne enemies he dives into down with him (stunned, no fall damage) and strikes only
  when he reaches terrain, walls and slopes included; enemies on the ground no longer stop the dive.
- **Lash** and **Seven** shoot differently: Lash is close range (full damage to 16 m), tight and recoil-free; Seven is mid range
  (full damage to 20 m) with climbing recoil and a lightning tracer.
- **Lash**, **Seven**, **Celeste**, **Victor** and **Viscous** are 15% bigger, each with a camera raised and pulled back to suit.
- **Viscous**: the **Goo Ball** camera rises and pulls back as the ball grows (Mystic Expansion), even mid-roll.
- **Celeste**: **Light Eater** reaches 15 m (was 10). **Carny Life** blades fly up to 90 m (was 45), slowing evenly from 10 m.
- **Seven**: his character-select intro settles smoothly from the storm into his idle pose.

## 1.5.0
- New survivor: **Viscous**. His **Goo Ball** rolls him up into a bowling ball that bowls through enemies. **Splatter**
  bounces a ball of goo that bursts three times; alt **Puddle Punch** launches everything off any surface, him included.
  **The Cube** encases an ally (tap) or himself (hold): invulnerable, then faster with quicker stamina.
- New survivor: **Celeste**. She moves like Lash (more stamina, two air jumps and air dashes) and falls slowly. **Carny Life** throws
  arcing blades of light that curve onto enemies and bounce off walls. **Light Eater** marks enemies: your blades hit them harder and
  heal you. Alt **Radiant Daggers**: aim, then call down a beam of light that strengthens you for every enemy it hits, with no cap.
  **Dazzling Trick** raises a barrier prism that bursts and stuns enemies if it breaks. **Shining Wonder**: hold, then release a
  slow orb of light that chases the nearest enemy and bounces between enemies close together.
- **Viscous**: the Goo Ball is **Heavy** (like Loader's punches): 250% plus 30% per m/s of speed, rolling or falling, and takes 50% less damage while rolling.
- New skins: **Mint** Celeste, **Coral** Vyper, **Blue** Viscous.
- **Kinetic Dash**: dash-jumping (Deadlock heroes) triggers it too, as in Deadlock.
- Fixed: Abrams' **Heavy Melee** now counts as his Special (Ethereal Shift's bonus and other Special effects apply).
- Viscous and Celeste show items on their bodies.
- Shorter skill descriptions for Seven, Vyper, Grey Talon, Victor, Viscous and Celeste.
- Fixed: Viscous' lobby cube no longer stays forever if you switch survivor during his intro.
- Fixed: in multiplayer, **Siphon Bullets**' stolen max health now shows for every player, not only the host.
- Fixed: while Viscous rolls, hits land on the whole Goo Ball (his hitbox was still his standing size).
- New survivor: **Victor**.
  **Pain Battery** charges only from damage you take, then fires a fan of shocking bolts that heal you for
  each enemy hit. **Jumpstart** costs some health, then heals back more than it took and speeds you up.
  **Aura of Suffering** hurts everything around you, and you, harder the longer it runs. Passive
  **Shocking Reanimation**: once every 90 seconds (and again each new stage), dying knocks you down instead, and you get back up with
  half your health and a stunning blast.
- Each survivor has its own colour on the character select screen.
- New artifact: **Artifact of the Goose**. Every player starts the run holding a random Goose Egg.
- **Goose Eggs**: each egg now matures on its own. Two players holding the same kind of egg no longer share one timer, and an egg you swap out keeps its own progress on the ground.
- **Frenzy**: the bonus now lasts 5 s at its strongest after you heal back above the threshold.
- **Sprint Boots**: also regenerate +1 health per second (+0.5 per stack) while out of danger.

## 1.4.0
- New interactable: the **Cursed Broker**, a rare shrine (stage 3 onward, or any stage after looping).
  Curse one non-lunar item (whites included): it and every copy you hold or find later become roughly twice as strong, but you
  take a harsh penalty to one specific thing (a skill's damage, a cooldown, gravity, healing...)
  for the rest of the run. Each player may curse once per broker; hover an item in its menu, or a
  cursed item in your inventory, to see exactly what is improved and what it costs.

## 1.3.0
- New survivor: **Grey Talon**. His bow fires slow, very long range arrows (full damage out to
  150m). **Charged Shot**: draw and hold a zoomed shot, then loose a large arrow that pierces
  everything for 800%. **Rain of Arrows**: leap high and glide while every shot splits into 5
  arrows (cancel the leap with jump to keep your speed; you can air dash while gliding).
  **Guided Owl**: steer your owl into enemies for 2500%, detonate it early with Primary; you are
  invulnerable and unseen while steering and for 2s after. Each boss it kills makes it stronger.
- Grey Talon has a character-select entrance: his owl crashes into the pedestal.
- Seven, Vyper and Grey Talon show items on their bodies.
- **Abrams**: Shoulder Charge's hit is 50% (was 250%), but slamming enemies into a wall deals
  300% to every enemy carried.
- **Goose Eggs** take 5 minutes per stack (was 2:30).
- Fixed: items Commando wears mirrored (one of the Ifrit horns, and others) now mirror on Abrams too.

## 1.2.5
- New survivors: **Seven** (Cold Calculus, Lightning Ball / Static Charge, Power Surge,
  Storm Cloud) and **Vyper** (The Rattler, Screwjab Dagger, Lethal Venom, Petrifying Bola,
  passive Slither).
- Lash, Seven and Vyper have character-select intros.
- New items: **Duration Extender** (white). New equipment: **Majestic Leap** and the
  **Goose Egg** family (Grey, Green, Red, Golden, Void).
- **Mystic Expansion** now gives +30% radius and range, and also grows many vanilla items'
  areas (Ukulele, Tesla Coil, Razorwire, Focus Crystal, Warbanner and more).
- **Spirit Snatch** reworked: close-range hits grant barrier.
- **Healing Tempo** reworked: more healing received, and overhealing builds Tempo stacks.
- **Spellslinger** buffed (10 stacks, +10% attack speed and shorter cooldowns each).
- **Lash**: Flog removed, Ground Strike 300%, Grapple 6s cooldown, Death Slam fixes.
- **Abrams**: Heavy Melee is the default special (Seismic Impact is the alt), Shoulder
  Charge 250%, Infernal Resilience 50% + 10 armor.
- Balance: Arctic Blast, Cultist Sacrifice, Torment Pulse.
- Every option is on the Risk Of Options settings page.
- Multiplayer: Spellslinger, Berserker, Burst Fire and Kinetic Dash now give their bonus to
  non-host players. Hero sounds are positional.
- Fixed equipment visual effects that never appeared (Colossus, Counterspell, Focus Lens,
  Ethereal Shift, Arctic Blast, Refresher and others) and the targeting reticle. Void Goose
  Eggs now roll from Void Cradle's drop table.

## 1.0.0
- New survivors: **Lash** and **Abrams** (Shotgun, Siphon Life / Heavy Melee, Shoulder Charge,
  Seismic Impact, passive Infernal Resilience).
- Both share Deadlock's movement: stamina, air jumps and dashes, dash-jump, slide and wall
  jump. Their slams don't cause fall damage.
- Items show on Lash's and Abrams' models.
- New items: **Mystic Expansion** (white), **Ricochet** and **Inhibitor** (red).
- New equipment: **Ethereal Shift**, **Refresher**, **Arctic Blast**, **Cultist Sacrifice**
  and **Focus Lens**.
- Equipment has visual and sound effects.
- **Curiosity Catalog** reworked: finishing the teleporter event drops a single shop with
  every Curiosity item. It stays open between purchases and prices update live. It also has
  an icon now.
- Every item and equipment has a model when it's on the ground and a short pickup description.
- **Mystic Shot**: the burst now deals 100% base damage (+100% per stack).
- **Tankbuster**: flat 10s cooldown; each extra stack adds +5% of the enemy's max HP.
- **Trophy Collector**: each Trophy also grants +10% sprint speed. Trophies clear when the
  teleporter is charged.
- The package now includes the model converter for players who own Deadlock.

## 0.9.1
- Fixed a config key that stopped the mod from loading, which kept the Curiosity shop from appearing.
- A broken item can no longer stop the rest of the mod from loading.

## 0.9.0
- Config file for every item's and equipment's main numbers.
- **Blood Tribute**: +50% move speed (was +100%), and -50% healing received while active.
- **Trophy Collector**: gold per Trophy is flat; each copy raises the Trophy cap instead.
- **Curiosity Catalog**: only holders can shop; printers and scrappers work again.

## 0.5.2
- Blood Tribute icon.

## 0.5.1
- New lunar item: **Curiosity Catalog**, a personal shop each stage in exchange for chests.
- New equipment: **Blood Tribute**.
- **Siphon Bullets** reworked: hits steal enemy max HP, lost when that enemy dies.
- Removed **Juggernaut**.

## 0.2.5
- New items: **Battle Vest**, **Mystic Shot**, **Fortitude**, **Radiant Regen**,
  **Toxic Bullets**, **Tankbuster**, **Spirit Burn**, **Trophy Collector** and **Juggernaut**.
- New artifact: **Artifact of the Curio**.

## 0.1.0
- Initial playtest release: 19 items and 3 equipment (Counterspell, Colossus, Golden Goose Egg).
