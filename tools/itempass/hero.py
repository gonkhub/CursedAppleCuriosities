# Abrams as the mod places him (GlbLoader + DeadlockModelSwap.TryAttach), in Commando's model space (Unity coords):
# rest bones (RestInRoot = inverse of the X-mirrored inverse bind matrices) and the bind-pose mesh.
import json, struct, math
import numpy as np
from rig import *

HEIGHT_SCALE = HEROES[HERO]["scale"]   # <Hero>Data.ModelHeightScale
CAPSULE_H = 1.82            # CommandoBody capsule
MODEL_Y = -0.898            # mdlCommandoDualies in the body
FEET_Y = -CAPSULE_H / 2     # body.footPosition (capsule bottom)

b = open(GLB, "rb").read()
jl = struct.unpack_from("<I", b, 12)[0]
j = json.loads(b[20:20 + jl]); bin0 = 20 + jl + 8
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

F = np.diag([-1.0, 1, 1, 1])
skin = j["skins"][0]
ibm = acc(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)  # column-major -> row-major
rest_root = {}
for k, ji in enumerate(skin["joints"]):
    rest_root[j["nodes"][ji]["name"]] = np.linalg.inv(F @ ibm[k] @ F)

parts = []
top = 0.0
for n in j["nodes"]:
    if "mesh" not in n: continue
    if j["meshes"][n["mesh"]].get("name", "").split(".")[-1] in HEROES[HERO]["hide"]: continue
    for pi, prim in enumerate(j["meshes"][n["mesh"]]["primitives"]):
        v = acc(prim["attributes"]["POSITION"]) * [-1, 1, 1]
        tri = acc(prim["indices"]).astype(int).reshape(-1, 3)
        top = max(top, v[:, 1].max())
        parts.append((f"{j['meshes'][n['mesh']].get('name', 'm')}_{pi}", v, tri))

# DeadlockModelSwap.TryAttach: face the frame's forward (hips left->right along +X), feet on the ground, scaled to height.
ll, lr = rest_root["leg_upper_L"][:3, 3], rest_root["leg_upper_R"][:3, 3]
right = lr - ll; right[1] = 0; right /= np.linalg.norm(right)
# Quaternion.FromToRotation(right, Vector3.right) about Y
ang = math.degrees(math.atan2(-right[2], right[0]))  # rotate right onto +X
yaw = axis_q((0, 1, 0), -ang) if False else None
def from_to(a, c):
    a = a / np.linalg.norm(a); c = c / np.linalg.norm(c)
    ax = np.cross(a, c); s = np.linalg.norm(ax); d = np.dot(a, c)
    if s < 1e-8: return np.array([0, 0, 0, 1.0])
    ax /= s; h = math.atan2(s, d) / 2
    return np.array([*(ax * math.sin(h)), math.cos(h)])
yaw = from_to(right, np.array([1.0, 0, 0]))
scale = CAPSULE_H * HEIGHT_SCALE / top
root_in_model = trs([0, FEET_Y - MODEL_Y, 0], yaw, [scale] * 3)
print("height", top, "scale", scale, "yaw", yaw.round(4))

bones = {k: (root_in_model @ m).tolist() for k, m in rest_root.items()}
json.dump({"bones": bones, "root_in_model": root_in_model.tolist(), "scale": scale, "top": top}, open(D + f"/{HERO}.json", "w"))
with open(D + f"/{HERO}_body.obj", "w") as f:
    base = 1
    for name, v, tri in parts:
        w = apply(root_in_model, v)
        f.write(f"o {name}\n")
        for p in w: f.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
        for a, c, d in tri: f.write(f"f {a+base} {c+base} {d+base}\n")
        base += len(v)
print("bones", len(bones), "parts", [p[0] for p in parts])
