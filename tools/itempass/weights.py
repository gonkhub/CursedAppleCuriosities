# Per-vertex dominant bone for both bodies (to cut them into regions), plus bone parents/rest positions.
import UnityPy, glob, os, json, struct
import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
from rig import D

# ---- Commando (once) ----
if not os.path.exists(D + "/commando_dom.npy"):
    G = r"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\StandaloneWindows64"
    env = UnityPy.load(*glob.glob(os.path.join(G, "ror2-base-commando_*")))
    smr_body = mesh = None
    for o in env.objects:
        if o.type.name != "SkinnedMeshRenderer": continue
        d = o.read_typetree()
        go = o.read().m_GameObject.read()
        if go.m_Name == "CommandoMesh" and d["m_Mesh"]["m_PathID"] != 0 and mesh is None:
            mesh = o.assets_file.objects[d["m_Mesh"]["m_PathID"]]
        if go.m_Name == "CommandoMesh" and d["m_Mesh"]["m_PathID"] == 0 and smr_body is None:
            smr_body = o
    d = smr_body.read_typetree()
    names, parents = [], []
    for bp in d["m_Bones"]:
        t = smr_body.assets_file.objects[bp["m_PathID"]]
        td = t.read_typetree()
        names.append(t.read().m_GameObject.read().m_Name)
        f = td["m_Father"]["m_PathID"]
        parents.append(smr_body.assets_file.objects[f].read().m_GameObject.read().m_Name if f else None)
    h = MeshHandler(mesh.read()); h.process()
    bi = np.array(h.m_BoneIndices).reshape(-1, 4); bw = np.array(h.m_BoneWeights).reshape(-1, 4)
    dom = bi[np.arange(len(bi)), bw.argmax(1)]
    np.save(D + "/commando_dom.npy", dom)
    json.dump({"names": names, "parents": parents}, open(D + "/commando_bones.json", "w"))
    print("commando verts", len(dom), "bones", len(names))
    print(sorted(set(names[i] for i in set(dom.tolist()))))

# ---- hero ----
from rig import HERO, HEROES, GLB
b = open(GLB, "rb").read(); jl = struct.unpack_from("<I", b, 12)[0]; j = json.loads(b[20:20 + jl]); bin0 = 20 + jl + 8
def acc(i):
    a = j["accessors"][i]; bv = j["bufferViews"][a["bufferView"]]
    n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
    dt = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]]
    off = bin0 + bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", 0); isz = np.dtype(dt).itemsize * n
    if stride and stride != isz:
        raw = np.frombuffer(b, np.uint8, a["count"] * stride, off).reshape(a["count"], stride)[:, :isz].copy()
        return raw.view(dt).reshape(a["count"], n).astype(float)
    return np.frombuffer(b, dt, a["count"] * n, off).reshape(a["count"], n).astype(float)
joints = [j["nodes"][ji]["name"] for ji in j["skins"][0]["joints"]]
parent = {}
for n in j["nodes"]:
    for c in n.get("children", []): parent[j["nodes"][c]["name"]] = n["name"]
doms = []
for n in j["nodes"]:
    if "mesh" not in n: continue
    if j["meshes"][n["mesh"]].get("name", "").split(".")[-1] in HEROES[HERO]["hide"]: continue
    for prim in j["meshes"][n["mesh"]]["primitives"]:
        ji = acc(prim["attributes"]["JOINTS_0"]).astype(int); wi = acc(prim["attributes"]["WEIGHTS_0"])
        doms.append(ji[np.arange(len(ji)), wi.argmax(1)])
dom = np.concatenate(doms)
np.save(D + f"/{HERO}_dom.npy", dom)
json.dump({"names": joints, "parent": parent}, open(D + f"/{HERO}_bones.json", "w"))
print(HERO, "verts", len(dom))
used = sorted(set(joints[i] for i in set(dom.tolist())))
print(len(used), used[:200])
