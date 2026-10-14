using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// AI navigation for a Deadlock section, made from its own collision: ground nodes on every walkable floor (several
    /// levels: streets, the plaza, roofs, tunnels), air nodes above them, links tested per hull size, line of sight.
    /// Built in the stage's physics scene the first time, then cached next to the section (same fingerprint = same map).
    /// </summary>
    internal static class NavBuilder
    {
        public const int Version = 5;
        private const float GroundSpacing = 4.5f, AirSpacing = 12f;
        private const float MaxLink = 15f, MaxAirLink = 30f;
        private const float LosRange = 140f;

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);

        public class Graph
        {
            public Vector3[] Pos = Array.Empty<Vector3>();
            public byte[] Flags = Array.Empty<byte>();
            public int[] Forbidden = Array.Empty<int>();
            public readonly List<(int A, int B, float Dist, float Jump, int Hulls, int JumpHulls)> Links = new List<(int, int, float, float, int, int)>();
            public byte[][] Los = Array.Empty<byte[]>();
            /// <summary>Ground only: where players appear (open, roofless, together), facing their longest clear view.</summary>
            public readonly List<(Vector3 Pos, float Yaw)> Spawns = new List<(Vector3, float)>();
        }

        /// <summary>
        /// The ground and air graphs for the section placed under root (local space = section space). inside tells
        /// whether a point (section space) is in the playable area; noWalk whether it's somewhere nobody may stand.
        /// </summary>
        public static (NodeGraph Ground, NodeGraph Air, List<(Vector3 Pos, float Yaw)> Spawns) Get(string cacheFile, string fingerprint, Transform root, Func<Vector3, bool> inside, Func<Vector3, bool> noWalk, Bounds area)
        {
            Graph? ground = null, air = null;
            string key = fingerprint + ":nav" + Version + ":" + root.localScale.x.ToString("0.###");
            if (File.Exists(cacheFile))
                try { (ground, air) = Read(cacheFile, key); } catch (Exception e) { Log("nav cache unreadable: " + e.Message); }
            if (ground == null || air == null)
            {
                var watch = Stopwatch.StartNew();
                ground = BuildGround(root, inside, noWalk, area);
                air = BuildAir(root, inside, area, ground);
                Log($"navigation built in {watch.ElapsedMilliseconds} ms: {ground.Pos.Length} ground nodes / {ground.Links.Count} links, {air.Pos.Length} air nodes / {air.Links.Count} links");
                try { Write(cacheFile, key, ground, air); } catch (Exception e) { Log("couldn't cache navigation: " + e.Message); }
            }
            return (ToNodeGraph(ground, root), ToNodeGraph(air, root), ground.Spawns);
        }

        // ---------------------------------------------------------------- ground

        private static Graph BuildGround(Transform root, Func<Vector3, bool> inside, Func<Vector3, bool> noWalk, Bounds area)
        {
            int mask = LayerIndex.world.mask;
            float s = root.lossyScale.x, step = GroundSpacing;
            var pos = new List<Vector3>();
            var normals = new List<Vector3>();
            float top = area.max.y + 20f;
            for (float x = area.min.x; x <= area.max.x; x += step)
                for (float z = area.min.z; z <= area.max.z; z += step)
                {
                    // Every floor under this column, top down: raycasts continue below each floor (backfaces aren't hit).
                    Vector3 from = root.TransformPoint(new Vector3(x, top, z));
                    for (int guard = 0; guard < 12; guard++)
                    {
                        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, (top - area.min.y + 40f) * s, mask, QueryTriggerInteraction.Ignore)) break;
                        from = hit.point + Vector3.down * 0.05f;
                        if (hit.normal.y < 0.7f) continue;
                        Vector3 local = root.InverseTransformPoint(hit.point);
                        if (!inside(local) || noWalk(local)) continue;
                        if (Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.up, 2.2f, mask, QueryTriggerInteraction.Ignore)) continue; // no headroom
                        pos.Add(hit.point);
                        normals.Add(hit.normal);
                    }
                }
            var g = new Graph { Pos = pos.ToArray(), Flags = new byte[pos.Count], Forbidden = new int[pos.Count] };
            for (int i = 0; i < g.Pos.Length; i++)
            {
                Vector3 p = g.Pos[i];
                for (int h = 0; h < 3; h++)
                {
                    HullDef hull = HullDef.Find((HullClassification)h);
                    float r = hull.radius * 0.9f;
                    if (Physics.CheckCapsule(p + Vector3.up * (hull.radius + 0.35f), p + Vector3.up * Mathf.Max(hull.radius + 0.35f, hull.height - hull.radius), r, mask, QueryTriggerInteraction.Ignore))
                        g.Forbidden[i] |= 1 << h;
                }
                bool open = !Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, 1000f, mask, QueryTriggerInteraction.Ignore);
                if (open) g.Flags[i] |= (byte)NodeFlags.NoCeiling;
            }
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < g.Pos.Length; i++) Cell(grid, g.Pos[i], MaxLink).Add(i);
            for (int a = 0; a < g.Pos.Length; a++)
                foreach (int b in Near(grid, g.Pos[a], MaxLink))
                {
                    if (a == b) continue;
                    Vector3 pa = g.Pos[a], pb = g.Pos[b];
                    float dist = Vector3.Distance(pa, pb);
                    float dy = pb.y - pa.y;
                    if (dist > MaxLink || dy > 6f || dy < -12f) continue;
                    int hulls = 0, jumps = 0;
                    for (int h = 0; h < 3; h++)
                    {
                        if (((g.Forbidden[a] | g.Forbidden[b]) & (1 << h)) != 0) continue;
                        HullDef hull = HullDef.Find((HullClassification)h);
                        float lift = Mathf.Max(0.6f, Mathf.Abs(dy) + 0.3f);
                        Vector3 c0 = pa + Vector3.up * (hull.radius + lift), c1 = pa + Vector3.up * Mathf.Max(hull.radius + lift, hull.height - hull.radius);
                        Vector3 d = pb - pa + Vector3.up * Mathf.Max(0f, dy);
                        if (Physics.CapsuleCast(c0, c1, hull.radius * 0.8f, (pb - pa).normalized, dist, mask, QueryTriggerInteraction.Ignore)) continue;
                        if (!GroundAlong(pa, pb, mask)) continue;
                        hulls |= 1 << h;
                        if (dy > 1.2f) jumps |= 1 << h;
                    }
                    if (hulls != 0) g.Links.Add((a, b, dist, dy > 1.2f ? dy + 0.5f : 0f, hulls, jumps));
                }
            Classify(g, normals, mask);
            g.Los = LineOfSight(g.Pos, Vector3.up * 1.2f, mask);
            return g;
        }

        /// <summary>
        /// Where things may spawn, as RoR2 reads it from the node flags. Every node is measured: how open it is (12
        /// horizontal rays), whether it is under Deadlock's stair clips, how flat it is, and which walkable region it is on.
        /// <list type="bullet">
        /// <item>Isolated regions (roof scraps, ledges, statue plinths): nothing spawns there, monsters included.</item>
        /// <item>Chests and other small interactables: not on stairs, slopes or in cramped corners.</item>
        /// <item>Shrines (bigger): more room around them.</item>
        /// <item>Teleporter: roofless, Golem-sized clearance, flat 7 m around, 8 m clear every way (relaxed only if that
        /// leaves too few spots).</item>
        /// </list>
        /// Also picks the player spawns.
        /// </summary>
        private static void Classify(Graph g, List<Vector3> normals, int mask)
        {
            int n = g.Pos.Length, clipMask = LayerIndex.collideWithCharacterHullOnly.mask;
            const int rays = 12;
            const float reach = 30f;
            var minClear = new float[n];
            var avgClear = new float[n];
            var bestYaw = new float[n];
            var onClip = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 eye = g.Pos[i] + Vector3.up * 1.0f;
                float min = reach, sum = 0f, best = -1f;
                for (int k = 0; k < rays; k++)
                {
                    float yaw = k * 360f / rays;
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float d = Physics.Raycast(eye, dir, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore) ? hit.distance : reach;
                    min = Mathf.Min(min, d);
                    sum += d;
                    if (d > best) { best = d; bestYaw[i] = yaw; }
                }
                minClear[i] = min;
                avgClear[i] = sum / rays;
                // On a clip, or inside one: a capsule wholly inside a clip box touches none of its faces, so a clip face more than
                // a metre overhead (higher than any ramp over stairs) counts too: players can't get there (a chest in a prop's clip).
                onClip[i] = Physics.CheckCapsule(g.Pos[i] + Vector3.up * 0.3f, g.Pos[i] + Vector3.up * 1.7f, 0.45f, clipMask, QueryTriggerInteraction.Ignore)
                    || Physics.Raycast(g.Pos[i] + Vector3.up * 6f, Vector3.down, 5f, clipMask, QueryTriggerInteraction.Ignore);
            }

            // Walkable regions: nodes joined by human-sized links (either way).
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (var l in g.Links) if ((l.Hulls & 1) != 0) { adj[l.A].Add(l.B); adj[l.B].Add(l.A); }
            var region = new int[n];
            for (int i = 0; i < n; i++) region[i] = -1;
            var sizes = new List<int>();
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (region[i] >= 0) continue;
                int r = sizes.Count, size = 0;
                region[i] = r;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();
                    size++;
                    foreach (int b in adj[a]) if (region[b] < 0) { region[b] = r; queue.Enqueue(b); }
                }
                sizes.Add(size);
            }
            if (n == 0) return;
            int main = 0;
            for (int r = 1; r < sizes.Count; r++) if (sizes[r] > sizes[main]) main = r;

            int chests = 0, shrines = 0, tele = 0, isolated = 0;
            bool TeleOk(int i, float clear, float flatR) =>
                (g.Flags[i] & (byte)NodeFlags.NoCeiling) != 0 && (g.Forbidden[i] & 2) == 0 && !onClip[i] && region[i] == main
                && minClear[i] >= clear && normals[i].y >= 0.95f && Flat(g.Pos[i], flatR, mask);
            for (int i = 0; i < n; i++)
            {
                if (region[i] != main) // rooftops and other floors you can't walk to: nothing spawns there (fliers use the air graph)
                {
                    g.Flags[i] |= (byte)(NodeFlags.NoCharacterSpawn | NodeFlags.NoChestSpawn | NodeFlags.NoShrineSpawn);
                    isolated++;
                    continue;
                }
                bool chestOk = !onClip[i] && normals[i].y >= 0.9f && minClear[i] >= 1.0f;
                bool shrineOk = chestOk && minClear[i] >= 2.0f && avgClear[i] >= 6f;
                if (chestOk) chests++; else g.Flags[i] |= (byte)NodeFlags.NoChestSpawn;
                if (shrineOk) shrines++; else g.Flags[i] |= (byte)NodeFlags.NoShrineSpawn;
                if (TeleOk(i, 8f, 7f)) { g.Flags[i] |= (byte)NodeFlags.TeleporterOK; tele++; }
            }
            if (tele < 6)
                for (int i = 0; i < n; i++)
                    if ((g.Flags[i] & (byte)NodeFlags.TeleporterOK) == 0 && TeleOk(i, 5f, 5f))
                    {
                        g.Flags[i] |= (byte)NodeFlags.TeleporterOK;
                        tele++;
                    }

            // Player spawns: the most open roofless spot on the main region, plus up to three good neighbours, all facing
            // that spot's longest clear view.
            int bestNode = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (region[i] != main || onClip[i] || (g.Flags[i] & (byte)NodeFlags.NoCeiling) == 0 || minClear[i] < 3f) continue;
                float score = avgClear[i] + minClear[i];
                if (score > bestScore) { bestScore = score; bestNode = i; }
            }
            if (bestNode >= 0)
            {
                float yaw = bestYaw[bestNode];
                g.Spawns.Add((g.Pos[bestNode], yaw));
                var near = new List<int>();
                for (int i = 0; i < n; i++)
                    if (i != bestNode && region[i] == main && !onClip[i] && minClear[i] >= 2f && Mathf.Abs(g.Pos[i].y - g.Pos[bestNode].y) < 1.5f
                        && (g.Pos[i] - g.Pos[bestNode]).sqrMagnitude < 12f * 12f)
                        near.Add(i);
                near.Sort((a, b) => (avgClear[b] + minClear[b]).CompareTo(avgClear[a] + minClear[a]));
                for (int k = 0; k < near.Count && g.Spawns.Count < 4; k++) g.Spawns.Add((g.Pos[near[k]], yaw));
            }
            Log($"spawn rules: {sizes.Count} walkable regions (main {sizes[main]} nodes), {isolated} isolated nodes off-limits, " +
                $"{chests} chest spots, {shrines} shrine spots, {tele} teleporter spots, {g.Spawns.Count} player spawns" +
                (bestNode >= 0 ? $" ({avgClear[bestNode]:0} m open on average)" : ""));
        }

        /// <summary>A walkable floor under the whole path (no gaps an AI would fall into).</summary>
        private static bool GroundAlong(Vector3 a, Vector3 b, int mask)
        {
            const int samples = 5;
            for (int i = 1; i < samples; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)samples);
                if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, 4.5f + Mathf.Abs(a.y - b.y), mask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        private static bool Flat(Vector3 p, float r, int mask)
        {
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI / 4f;
                Vector3 q = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (!Physics.Raycast(q + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 3.5f, mask, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(hit.point.y - p.y) > 0.8f || hit.normal.y < 0.85f) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- air

        private static Graph BuildAir(Transform root, Func<Vector3, bool> inside, Bounds area, Graph ground)
        {
            int mask = LayerIndex.world.mask;
            var pos = new List<Vector3>();
            float top = area.max.y + 20f;
            for (float x = area.min.x; x <= area.max.x; x += AirSpacing)
                for (float z = area.min.z; z <= area.max.z; z += AirSpacing)
                {
                    Vector3 local = new Vector3(x, 0f, z);
                    if (!inside(local)) continue;
                    Vector3 from = root.TransformPoint(new Vector3(x, top, z));
                    if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 2000f, mask, QueryTriggerInteraction.Ignore)) continue;
                    foreach (float up in new[] { 7f, 18f, 32f })
                    {
                        Vector3 p = hit.point + Vector3.up * up;
                        if (!Physics.CheckSphere(p, 2.5f, mask, QueryTriggerInteraction.Ignore)) pos.Add(p);
                    }
                }
            var g = new Graph { Pos = pos.ToArray(), Flags = new byte[pos.Count], Forbidden = new int[pos.Count] };
            for (int i = 0; i < g.Pos.Length; i++)
            {
                g.Forbidden[i] = 4; // no BeetleQueen-sized fliers
                if (Physics.CheckSphere(g.Pos[i], HullDef.Find(HullClassification.Golem).radius + 0.5f, mask, QueryTriggerInteraction.Ignore)) g.Forbidden[i] |= 2;
                if (!Physics.Raycast(g.Pos[i], Vector3.up, 1000f, mask, QueryTriggerInteraction.Ignore)) g.Flags[i] |= (byte)NodeFlags.NoCeiling;
            }
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < g.Pos.Length; i++) Cell(grid, g.Pos[i], MaxAirLink).Add(i);
            for (int a = 0; a < g.Pos.Length; a++)
                foreach (int b in Near(grid, g.Pos[a], MaxAirLink))
                {
                    if (a == b) continue;
                    float dist = Vector3.Distance(g.Pos[a], g.Pos[b]);
                    if (dist > MaxAirLink) continue;
                    int hulls = 0;
                    for (int h = 0; h < 2; h++)
                    {
                        if (((g.Forbidden[a] | g.Forbidden[b]) & (1 << h)) != 0) continue;
                        if (!Physics.SphereCast(g.Pos[a], HullDef.Find((HullClassification)h).radius, (g.Pos[b] - g.Pos[a]).normalized, out _, dist, mask, QueryTriggerInteraction.Ignore))
                            hulls |= 1 << h;
                    }
                    if (hulls != 0) g.Links.Add((a, b, dist, 0f, hulls, 0));
                }
            g.Los = LineOfSight(g.Pos, Vector3.zero, mask);
            return g;
        }

        // ---------------------------------------------------------------- shared

        private static List<int> Cell(Dictionary<(int, int), List<int>> grid, Vector3 p, float size)
        {
            var k = (Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.z / size));
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
            return l;
        }

        private static IEnumerable<int> Near(Dictionary<(int, int), List<int>> grid, Vector3 p, float size)
        {
            int cx = Mathf.FloorToInt(p.x / size), cz = Mathf.FloorToInt(p.z / size);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue((cx + dx, cz + dz), out var l)) foreach (int i in l) yield return i;
        }

        private static byte[][] LineOfSight(Vector3[] pos, Vector3 eye, int mask)
        {
            int n = pos.Length;
            var bits = new byte[n][];
            for (int i = 0; i < n; i++) bits[i] = new byte[(n + 7) / 8];
            float r2 = LosRange * LosRange;
            for (int i = 0; i < n; i++)
            {
                bits[i][i >> 3] |= (byte)(1 << (i & 7));
                for (int j = i + 1; j < n; j++)
                {
                    if ((pos[i] - pos[j]).sqrMagnitude > r2) continue;
                    if (Physics.Linecast(pos[i] + eye, pos[j] + eye, mask, QueryTriggerInteraction.Ignore)) continue;
                    bits[i][j >> 3] |= (byte)(1 << (j & 7));
                    bits[j][i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            return bits;
        }

        /// <summary>A NodeGraph from the data, through RoR2's own SetNodes (temporary MapNodes, destroyed after).</summary>
        private static NodeGraph ToNodeGraph(Graph g, Transform root)
        {
            var holder = new GameObject("NavBuild");
            var nodes = new List<MapNode>(g.Pos.Length);
            for (int i = 0; i < g.Pos.Length; i++)
            {
                var go = new GameObject("n");
                go.transform.SetParent(holder.transform, false);
                go.transform.position = g.Pos[i];
                MapNode m = go.AddComponent<MapNode>();
                m.flags = (NodeFlags)g.Flags[i];
                m.forbiddenHulls = (HullMask)g.Forbidden[i];
                nodes.Add(m);
            }
            foreach (var l in g.Links)
                nodes[l.A].links.Add(new MapNode.Link { nodeB = nodes[l.B], distanceScore = l.Dist, minJumpHeight = l.Jump, hullMask = l.Hulls, jumpHullMask = l.JumpHulls, gateName = "" });
            var masks = new List<SerializableBitArray>(g.Pos.Length);
            for (int i = 0; i < g.Pos.Length; i++)
            {
                var b = new SerializableBitArray(g.Pos.Length);
                for (int j = 0; j < g.Pos.Length; j++) b[j] = (g.Los[i][j >> 3] & (1 << (j & 7))) != 0;
                masks.Add(b);
            }
            NodeGraph graph = ScriptableObject.CreateInstance<NodeGraph>();
            graph.SetNodes(new ReadOnlyCollection<MapNode>(nodes), new ReadOnlyCollection<SerializableBitArray>(masks));
            UnityEngine.Object.DestroyImmediate(holder);
            return graph;
        }

        // ---------------------------------------------------------------- cache (positions in world space of the built stage)

        private static void Write(string file, string key, Graph ground, Graph air)
        {
            using var bw = new BinaryWriter(File.Create(file + ".part"));
            bw.Write(key);
            foreach (Graph g in new[] { ground, air })
            {
                bw.Write(g.Pos.Length);
                for (int i = 0; i < g.Pos.Length; i++) { bw.Write(g.Pos[i].x); bw.Write(g.Pos[i].y); bw.Write(g.Pos[i].z); bw.Write(g.Flags[i]); bw.Write(g.Forbidden[i]); bw.Write(g.Los[i]); }
                bw.Write(g.Links.Count);
                foreach (var l in g.Links) { bw.Write(l.A); bw.Write(l.B); bw.Write(l.Dist); bw.Write(l.Jump); bw.Write(l.Hulls); bw.Write(l.JumpHulls); }
            }
            bw.Write(ground.Spawns.Count);
            foreach (var (p, yaw) in ground.Spawns) { bw.Write(p.x); bw.Write(p.y); bw.Write(p.z); bw.Write(yaw); }
            bw.Close();
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".part", file);
        }

        private static (Graph?, Graph?) Read(string file, string key)
        {
            using var br = new BinaryReader(File.OpenRead(file));
            if (br.ReadString() != key) return (null, null);
            var o = new Graph[2];
            for (int k = 0; k < 2; k++)
            {
                int n = br.ReadInt32();
                var g = new Graph { Pos = new Vector3[n], Flags = new byte[n], Forbidden = new int[n], Los = new byte[n][] };
                for (int i = 0; i < n; i++)
                {
                    g.Pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    g.Flags[i] = br.ReadByte(); g.Forbidden[i] = br.ReadInt32(); g.Los[i] = br.ReadBytes((n + 7) / 8);
                }
                int links = br.ReadInt32();
                for (int i = 0; i < links; i++) g.Links.Add((br.ReadInt32(), br.ReadInt32(), br.ReadSingle(), br.ReadSingle(), br.ReadInt32(), br.ReadInt32()));
                o[k] = g;
            }
            int spawns = br.ReadInt32();
            for (int i = 0; i < spawns; i++) o[0].Spawns.Add((new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()), br.ReadSingle()));
            return (o[0], o[1]);
        }
    }
}
