# One tile per item group (front / side / back crops around the item), labelled; sheets of 12.  python tiles.py <fit file> <prefix> [keys...]
import json, sys
import numpy as np
from PIL import Image, ImageDraw
from rig import *
from regions import point_region
from anchors import C
orig = {g["key"]: g for g in json.load(open(D + "/rules.json"))}

fit = json.load(open(D + "/" + sys.argv[1])); prefix = sys.argv[2]; keys = sys.argv[3:]
meta = json.load(open(D + "/displays.json"))
body = [{"obj": D + f"/{HERO}_body.obj", "matrix": np.eye(4).tolist(), "color": [0.82, 0.82, 0.8], "name": "body"}]
shots, items, cshots = [], [], []
for g in fit["groups"]:
    if keys and g["key"] not in keys: continue
    rs = [r for r in g["rules"] if r.get("model") and r["prefab"] and not meta.get(r["prefab"], {}).get("empty")]
    if not rs: continue
    pts = []
    for r in rs:
        v = load_obj(D + "/displays/" + meta[r["prefab"]]["name"] + ".obj")[0]
        pts.append(apply(np.array(r["model"]), v))
    allp = np.vstack(pts); lo, hi = allp.min(0), allp.max(0)
    center = (lo + hi) / 2; size = max(0.45, float((hi - lo).max()) * 1.9)
    tag = f"{prefix}_{len(items):03d}"
    vs = [{"name": w, "dir": d, "center": center.tolist(), "size": size, "out": os.path.abspath(f"out/t_{tag}_{w}.png")}
          for w, d in (("front", [0, 0, 1]), ("side", [1, 0, 0]), ("back", [0, 0, -1]), ("lside", [-1, 0, 0]))]
    shots.append({"parts": [{"obj": D + "/displays/" + meta[r["prefab"]]["name"] + ".obj", "matrix": r["model"], "color": [0.9, 0.25, 0.2], "name": g["key"]} for r in rs], "views": vs})
    # Commando's original, for intent
    cm = [(C[o["child"]] @ rule_m(o), o) for o in (orig[g["key"]]["rules"][r["index"]] for r in rs)]
    cp = np.vstack([apply(m, load_obj(D + "/displays/" + meta[o["prefab"]]["name"] + ".obj")[0]) for m, o in cm])
    clo, chi = cp.min(0), cp.max(0); cc = (clo + chi) / 2; csz = max(0.45, float((chi - clo).max()) * 1.9)
    cshots.append({"parts": [{"obj": D + "/displays/" + meta[o["prefab"]]["name"] + ".obj", "matrix": m.tolist(), "color": [0.2, 0.45, 0.9], "name": g["key"]} for m, o in cm],
                   "views": [{"name": w, "dir": d, "center": cc.tolist(), "size": csz, "out": os.path.abspath(f"out/t_{tag}_c{w}.png")} for w, d in (("front", [0, 0, 1]), ("side", [1, 0, 0]))]})
    items.append((tag, g["key"], " ".join(sorted(set(r["child"] for r in rs)))))
render(body, [], f"out/tiles_{prefix}.json", res=260, shots=shots)
render([{"obj": D + "/commando_body.obj", "matrix": np.eye(4).tolist(), "color": [0.75, 0.78, 0.82], "name": "cmd"}], [], f"out/tilesc_{prefix}.json", res=260, shots=cshots)
W = 260
for s in range(0, len(items), 12):
    chunk = items[s:s + 12]
    sheet = Image.new("RGB", (W * 6 * 2 + 10, (W + 18) * ((len(chunk) + 1) // 2)), (255, 255, 255))
    d = ImageDraw.Draw(sheet)
    for i, (tag, key, child) in enumerate(chunk):
        x0, y0 = (i % 2) * (W * 6 + 10), (i // 2) * (W + 18)
        d.text((x0 + 4, y0 + 2), f"{s + i}: {key}  [{child}]", fill=(0, 0, 0))
        for j, w in enumerate(("front", "side", "back", "lside", "cfront", "cside")):
            sheet.paste(Image.open(f"out/t_{tag}_{w}.png"), (x0 + j * W, y0 + 18))
    sheet.save(f"out/tiles_{prefix}_{s // 12}.png")
    print("sheet", f"out/tiles_{prefix}_{s // 12}.png")
