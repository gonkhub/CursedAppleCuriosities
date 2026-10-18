"""Feasibility scan of a Deadlock map (read-only): exports world-node models in batches with
Source2Viewer-CLI, reads the GLBs, and builds a top-down height map + per-model bounds.
Usage: mapscan.py <citadel dir> <map name> <out dir>"""
import json, os, struct, subprocess, sys, shutil
import numpy as np

CLI = os.environ.get("VRF_CLI", r"C:\Users\EV-02\HeroBrewLab\vrf\Source2Viewer-CLI.exe")

COMP = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}

def read_glb(path):
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + n])
    off = 20 + n
    bn = struct.unpack("<I", b[off:off + 4])[0]
    binc = b[off + 8: off + 8 + bn]
    return j, binc

def accessor(j, binc, i):
    a = j["accessors"][i]; v = j["bufferViews"][a["bufferView"]]
    dt = COMP[a["componentType"]]; nc = NCOMP[a["type"]]
    start = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = v.get("byteStride", 0)
    item = np.dtype(dt).itemsize * nc
    if stride and stride != item:
        raw = np.frombuffer(binc, np.uint8, count=stride * a["count"], offset=start).reshape(-1, stride)[:, :item]
        return np.ascontiguousarray(raw).view(dt).reshape(a["count"], nc)
    return np.frombuffer(binc, dt, count=a["count"] * nc, offset=start).reshape(a["count"], nc)

def node_matrices(j):
    mats = {}
    def walk(i, parent):
        nd = j["nodes"][i]
        m = np.array(nd["matrix"], np.float64).reshape(4, 4).T if "matrix" in nd else np.eye(4)
        if "matrix" not in nd:
            t = nd.get("translation", [0, 0, 0]); s = nd.get("scale", [1, 1, 1]); q = nd.get("rotation", [0, 0, 0, 1])
            x, y, z, w = q
            r = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
            m = np.eye(4); m[:3, :3] = r * s; m[:3, 3] = t
        m = parent @ m
        if "mesh" in nd: mats.setdefault(nd["mesh"], []).append(m)
        for c in nd.get("children", []): walk(c, m)
    for sc in j.get("scenes", [{"nodes": range(len(j["nodes"]))}]):
        for i in sc["nodes"]: walk(i, np.eye(4))
    return mats

def triangles(path):
    """World-space triangles (N,3,3) in glTF metres, Y up."""
    j, binc = read_glb(path)
    out = []
    for mi, ms in node_matrices(j).items():
        for p in j["meshes"][mi]["primitives"]:
            if p.get("mode", 4) != 4 or "indices" not in p: continue
            pos = accessor(j, binc, p["attributes"]["POSITION"]).astype(np.float64)
            idx = accessor(j, binc, p["indices"]).reshape(-1).astype(np.int64)
            for m in ms:
                w = pos @ m[:3, :3].T + m[:3, 3]
                out.append(w[idx[: len(idx) // 3 * 3]].reshape(-1, 3, 3))
    return np.concatenate(out) if out else np.zeros((0, 3, 3))

if __name__ == "__main__":
    citadel, mapname, out = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(out, exist_ok=True)
    vpk = os.path.join(citadel, "maps", mapname + ".vpk")
    listing = [l.split(" ")[0] for l in open(os.path.join(out, "..", "maps", mapname + ".list.txt")) if "/worldnodes/" in l and l.split(" ")[0].endswith(".vmdl_c")]
    RES = 1.0  # metres per pixel
    X0, X1, Z0, Z1 = -950, 950, -950, 950
    W, H = int((X1 - X0) / RES), int((Z1 - Z0) / RES)
    top = np.full((H, W), -1e9, np.float32)
    bounds = {}
    total_tris = 0
    tmp = os.path.join(out, "tmp")
    B = 60
    for bi in range(0, len(listing), B):
        batch = listing[bi: bi + B]
        shutil.rmtree(tmp, ignore_errors=True); os.makedirs(tmp)
        subprocess.run([CLI, "-i", vpk, "-f", ",".join(batch), "-o", tmp, "-d", "--gltf_export_format", "glb"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for root, _, files in os.walk(tmp):
            for f in files:
                if not f.endswith(".glb") or f.endswith("_physics.glb"): continue
                t = triangles(os.path.join(root, f))
                if len(t) == 0: continue
                total_tris += len(t)
                bounds[f] = [t.reshape(-1, 3).min(0).tolist(), t.reshape(-1, 3).max(0).tolist(), len(t)]
                # splat: vertices + centroids + edge midpoints, subdividing long triangles a few times
                pts = [t.reshape(-1, 3)]
                cur = t
                for _ in range(6):
                    e = np.max(np.linalg.norm(cur[:, [1, 2, 0]] - cur, axis=2), axis=1)
                    big = cur[e > 2 * RES]
                    if len(big) == 0: break
                    a, b2, c = big[:, 0], big[:, 1], big[:, 2]
                    ab, bc, ca = (a + b2) / 2, (b2 + c) / 2, (c + a) / 2
                    pts += [ab, bc, ca]
                    cur = np.concatenate([np.stack([a, ab, ca], 1), np.stack([ab, b2, bc], 1), np.stack([ca, bc, c], 1), np.stack([ab, bc, ca], 1)])
                p = np.concatenate(pts)
                xi = ((p[:, 0] - X0) / RES).astype(int); zi = ((p[:, 2] - Z0) / RES).astype(int)
                ok = (xi >= 0) & (xi < W) & (zi >= 0) & (zi < H)
                np.maximum.at(top, (zi[ok], xi[ok]), p[ok, 1].astype(np.float32))
        print(f"{bi + len(batch)}/{len(listing)} files, {total_tris} tris", flush=True)
    shutil.rmtree(tmp, ignore_errors=True)
    np.save(os.path.join(out, "top.npy"), top)
    json.dump({"res": RES, "x0": X0, "z0": Z0, "bounds": bounds, "tris": total_tris}, open(os.path.join(out, "scan.json"), "w"))
