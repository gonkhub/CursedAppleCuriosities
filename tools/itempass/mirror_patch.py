# Replaces just the mirrored item rules (Commando displays with a negative scale) in a hero's ItemDisplays.cs with the
# same rules from a freshly emitted file, keeping every hand placement and spot fix. Before 2026-10-12 decompose()
# dropped the mirror, so these came out unmirrored (both Ifrit horns curling the same way).
#   python mirror_patch.py <hero ItemDisplays.cs> <freshly emitted .cs>
import json, re, sys
from rig import D
mirrored = {(g["key"], i) for g in json.load(open(D + "/rules.json")) for i, r in enumerate(g["rules"]) if any(v < 0 for v in r.get("scale", []))}
pat = re.compile(r'new HeroItemRule\("(\w+)", (\d+),')
fresh = {}
for line in open(sys.argv[2], encoding="utf-8").read().splitlines():
    m = pat.search(line)
    if m and (m[1], int(m[2])) in mirrored: fresh[(m[1], int(m[2]))] = line
raw = open(sys.argv[1], "rb").read().decode("utf-8"); nl = "\r\n" if "\r\n" in raw else "\n"
out, n = [], 0
for line in raw.split(nl):
    m = pat.search(line)
    if m and (m[1], int(m[2])) in fresh and "// yours" not in line:
        line = fresh[(m[1], int(m[2]))]; n += 1
    out.append(line)
open(sys.argv[1], "wb").write(nl.join(out).encode("utf-8"))
print(f"{n} mirrored rules replaced ({len(mirrored)} mirrored in Commando's table)")
