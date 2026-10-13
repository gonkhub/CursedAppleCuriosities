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
    /// Deadlock map stages: converts the sections from the player's own Deadlock install at launch (background, once
    /// per Deadlock map update) and builds them on demand.
    /// </summary>
    public static class DeadlockMaps
    {
        public static ConfigEntry<bool>? Enabled;
        public static ConfigEntry<float>? Scale;
        public static MapConverter? SunkenPlaza { get; private set; }

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("Deadlock Map", "Enabled", true,
                "Convert Deadlock's map from your own Deadlock install (once, in the background) for the Sunken Plaza stage. " +
                "The stage is only picked when every player in the run has it.");
            Scale = config.Bind("Deadlock Map", "Scale", 1f, "Size of the Deadlock map in Risk of Rain 2 (1 = Deadlock's own size).");
            // Registered on every machine whatever the config: scene catalogs must match between host and clients.
            SunkenPlazaStage.Init(config);
            PartyCheck.Init();
            if (!Enabled.Value) { Log("off in the config"); return; }
            SunkenPlaza = new MapConverter(MapSectionDef.SunkenPlaza);
            SunkenPlaza.Start();
        }

        // ---------------------------------------------------------------- loading (shared by the stage and the walk test)

        private static MapBuilder.Built? _template;

        /// <summary>The section's objects, built once and kept (inactive) for every later stage; null until converted.</summary>
        internal static MapBuilder.Built? Template()
        {
            if (_template != null && _template.Root != null) return _template;
            MapConverter? c = SunkenPlaza;
            if (c == null || c.State != MapState.Ready) return null;
            _template = MapBuilder.Build(c, true);
            UnityEngine.Object.DontDestroyOnLoad(_template.Root);
            return _template;
        }

        /// <summary>The Sunken Plaza's centre (Source units -2400, 2700, at street height), in the section's Unity space.</summary>
        internal static Vector3 PlazaCenter(Vector3 originUnits) => MapCache.ToUnity(new Vector3(-2400f, 2700f, 600f), originUnits);

        // ---------------------------------------------------------------- debug

        [ConCommand(commandName = "cac_map_status", flags = ConVarFlags.None, helpText = "Cursed Apple: the Deadlock map conversion's state.")]
        private static void CCStatus(ConCommandArgs args)
        {
            MapConverter? c = SunkenPlaza;
            UnityEngine.Debug.Log(c == null ? "[DeadlockMap] off" : $"[DeadlockMap] {c.Def.Id}: {c.State}, {c.Status}, fingerprint {c.Fingerprint ?? "-"}");
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

        private static GameObject? _walk;

        [ConCommand(commandName = "cac_map_walk", flags = ConVarFlags.None,
            helpText = "Cursed Apple: put the Sunken Plaza section into this stage, high above it, and move you there (testing). " +
                       "Optional: metres east/north of the plaza's centre to land at.")]
        private static void CCWalk(ConCommandArgs args)
        {
            MapBuilder.Built? t = Template();
            if (t == null) throw new ConCommandException("The map isn't converted yet: see cac_map_status.");
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
            Vector3 at = PlazaCenter(t.OriginUnits) + new Vector3(args.Count > 0 ? args.GetArgFloat(0) : 0f, 0f, args.Count > 1 ? args.GetArgFloat(1) : 0f);
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
