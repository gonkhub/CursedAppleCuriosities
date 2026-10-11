# Extracts what the Abrams item pass needs from Risk of Rain 2's addressable bundles (all in Unity's own
# left-handed coordinates): Commando's rest skeleton and attach points, his body mesh, idrsCommando's rules,
# and the mesh of every display prefab those rules use.
import UnityPy, os, json, time, sys
import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler

G = r"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\StandaloneWindows64"
OUT = "data"
os.makedirs(OUT + "/displays", exist_ok=True)
guid = json.load(open("guid.json"))

t0 = time.time()
env = UnityPy.load(G)
print("loaded", time.time() - t0, flush=True)

# serialized files by CAB name, for cross-bundle pointers
files = {}
def walk(f):
    for name, sub in getattr(f, "files", {}).items():
        if hasattr(sub, "objects") and hasattr(sub, "externals"): files[str(name).lower()] = sub
        elif hasattr(sub, "files"): walk(sub)
for f in env.files.values(): walk(f)

def deref(src_file, pptr):
    fid, pid = pptr["m_FileID"], pptr["m_PathID"]
    if pid == 0: return None
    sf = src_file if fid == 0 else files.get(src_file.externals[fid - 1].path.split("/")[-1].lower())
    return sf.objects.get(pid) if sf else None

def quat(q): return np.array([q["x"], q["y"], q["z"], q["w"]], float) if isinstance(q, dict) else np.array([q.x, q.y, q.z, q.w], float)
def vec(v): return np.array([v["x"], v["y"], v["z"]], float) if isinstance(v, dict) else np.array([v.x, v.y, v.z], float)

def qmat(q):
    x, y, z, w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])

def trs(p, q, s):
    m = np.eye(4); m[:3, :3] = qmat(q) * s; m[:3, 3] = p; return m

class T:  # a transform read from a prefab
    def __init__(self, obj):
        d = obj.read_typetree()
        self.obj, self.d = obj, d
        self.local = trs(vec(d["m_LocalPosition"]), quat(d["m_LocalRotation"]), vec(d["m_LocalScale"]))
        self.go = deref(obj.assets_file, d["m_GameObject"])
        self.name = self.go.read_typetree()["m_Name"]
        self.children = [deref(obj.assets_file, c) for c in d["m_Children"]]
        self.father = deref(obj.assets_file, d["m_Father"])

tcache = {}
def tf(obj):
    k = (id(obj.assets_file), obj.path_id)
    if k not in tcache: tcache[k] = T(obj)
    return tcache[k]

def components(go_obj):
    d = go_obj.read_typetree()
    out = []
    for c in d["m_Component"]:
        o = deref(go_obj.assets_file, c["component"])
        if o: out.append(o)
    return out

def subtree(t, rel=np.eye(4)):  # (transform, matrix relative to the starting transform)
    yield t, rel
    for c in t.children:
        ct = tf(c)
        yield from subtree(ct, rel @ ct.local)

def mesh_data(mesh_obj):
    m = mesh_obj.read()
    h = MeshHandler(m); h.process()
    v = np.array(h.m_Vertices, float).reshape(-1, 3)
    tris = []
    for sub in h.get_triangles():
        tris.extend(sub)
    bind = [np.array([[b.e00, b.e01, b.e02, b.e03], [b.e10, b.e11, b.e12, b.e13], [b.e20, b.e21, b.e22, b.e23], [b.e30, b.e31, b.e32, b.e33]]) for b in (m.m_BindPose or [])]
    return v, np.array(tris, int).reshape(-1, 3), bind, m.m_Name

def write_obj(path, parts):
    with open(path, "w") as f:
        base = 1
        for name, v, t in parts:
            f.write(f"o {name}\n")
            for p in v: f.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
            for a, b, c in t: f.write(f"f {a+base} {b+base} {c+base}\n")
            base += len(v)

def apply(m, v): return (np.c_[v, np.ones(len(v))] @ m.T)[:, :3]

# ---------------- Commando ----------------
body_go = None
for o in env.objects:
    if o.type.name == "GameObject" and o.peek_name() == "CommandoBody":
        if any(c.type.name == "MonoBehaviour" and c.read_typetree().get("baseNameToken") for c in components(o) if c.type.name == "MonoBehaviour"):
            body_go = o; break
body_t = next(tf(c) for c in components(body_go) if c.type.name == "Transform")
model_t, model_rel = next((t, r) for t, r in subtree(body_t) if t.name == "mdlCommandoDualies")
print("model", model_t.name)

nodes = {}  # name -> (transform, matrix in model space, prefab pose)
by_key = {}
for t, rel in subtree(model_t):
    by_key[(id(t.obj.assets_file), t.obj.path_id)] = (t, rel)

bones_rest = {}
body_parts = []
fallback_mesh = {}
for o in env.objects:
    if o.type.name == "SkinnedMeshRenderer":
        sd = o.read_typetree()
        if sd["m_Mesh"]["m_PathID"] != 0:
            fallback_mesh.setdefault((o.read().m_GameObject.read().m_Name, len(sd["m_Bones"])), deref(o.assets_file, sd["m_Mesh"]))
