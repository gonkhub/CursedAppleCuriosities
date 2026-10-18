"""Crop one section of a Deadlock map (read-only) into per-model GLBs with materials.
Usage: mapcrop.py <citadel dir> <map name> <scan dir> <out dir> x0 x1 z0 z1   (glTF metres)"""
import json, os, struct, subprocess, sys, shutil
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mapscan import CLI, read_glb, accessor, node_matrices
from mapcrop_io import write_glb

citadel, mapname, scandir, out = sys.argv[1:5]
x0, x1, z0, z1 = map(float, sys.argv[5:9])
scan = json.load(open(os.path.join(scandir, "scan.json")))
vpk = os.path.join(citadel, "maps", mapname + ".vpk")

hits = [f for f, (mn, mx, n) in scan["bounds"].items() if mx[0] >= x0 and mn[0] <= x1 and mx[2] >= z0 and mn[2] <= z1]
print(len(hits), "models touch the box")
shutil.rmtree(out, ignore_errors=True); os.makedirs(out)
for bi in range(0, len(hits), 60):
    paths = ",".join(f"maps/{mapname}/worldnodes/{f[:-4]}.vmdl_c" for f in hits[bi:bi + 60])
    subprocess.run([CLI, "-i", vpk, "-f", paths, "-o", out, "-d", "--gltf_export_format", "glb",
                    "--gltf_export_materials", "--gltf_textures_adapt"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

kept_total = 0; models = []
for root, _, files in os.walk(out):
    for f in files:
        if not f.endswith(".glb"): continue
        p = os.path.join(root, f)
        j, binc = read_glb(p); binc = bytearray(binc)
        mats = node_matrices(j); kept_file = 0
        for mi, ms in mats.items():
            m = ms[0]
            for pr in j["meshes"][mi]["primitives"]:
                if "indices" not in pr: continue
                pos = accessor(j, binc, pr["attributes"]["POSITION"]).astype(np.float64) @ m[:3, :3].T + m[:3, 3]
                idx = accessor(j, binc, pr["indices"]).reshape(-1).astype(np.uint32)
                tri = idx[: len(idx) // 3 * 3].reshape(-1, 3)
                c = pos[tri].mean(1)
                keep = tri[(c[:, 0] >= x0) & (c[:, 0] <= x1) & (c[:, 2] >= z0) & (c[:, 2] <= z1)]
                kept_file += len(keep)
                data = keep.astype(np.uint32).tobytes()
                binc += b"\0" * (-len(binc) % 4)
                j["bufferViews"].append({"buffer": 0, "byteOffset": len(binc), "byteLength": len(data), "target": 34963})
                binc += data
                j["accessors"].append({"bufferView": len(j["bufferViews"]) - 1, "componentType": 5125, "count": int(keep.size), "type": "SCALAR"})
                pr["indices"] = len(j["accessors"]) - 1
        if kept_file == 0:
            os.remove(p); continue
        j["buffers"][0]["byteLength"] = len(binc) + (-len(binc) % 4)
        write_glb(p, j, bytes(binc))  # old index data stays as dead bytes until compact.py
        kept_total += kept_file
        models.append(os.path.relpath(p, out).replace("\\", "/"))
json.dump({"models": models, "box": [x0, x1, z0, z1]}, open(os.path.join(out, "section.json"), "w"))
print(len(models), "models,", kept_total, "triangles kept")
