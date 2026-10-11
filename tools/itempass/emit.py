# Writes AbramsItemDisplays.cs from the fit: the anchor table (ChildLocator point -> Abrams bone + local pose) and
# every rule override (by group key and rule index, checked against Commando's original attach point).
import json, sys, os
from rig import D
from rig import HERO
fit = json.load(open(D + (f"/{HERO}_fit_final.json" if os.path.exists(D + f"/{HERO}_fit_final.json") else f"/{HERO}_fit.json")))
NAME = HERO.capitalize()
orig = {g["key"]: g for g in json.load(open(D + "/rules.json"))}
OUT = sys.argv[1]
def f(x): s = f"{x:.5f}".rstrip("0").rstrip("."); return (s if s not in ("-0", "") else "0") + "F"
def v3(a): return f"new Vector3({f(a[0])}, {f(a[1])}, {f(a[2])})"
lines = []
for name, a in sorted(fit["anchors"].items()):
    lines.append(f'            ["{name}"] = ("{a["bone"]}", {v3(a["pos"])}, {v3(a["ang"])}, {v3(a["scale"])}),')
rules = []
for g in fit["groups"]:
    for r in g["rules"]:
        if r.get("note", "").startswith("as Commando") or "pos" not in r: continue
        src = orig[g["key"]]["rules"][r["index"]]["child"]
        tag = "  // yours" if r.get("note") == "user placement" else ""
        rules.append(f'            new HeroItemRule("{g["key"]}", {r["index"]}, "{src}", "{r["child"]}", {v3(r["pos"])}, {v3(r["ang"])}, {v3(r["scale"])}),{tag}')
src = f'''using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.{NAME}
{{
    /// <summary>
    /// {NAME}'s item displays: every vanilla item and equipment fitted to the hero's body (tools in the item-pass scripts:
    /// Commando's placements carried region-to-region onto the hero's rest-pose mesh, then snapped to his surface the way
    /// they sit on Commando's), plus the hand placements marked "yours". Values are relative to <see cref="Anchors"/>.
    /// Generated: edit freely (ItemDisplayPlacementHelper copies values in this format), or regenerate the lot.
    /// </summary>
    public static class {NAME}ItemDisplays
    {{
        public static void Register() =>
            HeroItemDisplays.Register("{NAME}", () => new[] {{ {NAME}Survivor.BodyPrefab, {NAME}Survivor.DisplayPrefab }}, Rules);

        /// <summary>
        /// Where each of Commando's attach points sits on {NAME}: ({NAME}'s bone, local position, angles, scale on it).
        /// Fixed, so a placement looks the same every spawn and in the placement helper as in a run.
        /// </summary>
        public static readonly Dictionary<string, (string Bone, Vector3 Pos, Vector3 Angles, Vector3 Scale)> Anchors =
            new Dictionary<string, (string, Vector3, Vector3, Vector3)>
        {{
{chr(10).join(lines)}
        }};

        // (item or equipment, rule index in Commando's group, Commando's attach point, attach point, position, angles, scale)
        private static readonly HeroItemRule[] Rules =
        {{
{chr(10).join(rules)}
        }};
    }}
}}
'''
open(OUT, "w", encoding="utf-8").write(src)
print(len(lines), "anchors,", len(rules), "rules")
