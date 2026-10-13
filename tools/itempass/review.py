# Review sheets for every item display as <Hero>ItemDisplays.cs places it (red, on the hero's rest mesh: front / side /
# back / left side), next to Commando's original (blue: front / side), plus out/<hero>_review.csv with how each item
# sits against the body on both. Needs data/ from steps 1-3 and `HERO=<hero> python hero.py`.
#   HERO=abrams python review.py [Key ...]
import sys, re, json, os, csv
import numpy as np
from PIL import Image, ImageDraw
from rig import euler_q, trs, render, load_obj, apply, D, HERO
NAME = HERO.capitalize()

CS = os.path.join(os.path.dirname(os.path.abspath(__file__)), f"../../CursedAppleCuriosities/Survivors/{NAME}/{NAME}ItemDisplays.cs")
V = r"new Vector3\(([-\d.]+)F, ([-\d.]+)F, ([-\d.]+)F\)"
src = open(CS, encoding="utf-8").read()
anc = {m[0]: (m[1], [float(x) for x in m[2:5]], [float(x) for x in m[5:8]], [float(x) for x in m[8:11]])
       for m in re.findall(r'\["(\w+)"\] = \("(\w+)", ' + V + ", " + V + ", " + V, src)}
rules = [(m[0], int(m[1]), m[2], m[3], [float(x) for x in m[4:7]], [float(x) for x in m[7:10]], [float(x) for x in m[10:13]])
         for m in re.findall(r'HeroItemRule\("(\w+)", (\d+), "(\w+)", "(\w+)", ' + V + ", " + V + ", " + V, src)]
H = {k: np.array(v) for k, v in json.load(open(D + f"/{HERO}.json"))["bones"].items()}
C = {k: np.array(v["rest"]) for k, v in json.load(open(D + "/commando.json"))["points"].items()}
orig = {g["key"]: g["rules"] for g in json.load(open(D + "/rules.json"))}
meta = json.load(open(D + "/displays.json"))
def M(p, a, s): return trs(p, euler_q(a), s)

class Near:  # nearest body vertex distance (brute force, subsampled body)
    def __init__(self, path): self.p = load_obj(path)[0][::2]
    def __call__(self, q):
        return np.concatenate([np.sqrt(((q[i:i + 128, None] - self.p[None]) ** 2).sum(-1)).min(1) for i in range(0, len(q), 128)])
near_l, near_c = Near(D + f"/{HERO}_body.obj"), Near(D + "/commando_body.obj")

keys = sys.argv[1:]
groups = {}
for key, idx, frm, child, p, a, s in rules:
    if keys and key not in keys: continue
    o = orig[key][idx]
    if not o.get("prefab") or meta.get(o["prefab"], {}).get("empty"): continue
    obj = D + "/displays/" + meta[o["prefab"]]["name"] + ".obj"
    bone, ap, aa, asc = anc[child]
    groups.setdefault(key, []).append((idx, child, obj, H[bone] @ M(ap, aa, asc) @ M(p, a, s), C[o["child"]] @ M(o["pos"], o["ang"], o["scale"])))

os.makedirs("out", exist_ok=True)
rows, shots, cshots, items = [], [], [], []
def frame(pts): lo, hi = pts.min(0), pts.max(0); return ((lo + hi) / 2).tolist(), max(0.45, float((hi - lo).max()) * 1.9)
for n, (key, rs) in enumerate(sorted(groups.items())):
    lp, cp = [], []
    for idx, child, obj, lm, cm in rs:
        v = load_obj(obj)[0]; wl, wc = apply(lm, v), apply(cm, v); lp.append(wl); cp.append(wc)
        dl, dc = near_l(wl), near_c(wc)
        rows.append({"key": key, "index": idx, "child": child, "hero_gap": round(float(dl.min()), 3), "cmd_gap": round(float(dc.min()), 3),
                     "hero_median": round(float(np.median(dl)), 3), "cmd_median": round(float(np.median(dc)), 3),
                     "size_ratio": round(float(np.ptp(wl, 0).max() / max(1e-6, np.ptp(wc, 0).max())), 2)})
    tag = f"lr_{n:03d}"
    c, sz = frame(np.vstack(lp))
    shots.append({"parts": [{"obj": obj, "matrix": lm.tolist(), "color": [0.9, 0.25, 0.2], "name": key} for _, _, obj, lm, _ in rs],
                  "views": [{"name": w, "dir": d, "center": c, "size": sz, "out": os.path.abspath(f"out/t_{tag}_{w}.png")}
                            for w, d in (("front", [0, 0, 1]), ("side", [1, 0, 0]), ("back", [0, 0, -1]), ("lside", [-1, 0, 0]))]})
    c, sz = frame(np.vstack(cp))
    cshots.append({"parts": [{"obj": obj, "matrix": cm.tolist(), "color": [0.2, 0.45, 0.9], "name": key} for _, _, obj, _, cm in rs],
                   "views": [{"name": w, "dir": d, "center": c, "size": sz, "out": os.path.abspath(f"out/t_{tag}_c{w}.png")}
                             for w, d in (("front", [0, 0, 1]), ("side", [1, 0, 0]))]})
    items.append((tag, key, " ".join(sorted({r[1] for r in rs}))))

with open(f"out/{HERO}_review.csv", "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
eye = np.eye(4).tolist()
render([{"obj": D + f"/{HERO}_body.obj", "matrix": eye, "color": [0.82, 0.82, 0.8], "name": "body"}], [], f"out/{HERO}_review.json", res=260, shots=shots)
render([{"obj": D + "/commando_body.obj", "matrix": eye, "color": [0.75, 0.78, 0.82], "name": "cmd"}], [], f"out/{HERO}_reviewc.json", res=260, shots=cshots)

W = 260
for s in range(0, len(items), 12):
    chunk = items[s:s + 12]
    sheet = Image.new("RGB", (W * 6 * 2 + 10, (W + 18) * ((len(chunk) + 1) // 2)), (255, 255, 255))
    d = ImageDraw.Draw(sheet)
    for i, (tag, key, child) in enumerate(chunk):
        x0, y0 = (i % 2) * (W * 6 + 10), (i // 2) * (W + 18)
        d.text((x0 + 4, y0 + 2), f"{s + i}: {key}  [{child}]", fill=(0, 0, 0))
        for j, v in enumerate(("front", "side", "back", "lside", "cfront", "cside")):
            sheet.paste(Image.open(f"out/t_{tag}_{v}.png"), (x0 + j * W, y0 + 18))
    sheet.save(f"out/{HERO}_review_{s // 12}.png"); print("sheet", f"out/{HERO}_review_{s // 12}.png")
