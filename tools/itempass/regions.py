# Body regions on both models and the region-to-region maps (Commando model space -> Abrams model space).
import json, math
import numpy as np
from rig import *
from anchors import C, H

SIDES = ("L", "R")

def cmd_region(bone):
    b = bone.lower()
    s = "L" if b.endswith(".l") else "R" if b.endswith(".r") else ""
    for key, reg in (("upper_arm", "uarm"), ("lower_arm", "larm"), ("hand", "hand"), ("finger", "hand"), ("thumb", "hand"),
                     ("thigh", "thigh"), ("calf", "calf"), ("foot", "foot"), ("toe", "foot")):
        if b.startswith(key): return f"{reg}_{s}"
    return {"head": "head", "chest": "torso", "stomach": "torso", "pelvis": "torso"}.get(b)

AB_ROOTS = {"head": "head", "neck_0": "torso", "spine_3": "torso", "spine_2": "torso", "spine_1": "torso", "spine_0": "torso", "pelvis": "torso"}
for s in SIDES:
    AB_ROOTS.update({f"clavicle_{s}": "chest", f"arm_upper_{s}": f"uarm_{s}", f"arm_lower_{s}": f"larm_{s}", f"hand_{s}": f"hand_{s}",
                     f"leg_upper_{s}": f"thigh_{s}", f"leg_lower_{s}": f"calf_{s}", f"ankle_{s}": f"foot_{s}"})
AB_SKIP = ("$", "coat", "book", "gun", "weapon", "flask", "tail", "lapel", "collar", "back_", "front_", "page", "muzzle")

ab_bones = json.load(open(D + f"/{HERO}_bones.json"))
def ab_region(bone):
    b = bone
    while b is not None:
        if b.startswith(AB_SKIP) or any(k in b for k in ("coat", "lapel", "collar", "book", "flask", "tail", "teeth", "tongue", "eye_", "glasses", "cig", "cuff", "can_", "rope", "wieners")): return None
        if b in AB_ROOTS: return AB_ROOTS[b]
        b = ab_bones["parent"].get(b)
    return None

cmd_v = load_obj(D + "/commando_body.obj")[0]
ab_v = load_obj(D + f"/{HERO}_body.obj")[0]
cmd_names = json.load(open(D + "/commando_bones.json"))["names"]
cmd_dom = np.load(D + "/commando_dom.npy"); ab_dom = np.load(D + f"/{HERO}_dom.npy")
assert len(cmd_dom) == len(cmd_v), (len(cmd_dom), len(cmd_v))
ab_v = ab_v[: len(ab_dom)]
cmd_reg = np.array([cmd_region(cmd_names[i]) or "" for i in cmd_dom])
ab_reg_cache = {}
ab_reg = np.array([ab_reg_cache.setdefault(i, ab_region(ab_bones["names"][i]) or "") for i in ab_dom])

def bone_pos(m): return np.asarray(m)[:3, 3]

# Limb axes (Commando attach points / Abrams bones), joint a -> joint b.
AXES = {}
for s, cs in (("L", "L"), ("R", "R")):
    AXES[f"uarm_{s}"] = ((f"UpperArm{cs}", f"LowerArm{cs}"), (f"arm_upper_{s}", f"arm_lower_{s}"))
    AXES[f"larm_{s}"] = ((f"LowerArm{cs}", f"Hand{cs}"), (f"arm_lower_{s}", f"hand_{s}"))
    AXES[f"hand_{s}"] = ((f"LowerArm{cs}", f"Hand{cs}"), (f"arm_lower_{s}", f"hand_{s}"))
    AXES[f"thigh_{s}"] = ((f"Thigh{cs}", f"Calf{cs}"), (f"leg_upper_{s}", f"leg_lower_{s}"))
    AXES[f"calf_{s}"] = ((f"Calf{cs}", f"Foot{cs}"), (f"leg_lower_{s}", f"ankle_{s}"))

def from_to_m(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    v = np.cross(a, b); c = float(np.dot(a, b))
    if np.linalg.norm(v) < 1e-9: return np.eye(3)
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))

