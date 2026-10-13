# The hero wearing every fitted item at once (front / side / back / quarter), to spot outliers: HERO=<hero> python allview.py [fit file] [keys...]
import json, sys
import numpy as np
from rig import *
fitname = sys.argv[1] if len(sys.argv) > 1 else f"{HERO}_fit.json"
keys = sys.argv[2:]
fit = json.load(open(D + "/" + fitname))
meta = json.load(open(D + "/displays.json"))
parts = [{"obj": D + f"/{HERO}_body.obj", "matrix": np.eye(4).tolist(), "color": [0.82, 0.82, 0.8], "name": "body"}]
for i, g in enumerate(fit["groups"]):
    if keys and g["key"] not in keys: continue
    for r in g["rules"]:
        if not r.get("model") or not r["prefab"] or meta.get(r["prefab"], {}).get("empty"): continue
        parts.append({"obj": D + "/displays/" + meta[r["prefab"]]["name"] + ".obj", "matrix": r["model"], "color": list(PALETTE[i % len(PALETTE)]), "name": g["key"]})
H = json.load(open(D + f"/{HERO}.json"))
top = H["top"] * H["scale"]
center = [0, -0.91 + top / 2, 0]
vs = views(f"out/all_{HERO}", center, top * 1.15, ("front", "side", "back", "q"))
render(parts, vs, f"out/all_{HERO}.json", res=700)
montage([v["out"] for v in vs], f"out/all_{HERO}.png")
print("wrote", f"out/all_{HERO}.png", len(parts) - 1, "items")
