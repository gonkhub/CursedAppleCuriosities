# Blender (for BVH trees): fits every idrsCommando rule onto Abrams.
#   blender -b --factory-startup -P gen.py
# 1. Anchors: each attach point maps into the matching Abrams region (box-to-box), turned with the limb and scaled
#    by the region's size ratio; it rides the matching Abrams bone.
# 2. Items: mapped the same way, then slid along the region's outward direction until they touch Abrams' surface
#    the way they touch Commando's (same signed gap, scaled).
import sys, os, json, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from rig import *
from anchors import C, H
from regions import REGIONS, point_region

random.seed(1)

def load_parts(path):
    parts, cur, vs, fs = {}, None, [], []
    base = 0
    for line in open(path):
        if line.startswith("o "): cur = line[2:].strip(); parts[cur] = []
        elif line.startswith("v "): vs.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("f "): parts[cur].append([int(x) - 1 for x in line.split()[1:4]])
    return np.array(vs), parts

def bvh(path, skip=()):
    v, parts = load_parts(path)
    polys = [f for name, fs in parts.items() if not any(s in name for s in skip) for f in fs]
    return BVHTree.FromPolygons([tuple(p) for p in v], polys, epsilon=0.0)

from regions import cmd_reg, ab_surf, REGIONS as _R, surface_set
def region_tree(path, labels, skip):
    v, parts = load_parts(path)
    trees = {}
    for rn in _R:
        want = surface_set(rn); polys = []
        for name, fs in parts.items():
            if any(s in name for s in skip): continue
            for f in fs:
                if max(f) < len(labels) and (rn == "base" or any(labels[i] in want for i in f)): polys.append(f)
        trees[rn] = BVHTree.FromPolygons([tuple(p) for p in v], polys, epsilon=0.0)
    return trees
T_CMD = region_tree(D + "/commando_body.obj", cmd_reg, ("gun", "Gun"))
T_ABR = region_tree(D + f"/{HERO}_body.obj", ab_surf, HEROES[HERO]["skip"])

def radial(tree, core, v, extra=0.3):
    """Signed distance of v past the outermost surface on the ray core -> v (negative: under the surface)."""
    d = Vector(v) - Vector(core); L = d.length
    if L < 1e-5: return -1.0
    d.normalize(); o = Vector(core); travelled = 0.0; last = None
    for _ in range(40):
        hit = tree.ray_cast(o, d, L + extra - travelled)
        if hit[0] is None: break
        travelled += hit[3] + 1e-4; last = travelled; o = hit[0] + d * 1e-4
    return L if last is None else L - last

def gap(tree, region, verts, abrams):
    sd = [radial(tree, region.core(v, abrams), v) for v in verts]
    return float(np.percentile(sd, 10))

def contact(tree, region, verts, abrams, frac=0.2):
    """Contact point and outward normal where the item meets the surface (its closest verts)."""
    res = []
    for v in verts:
        loc, nrm, idx, dist = tree.find_nearest(Vector(v))
        if loc is None: continue
        n = np.array(nrm); q = np.array(loc)
        if np.dot(n, q - region.core(q, abrams)) < 0: n = -n
        res.append((dist, q, n))
    res.sort(key=lambda r: r[0])
    best = res[: max(3, int(len(res) * frac))]
    q = np.mean([r[1] for r in best], 0); n = np.sum([r[2] for r in best], 0)
    return q, n / max(np.linalg.norm(n), 1e-9)

def rot_toward(a, b, max_deg=55):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    ang = np.degrees(np.arccos(np.clip(np.dot(a, b), -1, 1)))
    if ang < 1e-3: return np.eye(3)
    ax = np.cross(a, b); ax /= max(np.linalg.norm(ax), 1e-9)
    th = np.radians(min(ang, max_deg))
    K = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]])
    return np.eye(3) + np.sin(th) * K + (1 - np.cos(th)) * K @ K

def sample(v, n=120):
    if len(v) <= n: return v
    ext = np.r_[v.argmin(0), v.argmax(0)]
    idx = list(set(ext.tolist()) | set(random.sample(range(len(v)), n - len(ext))))
    return v[idx]

