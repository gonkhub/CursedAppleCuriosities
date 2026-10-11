# Commando's world placement of an item, moved/scaled onto Lash, written back as a rule on Lash's anchor.
# python lash_fit.py '[[key, idx, child, [dx,dy,dz], scale, pivot], ...]'   (run from tools/itempass)
# pivot: "centre", "base" (bottom centre) or a model-space point [x,y,z] to scale about
import sys, re, json, numpy as np
sys.path.insert(0, "."); from rig import euler_q, trs, mat_euler
CS = r"../../CursedAppleCuriosities/Survivors/Lash/LashItemDisplays.cs"
V = r"new Vector3\(([-\d.]+)F, ([-\d.]+)F, ([-\d.]+)F\)"
s = open(CS, encoding="utf-8").read()
anc = {m[0]: (m[1], [float(x) for x in m[2:5]], [float(x) for x in m[5:8]], [float(x) for x in m[8:11]])
       for m in re.findall(r'\["(\w+)"\] = \("(\w+)", ' + V + ", " + V + ", " + V, s)}
H = {k: np.array(v) for k, v in json.load(open("data/lash.json"))["bones"].items()}
C = {k: np.array(v["rest"]) for k, v in json.load(open("data/commando.json"))["points"].items()}
cr = {r["key"]: r["rules"] for r in json.load(open("data/rules.json"))}

def M(p, a, sc): return trs(p, euler_q(a), sc)
def objv(p): return np.array([[float(x) for x in l.split()[1:4]] for l in open(p) if l.startswith("v ")])
def f(a): return ", ".join("%.5fF" % x for x in a)

def write(key, idx, child, offset, scale=1.0, pivot="centre"):
    global s
    r = cr[key][idx]
    W = C[r["child"]] @ M(r["pos"], r["ang"], r["scale"])
    v = objv("data/displays/" + r["prefab"].split("/")[-1].replace(".prefab", ".obj"))
    w = v @ W[:3, :3].T + W[:3, 3]
    piv = np.array(pivot, float) if isinstance(pivot, list) else w.mean(0) if pivot == "centre" else np.array([w[:, 0].mean(), w[:, 1].min(), w[:, 2].mean()])
    S = np.eye(4); S[:3, :3] *= scale; S[:3, 3] = piv - scale * piv + np.array(offset)
    W = S @ W
    bone, ap, aa, asc = anc[child]
    L = np.linalg.inv(H[bone] @ M(ap, aa, asc)) @ W
    sc = np.linalg.norm(L[:3, 0]); ang = mat_euler(L[:3, :3] / sc); pos = L[:3, 3]
    new = ('new HeroItemRule("%s", %d, "%s", "%s", new Vector3(%s), new Vector3(%s), new Vector3(%s)),'
           " // Commando's placement moved onto Lash") % (key, idx, r["child"], child, f(pos), f(ang), f([sc] * 3))
    pat = r'new HeroItemRule\("' + key + r'", ' + str(idx) + r', "\w*", "\w+", [^\n]*'
    s, n = re.subn(pat, lambda _: new, s)
    assert n == 1, key

for spec in json.loads(sys.argv[1]): write(*spec)
open(CS, "w", encoding="utf-8").write(s)
