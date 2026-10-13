"""Top-down overview PNG of the scanned map with landmark markers and a 100 m grid.
Usage: overview.py <scan dir> <ents.json> <out.png> [x0 x1 z0 z1 box to outline]"""
import json, sys
import numpy as np
from PIL import Image, ImageDraw

scandir, entsf, outp = sys.argv[1:4]
box = list(map(float, sys.argv[4:8])) if len(sys.argv) >= 8 else None
s = json.load(open(scandir + "/scan.json")); top = np.load(scandir + "/top.npy")
res, X0, Z0 = s["res"], s["x0"], s["z0"]
valid = top > -1e8
h = np.where(valid, top, np.nan)
lo, hi = np.nanpercentile(h, 2), np.nanpercentile(h, 98)
gz, gx = np.gradient(np.nan_to_num(h, nan=lo))
shade = np.clip(0.75 + 0.25 * (gx - gz) / 1.5, 0.35, 1.2)
t = np.clip((np.nan_to_num(h, nan=lo) - lo) / (hi - lo), 0, 1)
ramp = np.array([[40, 60, 90], [70, 120, 110], [180, 170, 110], [220, 120, 80], [245, 240, 230]], float)
k = t * (len(ramp) - 1); i = np.minimum(k.astype(int), len(ramp) - 2); f = (k - i)[..., None]
rgb = (ramp[i] * (1 - f) + ramp[i + 1] * f) * shade[..., None]
rgb[~valid] = [18, 18, 22]
# image: +X right, +Z down would mirror the in-game view; flip so source +y is up
img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)[::-1])  # row 0 = max Z
H, W = top.shape
d = ImageDraw.Draw(img)
def px(x, z): return ((x - X0) / res, H - 1 - (z - Z0) / res)
for g in range(-900, 901, 100):
    a = px(g, -950); b = px(g, 950); d.line([a, b], fill=(255, 255, 255, 40), width=1)
    a = px(-950, g); b = px(950, g); d.line([a, b], fill=(255, 255, 255, 40), width=1)
ents = json.load(open(entsf))
style = {"npc_boss_tier2": ((255, 60, 60), "walker"), "npc_barrack_boss": ((255, 160, 0), "barracks"),
         "info_team_spawn": ((80, 200, 255), "spawn"), "destroyable_building": ((255, 255, 0), "base"),
         "citadel_zipline_path": ((200, 120, 255), None), "info_neutral_trooper_camp": ((120, 255, 120), None)}
for e in ents:
    st = style.get(e.get("classname"))
    if not st or "origin" not in e: continue
    try: x, y, z = map(float, e["origin"].strip("[] ").split(","))
    except ValueError: continue
    gx_, gz_ = y * 0.0254, x * 0.0254
    p = px(gx_, gz_); r = 6 if st[1] else 3
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], outline=st[0], width=2)
    if st[1]: d.text((p[0] + 8, p[1] - 6), st[1], fill=st[0])
if box:
    a = px(box[0], box[3]); b = px(box[1], box[2]); d.rectangle([a, b], outline=(0, 255, 255), width=3)
img.save(outp)
print("saved", img.size, "height range", round(lo, 1), round(hi, 1))