rules = json.load(open(D + "/rules.json")); meta = json.load(open(D + "/displays.json"))

BONE_FOR = {"head": "head", "base": "root_motion" if "root_motion" in H else "pelvis"}
def hero_bone(point, region):
    if region == "torso":
        n = point.lower()
        return "spine_1" if "stomach" in n else "pelvis" if "pelvis" in n else "spine_3"
    if region in BONE_FOR: return BONE_FOR[region]
    kind, s = region.split("_")
    return {"uarm": "arm_upper_", "larm": "arm_lower_", "hand": "hand_", "thigh": "leg_upper_", "calf": "leg_lower_", "foot": "ankle_"}[kind] + s

def region_k(rg):
    return float(np.clip(np.prod(rg.D) ** (1 / 3), 0.6, 1.55))

# ---------------- anchors ----------------
anchors, A = {}, {}
for name, c in C.items():
    rn = point_region(name)
    if rn is None: A[name] = c; continue
    rg = REGIONS[rn]
    p, R, s = decompose(c)
    m = np.eye(4); m[:3, :3] = rg.Rseg @ R * (s * region_k(rg)); m[:3, 3] = rg.point(p)
    A[name] = m
    bone = hero_bone(name, rn)
    local = np.linalg.inv(H[bone]) @ m
    lp, lR, ls = decompose(local)
    anchors[name] = {"bone": bone, "pos": lp.tolist(), "ang": mat_euler(lR), "scale": ls.tolist(), "region": rn}

