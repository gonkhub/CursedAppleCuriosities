"""Map of covered floors (a floor with another floor/roof above it) below a height, plus trigger volumes.
Usage: underground.py region.npz trig.json selection.json out.png [maxY]"""
import json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

d = np.load(sys.argv[1]); T = d["tris"].astype(np.float64)
trig = json.load(open(sys.argv[2])); sel = json.load(open(sys.argv[3])); outp = sys.argv[4]
maxY = float(sys.argv[5]) if len(sys.argv) > 5 else 3.0
n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); ln = np.linalg.norm(n, axis=1) + 1e-12; ny = n[:, 1] / ln
area = ln / 2
c = T.mean(1)
# image space: col = source x = glTF Z, row = -source y = -glTF X ; 0.5 m pixels
RES = 0.5
zmin, zmax = c[:, 2].min(), c[:, 2].max(); xmin, xmax = c[:, 0].min(), c[:, 0].max()
W = int((zmax - zmin) / RES) + 1; H = int((xmax - xmin) / RES) + 1
def cell(p):
    return np.clip(((xmax - p[..., 0]) / RES).astype(int), 0, H - 1), np.clip(((p[..., 2] - zmin) / RES).astype(int), 0, W - 1)
# splat sample points of every triangle (subdivide large ones)
def samples(tris):
    pts = [tris.mean(1), tris.reshape(-1, 3)]
    cur = tris
    for _ in range(6):
        e = np.max(np.linalg.norm(cur[:, [1, 2, 0]] - cur, axis=2), axis=1); big = cur[e > 2 * RES]
        if not len(big): break
        a, b, cc = big[:, 0], big[:, 1], big[:, 2]; ab, bc, ca = (a + b) / 2, (b + cc) / 2, (cc + a) / 2
        pts += [ab, bc, ca, (a + b + cc) / 3]
        cur = np.concatenate([np.stack([a, ab, ca], 1), np.stack([ab, b, bc], 1), np.stack([ca, bc, cc], 1), np.stack([ab, bc, ca], 1)])
    return np.concatenate(pts)
floor = T[ny > 0.7]; anyt = T
fp = samples(floor); ap = samples(anyt)
fr, fc = cell(fp); ar, ac = cell(ap)
lowfloor = np.full((H, W), np.inf); np.minimum.at(lowfloor, (fr, fc), fp[:, 1])
top = np.full((H, W), -np.inf); np.maximum.at(top, (ar, ac), ap[:, 1])
covered = (lowfloor < maxY) & (top > lowfloor + 2.5)
street = np.full((H, W), -np.inf); np.maximum.at(street, (fr, fc), fp[:, 1])
rgb = np.zeros((H, W, 3)); g = np.clip((np.where(np.isfinite(top), top, -5) + 5) / 40, 0, 1)
rgb[:] = (g * 120 + 20)[..., None]
cov = covered
t = np.clip((lowfloor - (-12)) / (maxY + 12), 0, 1)
rgb[cov] = np.stack([255 * (1 - t[cov]), 120 + 100 * t[cov], 40 + 0 * t[cov]], 1)
img = Image.fromarray(rgb.astype(np.uint8)).convert("RGBA")
ov = Image.new("RGBA", img.size, (0, 0, 0, 0)); dr = ImageDraw.Draw(ov)
def P(sx, sy): return ((sx * 0.0254 - zmin) / RES, (xmax - sy * 0.0254) / RES)
col = {"citadel_tunnel_trigger": (255, 40, 40, 255), "citadel_trigger_shop_tunnel": (40, 255, 120, 255),
       "trigger_item_shop": (255, 255, 0, 255), "citadel_passthrough_fake_wall": (255, 0, 255, 255), "citadel_trigger_interior": (80, 160, 255, 255)}
for o in trig:
    x, y, z = o["org"]; a = P(x + o["mn"][0], y + o["mx"][1]); b = P(x + o["mx"][0], y + o["mn"][1])
    dr.rectangle([min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])], outline=col[o["cls"]], width=2)
dr.line([P(*p) for p in sel] + [P(*sel[0])], fill=(255, 40, 160, 255), width=3)
img = Image.alpha_composite(img, ov)
try: f = ImageFont.truetype("arial.ttf", 16)
except OSError: f = ImageFont.load_default()
dd = ImageDraw.Draw(img); y0 = 8
for k, v in [("covered floor below %.0f m (orange = deep, green = near street)" % maxY, (255, 160, 40)), ("vent tunnel trigger", col["citadel_tunnel_trigger"][:3]),
             ("shop tunnel trigger", col["citadel_trigger_shop_tunnel"][:3]), ("shop", (255, 255, 0)), ("tiny passthrough wall", (255, 0, 255)), ("interior", (80, 160, 255))]:
    dd.text((10, y0), k, font=f, fill=v, stroke_width=2, stroke_fill=(0, 0, 0)); y0 += 20
img.save(outp); print(img.size, "covered cells", int(cov.sum()))
