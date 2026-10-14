using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// Scouting tool: stand somewhere in a Deadlock stage, type <c>cac_map_mark &lt;label&gt;</c>, and the spot (and the way you
    /// face) is written to markers.txt next to the section's cache, in the section's own space, so it can go straight into
    /// the stage's definition. A pillar shows each mark until the stage unloads.
    /// </summary>
    internal static class StageMarkers
    {
        private static readonly List<GameObject> Shown = new List<GameObject>();

        /// <summary>The current district's marks (each district keeps its own, next to its converted cache).</summary>
        private static string File_ => System.IO.Path.Combine(DeadlockMaps.Current?.Converter?.Folder ?? Application.persistentDataPath, "markers.txt");

        private static Transform? World()
        {
            GameObject? w = GameObject.Find("DeadlockMap");
            return w != null ? w.transform : null;
        }

        [ConCommand(commandName = "cac_map_mark", flags = ConVarFlags.None,
            helpText = "Cursed Apple: mark where you stand in a Deadlock stage (and the way you face) with a label, e.g. cac_map_mark newt.")]
        private static void CCMark(ConCommandArgs args)
        {
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            Transform? world = World();
            if (body == null || world == null) throw new ConCommandException("Needs your body, in a Deadlock stage.");
            string label = args.Count > 0 ? string.Join(" ", args.userArgs) : "mark";
            Vector3 feet = body.footPosition;
            Vector3 aim = body.inputBank != null ? body.inputBank.aimDirection : body.transform.forward;
            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            Vector3 local = world.InverseTransformPoint(feet);
            float localYaw = yaw - world.eulerAngles.y;
            string line = string.Format(CultureInfo.InvariantCulture, "{0}\t{1:0.00}\t{2:0.00}\t{3:0.00}\t{4:0.0}\t{5:yyyy-MM-dd HH:mm:ss}",
                label, local.x, local.y, local.z, localYaw, DateTime.Now);
            File.AppendAllText(File_, line + Environment.NewLine);
            Show(feet, label);
            Chat.AddMessage($"<color=#ff7cff>Marked \"{label}\" at {local.x:0.0}, {local.y:0.0}, {local.z:0.0} (facing {localYaw:0}°). {Count()} marks so far.</color>");
            Debug.Log("[DeadlockMap] mark " + line);
        }

        [ConCommand(commandName = "cac_map_unmark", flags = ConVarFlags.None, helpText = "Cursed Apple: remove your last cac_map_mark.")]
        private static void CCUnmark(ConCommandArgs args)
        {
            if (!File.Exists(File_)) throw new ConCommandException("No marks yet.");
            var lines = new List<string>(File.ReadAllLines(File_));
            if (lines.Count == 0) throw new ConCommandException("No marks yet.");
            string last = lines[lines.Count - 1];
            lines.RemoveAt(lines.Count - 1);
            File.WriteAllLines(File_, lines.ToArray());
            if (Shown.Count > 0) { GameObject g = Shown[Shown.Count - 1]; Shown.RemoveAt(Shown.Count - 1); if (g != null) UnityEngine.Object.Destroy(g); }
            Chat.AddMessage($"<color=#ff7cff>Removed the last mark ({last.Split('\t')[0]}). {lines.Count} marks left.</color>");
        }

        [ConCommand(commandName = "cac_map_marks", flags = ConVarFlags.None, helpText = "Cursed Apple: list your cac_map_mark marks (and show them in this stage).")]
        private static void CCMarks(ConCommandArgs args)
        {
            if (!File.Exists(File_)) { Debug.Log("[DeadlockMap] no marks yet"); return; }
            Transform? world = World();
            foreach (string l in File.ReadAllLines(File_))
            {
                Debug.Log("[DeadlockMap] mark " + l);
                string[] p = l.Split('\t');
                if (world != null && p.Length >= 4 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    Show(world.TransformPoint(new Vector3(x, y, z)), p[0]);
            }
        }

        [ConCommand(commandName = "cac_map_goto", flags = ConVarFlags.None,
            helpText = "Cursed Apple: jump to a spot in the current Deadlock district: arrival (or plaza: where players arrive), statue, shop1, shop2, ..., subway (subwayin: into its doorway), rope1, rope2, ... (onto a rope's geyser), or x y z (section space, as cac_map_mark writes it).")]
        private static void CCGoto(ConCommandArgs args)
        {
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            Transform? world = World();
            District? d = DeadlockMaps.Current;
            MapBuilder.Built? t = d != null ? DeadlockMaps.Template(d) : null;
            if (body == null || world == null || d == null || t == null) throw new ConCommandException("Needs your body, in a Deadlock stage.");
            string where = args.Count > 0 ? args.GetArgString(0).ToLowerInvariant() : "arrival";
            MapSectionDef def = d.Def;
            Vector3 local;
            int[]? box = null;
            if (where.StartsWith("shop") && int.TryParse(where.Substring(4), out int n) && n >= 1 && n * 6 <= def.Shops.Length) box = def.Shops[((n - 1) * 6)..(n * 6)];
            else if (where.StartsWith("subway") && def.SubwayTeleports.Length >= 6) box = def.SubwayTeleports[0..6];
            else if (where.StartsWith("rope") && int.TryParse(where.Substring(4), out int r) && r >= 1 && r * 6 <= def.Ropes.Length)
            {
                int[] rb = def.Ropes[((r - 1) * 6)..(r * 6)];
                rb[1] = rb[4] = (rb[1] + rb[4]) / 2 + 200; // centred, then backed off as for the other boxes: onto its geyser
                rb[5] = rb[2] + 120;                       // at its foot, not its top
                box = rb;
            }
            if (args.Count >= 3 && float.TryParse(args.GetArgString(0), NumberStyles.Float, CultureInfo.InvariantCulture, out float gx)
                && float.TryParse(args.GetArgString(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float gy)
                && float.TryParse(args.GetArgString(2), NumberStyles.Float, CultureInfo.InvariantCulture, out float gz))
                local = new Vector3(gx, gy, gz); // section space, as cac_map_mark writes it
            else if (box != null)
                local =MapCache.ToUnity(new Vector3((box[0] + box[3]) / 2f, (box[1] + box[4]) / 2f - (where == "subwayin" ? 0f : 200f), box[5]), t.OriginUnits); // just outside it (subwayin: in the doorway)
            else if (where == "statue" && t.HasHidden)
                local = t.Hidden.center + new Vector3(0f, -t.Hidden.extents.y, 0f) + (d.ArrivalLocal(t.OriginUnits) - t.Hidden.center).normalized * 12f;
            else local = d.ArrivalLocal(t.OriginUnits);
            Vector3 from = world.TransformPoint(local) + Vector3.up * 3f;
            Vector3 to = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f, LayerIndex.world.mask) ? hit.point + Vector3.up * 0.5f : from;
            TeleportHelper.TeleportBody(body, to);
        }

        private static int Count() => File.Exists(File_) ? File.ReadAllLines(File_).Length : 0;

        /// <summary>A thin glowing pillar at the spot (no collision), in the current scene.</summary>
        private static void Show(Vector3 at, string label)
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(pillar.GetComponent<Collider>());
            pillar.name = "Mark " + label;
            pillar.transform.position = at + Vector3.up * 2f;
            pillar.transform.localScale = new Vector3(0.35f, 2f, 0.35f);
            Material? m = DeadlockStages.GlassMaterial != null ? new Material(DeadlockStages.GlassMaterial) : null;
            if (m != null)
            {
                m.color = new Color(1f, 0.3f, 1f, 0.85f);
                pillar.GetComponent<Renderer>().sharedMaterial = m;
            }
            SceneManager.MoveGameObjectToScene(pillar, SceneManager.GetActiveScene());
            Shown.Add(pillar);
        }
    }
}
