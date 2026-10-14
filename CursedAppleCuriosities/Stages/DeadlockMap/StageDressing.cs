using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// The pieces of a Deadlock stage that aren't Deadlock's: the barrier along the outline (seen only up close), solid
    /// seals on the vent openings, a kill floor that returns fallers to the map, and the sun.
    /// </summary>
    internal static class StageDressing
    {
        private const float BarrierBelow = 40f, BarrierAbove = 120f; // metres around the playable area's height range

        public static void Build(Scene scene, Transform world, SectionSpace section, Bounds playable)
        {
            var root = new GameObject("StageDressing");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(world, false);
            Barrier(root.transform, section, playable);
            Seals(root.transform, section);
            SubwayDoors(root.transform, section);
            KillFloor(root.transform, playable);
            Sun(scene);
        }

        /// <summary>Walls straight up the outline, solid for characters and shots, drawn only near the local player.</summary>
        private static void Barrier(Transform parent, SectionSpace section, Bounds playable)
        {
            Vector2[] o = section.OutlineUnits;
            float y0 = playable.min.y - BarrierBelow, y1 = playable.max.y + BarrierAbove;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < o.Length; i++)
            {
                Vector3 a = section.ToSection(new Vector3(o[i].x, o[i].y, 0f)), b = section.ToSection(new Vector3(o[(i + 1) % o.Length].x, o[(i + 1) % o.Length].y, 0f));
                int k = v.Count;
                v.Add(new Vector3(a.x, y0, a.z)); v.Add(new Vector3(b.x, y0, b.z)); v.Add(new Vector3(b.x, y1, b.z)); v.Add(new Vector3(a.x, y1, a.z));
                t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3, k, k + 2, k + 1, k, k + 3, k + 2 }); // both faces
            }
            var mesh = new Mesh { name = "barrier" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Barrier") { layer = LayerIndex.world.intVal };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (SunkenPlazaStage.BarrierMaterial != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = SunkenPlazaStage.BarrierMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.AddComponent<BarrierProximity>();
            }
        }

        /// <summary>Deadlock's vent entrances (only tiny heroes fit there): solid boxes, so nobody walks into the tunnels.</summary>
        private static void Seals(Transform parent, SectionSpace section)
        {
            int[] b = MapSectionDef.SunkenPlaza.Seal;
            for (int i = 0; i + 5 < b.Length; i += 6)
            {
                Vector3 p0 = section.ToSection(new Vector3(b[i] - 24, b[i + 1] - 24, b[i + 2] - 24));
                Vector3 p1 = section.ToSection(new Vector3(b[i + 3] + 24, b[i + 4] + 24, b[i + 5] + 24));
                var go = new GameObject("VentSeal") { layer = LayerIndex.world.intVal };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = (p0 + p1) / 2f;
                go.AddComponent<BoxCollider>().size = new Vector3(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p1.y - p0.y), Mathf.Abs(p1.z - p0.z));
            }
        }

        /// <summary>Deadlock's subway "TELEPORTER" doorways: whoever walks in comes out at the stage's teleporter.</summary>
        private static void SubwayDoors(Transform parent, SectionSpace section)
        {
            int[] b = MapSectionDef.SunkenPlaza.SubwayTeleports;
            for (int i = 0; i + 5 < b.Length; i += 6)
            {
                Vector3 p0 = section.ToSection(new Vector3(b[i] - 24, b[i + 1] - 24, b[i + 2]));
                Vector3 p1 = section.ToSection(new Vector3(b[i + 3] + 24, b[i + 4] + 24, b[i + 5]));
                var go = new GameObject("SubwayDoor");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = (p0 + p1) / 2f;
                var door = go.AddComponent<SubwayDoor>();
                door.HalfSize = new Vector3(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p1.y - p0.y), Mathf.Abs(p1.z - p0.z)) / 2f;
            }
        }

        /// <summary>A trigger well below the map: anyone falling in is put back on the ground, as in vanilla stages.</summary>
        private static void KillFloor(Transform parent, Bounds playable)
        {
            var go = new GameObject("KillFloor") { layer = LayerIndex.collideWithCharacterHullOnly.intVal };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(playable.center.x, playable.min.y - BarrierBelow - 30f, playable.center.z);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(playable.size.x + 600f, 40f, playable.size.z + 600f);
            MapZone z = go.AddComponent<MapZone>();
            z.zoneType = MapZone.ZoneType.OutOfBounds;
            z.triggerType = MapZone.TriggerType.TriggerEnter;
        }

        /// <summary>A low, warm evening sun under a vanilla sunset sky (config: Sunken Plaza Sky), with matching ambient and fog.</summary>
        private static void Sun(Scene scene)
        {
            var go = new GameObject("Sun");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.Euler(24f, -60f, 0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.74f, 0.5f);
            // Deadlock's own sun is off on this dusk map (all its light is baked): under the lightmap ours mostly lights the
            // characters, so it is kept soft enough not to make them glow against the baked scene.
            l.intensity = MapBuilder.Lit ? 0.75f : 1.35f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            RenderSettings.sun = l;

            string key = SunkenPlazaStage.Sky?.Value ?? "";
            Material? sky = null;
            if (key.Length > 0)
                try { sky = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>(key).WaitForCompletion(); }
                catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[DeadlockMap] sky {key}: {e.Message}"); }
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.55f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.42f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.18f, 0.17f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.52f, 0.42f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 1100f;
        }
    }

    /// <summary>
    /// A doorway that sends a local player who walks into it to the teleporter (on a safe spot beside it). Checked per frame
    /// against the player's own body, which this machine moves (RoR2 movement is client-side), so no physics trigger is needed.
    /// </summary>
    internal class SubwayDoor : MonoBehaviour
    {
        public Vector3 HalfSize;
        private float _cooldown;

        private void Update()
        {
            if (_cooldown > 0f) { _cooldown -= Time.deltaTime; return; }
            TeleporterInteraction? tp = TeleporterInteraction.instance;
            if (tp == null) return;
            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterBody? body = user.cachedBody;
                if (body == null || !body.hasEffectiveAuthority) continue;
                Vector3 local = transform.InverseTransformPoint(body.footPosition + Vector3.up * 0.5f);
                Vector3 s = transform.lossyScale;
                if (Mathf.Abs(local.x) * s.x > HalfSize.x || Mathf.Abs(local.y) * s.y > HalfSize.y || Mathf.Abs(local.z) * s.z > HalfSize.z) continue;
                Vector3 near = tp.transform.position + tp.transform.forward * 6f;
                Vector3 to = TeleportHelper.FindSafeTeleportDestination(near, body, RoR2Application.rng) ?? near + Vector3.up;
                TeleportHelper.TeleportBody(body, to);
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[DeadlockMap] subway door: {body.GetDisplayName()} sent to the teleporter");
                _cooldown = 1f;
            }
        }
    }

    /// <summary>Feeds the barrier shader the local player's position each frame (it fades in within a few metres).</summary>
    internal class BarrierProximity : MonoBehaviour
    {
        private static readonly int PlayerPos = Shader.PropertyToID("_CacPlayerPos");

        private void LateUpdate()
        {
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            Vector3 p = body != null ? body.corePosition : Camera.main != null ? Camera.main.transform.position : new Vector3(0f, -1e6f, 0f);
            Shader.SetGlobalVector(PlayerPos, p);
        }
    }
}
