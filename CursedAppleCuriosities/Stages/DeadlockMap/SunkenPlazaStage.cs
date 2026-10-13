using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using R2API;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// The Sunken Plaza stage: an empty shell scene (our own bundle: no Valve content) that each machine fills at load time
    /// from its own converted Deadlock map. Offered as a stage-3 alternative, and only when every player in the run has a
    /// working Deadlock install that converted the same map (<see cref="PartyCheck"/>); checked again right before loading.
    /// </summary>
    public static class SunkenPlazaStage
    {
        public const string SceneName = "cac_sunkenplaza";
        public static ConfigEntry<float>? Weight;
        public static ConfigEntry<string>? Sky;
        private static SceneDef? _def;
        private static AssetBundle? _scenes, _assets;
        internal static Material? BarrierMaterial;

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[DeadlockMap] " + s);

        public static void Init(ConfigFile config)
        {
            Sky = config.Bind("Deadlock Map", "Sunken Plaza Sky", "RoR2/DLC1/ancientloft/matSkyboxAncientLoft.mat",
                "Skybox material borrowed from a vanilla stage (Addressables key). Default: Aphelion Sanctuary's sunset.");
            Weight = config.Bind("Deadlock Map", "Sunken Plaza Weight", 1f,
                "How likely Sunken Plaza is as stage 3, against each vanilla stage 3 (1 = as likely as each of them, 0 = never on its own).");
            _scenes = LoadBundle("cac_stagescenes");
            _assets = LoadBundle("cac_stageassets");
            BarrierMaterial = _assets?.LoadAsset<Material>("matBarrier");

            LanguageAPI.Add("CURSEDAPPLE_STAGE_SUNKENPLAZA_NAME", "Sunken Plaza");
            LanguageAPI.Add("CURSEDAPPLE_STAGE_SUNKENPLAZA_SUBTITLE", "Archmother's Ward");
            _def = ScriptableObject.CreateInstance<SceneDef>();
            _def.cachedName = SceneName;
            _def.sceneType = SceneType.Stage;
            _def.stageOrder = 3;
            _def.nameToken = "CURSEDAPPLE_STAGE_SUNKENPLAZA_NAME";
            _def.subtitleToken = "CURSEDAPPLE_STAGE_SUNKENPLAZA_SUBTITLE";
            _def.shouldIncludeInLogbook = false;
            _def.validForRandomSelection = false; // random stage orders don't check the party
            _def.filterOutOfBazaar = true;
            _def.environmentColor = new Color(0.45f, 0.55f, 0.7f);
            ContentAddition.AddSceneDef(_def);

            RoR2Application.onLoad += FinishDef;
            On.RoR2.Run.PickNextStageScene += PickNextStageScene;
            On.RoR2.Run.AdvanceStage += AdvanceStage;
            SceneManager.sceneLoaded += (scene, mode) => { if (scene.name == SceneName) Populate(scene); };
        }

        private static AssetBundle? LoadBundle(string name)
        {
            using Stream? s = typeof(SunkenPlazaStage).Assembly.GetManifestResourceStream(name);
            if (s == null) { Warn($"bundle {name} missing from the DLL"); return null; }
            var bytes = new byte[s.Length];
            s.Read(bytes, 0, bytes.Length);
            return AssetBundle.LoadFromMemory(bytes);
        }

        /// <summary>Destinations and music borrowed from vanilla once the catalogs are up.</summary>
        private static void FinishDef()
        {
            if (_def == null) return;
            _def.destinationsGroup = Addressables.LoadAssetAsync<SceneCollection>("RoR2/Base/SceneGroups/sgStage4.asset").WaitForCompletion();
            _def.loopedDestinationsGroup = Addressables.LoadAssetAsync<SceneCollection>("RoR2/Base/SceneGroups/loopSgStage4.asset").WaitForCompletion();
            SceneDef? rallypoint = Addressables.LoadAssetAsync<SceneDef>("RoR2/Base/frozenwall/frozenwall.asset").WaitForCompletion();
            if (rallypoint != null)
            {
                _def.mainTrack = rallypoint.mainTrack;
                _def.bossTrack = rallypoint.bossTrack;
                _def.portalMaterial = rallypoint.portalMaterial;
                _def.portalMaterialReference = rallypoint.portalMaterialReference;
                _def.previewTextureReference = rallypoint.previewTextureReference;
            }
        }

        // ---------------------------------------------------------------- picking it (server)

        /// <summary>Stage 3 picks: Sunken Plaza joins the choices when the whole party can load it.</summary>
        private static void PickNextStageScene(On.RoR2.Run.orig_PickNextStageScene orig, Run self, WeightedSelection<SceneDef> choices)
        {
            if (_def != null && NetworkServer.active && (Weight?.Value ?? 0f) > 0f && IsStage3Pick(choices))
            {
                if (PartyCheck.AllReady(out string why)) choices.AddChoice(_def, Weight!.Value);
                else Log($"Sunken Plaza left out of the stage 3 choices: {why}");
            }
            orig(self, choices);
        }

        private static bool IsStage3Pick(WeightedSelection<SceneDef> choices)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                SceneDef d = choices.GetChoice(i).value;
                if (d == _def) return false; // already offered
                if (d != null && d.stageOrder == 3) return true;
            }
            return false;
        }

        /// <summary>Last check before loading: if anyone can't load it now (joined since, conversion broke), go elsewhere.</summary>
        private static void AdvanceStage(On.RoR2.Run.orig_AdvanceStage orig, Run self, SceneDef nextScene)
        {
            if (nextScene == _def && _def != null && !PartyCheck.AllReady(out string why))
            {
                SceneCollection? stage3 = Addressables.LoadAssetAsync<SceneCollection>("RoR2/Base/SceneGroups/sgStage3.asset").WaitForCompletion();
                var pick = new WeightedSelection<SceneDef>();
                stage3?.AddToWeightedSelection(pick, d => d != _def);
                SceneDef? instead = pick.Count > 0 ? pick.Evaluate(UnityEngine.Random.value) : null;
                Warn($"Sunken Plaza can't load ({why}): going to {instead?.cachedName ?? "the default next stage"} instead");
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = $"<color=#ffb347>Sunken Plaza skipped: {why}.</color>" });
                if (instead != null) nextScene = instead;
            }
            orig(self, nextScene);
        }

        [ConCommand(commandName = "cac_map_go", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: go to Sunken Plaza now (host; only if the whole party can load it).")]
        private static void CCGo(ConCommandArgs args)
        {
            if (Run.instance == null || _def == null) throw new ConCommandException("Needs a run.");
            if (!PartyCheck.AllReady(out string why)) throw new ConCommandException("Sunken Plaza can't load: " + why);
            Run.instance.AdvanceStage(_def);
        }

        [ConCommand(commandName = "cac_map_party", flags = ConVarFlags.ExecuteOnServer, helpText = "Cursed Apple: can everyone load Sunken Plaza? (host)")]
        private static void CCParty(ConCommandArgs args)
        {
            bool ok = PartyCheck.AllReady(out string why);
            Debug.Log(ok ? "[DeadlockMap] everyone can load Sunken Plaza" : "[DeadlockMap] not everyone can load Sunken Plaza: " + why);
        }

        // ---------------------------------------------------------------- building the scene (every machine)

        private static void Populate(Scene scene)
        {
            MapConverter? conv = DeadlockMaps.SunkenPlaza;
            MapBuilder.Built? t = DeadlockMaps.Template();
            if (conv == null || t == null)
            {
                // Shouldn't happen (the party check gates loading): a floor so nobody falls forever, and say why.
                Warn("Sunken Plaza loaded without a converted map on this machine");
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.localScale = new Vector3(200f, 1f, 200f);
                floor.layer = LayerIndex.world.intVal;
                SceneManager.MoveGameObjectToScene(floor, scene);
                return;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float scale = DeadlockMaps.Scale?.Value ?? 1f;
            GameObject world = UnityEngine.Object.Instantiate(t.Root);
            world.name = "DeadlockMap";
            world.transform.localScale = Vector3.one * scale;
            SceneManager.MoveGameObjectToScene(world, scene);
            world.SetActive(true);
            Physics.SyncTransforms();

            var section = new SectionSpace(conv.Def, t.OriginUnits);
            StageDressing.Build(scene, world.transform, section, t.Playable);

            // AI navigation from the geometry (cached after the first time), then the stage's own objects.
            Bounds area = t.Playable;
            var (ground, air) = NavBuilder.Get(conv.NavFile, conv.Fingerprint ?? "", world.transform, section.Inside, section.NoWalk, area);

            var info = new GameObject("SceneInfo");
            info.SetActive(false);
            SceneManager.MoveGameObjectToScene(info, scene);
            SceneInfo si = info.AddComponent<SceneInfo>();
            typeof(SceneInfo).GetField("groundNodesAsset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(si, ground);
            typeof(SceneInfo).GetField("airNodesAsset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(si, air);
            ClassicStageInfo csi = info.AddComponent<ClassicStageInfo>();
            csi.sceneDirectorInteractibleCredits = 300;
            csi.sceneDirectorMonsterCredits = 30;
            csi.bonusInteractibleCreditObjects = Array.Empty<ClassicStageInfo.BonusInteractibleCreditObject>();
            csi.possibleMonsterFamilies = Array.Empty<ClassicStageInfo.MonsterFamily>();
            csi.SetMonsterDCCSP(Addressables.LoadAssetAsync<DccsPool>("RoR2/Base/frozenwall/dpFrozenWallMonsters.asset").WaitForCompletion());
            csi.SetInteractableDCCSP(Addressables.LoadAssetAsync<DccsPool>("RoR2/Base/frozenwall/dpFrozenWallInteractables.asset").WaitForCompletion());
            info.SetActive(true);

            // Scene-level managers every vanilla stage carries (GlobalEventManager and friends).
            GameObject manager = UnityEngine.Object.Instantiate(Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Core/GameManager.prefab").WaitForCompletion());
            SceneManager.MoveGameObjectToScene(manager, scene);

            // The director only runs on the server, and only once the run has spawned this stage's Stage object (vanilla
            // scene objects wait for the network layer the same way): it reads Stage.instance from its first frame.
            if (NetworkServer.active)
            {
                var wait = new GameObject("DirectorWhenStageReady");
                SceneManager.MoveGameObjectToScene(wait, scene);
                wait.AddComponent<DirectorWhenStageReady>();
            }
            Log($"Sunken Plaza populated in {watch.ElapsedMilliseconds} ms ({ground.GetNodeCount()} ground / {air.GetNodeCount()} air nodes; " +
                $"manager: {string.Join(", ", Array.ConvertAll(manager.GetComponentsInChildren<Component>(), c => c.GetType().Name))})");
        }

        /// <summary>Server: starts the vanilla Director (scene, combat) once Stage.instance exists.</summary>
        private class DirectorWhenStageReady : MonoBehaviour
        {
            private void Update()
            {
                if (Stage.instance == null) return;
                GameObject director = Instantiate(Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/Director.prefab").WaitForCompletion());
                SceneManager.MoveGameObjectToScene(director, gameObject.scene);
                SceneDirector? sd = director.GetComponent<SceneDirector>();
                if (sd != null && sd.teleporterSpawnCard == null)
                    sd.teleporterSpawnCard = Addressables.LoadAssetAsync<SpawnCard>("RoR2/Base/Teleporters/iscTeleporter.asset").WaitForCompletion();
                Log("director started (stage ready)");
                Destroy(gameObject);
            }
        }
    }

    /// <summary>The section's local space (Unity metres from its origin) against Deadlock units: inside the outline, no-walk boxes.</summary>
    internal class SectionSpace
    {
        private readonly MapSectionDef _def;
        private readonly Vector3 _origin;
        private readonly Vector2[] _outline;

        public SectionSpace(MapSectionDef def, Vector3 originUnits)
        {
            _def = def;
            _origin = originUnits;
            _outline = new Vector2[def.Outline.Length / 2];
            for (int i = 0; i < _outline.Length; i++) _outline[i] = new Vector2(def.Outline[i * 2], def.Outline[i * 2 + 1]);
        }

        /// <summary>Section space -> Deadlock units (inverse of MapCache.ToUnity).</summary>
        public Vector3 ToUnits(Vector3 u) => new Vector3(u.z / 0.0254f + _origin.x, -u.x / 0.0254f + _origin.y, u.y / 0.0254f + _origin.z);
        public Vector3 ToSection(Vector3 units) => MapCache.ToUnity(units, _origin);
        public Vector2[] OutlineUnits => _outline;

        public bool Inside(Vector3 u)
        {
            Vector3 s = ToUnits(u);
            bool inside = false;
            for (int i = 0, j = _outline.Length - 1; i < _outline.Length; j = i++)
            {
                Vector2 pi = _outline[i], pj = _outline[j];
                if ((pi.y > s.y) != (pj.y > s.y) && s.x < (pj.x - pi.x) * (s.y - pi.y) / (pj.y - pi.y) + pi.x) inside = !inside;
            }
            return inside;
        }

        public bool NoWalk(Vector3 u)
        {
            Vector3 s = ToUnits(u);
            int[] b = _def.NoWalk;
            for (int i = 0; i + 5 < b.Length; i += 6)
                if (s.x >= b[i] - 32 && s.y >= b[i + 1] - 32 && s.z >= b[i + 2] - 32 && s.x <= b[i + 3] + 32 && s.y <= b[i + 4] + 32 && s.z <= b[i + 5] + 32)
                    return true;
            return false;
        }
    }
}
