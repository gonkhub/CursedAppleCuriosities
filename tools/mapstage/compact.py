"""Rewrite GLBs keeping only vertices referenced by each primitive's indices (in place)."""
import os, sys, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mapscan import read_glb, accessor
from mapcrop_io import write_glb
for root, _, files in os.walk(sys.argv[1]):
    for f in files:
        if not f.endswith(".glb"): continue
        p = os.path.join(root, f); j, binc = read_glb(p)
        out = bytearray(); views = []; accs = []
        def add(arr, target, typ, comp, minmax=False):
            out.extend(b"\0" * (-len(out) % 4))
            data = np.ascontiguousarray(arr).tobytes()
            views.append({"buffer": 0, "byteOffset": len(out), "byteLength": len(data), "target": target}); out.extend(data)
            a = {"bufferView": len(views) - 1, "componentType": comp, "count": int(arr.shape[0]), "type": typ}
            if minmax: a["min"] = arr.min(0).tolist(); a["max"] = arr.max(0).tolist()
            accs.append(a); return len(accs) - 1
        COMPC = {np.dtype(np.float32): 5126, np.dtype(np.uint16): 5123, np.dtype(np.uint8): 5121, np.dtype(np.int16): 5122, np.dtype(np.int8): 5120, np.dtype(np.uint32): 5125}
        TYP = {1: "SCALAR", 2: "VEC2", 3: "VEC3", 4: "VEC4"}
        for m in j["meshes"]:
            prims = []
            for pr in m["primitives"]:
                if "indices" not in pr: continue
                idx = accessor(j, binc, pr["indices"]).reshape(-1).astype(np.int64)
                if len(idx) == 0: continue
                used, remap = np.unique(idx, return_inverse=True)
                attrs = {}
                for k, ai in pr["attributes"].items():
                    a = accessor(j, binc, ai)[used]
                    src = j["accessors"][ai]
                    attrs[k] = add(a, 34962, TYP[a.shape[1]], COMPC[a.dtype], k == "POSITION")
                    if src.get("normalized"): accs[attrs[k]]["normalized"] = True
                pr["attributes"] = attrs
                pr["indices"] = add(remap.astype(np.uint32), 34963, "SCALAR", 5125)
                prims.append(pr)
            m["primitives"] = prims
        j["accessors"] = accs; j["bufferViews"] = views
        j["buffers"] = [{"byteLength": len(out) + (-len(out) % 4)}]
        write_glb(p, j, bytes(out))
