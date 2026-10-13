# Seven's head items, merged from two fits (run gen.py with HERO=seven and HERO=seven_bare first):
# his halo headgear makes the head region twice as big, so face items (glasses, masks, eyes) slid up onto the halo's rim.
# Face items come from the fit without the halo (seven_bare: on his face); everything worn higher (crowns, horns,
# antennae) keeps the halo fit's placement, re-expressed on the bare fit's Head anchor so all head items share one.
# Writes data/seven_fit_final.json (what emit.py reads).
import json
import numpy as np
from rig import load_obj, apply, D, decompose, mat_euler, rule_m

FACE = -0.09   # an item whose centre sits this far below the top of his bare head is a face item
meta = json.load(open(D + "/displays.json"))
halo = json.load(open(D + "/seven_fit.json")); bare = json.load(open(D + "/seven_bare_fit.json"))
head = np.array(json.load(open(D + "/seven_bare.json"))["bones"]["head"])[:3, 3]
v = load_obj(D + "/seven_bare_body.obj")[0]
top = v[np.linalg.norm(v[:, [0, 2]] - head[[0, 2]], axis=1) < 0.25][:, 1].max()

# Face items the bare fit sank into his face (Lens-Maker's Glasses): slide forward (+Z, his front) until 90% of the
# item is in front of the face surface behind it.
fv = v[np.linalg.norm(v - head, axis=1) < 0.35]
def surface(r, mesh, key):
    M = np.array(r["model"]); w = apply(M, mesh)
    if w[:, 2].mean() < head[2] + 0.06: return  # not worn on the front of his face (hair, back of the head)
    behind = []
    for q in w:
        near = fv[np.linalg.norm(fv[:, :2] - q[:2], axis=1) < 0.012]
        if len(near): behind.append(near[:, 2].max() - q[2])
    if not behind: return
    hidden = float(np.mean(np.array(behind) > 0))
    if hidden < 0.5: return
    dz = float(np.quantile(behind, 0.9)) + 0.004
    M[2, 3] += dz
    A = np.array(bare["anchor_model"][r["child"]])
    p, R, s = decompose(np.linalg.inv(A) @ M)
    r.update(pos=p.tolist(), ang=mat_euler(R), scale=s.tolist(), model=M.tolist(), note=(r.get("note", "") + f" pushed out {dz:.3f}").strip())
    print(f"  {key}: {hidden:.0%} inside his face, pushed {dz * 100:.1f} cm forward")

out = json.loads(json.dumps(halo))
HEAD = ("Head", "HeadCenter")
for k in HEAD:
    out["anchors"][k] = bare["anchors"][k]; out["anchor_model"][k] = bare["anchor_model"][k]
bg = {g["key"]: g for g in bare["groups"]}
face = crown = 0
for g in out["groups"]:
    for i, r in enumerate(g["rules"]):
        if r.get("child") not in HEAD or "pos" not in r: continue
        rb = bg[g["key"]]["rules"][i]
        if min(abs(x) for x in r["scale"]) < 1e-6:  # a hidden (zero-scale) display: nothing to place
            g["rules"][i] = rb; continue
        if r.get("model") and r.get("prefab") and not meta.get(r["prefab"], {}).get("empty"):
            c = apply(np.array(rb["model"]), load_obj(D + "/displays/" + meta[r["prefab"]]["name"] + ".obj")[0]).mean(0)
            if c[1] - top < FACE:
                g["rules"][i] = rb; face += 1
                surface(rb, load_obj(D + "/displays/" + meta[r["prefab"]]["name"] + ".obj")[0], g["key"]); continue
        M = np.array(r["model"]) if r.get("model") else np.array(halo["anchor_model"][r["child"]]) @ rule_m(r)
        A = np.array(bare["anchor_model"][r["child"]])
        p, R, s = decompose(np.linalg.inv(A) @ M)
        r.update(pos=p.tolist(), ang=mat_euler(R), scale=s.tolist(), model=M.tolist())
        assert np.allclose(A @ rule_m(r), M, atol=1e-4), g["key"]
        crown += 1
# Hand tweaks (model-space metres): his halo's brim hangs at brow height, so glasses go down to the eye line that the
# Blindness eyes (visible in the renders) sit on.
TWEAKS = {("CritGlasses", 0): (0, -0.036, 0)}
for g in out["groups"]:
    for i, r in enumerate(g["rules"]):
        d = TWEAKS.get((g["key"], i))
        if not d: continue
        M = np.array(r["model"]); M[:3, 3] += d
        p, R, s = decompose(np.linalg.inv(np.array(out["anchor_model"][r["child"]])) @ M)
        r.update(pos=p.tolist(), ang=mat_euler(R), scale=s.tolist(), model=M.tolist(), note=r.get("note", "") + " tweaked")
json.dump(out, open(D + "/seven_fit_final.json", "w"), indent=1)
print(f"head top {top:.3f}: {face} face items from the bare fit, {crown} worn higher from the halo fit")
