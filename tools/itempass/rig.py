# Shared math (Unity conventions: left-handed, Y up, Quaternion.Euler = Z then X then Y) and job helpers.
import json, math, os, subprocess
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.join(HERE, "data")
HERO = os.environ.get("HERO", "abrams")  # which hero the pass is for
# scale = <Hero>Data.ModelHeightScale before the playtest multiplier; hide = meshes the hero hides; skip = body parts items
# don't snap to (held guns); glb = the converted model's file name in the cache.
HEROES = {"abrams": {"scale": 1.2, "hide": [], "skip": ("gun_model", "book_model")},
          "lash": {"scale": 1.08, "hide": ["sparkles"], "skip": ("gun_model", "book_model")},
          "seven": {"scale": 1.08, "hide": ["skeleton"], "skip": (".gun_",), "glb": "gigawatt_prisoner"},  # skeleton = Storm Cloud look
          # Seven without his halo headgear: face items (glasses, eyes) fitted to his face instead of the halo's rim.
          "seven_bare": {"scale": 1.08, "hide": ["skeleton"], "fit_hide": ["headgear"], "skip": (".gun_",), "glb": "gigawatt_prisoner", "dir": "seven"},
          # Grey Talon: items skip his bow and the owl on his arm; reload props are hidden as in the game.
          "greytalon": {"scale": 1.08, "hide": ["archer_clip_inhand_model", "archer_quiverclips_anim_model"], "fit_hide": ["owl_bodygroup"],
                        "skip": (".archer_bow_model_", ".archer_clip_inbow_model_"), "glb": "archer", "head_floor": 0.05, "name": "GreyTalon"},  # braids hang to his chest
          "vyper": {"scale": 1.08, "hide": [], "skip": (".gun_",), "glb": "viper"}}
GLB = os.environ.get("HERO_GLB") or os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "Hopoo Games, LLC", "Risk of Rain 2", "CursedApple", "deadlock", "models", HEROES[HERO].get("dir", HERO), HEROES[HERO].get("glb", HERO) + ".glb")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return np.array([aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx, aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz])

def axis_q(axis, deg):
    h = math.radians(deg) / 2; s = math.sin(h)
    return np.array([axis[0]*s, axis[1]*s, axis[2]*s, math.cos(h)])

def euler_q(e):  # Unity Quaternion.Euler
    return qmul(qmul(axis_q((0, 1, 0), e[1]), axis_q((1, 0, 0), e[0])), axis_q((0, 0, 1), e[2]))

def qmat(q):
    x, y, z, w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])

def trs(p, q, s):
    m = np.eye(4); m[:3, :3] = qmat(q) * np.asarray(s, float); m[:3, 3] = p; return m

def rule_m(r): return trs(r["pos"], euler_q(r["ang"]), r["scale"])

def mat_euler(R):  # rotation matrix -> Unity euler (inverse of euler_q), degrees in [0, 360)
    x = math.degrees(math.asin(max(-1, min(1, -R[1][2]))))
    if abs(R[1][2]) < 0.9999:
        y = math.degrees(math.atan2(R[0][2], R[2][2])); z = math.degrees(math.atan2(R[1][0], R[1][1]))
    else:
        y = math.degrees(math.atan2(-R[2][0], R[0][0])); z = 0.0
    return [a % 360 for a in (x, y, z)]

def decompose(m):  # 4x4 -> pos, rotation matrix, scale (no shear assumed)
    s = np.linalg.norm(m[:3, :3], axis=0)
    if np.linalg.det(m[:3, :3]) < 0: s[0] = -s[0]  # a mirrored display (Commando uses negative x scale): keep the mirror in the scale
    return m[:3, 3].copy(), m[:3, :3] / s, s

def apply(m, v): return (np.c_[v, np.ones(len(v))] @ np.asarray(m).T)[:, :3]

def load_obj(path):
    vs, fs = [], []
    for line in open(path):
        if line.startswith("v "): vs.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("f "): fs.append([int(x) - 1 for x in line.split()[1:4]])
    return np.array(vs, float).reshape(-1, 3), np.array(fs, int).reshape(-1, 3)

PALETTE = [(0.9, 0.2, 0.2), (0.2, 0.6, 0.95), (0.2, 0.8, 0.3), (0.95, 0.75, 0.1), (0.7, 0.3, 0.9), (0.1, 0.8, 0.8),
           (0.95, 0.45, 0.1), (0.9, 0.3, 0.6), (0.4, 0.4, 0.95), (0.55, 0.85, 0.2), (0.6, 0.35, 0.2), (0.15, 0.15, 0.15)]

def views(prefix, center, size, which=("front", "side", "back", "top")):
    dirs = {"front": [0, 0, 1], "back": [0, 0, -1], "side": [1, 0, 0], "lside": [-1, 0, 0], "top": [0, 1, 0.001], "q": [0.7, 0.3, 0.7], "qb": [-0.7, 0.3, -0.7]}
    return [{"name": w, "dir": dirs[w], "center": list(center), "size": size, "out": os.path.abspath(f"{prefix}_{w}.png")} for w in which]

def render(parts, view_list, job_path, res=700, shots=None):
    json.dump({"parts": parts, "views": view_list, "res": res, "shots": shots or []}, open(job_path, "w"))
    r = subprocess.run([BLENDER, "-b", "--factory-startup", "-P", os.path.join(HERE, "render.py"), "--", job_path], capture_output=True, text=True)
    if "rendered" not in r.stdout: print(r.stdout[-3000:], r.stderr[-3000:])

def montage(paths, out, cols=None):
    """Side-by-side PNGs (Blender can't easily; use PIL if present, else leave separate)."""
    try:
        from PIL import Image
    except ImportError:
        return paths
    ims = [Image.open(p) for p in paths]
    cols = cols or len(ims)
    w, h = ims[0].size
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new("RGB", (w * cols, h * rows), (255, 255, 255))
    for i, im in enumerate(ims): sheet.paste(im, ((i % cols) * w, (i // cols) * h))
    sheet.save(out)
    return [out]
