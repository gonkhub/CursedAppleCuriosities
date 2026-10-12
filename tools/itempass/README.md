# Item pass (hero item displays)

Fits every vanilla item display onto a Deadlock hero, offline, and writes the hero's `*ItemDisplays.cs`
(anchor table + rule overrides). Extracted game data goes to `data/` and renders to `out/` (both gitignored:
they hold Risk of Rain 2 assets).

Run from this folder, in order (Python needs `UnityPy`, `numpy`, `Pillow`; Blender 5.1 for the BVH and renders):

1. `python catalog.py` - Addressables GUID -> asset path (`guid.json`)
2. `python extract.py` - Commando's rest skeleton/body, `idrsCommando` rules, every display prefab mesh (~2 min)
3. `python weights.py` - per-vertex dominant bones, for body regions
Steps 3-7 take the hero from `HERO=<abrams|lash|seven|vyper>` (configs in `rig.py`: scale, hidden meshes, gun to skip, GLB name).

4. `python hero.py` - the hero (cached GLB) placed exactly as `DeadlockModelSwap.TryAttach` places him
5. `blender -b --factory-startup -P gen.py` - anchors + fitted rules -> `data/abrams_fit.json`
6. `python tiles.py abrams_fit.json all` - review sheets in `out/` (hero in red, Commando's original in blue)
7. `python user10.py` - folds in hand placements; merge into `data/abrams_fit_final.json` (see the merge in the
   session that made this), then `python emit.py <path to AbramsItemDisplays.cs>`

How it fits: each Commando attach point maps into the matching hero body region (box to box; torso heights by
hip/shoulder/neck landmarks; limbs turned with the bone), items are mapped by their mesh centre, then slid along
the region's outward direction until they touch the hero's surface the way they touch Commando's, turned to the
hero's surface normal, and items that wrap a limb are grown until as much shows as on Commando.
The anchors are fixed per bone (`DeadlockHeroDef.ItemAnchors`), so placements are the same every spawn.

Spot fixes on Lash (no full re-fit; needs `data/` from steps 1-3 plus `HERO=lash python hero.py`):
- `python lash_view.py <out prefix> Key:index ...` - renders Lash's rest mesh with those rules as `LashItemDisplays.cs` has them
- `python lash_fit.py '[["Key", index, "AttachPoint", [dx,dy,dz], scale, "centre"|"base"], ...]'` - rewrites those rules as
  Commando's own placement, moved by the offset (and scaled about its centre or base) onto Lash
- `python lash_review.py [Key ...]` - review sheets `out/lash_review_*.png` (Lash red, Commando blue) + `out/lash_review.csv`
  (closest gap to the body on each); all items takes ~17 min
- `python lash_push.py Key:index[:gap] ...` - slides an item straight out from its bone until it clears the body

Seven (2026-10-12): his halo headgear doubles the head region, so run the fit twice (`HERO=seven` and `HERO=seven_bare`,
which keeps the halo for his height but not for the fit), then `python seven_head.py` merges them into
`data/seven_fit_final.json` (face items from the bare fit, pushed out of his face; crowns/horns on the halo; glasses
lowered under the brim), then `HERO=seven python emit.py`.

Review any hero's table as the game file has it: `HERO=abrams python review.py [Key ...]` -> `out/<hero>_review_*.png`.

Mirrored displays: before 2026-10-12 `decompose()` dropped Commando's negative scales (9 rules, e.g. the second Ifrit horn).
`python mirror_patch.py <Hero>ItemDisplays.cs <fresh emit>` replaces only those rules, keeping hand placements. Applied to
Abrams; **Lash left as-is by the user's choice** (his placements are hand-tuned).
