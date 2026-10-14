"""A hand-drawn green outline (drawn on a copy of an overview_minimap.py render) -> a new outline in Source units.
Registers the drawing to the render via its pink outline (ICP, per-axis scale + offset), fills the green loop, traces it.
Usage: trace_outline.py <drawing.png> <current outline.json> <out.json> <scan dir> <crop x0,x1,y0,y1> <scale>
(the crop and scale the render was made with: CROP and SCALE of overview_minimap.py). Smooth the result afterwards, e.g.
Polygon(p).buffer(-160, join_style=2).buffer(160, join_style=2).simplify(45), and check it on a fresh render."""
import json, sys
import numpy as np
from PIL import Image
from scipy import ndimage
from shapely.geometry import Polygon, Point, box
from shapely.ops import unary_union, nearest_points

theirs_png, outline_json, out_json, scan_dir = sys.argv[1:5]
CX0, CX1, CY0, CY1 = map(float, sys.argv[5].split(","))
s = json.load(open(scan_dir + "/scan.json")); top = np.load(scan_dir + "/top.npy")
res, X0, Z0 = s["res"], s["x0"], s["z0"]
N = top.T.shape[0]
SC = float(sys.argv[6])
def px(sx, sy): return ((sx * 0.0254 - Z0) / res, N - 1 - (sy * 0.0254 - X0) / res)
MINX = int(min(px(CX0, 0)[0], px(CX1, 0)[0])); MINY = int(min(px(0, CY1)[1], px(0, CY0)[1]))
def mine(sx, sy): c, r = px(sx, sy); return ((c - MINX) * SC, (r - MINY) * SC)
def source(u, v):
    c, r = u / SC + MINX, v / SC + MINY
    return ((c * res + Z0) / 0.0254, ((N - 1 - r) * res + X0) / 0.0254)

poly_src = json.load(open(outline_json))
my_poly = Polygon([mine(x, y) for x, y in poly_src])
ring = my_poly.exterior

im = np.asarray(Image.open(theirs_png).convert("RGB")).astype(int)
R, G, B = im[..., 0], im[..., 1], im[..., 2]
pink = (R > 190) & (G < 110) & (B > 90) & (B < 220)
green = (G > 70) & (G > R + 40) & (G > B + 30) & (R < 90)
pv, pu = np.nonzero(pink)
rng = np.random.default_rng(1)
pick = rng.choice(len(pu), min(800, len(pu)), replace=False)
P = np.stack([pu[pick], pv[pick]], 1).astype(float)

a, b, c, d = 825 / 760, 0.0, 670 / 595, 0.0
for it in range(40):
    T = np.stack([P[:, 0] * a + b, P[:, 1] * c + d], 1)
    Q = np.array([ring.interpolate(ring.project(Point(t))).coords[0] for t in T])
    dist = np.hypot(*(T - Q).T)
    keep = dist < np.percentile(dist, 80)
    a, b = np.polyfit(P[keep, 0], Q[keep, 0], 1)
    c, d = np.polyfit(P[keep, 1], Q[keep, 1], 1)
print(f"fit x={a:.4f}u+{b:.1f} y={c:.4f}v+{d:.1f}, median residual {np.median(dist):.2f} px", file=sys.stderr)

g = ndimage.binary_dilation(green, iterations=3)
H, W = g.shape
g[:, :2] |= g[:, 2:4]  # the loop runs along the image's left edge: close it there
inside = ndimage.binary_fill_holes(g)
lab, n = ndimage.label(inside)
sizes = ndimage.sum(inside, lab, range(1, n + 1))
inside = lab == (1 + int(np.argmax(sizes)))
inside = ndimage.binary_erosion(inside, iterations=3)  # back to the line's middle
print(f"green area {inside.sum()} px of {H * W}", file=sys.stderr)
cells = []
for v in range(0, H, 2):
    row = inside[v]
    u = 0
    while u < W:
        if row[u]:
            u0 = u
            while u < W and row[u]: u += 1
            cells.append(box(u0, v, u, v + 2))
        else: u += 1
area = unary_union(cells)
if area.geom_type != "Polygon": area = max(area.geoms, key=lambda q: q.area)
area = Polygon(area.exterior).simplify(2.5)
pts = [source(u * a + b, v * c + d) for u, v in area.exterior.coords]
newp = Polygon(pts)
print(f"{len(pts)} vertices, area {newp.area * 0.0254 ** 2:.0f} m^2 (was {Polygon(poly_src).area * 0.0254 ** 2:.0f})", file=sys.stderr)
json.dump([[round(x), round(y)] for x, y in pts], open(out_json, "w"))