for t, rel in by_key.values():
    for c in components(t.go):
        if c.type.name != "SkinnedMeshRenderer": continue
        d = c.read_typetree()
        if not d.get("m_Enabled", 1): continue
        mo = deref(c.assets_file, d["m_Mesh"]) or fallback_mesh.get((t.name, len(d["m_Bones"])))
        if mo is None: print("no mesh for", t.name); continue
        v, tri, bind, mname = mesh_data(mo)
        print("smr", t.name, mname, len(v), len(tri))
        for i, bp in enumerate(d["m_Bones"]):
            bo = deref(c.assets_file, bp)
            if bo is None or i >= len(bind): continue
            k = (id(bo.assets_file), bo.path_id)
            bones_rest[k] = rel @ np.linalg.inv(bind[i])
        if "gun" not in mname.lower():
            body_parts.append((mname, apply(rel, v), tri))
write_obj(OUT + "/commando_body.obj", body_parts)

def rest_of(key):
    t, rel = by_key[key]
    k, cur = key, t
    while True:
        if k in bones_rest:
            return bones_rest[k] @ np.linalg.inv(by_key[k][1]) @ rel
        if cur.father is None: return rel
        k = (id(cur.father.assets_file), cur.father.path_id)
        if k not in by_key: return rel
        cur = by_key[k][0]

locator = None
for c in components(model_t.go):
    if c.type.name == "MonoBehaviour" and "transformPairs" in c.read_typetree():
        locator = c.read_typetree()
points = {}
for p in locator["transformPairs"]:
    o = deref(model_t.obj.assets_file, p["transform"])
    if o is None: continue
    k = (id(o.assets_file), o.path_id)
    if k not in by_key: continue
    points[p["name"]] = {"rest": rest_of(k).tolist(), "pose": by_key[k][1].tolist(), "bone": by_key[k][0].name}
# bone names too (the hero-bone mapping uses ChildLocator names, the report wants both)
cap = None
for c in components(body_go):
    if c.type.name == "CapsuleCollider": cap = c.read_typetree()
json.dump({"points": points, "model_in_body": model_rel.tolist(), "capsule": {"height": cap["m_Height"], "radius": cap["m_Radius"], "center": [cap["m_Center"]["x"], cap["m_Center"]["y"], cap["m_Center"]["z"]]} if cap else None},
          open(OUT + "/commando.json", "w"), indent=1)
print("points", len(points), flush=True)

# ---------------- idrsCommando ----------------
idrs = next(o for o in env.objects if o.type.name == "MonoBehaviour" and o.peek_name() == "idrsCommando")
d = idrs.read_typetree()
rules, prefabs = [], set()
for g in d["keyAssetRuleGroups"]:
    ka = deref(idrs.assets_file, g["keyAsset"])
    kd = ka.read_typetree() if ka else {}
    name = kd.get("m_Name") or g["keyAssetAddress"]["m_AssetGUID"]
    kind = "equipment" if "equipmentIndex" in kd or "cooldown" in kd else ("item" if "tier" in kd or "_itemTierDef" in kd or "deprecatedTier" in kd else "other")
    rs = []
    for r in g["displayRuleGroup"]["rules"]:
        path = None
        fo = deref(idrs.assets_file, r["followerPrefab"])
        if fo is not None: path = "obj:" + fo.peek_name()
        elif r["followerPrefabAddress"]["m_AssetGUID"]: path = guid.get(r["followerPrefabAddress"]["m_AssetGUID"])
        if path: prefabs.add(path)
        rs.append({"type": r["ruleType"], "prefab": path, "child": r["childName"], "pos": [r["localPos"][a] for a in "xyz"],
                   "ang": [r["localAngles"][a] for a in "xyz"], "scale": [r["localScale"][a] for a in "xyz"], "limb": r["limbMask"]})
    rules.append({"key": name, "kind": kind, "rules": rs})
json.dump(rules, open(OUT + "/rules.json", "w"), indent=1)
print("rules", len(rules), "prefabs", len(prefabs), flush=True)

# ---------------- display prefabs ----------------
container = {k.lower(): v for k, v in env.container.items()}
meta = {}
for path in sorted(p for p in prefabs if p):
    obj = container.get(path.lower())
    if obj is None: print("missing", path); continue
    o = obj.deref() if hasattr(obj, "deref") else obj
    if o.type.name != "GameObject": o = o.read().m_GameObject.deref() if hasattr(o.read(), "m_GameObject") else o
    root = next(tf(c) for c in components(o) if c.type.name == "Transform")
    parts = []
    for t, rel in subtree(root):
        for c in components(t.go):
            tn = c.type.name
            if tn not in ("MeshFilter", "SkinnedMeshRenderer"): continue
            cd = c.read_typetree()
            if tn == "MeshFilter":
                if not any(x.type.name == "MeshRenderer" for x in components(t.go)): continue
            mo = deref(c.assets_file, cd["m_Mesh"])
            if mo is None: continue
            try: v, tri, _, mname = mesh_data(mo)
            except Exception as e: print("mesh fail", path, e); continue
            if len(tri) == 0: continue
            parts.append((mname, apply(rel, v), tri))
    short = os.path.splitext(os.path.basename(path))[0]
    if parts:
        write_obj(f"{OUT}/displays/{short}.obj", parts)
        allv = np.vstack([p[1] for p in parts])
        meta[path] = {"name": short, "min": allv.min(0).tolist(), "max": allv.max(0).tolist(), "tris": int(sum(len(p[2]) for p in parts))}
    else:
        meta[path] = {"name": short, "empty": True}
json.dump(meta, open(OUT + "/displays.json", "w"), indent=1)
print("displays", len(meta), "done", time.time() - t0)
