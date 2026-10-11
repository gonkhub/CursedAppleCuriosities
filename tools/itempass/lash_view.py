# Renders Lash's rest mesh with chosen items as LashItemDisplays.cs places them. python view.py <outprefix> Key:idx ...
import sys, re, json, os, subprocess, numpy as np
sys.path.insert(0, "."); from rig import euler_q, trs, BLENDER
CS = r"../../CursedAppleCuriosities/Survivors/Lash/LashItemDisplays.cs"
s = open(CS, encoding="utf-8").read()
V = r"new Vector3\(([-\d.]+)F, ([-\d.]+)F, ([-\d.]+)F\)"
anc = {m[0]: (m[1], [float(x) for x in m[2:5]], [float(x) for x in m[5:8]], [float(x) for x in m[8:11]])
       for m in re.findall(r'\["(\w+)"\] = \("(\w+)", ' + V + ", " + V + ", " + V, s)}
rules = {(m[0], int(m[1])): (m[3], [float(x) for x in m[4:7]], [float(x) for x in m[7:10]], [float(x) for x in m[10:13]])
         for m in re.findall(r'HeroItemRule\("(\w+)", (\d+), "(\w+)", "(\w+)", ' + V + ", " + V + ", " + V, s)}
H = {k: np.array(v) for k, v in json.load(open("data/lash.json"))["bones"].items()}
cr = {r["key"]: r["rules"] for r in json.load(open("data/rules.json"))}
def M(p, a, sc): return trs(p, euler_q(a), sc)
out = os.path.abspath(sys.argv[1]); parts = [{"obj": os.path.abspath("data/lash_body.obj"), "matrix": np.eye(4).tolist(), "color": [0.82, 0.82, 0.8]}]
cols = [[0.9, 0.25, 0.2], [0.2, 0.5, 0.9], [0.2, 0.75, 0.3], [0.9, 0.6, 0.1]]
for i, a in enumerate(sys.argv[2:]):
    key, idx = a.split(":"); idx = int(idx); child, p, ang, sc = rules[(key, idx)]; bone, ap, aa, asc = anc[child]
    m = H[bone] @ M(ap, aa, asc) @ M(p, ang, sc)
    parts.append({"obj": os.path.abspath("data/displays/" + cr[key][idx]["prefab"].split("/")[-1].replace(".prefab", ".obj")), "matrix": m.tolist(), "color": cols[i % 4 if False else (i // 2) % 4]})
views = [{"name": n, "dir": d, "center": [0, 1.45, 0], "size": 1.3, "out": f"{out}_{n}.png"} for n, d in (("front", [0, 0, 1]), ("side", [1, 0, 0]), ("back", [0, 0, -1]))]
job = out + ".json"; json.dump({"parts": parts, "views": views, "res": 600}, open(job, "w"))
subprocess.run([BLENDER, "-b", "--factory-startup", "-P", "render.py", "--", job], capture_output=True)
from PIL import Image
ims = [Image.open(v["out"]) for v in views]; sheet = Image.new("RGB", (1800, 600)); [sheet.paste(im, (600 * k, 0)) for k, im in enumerate(ims)]; sheet.save(out + ".png"); print(out + ".png")
