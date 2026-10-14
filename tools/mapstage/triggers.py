"""Trigger volumes of a whole map (read-only on Deadlock): every vent tunnel, vent opening, shop counter, shop tunnel and
subway teleporter doorway, as axis-aligned bounds around each trigger's origin. The input of section_consts.py.

Usage: triggers.py <citadel dir> <map name> <entsfull.json> <work dir> <out trig.json>
Set VRF_CLI to the Source2Viewer-CLI that reads the current Deadlock (tools/vrf). Never while Deadlock is running."""
import json, os, subprocess, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mapscan import CLI, triangles

CLASSES = {"citadel_tunnel_trigger", "citadel_passthrough_fake_wall", "trigger_item_shop", "citadel_trigger_shop_tunnel",
           "citadel_trigger_teleport", "citadel_trigger_interior"}
citadel, mapname, ents_p, work, out_p = sys.argv[1:6]
def model_of(e): return e.get("model", "").split('"')[-1]  # the dump writes resource_name:"path"
ents = [e for e in json.load(open(ents_p)) if e.get("classname") in CLASSES and model_of(e).endswith(".vmdl")]
models = sorted({model_of(e) + "_c" for e in ents})
print(f"{len(ents)} triggers, {len(models)} models", file=sys.stderr)
os.makedirs(work, exist_ok=True)
vpk = os.path.join(citadel, "maps", mapname + ".vpk")
for i in range(0, len(models), 40):
    subprocess.run([CLI, "-i", vpk, "-f", ",".join(models[i:i + 40]), "-o", work, "-d", "--gltf_export_format", "glb"],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
out, missing = [], 0
for e in ents:
    base = os.path.join(work, model_of(e).replace(".vmdl", ""))
    tri = None
    for suffix in ("_physics.glb", ".glb"):
        if os.path.exists(base + suffix):
            t = triangles(base + suffix)
            if len(t): tri = t; break
    if tri is None: missing += 1; continue
    v = tri.reshape(-1, 3) / 0.0254          # back to Source units, glTF axes: (X, Y, Z) = (sy, sz, sx)
    mn, mx = v.min(0), v.max(0)
    o = [float(x) for x in e["origin"].strip("[] ").split(",")]
    out.append(dict(cls=e["classname"], org=o, tid=e.get("targetname", ""), team=e.get("teamnumber", ""),
                    mn=[float(mn[2]), float(mn[0]), float(mn[1])], mx=[float(mx[2]), float(mx[0]), float(mx[1])], ntri=int(len(tri))))
json.dump(out, open(out_p, "w"))
print(f"{len(out)} trigger volumes written, {missing} without a model export", file=sys.stderr)
