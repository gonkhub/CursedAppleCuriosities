using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// Deadlock district stages: converts the districts from the player's own Deadlock install at launch (in the background,
    /// one after another, once per Deadlock map update) and builds the one a stage needs when it loads.
    /// </summary>
    public static class DeadlockMaps
    {
        public static ConfigEntry<bool>? Enabled;
        public static ConfigEntry<float>? Scale;
        /// <summary>Every district (MapSectionDef.All), registered on every machine whatever the config.</summary>
        public static readonly List<District> Districts = new List<District>();
        /// <summary>The district whose world was built last (the one in the current stage, or the walk test's).</summary>
        public static District? Current { get; private set; }

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("Deadlock Map", "Enabled", true,
                "Convert Deadlock's map from your own Deadlock install (once, in the background) for the Deadlock district stages. " +
                "A district is only picked when every player in the run has it.");
            Scale = config.Bind("Deadlock Map", "Scale", 1f, "Size of the Deadlock map in Risk of Rain 2 (1 = Deadlock's own size).");
            foreach (MapSectionDef def in MapSectionDef.All) Districts.Add(new District(def));
            // Registered on every machine whatever the config: scene catalogs must match between host and clients.
            DeadlockStages.Init(config, Districts);
            PartyCheck.Init();
            if (!Enabled.Value) { Log("off in the config"); return; }
            foreach (District d in Districts) d.Converter = new MapConverter(d.Def);
            // One conversion at a time (each one peaks near 2 GB): the first district is ready first.
            new Thread(() => { foreach (District d in Districts) d.Converter!.RunNow(); })
            { IsBackground = true, Name = "CursedApple map converter", Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
        }

        public static District? Find(string id) => Districts.Find(d => d.Id == id);
        public static District? ByScene(string scene) => Districts.Find(d => d.Def.Scene == scene);

        /// <summary>True while any district is still waiting for, or in, its conversion.</summary>
        public static bool Converting => Districts.Exists(d => d.Converter != null && (d.Converter.State == MapState.NotStarted || d.Converter.State == MapState.Converting));

        // ---------------------------------------------------------------- loading (shared by the stages and the walk test)

        private static MapBuilder.Built? _template;
        private static District? _templateFor;

        /// <summary>
        /// A district's objects, built when first needed and kept (inactive) for its later stages; null until converted.
        /// Only one district's are kept: building another releases the previous one's meshes, materials and textures.
        /// </summary>
        internal static MapBuilder.Built? Template(District d)
        {
            if (_template != null && _template.Root != null && _templateFor == d) return _template;
            MapConverter? c = d.Converter;
            if (c == null || c.State != MapState.Ready) return null;
            if (_template != null) { _template.Release(); _template = null; _templateFor = null; }
            _template = MapBuilder.Build(c, true);
            UnityEngine.Object.DontDestroyOnLoad(_template.Root);
            _templateFor = d;
            Current = d;
            return _template;
        }

        // ---------------------------------------------------------------- debug

        [ConCommand(commandName = "cac_map_status", flags = ConVarFlags.None, helpText = "Cursed Apple: each Deadlock district's conversion state.")]
        private static void CCStatus(ConCommandArgs args)
        {
            foreach (District d in Districts)
            {
                MapConverter? c = d.Converter;
                UnityEngine.Debug.Log(c == null ? $"[DeadlockMap] {d.Id}: off" : $"[DeadlockMap] {d.Id} ({d.Def.Name}): {c.State}, {c.Status}, fingerprint {c.Fingerprint ?? "-"}");
            }
        }

        [ConCommand(commandName = "cac_map_shader", flags = ConVarFlags.None, helpText = "Cursed Apple: list the world shader's properties (debug).")]
        private static void CCShader(ConCommandArgs args)
        {
            Material m = MapBuilder.MakeMaterial(new MapMaterial { Name = "probe" }, null);
            Shader sh = m.shader;
            var names = new List<string>();
            for (int i = 0; i < sh.GetPropertyCount(); i++) names.Add(sh.GetPropertyName(i) + ":" + sh.GetPropertyType(i));
            UnityEngine.Debug.Log($"[DeadlockMap] shader {sh.name}: {string.Join(", ", names)}; keywords {string.Join(" ", m.shaderKeywords)}");
        }

        [ConCommand(commandName = "cac_map_light", flags = ConVarFlags.None,
            helpText = "Cursed Apple: try the map's lighting live (not saved): cac_map_light <lightmap exposure> [dynamic share] [sun intensity].")]
        private static void CCLight(ConCommandArgs args)
        {
            if (args.Count > 0) Shader.SetGlobalFloat("_CacExposure", args.GetArgFloat(0));
            if (args.Count > 1) Shader.SetGlobalFloat("_CacDynamic", args.GetArgFloat(1));
            if (args.Count > 2 && RenderSettings.sun != null) RenderSettings.sun.intensity = args.GetArgFloat(2);
            UnityEngine.Debug.Log($"[DeadlockMap] lighting: exposure {Shader.GetGlobalFloat("_CacExposure")}, dynamic share {Shader.GetGlobalFloat("_CacDynamic")}, " +
                $"lightmap {(Shader.GetGlobalFloat("_CacLightmapOn") > 0.5f ? "on" : "off")}, sun {(RenderSettings.sun != null ? RenderSettings.sun.intensity : 0f)}");
        }

        private static GameObject? _walk;
        private static District? _walkFor;

        [ConCommand(commandName = "cac_map_walk", flags = ConVarFlags.None,
            helpText = "Cursed Apple: put a Deadlock district into this stage, high above it, and move you there (testing). " +
                       "Optional: metres east/north of its arrival point to land at, then the district's id.")]
        private static void CCWalk(ConCommandArgs args)
        {
            District d = DeadlockStages.Named(args, 2);
            MapBuilder.Built? t = Template(d);
            if (t == null) throw new ConCommandException($"{d.Def.Name} isn't converted yet: see cac_map_status.");
            if (_walk != null && _walkFor != d) { UnityEngine.Object.Destroy(_walk); _walk = null; }
            _walkFor = d;
            if (_walk == null)
            {
                _walk = UnityEngine.Object.Instantiate(t.Root);
                _walk.name = "DeadlockMapWalk";
                _walk.transform.position = new Vector3(0f, 2000f, 0f);
                _walk.transform.localScale = Vector3.one * (Scale?.Value ?? 1f);
                _walk.SetActive(true);
                foreach (MapZone z in UnityEngine.Object.FindObjectsOfType<MapZone>()) z.enabled = false; // far outside this stage's bounds
            }
            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (body == null) return;
            Vector3 at = d.ArrivalLocal(t.OriginUnits) + new Vector3(args.Count > 0 ? args.GetArgFloat(0) : 0f, 0f, args.Count > 1 ? args.GetArgFloat(1) : 0f);
            Vector3 drop = _walk.transform.TransformPoint(at + Vector3.up * 80f);
            if (Physics.Raycast(drop, Vector3.down, out RaycastHit hit, 300f, LayerIndex.world.mask)) drop = hit.point + Vector3.up;
            TeleportHelper.TeleportBody(body, drop, false);
            UnityEngine.Debug.Log($"[DeadlockMap] walk test at {drop}");
        }

        [ConCommand(commandName = "cac_fps", flags = ConVarFlags.None, helpText = "Cursed Apple: log the frame rate over the next N seconds (default 5).")]
        private static void CCFps(ConCommandArgs args)
        {
            float seconds = args.Count > 0 ? args.GetArgFloat(0) : 5f;
            var go = new GameObject("cac_fps");
            go.AddComponent<FpsProbe>().Seconds = seconds;
        }

        private class FpsProbe : MonoBehaviour
        {
            public float Seconds = 5f;
            private float _t;
            private int _frames;
            private float _worst;
            private void Update()
            {
                _t += Time.unscaledDeltaTime; _frames++;
                _worst = Mathf.Max(_worst, Time.unscaledDeltaTime);
                if (_t < Seconds) return;
                UnityEngine.Debug.Log($"[DeadlockMap] fps {_frames / _t:0.0} over {_t:0.0} s, worst frame {_worst * 1000f:0} ms");
                Destroy(gameObject);
            }
        }
    }
}
