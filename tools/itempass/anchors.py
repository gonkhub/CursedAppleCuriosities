# The deterministic hero anchors, exactly as the C# side builds them (DeadlockHeroModel.MoveItemPoints):
# anchor N sits on hero bone HeroBoneFor(N); at rest its position is the hero bone's, its rotation and scale are
# Commando's attach point N at rest (all in model space). Rules are authored in that frame.
import json
import numpy as np
from rig import *

def hero_bone_for(name):
    n = name.lower()
    side = "_L" if n.endswith("l") or "left" in n else "_R" if n.endswith("r") or "right" in n else ""
    if "head" in n: return "head"
    if "neck" in n: return "neck_0"
    if "chest" in n or "upperbody" in n: return "spine_3"
    if "stomach" in n or "spine" in n: return "spine_1"
    if "pelvis" in n or "hip" in n: return "pelvis"
    if side == "": return "pelvis" if n in ("base", "root") else None
    if "clavicle" in n or "shoulder" in n: return "clavicle" + side
    if "upperarm" in n: return "arm_upper" + side
    if "lowerarm" in n or "forearm" in n or "elbow" in n: return "arm_lower" + side
    if "hand" in n or "gun" in n or "muzzle" in n: return "hand" + side
    if "thigh" in n: return "leg_upper" + side
    if "calf" in n or "shin" in n or "knee" in n: return "leg_lower" + side
    if "foot" in n or "toe" in n: return "ankle" + side
    return None

cmd = json.load(open(D + "/commando.json"))
from rig import HERO
abr = json.load(open(D + f"/{HERO}.json"))
C = {k: np.array(v["rest"]) for k, v in cmd["points"].items()}
H = {k: np.array(v) for k, v in abr["bones"].items()}

def anchor(name):
    """Anchor N at rest in model space (on Abrams), or Commando's own point when N isn't moved."""
    hb = hero_bone_for(name)
    c = C[name]
    if hb is None or hb not in H: return c
    p, R, s = decompose(c)
    m = np.eye(4); m[:3, :3] = R * s; m[:3, 3] = H[hb][:3, 3]
    return m

ANCHORS = {k: anchor(k) for k in C}
