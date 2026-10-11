# The user's 10 hand placements (made in ItemDisplayPlacementHelper against the old anchors: Abrams' bone position,
# Commando's attach-point rotation, unit scale) carried onto the new anchors.
import json, copy, numpy as np
from rig import *
from anchors import ANCHORS as OLD   # old-style anchors (hero bone position + Commando rest rotation)
fit = json.load(open(D + "/abrams_fit.json"))
NEW = {k: np.array(v) for k, v in fit["anchor_model"].items()}
U = [("ExplodeOnDeath", None, "HandR", (0.10251, -0.00025, -0.22002), (344.4106, 108.9855, 105.1453), (0.10129, 0.09148, 0.09502)),
     ("Seed", None, "ThighL", (0.48435, -0.45041, 0.36084), (72.30868, 100.3514, 94.07923), (0.08, 0.08, 0.08)),
     ("Bear", None, "Chest", (0, 0.05, -0.3), (353.555, 183.8123, 359.8003), (0.32535,) * 3),
     ("Hoof", None, "CalfR", (-0.05497, 0.28956, -0.08468), (76.59, 2.75642, 3.73079), (0.09659, 0.10133, 0.12213)),
     ("ChainLightning", None, "Chest", (0.0634, -0.3774, -0.35105), (336.556, 175.9843, 41.55862), (0.7,) * 3),
     ("LunarSun", "DisplaySunHead", "Head", (0.00322, 0.05336, -0.01214), (0, 0, 0), (1, 1, 1)),
     ("LunarSun", "DisplaySunHeadNeck", "Chest", (-0.02854, 0.11997, 0.07642), (-0.00001, 262.1551, 0.00001), (1.38764,) * 3),
     ("Crowbar", None, "HandL", (0.18258, 0.2686, 0.04095), (342.1374, 359.898, 359.4743), (0.4,) * 3),
     ("WarCryOnMultiKill", None, "Chest", (-0.42789, 0.08627, -0.03709), (291.5516, 18.34039, 66.04411), (1.11482,) * 3),
     ("BossHunterConsumed", None, "Head", (0.0115, 0.19191, -0.08489), (11.45647, 0.00001, 0), (1, 1, 1))]
meta = json.load(open(D + "/displays.json"))
out = copy.deepcopy(fit)
for key, prefab, child, p, a, s in U:
    g = next(x for x in out["groups"] if x["key"] == key)
    r = next(x for x in g["rules"] if x["type"] == 0 and (prefab is None or (x["prefab"] or "").endswith(prefab + ".prefab")))
    M = OLD[child] @ rule_m({"pos": p, "ang": a, "scale": s})
    local = np.linalg.inv(NEW[child]) @ M
    lp, lR, ls = decompose(local)
    r.update(child=child, pos=lp.tolist(), ang=mat_euler(lR), scale=ls.tolist(), model=M.tolist(), note="user placement")
json.dump(out, open(D + "/abrams_fit_user.json", "w"), indent=1)
print("ok")
