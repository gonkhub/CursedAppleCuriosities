"""Minimap-oriented overview (Hidden King bottom, Archmother top): lanes, landmarks, optional selection polygon.
Usage: overview2.py <scan dir> <entsfull.json> <out.png> [poly json: [[srcx,srcy],...]]"""
import json, sys, collections, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

scandir, entsf, outp = sys.argv[1:4]
poly = json.loads(sys.argv[4]) if len(sys.argv) > 4 else None
s = json.load(open(scandir + "/scan.json")); top = np.load(scandir + "/top.npy")
res, X0, Z0 = s["res"], s["x0"], s["z0"]          # top[zi, xi]: zi ~ source x, xi ~ source y
img_h = top.T[::-1]                                # rows: source y descending, cols: source x
valid = img_h > -1e8
h = np.where(valid, img_h, np.nan)
lo, hi = (float(v) for v in os.environ.get("HRANGE", "nan,nan").split(","))
if lo != lo: lo, hi = np.nanpercentile(h, 2), np.nanpercentile(h, 90)
hh = np.nan_to_num(h, nan=lo)
gy, gx = np.gradient(hh)
shade = np.clip(0.8 + 0.35 * (gx - gy) / 2.0, 0.35, 1.25)
t = np.clip((hh - lo) / (hi - lo), 0, 1)
ramp = np.array([[10, 30, 90], [30, 110, 160], [90, 150, 90], [190, 170, 110], [235, 225, 210], [255, 255, 255]], float)
k = t * (len(ramp) - 1); i = np.minimum(k.astype(int), len(ramp) - 2); f = (k - i)[..., None]
rgb = (ramp[i] * (1 - f) + ramp[i + 1] * f) * shade[..., None]
rgb[~valid] = [14, 14, 18]
img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).convert("RGBA")
N = img_h.shape[0]
def px(sx, sy):  # source units -> pixel
    gx_, gz_ = sy * 0.0254, sx * 0.0254           # glTF X = source y, glTF Z = source x
    return ((gz_ - Z0) / res, N - 1 - (gx_ - X0) / res)
over = Image.new("RGBA", img.size, (0, 0, 0, 0)); d = ImageDraw.Draw(over)
try: font = ImageFont.truetype("arial.ttf", 18); small = ImageFont.truetype("arial.ttf", 14)
except OSError: font = small = ImageFont.load_default()
for g in range(-900, 901, 100):   # 100 m grid
    a, b = px(g / 0.0254, -950 / 0.0254), px(g / 0.0254, 950 / 0.0254); d.line([a, b], fill=(255, 255, 255, 28))
    a, b = px(-950 / 0.0254, g / 0.0254), px(950 / 0.0254, g / 0.0254); d.line([a, b], fill=(255, 255, 255, 28))
E = json.load(open(entsf))
def org(e): return list(map(float, e["origin"].strip("[] ").split(",")))
LANE = {"1": (255, 210, 40), "4": (60, 140, 255), "6": (190, 90, 255)}
paths = {e["hammerUniqueId"]: e.get("lane_number") for e in E if e.get("classname") == "citadel_zipline_path"}
nodes = collections.defaultdict(list)
for e in E:
    if e.get("classname") == "citadel_zipline_path_node":
        nodes[e["path_uniqueid"]].append((int(e["path_Index"]), org(e)))
for pid, ns in nodes.items():
    col = LANE.get(paths.get(pid), (200, 200, 200))
    pts = [px(o[0], o[1]) for _, o in sorted(ns)]
    d.line(pts, fill=col + (230,), width=5)
for e in E:
    c = e.get("classname"); lane = e.get("LaneNum")
    if c == "npc_boss_tier2":
        p = px(*org(e)[:2]); d.ellipse([p[0]-9, p[1]-9, p[0]+9, p[1]+9], fill=LANE.get(lane, (255,255,255)) + (255,), outline=(0,0,0,255), width=2)
        d.text((p[0]+12, p[1]-9), "Walker", font=small, fill=(255,255,255,255))
    elif c == "npc_barrack_boss":
        p = px(*org(e)[:2]); d.rectangle([p[0]-6, p[1]-6, p[0]+6, p[1]+6], fill=LANE.get(lane, (255,255,255)) + (255,), outline=(0,0,0,255))
    elif c == "info_neutral_trooper_camp":
        p = px(*org(e)[:2]); d.ellipse([p[0]-3, p[1]-3, p[0]+3, p[1]+3], fill=(120, 255, 140, 200))
for name, (sx, sy) in {"ARCHMOTHER BASE": (1280, 10600), "HIDDEN KING BASE": (-1280, -10600)}.items():
    p = px(sx, sy); d.text((p[0], p[1]), name, font=font, fill=(255, 255, 255, 255), anchor="mm")
if poly:
    sel = Image.new("RGBA", img.size, (0, 0, 0, 0)); ds = ImageDraw.Draw(sel)
    pts = [px(x, y) for x, y in poly]
    ds.polygon(pts, fill=(255, 40, 120, 60)); ds.line(pts + [pts[0]], fill=(255, 40, 160, 255), width=4)
    img = Image.alpha_composite(img, sel)
img = Image.alpha_composite(img, over)
# crop (source units: CROP=x0,x1,y0,y1 env) and scale
import os
cx0, cx1, cy0, cy1 = map(float, os.environ.get("CROP", "-15000,15000,-14000,14000").split(","))
xs = [px(cx0, 0)[0], px(cx1, 0)[0]]; ys = [px(0, cy1)[1], px(0, cy0)[1]]
img = img.crop((int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))))
sc = float(os.environ.get("SCALE", "1")); img = img.resize((int(img.width * sc), int(img.height * sc)), Image.LANCZOS)
dd = ImageDraw.Draw(img)
bar = 50 / res * sc; dd.rectangle([20, img.height - 30, 20 + bar, img.height - 22], fill=(255, 255, 255, 255), outline=(0, 0, 0, 255))
dd.text((20 + bar / 2, img.height - 42), "50 m", font=small, fill=(255, 255, 255, 255), anchor="mm", stroke_width=2, stroke_fill=(0, 0, 0, 255))
for label, (lx, ly) in json.loads(os.environ.get("LABELS", "{}")).items():
    q = px(lx, ly); q = ((q[0] - min(xs)) * sc, (q[1] - min(ys)) * sc)
    dd.text(q, label, font=font, fill=(255, 255, 255, 255), anchor="mm", stroke_width=3, stroke_fill=(0, 0, 0, 255))
img.save(outp); print("saved", img.size)