# ---------------- items ----------------
out = []
for g in rules:
    og = {"key": g["key"], "kind": g["kind"], "rules": []}
    for i, r in enumerate(g["rules"]):
        o = {"index": i, "prefab": r["prefab"], "type": r["type"], "child": r["child"]}
        rn = point_region(r["child"]) if r["type"] == 0 else None
        if rn is None or r["child"] not in C:
            o.update(pos=r["pos"], ang=r["ang"], scale=r["scale"], note="as Commando")
            og["rules"].append(o); continue
        rg = REGIONS[rn]
        Mc = C[r["child"]] @ rule_m(r)
        pc, Rc, sc = decompose(Mc)
        k = region_k(rg)
        Ma = np.eye(4); Ma[:3, :3] = rg.Rseg @ Rc * (sc * k); Ma[:3, 3] = rg.point(pc)
        note = "mapped"
        info = meta.get(r["prefab"] or "", {})
        if r["prefab"] and not info.get("empty"):
            full = load_obj(D + "/displays/" + info["name"] + ".obj")[0]
            lv = sample(full)
            # map the mesh's centre, not its pivot (pivots can sit off the mesh, and the map isn't linear)
            lcen = (full.min(0) + full.max(0)) / 2
            Ma[:3, 3] = rg.point(apply(Mc, lcen[None])[0]) - Ma[:3, :3] @ lcen
            gc = gap(T_CMD[rn], rg, apply(Mc, lv), False)
            cen_c = apply(Mc, lcen[None])[0]
            wraps = radial(T_CMD[rn], rg.core(cen_c, False), cen_c) < -0.01
            if wraps:
                # keep as much of it showing as on Commando: grow it about its centre (Abrams' limbs thickened
                # more than the region's average), then push it outward if growing isn't enough
                def shown(tree, V, abrams):
                    return float(np.mean([radial(tree, rg.core(v, abrams), v) > 0 for v in V]))
                fc = shown(T_CMD[rn], apply(Mc, lv), False)
                cen_a = Ma[:3, :3] @ lcen + Ma[:3, 3]
                A3 = Ma[:3, :3].copy()
                def at(sm, t, d):
                    M = np.eye(4); M[:3, :3] = A3 * sm; M[:3, 3] = cen_a + d * t - M[:3, :3] @ lcen
                    return M
                d0 = cen_a - rg.core(cen_a, True); d0 = d0 / max(np.linalg.norm(d0), 1e-6)
                want = min(fc, 0.9) - 0.05
                def ok(sm, t): return shown(T_ABR[rn], apply(at(sm, t, d0), lv), True) >= want
                sm, t = 1.0, 0.0
                if not ok(1.0, 0.0):
                    # first slide it out (up to 12 cm), then grow it (up to x1.35)
                    if ok(1.0, 0.12):
                        lo, hi = 0.0, 0.12
                        for _ in range(10):
                            mid = (lo + hi) / 2
                            if ok(1.0, mid): hi = mid
                            else: lo = mid
                        t = hi
                    else:
                        t = 0.12
                        lo, hi = 1.0, 1.35
                        if ok(hi, t):
                            for _ in range(10):
                                mid = (lo + hi) / 2
                                if ok(mid, t): hi = mid
                                else: lo = mid
                        sm = hi
                Ma = at(sm, t, d0)
                fa = shown(T_ABR[rn], apply(Ma, lv), True)
                note = f"wraps: shown {fc:.2f} -> {fa:.2f}, grown x{sm:.2f}, out {t:+.3f}"
            elif gc < (0.3 if rn == "head" else 0.15):
                target = float(np.clip(gc * k, -0.1, 0.08))
                def snap(M0, d, lo, hi):
                    pa = M0[:3, 3].copy()
                    def f(t):
                        M = M0.copy(); M[:3, 3] = pa + d * t
                        return gap(T_ABR[rn], rg, apply(M, lv), True)
                    if f(hi) < target: t = hi
                    elif f(lo) > target: t = lo
                    else:
                        for _ in range(14):
                            mid = (lo + hi) / 2
                            if f(mid) < target: lo = mid
                            else: hi = mid
                        t = (lo + hi) / 2
                    M = M0.copy(); M[:3, 3] = pa + d * t
                    return M, t
                pa = Ma[:3, 3].copy()
                d = pa - rg.core(pa, True); nd = np.linalg.norm(d)
                if nd > 1e-4:
                    d /= nd
                    Ma, t = snap(Ma, d, -min(0.45, 0.85 * nd), 0.45)
                    # sit on Abrams' surface the way it sits on Commando's: turn the Commando contact normal
                    # (carried over by the region map) onto Abrams' normal there, about the contact, then re-snap.
                    qc, nc = contact(T_CMD[rn], rg, apply(Mc, lv), False)
                    nc_m = rg.Fa @ ((rg.Fc.T @ nc) / rg.D); nc_m /= np.linalg.norm(nc_m)
                    qa, na = contact(T_ABR[rn], rg, apply(Ma, lv), True)
                    tilt = np.degrees(np.arccos(np.clip(np.dot(nc_m, na), -1, 1)))
                    Q = rot_toward(nc_m, na, 35)
                    ctr = apply(Ma, lv).mean(0)
                    M2 = np.eye(4); M2[:3, :3] = Q @ Ma[:3, :3]; M2[:3, 3] = ctr + Q @ (Ma[:3, 3] - ctr)
                    M3, t2 = snap(M2, na, -0.2, 0.2)
                    if abs(t2) < 0.19 and tilt < 70:
                        Ma = M3
                        note = f"snapped t={t:+.3f}/{t2:+.3f} tilt={tilt:.0f} gap_c={gc:+.3f}"
                    else:
                        note = f"snapped t={t:+.3f} (no tilt: {tilt:.0f}/{t2:+.2f}) gap_c={gc:+.3f}"
            else:
                note = f"floating gap_c={gc:+.3f}"
        local = np.linalg.inv(A[r["child"]]) @ Ma
        lp, lR, ls = decompose(local)
        o.update(pos=lp.tolist(), ang=mat_euler(lR), scale=ls.tolist(), note=note, model=Ma.tolist())
        og["rules"].append(o)
    out.append(og)
    print(g["key"], [x.get("note") for x in og["rules"]], flush=True)

json.dump({"anchors": anchors, "groups": out, "anchor_model": {k: v.tolist() for k, v in A.items()}}, open(D + f"/{HERO}_fit.json", "w"), indent=1)
print("done", len(out))
