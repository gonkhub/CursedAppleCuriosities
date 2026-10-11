# Blender: renders a job (JSON) of meshes placed in Unity space. Usage:
#   blender -b --factory-startup -P render.py -- job.json
# job: {"parts": [{"obj": path, "matrix": 4x4 unity, "color": [r,g,b], "name": str}], "views": [{"name", "dir": [x,y,z] unity, "out", "center": [..], "size": float}], "res": int}
import bpy, sys, json, math
import numpy as np
from mathutils import Vector, Matrix

job = json.load(open(sys.argv[-1]))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'OBJECT'
scene.display.shading.show_cavity = True
scene.display.shading.show_object_outline = True
scene.render.resolution_x = scene.render.resolution_y = job.get("res", 800)
scene.render.film_transparent = False
world = bpy.data.worlds.new("w"); scene.world = world
world.color = (0.9, 0.9, 0.9)

def u2b(v): return np.stack([v[:, 0], v[:, 2], v[:, 1]], 1)  # Unity (LH, Y up) -> Blender (RH, Z up), not mirrored

objcache = {}
def load_obj(path):
    if path in objcache: return objcache[path]
    vs, fs = [], []
    for line in open(path):
        if line.startswith("v "): vs.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("f "): fs.append([int(x.split("/")[0]) - 1 for x in line.split()[1:4]])
    objcache[path] = (np.array(vs, float).reshape(-1, 3), fs)
    return objcache[path]

def add(i, p):
    v, f = load_obj(p["obj"])
    if len(v) == 0: return None
    m = np.array(p["matrix"], float)
    w = (np.c_[v, np.ones(len(v))] @ m.T)[:, :3]
    me = bpy.data.meshes.new(p.get("name", f"p{i}"))
    me.from_pydata([tuple(x) for x in u2b(w)], [], f)
    me.update()
    ob = bpy.data.objects.new(me.name, me)
    c = p.get("color", [0.7, 0.7, 0.7])
    ob.color = (c[0], c[1], c[2], 1.0)
    scene.collection.objects.link(ob)
    return ob

for i, p in enumerate(job["parts"]): add(i, p)

cam_data = bpy.data.cameras.new("cam"); cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
def shoot(view_list):
  for view in view_list:
    d = np.array(view["dir"], float); d /= np.linalg.norm(d)  # from the subject toward the camera (Unity)
    c = np.array(view["center"], float)
    pos = u2b((c + d * 10.0)[None])[0]
    tgt = u2b(c[None])[0]
    cam.location = Vector(pos)
    cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    cam_data.ortho_scale = view["size"]
    cam_data.clip_end = 100
    scene.render.filepath = view["out"]
    bpy.ops.render.render(write_still=True)

shoot(job.get("views", []))
for shot in job.get("shots", []):
    obs = [add(1000 + i, p) for i, p in enumerate(shot["parts"])]
    shoot(shot["views"])
    for ob in obs:
        if ob is not None: bpy.data.objects.remove(ob)
print("rendered", len(job.get("views", [])) + sum(len(s["views"]) for s in job.get("shots", [])))