class Region:
    def __init__(self, name):
        self.name = name
        if name in AXES:
            (ca, cb), (aa, ab) = AXES[name]
            axc = bone_pos(C[cb]) - bone_pos(C[ca]); axa = bone_pos(H[ab]) - bone_pos(H[aa])
            self.Fc = from_to_m(np.array([0, -1.0, 0]), axc)       # frame: local -Y runs down the limb
            self.Rseg = from_to_m(axc, axa)
        else:
            self.Fc = np.eye(3); self.Rseg = np.eye(3)
        self.Fa = self.Rseg @ self.Fc
        if name == "base":
            vc, va = cmd_v[cmd_reg != ""], ab_v[ab_reg != ""]
        else:
            vc, va = cmd_v[cmd_reg == name], ab_v[ab_reg == name]
        lc = vc @ self.Fc; la = va @ self.Fa                   # coordinates in each frame
        lo_c, hi_c = np.percentile(lc, 0.3, 0), np.percentile(lc, 99.7, 0)
        lo_a, hi_a = np.percentile(la, 0.3, 0), np.percentile(la, 99.7, 0)
        self.cc, self.ca = (lo_c + hi_c) / 2, (lo_a + hi_a) / 2
        self.D = np.clip((hi_a - lo_a) / np.maximum(hi_c - lo_c, 1e-4), 0.5, 2.2)
        if name == "base":  # the ground stays the ground: map from the feet up, keep the overall size ratio
            self.cc[1], self.ca[1] = lo_c[1], lo_a[1]
        self.k = float(np.clip(np.prod(self.D) ** (1 / 3), 0.8, 2.0))
        self.ext_c, self.ext_a = hi_c - lo_c, hi_a - lo_a
        self.nc, self.na = len(vc), len(va)

    def point(self, x):
        """Commando model-space point -> Abrams."""
        u = self.Fc.T @ (np.asarray(x) - 0) - self.cc
        a = self.ca + self.D * u
        if self.name == "torso":  # heights by landmarks: hips, shoulders, neck (Abrams has a neck, Commando hardly any)
            a[1] = float(np.interp(np.asarray(x)[1], TORSO_Y_C, TORSO_Y_A, left=None, right=None))
            y = np.asarray(x)[1]
            if y < TORSO_Y_C[0]: a[1] = TORSO_Y_A[0] + (y - TORSO_Y_C[0]) * self.D[1]
            if y > TORSO_Y_C[-1]: a[1] = TORSO_Y_A[-1] + (y - TORSO_Y_C[-1]) * self.D[1]
        return self.Fa @ a

    def core(self, x, abrams=True):
        """Closest point on the region's core line (inside the body), for inside/outside tests and push directions."""
        F, c, ext = (self.Fa, self.ca, self.ext_a) if abrams else (self.Fc, self.cc, self.ext_c)
        u = F.T @ np.asarray(x)
        y = np.clip(u[1], c[1] - ext[1] * 0.35, c[1] + ext[1] * 0.35) if self.name != "base" else c[1] + ext[1] * 0.5
        return F @ np.array([c[0], y, c[2]])

def _y(m): return float(np.asarray(m)[1, 3])
_ct = cmd_v[cmd_reg == "torso"][:, 1]; _at = ab_v[ab_reg == "torso"][:, 1]
TORSO_Y_C = [float(np.percentile(_ct, 0.3)), (_y(C["ThighL"]) + _y(C["ThighR"])) / 2, (_y(C["UpperArmL"]) + _y(C["UpperArmR"])) / 2, 1.62]
TORSO_Y_A = [0.0, (_y(H["leg_upper_L"]) + _y(H["leg_upper_R"])) / 2, (_y(H["arm_upper_L"]) + _y(H["arm_upper_R"])) / 2, _y(H["neck_0"]) + 0.27 * (_y(H["head"]) - _y(H["neck_0"]))]
# below the hips Abrams' mesh belongs to his thighs: carry the hip-to-shoulder stretch on down
TORSO_Y_A[0] = TORSO_Y_A[1] - (TORSO_Y_C[1] - TORSO_Y_C[0]) * (TORSO_Y_A[2] - TORSO_Y_A[1]) / (TORSO_Y_C[2] - TORSO_Y_C[1])
REGION_NAMES = ["head", "torso", "base"] + [f"{r}_{s}" for r in ("uarm", "larm", "hand", "thigh", "calf", "foot") for s in SIDES]
REGIONS = {n: Region(n) for n in REGION_NAMES}

def point_region(p):
    n = p.lower()
    s = "L" if n.endswith("l") or "left" in n else "R" if n.endswith("r") or "right" in n else ""
    if "head" in n: return "head"
    if n in ("base", "root"): return "base"
    if "chest" in n or "jet" in n or n == "muzzlecenter" or "stomach" in n or "pelvis" in n: return "torso"
    for k, r in (("upperarm", "uarm"), ("lowerarm", "larm"), ("hand", "hand"), ("gun", "hand"), ("muzzle", "hand"), ("finger", "hand"), ("reload", "hand"),
                 ("thigh", "thigh"), ("calf", "calf"), ("foot", "foot")):
        if k in n: return f"{r}_{s}"
    return None

if __name__ == "__main__":
    for n, r in REGIONS.items():
        print(f"{n:8s} verts c{r.nc:6d} a{r.na:6d}  ext_c {r.ext_c.round(3)} ext_a {r.ext_a.round(3)}  D {r.D.round(2)} k {r.k:.2f}")


# Surfaces each region snaps against: its own skin, plus what hangs on it (the coat on the torso, glasses on the head),
# never a neighbour (an A-posed arm beside the torso). The coat's collar and lapels are left out: they stand off the
# back and chest, and items should rest on the coat, not on the collar's rim.
def ab_surface_region(bone):
    b = bone
    while b is not None:
        if any(k in b for k in ("collar", "lapel", "book", "gun", "weapon", "tail")): return None
        if b.startswith("$cloth") or "coat" in b: return "coat"
        if "flask" in b: return "torso"
        if "cuff" in b: return None
        if b in AB_ROOTS: return AB_ROOTS[b]
        b = ab_bones["parent"].get(b)
    return None
ab_surf = np.array([ab_reg_cache.setdefault(("s", i), ab_surface_region(ab_bones["names"][i]) or "") for i in ab_dom])


def surface_set(region):
    """Labels whose faces a region's items snap against: the whole limb chain (pants and sleeves are split over
    several bones), and for legs and the torso the coat too."""
    if region in ("head",): return {"head"}
    if region in ("torso", "base"): return {"torso", "coat"}
    kind, s = region.split("_")
    if kind in ("uarm", "larm", "hand"): return {f"uarm_{s}", f"larm_{s}", f"hand_{s}"}
    return {f"thigh_{s}", f"calf_{s}", f"foot_{s}", "coat"}
