"""A district's data block for MapSectionDef.cs, from its outline: the outline itself, the vent tunnels and their openings
(sealed, never walked), the shop counters, the climb ropes, the interactable spots (neutral camps, their spawns, item crates) and the
subway "TELEPORTER" doorways inside it.

Usage: section_consts.py <outline.json> <entsfull.json> <trig.json> [margin units, default 200]
  outline.json  [[x, y], ...] in Source units (trace_outline.py / selpoly.py)
  entsfull.json entities.py's output
  trig.json     trigger volumes: [{cls, org, mn, mx}, ...] in Source units (bounds of each trigger's model, relative to org)
Prints C# to paste into the district's data class."""
import json, sys
from shapely.geometry import Polygon, Point, box

outline = json.load(open(sys.argv[1]))
if outline[0] == outline[-1]: outline = outline[:-1]
ents = json.load(open(sys.argv[2]))
trig = json.load(open(sys.argv[3]))
margin = float(sys.argv[4]) if len(sys.argv) > 4 else 200.0
poly = Polygon(outline)


def boxes(cls):
    out = []
    for o in trig:
        if o['cls'] != cls: continue
        x, y, z = o['org']
        b = box(x + o['mn'][0], y + o['mn'][1], x + o['mx'][0], y + o['mx'][1])
        if b.intersects(poly.buffer(margin)):
            out.append([round(x + o['mn'][0]), round(y + o['mn'][1]), round(z + o['mn'][2]), round(x + o['mx'][0]), round(y + o['mx'][1]), round(z + o['mx'][2])])
    return out


def block(name, rows, note):
    body = "".join("            " + ", ".join(map(str, r)) + ",\n" for r in rows)
    return f"        // {note}\n        public static readonly int[] {name} = {{ // min x, y, z, max x, y, z\n{body}        }};\n"


def origin(e): return [round(float(t)) for t in e['origin'].strip('[] ').split(',')]


spot_classes = {'info_neutral_trooper_spawn', 'info_neutral_trooper_camp', 'item_crate_spawn'}
spots = sorted({tuple(origin(e)) for e in ents if e.get('classname') in spot_classes and 'origin' in e and poly.contains(Point(origin(e)[:2]))})

print("        public static readonly float[] Outline = {" + ", ".join(f"{x}, {y}" for x, y in outline) + " };")
print(block("VentTunnels", boxes("citadel_tunnel_trigger"), "the small-hero vent tunnels: no walking there"), end="")
print(block("VentOpenings", boxes("citadel_passthrough_fake_wall"), "their openings: sealed"), end="")
print(block("Shops", boxes("trigger_item_shop"), "Deadlock's shop counters: Curiosity shops"), end="")
print(block("Ropes", boxes("citadel_trigger_climb_rope"), "climb ropes: a RoR2 geyser at each one's foot launches you to its top"), end="")
tele = boxes("citadel_trigger_teleport")
if tele: print(block("SubwayTeleports", tele, 'subway "TELEPORTER" doorways: walking in sends you to the teleporter'), end="")
else: print('        // SubwayTeleports: no citadel_trigger_teleport in trig.json near the outline: export those triggers or set the doorway by hand')
print("        // Deadlock's neutral trooper camps and spawns and item crates (x, y, z units): where interactables may stand.")
print("        public static readonly int[] InteractableSpots = {")
for i in range(0, len(spots), 4):
    print("            " + ", ".join(", ".join(map(str, p)) for p in spots[i:i + 4]) + ",")
print("        };")
print(f"// area {poly.area * 0.0254 ** 2:.0f} m^2, {len(spots)} interactable spots", file=sys.stderr)
