# Slides buried Lash item displays out of the body: each rule's item is moved (in model space, away from its bone,
# horizontally) until its closest point clears Lash's rest mesh by the gap Commando's original has (or a given gap).
#   python lash_push.py Key:index[:gap] ...        (run from tools/itempass; rewrites LashItemDisplays.cs)
import sys, re, json, numpy as np
from rig import euler_q, trs, mat_euler, load_obj, D

CS = r"../../CursedAppleCuriosities/Survivors/Lash/LashItemDisplays.cs"
V = r"new Vector3\(([-\d.]+)F, ([-\d.]+)F, ([-\d.]+)F\)"
s = open(CS, encoding="utf-8").read()
anc = {m[0]: (m[1], [float(x) for x in m[2:5]], [float(x) for x in m[5:8]], [float(x) for x in m[8:11]])
       for m in re.findall(r'\["(\w+)"\] = \("(\w+)", ' + V + ", " + V + ", " + V, s)}
H = {k: np.array(v) for k, v in json.load(open(D + "/lash.json"))["bones"].items()}
C = {k: np.array(v["rest"]) for k, v in json.load(open(D + "/commando.json"))["points"].items()}
cr = {r["key"]: r["rules"] for r in json.load(open(D + "/rules.json"))}
def M(p, a, sc): return trs(p, euler_q(a), sc)
def gap(body, q): return min(np.sqrt(((q[i:i + 128, None] - body[None]) ** 2).sum(-1)).min() for i in range(0, len(q), 128))
lash, cmd = load_obj(D + "/lash_body.obj")[0][::2], load_obj(D + "/commando_body.obj")[0][::2]

for arg in sys.argv[1:]:
    key, idx, *g = arg.split(":"); idx = int(idx)
    pat = r'new HeroItemRule\("' + key + r'", ' + str(idx) + r', "(\w+)", "(\w+)", ' + V + ", " + V + ", " + V
    m = re.search(pat, s); child = m.group(2); vals = [float(x) for x in m.groups()[2:]]
    o = cr[key][idx]; v = load_obj(D + "/displays/" + o["prefab"].split("/")[-1].replace(".prefab", ".obj"))[0]
    target = float(g[0]) if g else max(0.005, gap(cmd, v @ (Wc := C[o["child"]] @ M(o["pos"], o["ang"], o["scale"]))[:3, :3].T + Wc[:3, 3]))
    bone, ap, aa, asc = anc[child]; A = H[bone] @ M(ap, aa, asc)
    W = A @ M(vals[0:3], vals[3:6], vals[6:9]); w = v @ W[:3, :3].T + W[:3, 3]
    d = w.mean(0) - H[bone][:3, 3]; d[1] = 0; d /= np.linalg.norm(d)
    step = 0.0
    while gap(lash, w + d * step) < target and step < 0.3: step += 0.01
    W[:3, 3] += d * step
    L = np.linalg.inv(A) @ W; sc = np.linalg.norm(L[:3, 0])
    line = 'new HeroItemRule("%s", %d, "%s", "%s", new Vector3(%s), new Vector3(%s), new Vector3(%s)' % (
        key, idx, m.group(1), child, ", ".join("%.5fF" % x for x in L[:3, 3]), ", ".join("%.5fF" % x for x in mat_euler(L[:3, :3] / sc)), ", ".join("%.5fF" % sc for _ in range(3)))
    s = s[:m.start()] + line + s[m.end():]
    print(f"{key}[{idx}] pushed {step:.2f} out (target gap {target:.3f})")
open(CS, "w", encoding="utf-8").write(s)
