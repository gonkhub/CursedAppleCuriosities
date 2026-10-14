using System;
using System.Collections.Generic;
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
        public static ConfigEntry<int>? ShopWhite, ShopGreen, ShopRed;
        public static ConfigEntry<float>? Exposure, DynamicLight;
        private static SceneDef? _def;
        private static AssetBundle? _scenes, _assets;
        internal static Material? BarrierMaterial, GlassMaterial, WorldMaterial;

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[DeadlockMap] " + s);

        public static void Init(ConfigFile config)
        {
            Sky = config.Bind("Deadlock Map", "Sunken Plaza Sky", "RoR2/DLC1/ancientloft/matSkyboxAncientLoft.mat",
                "Skybox material borrowed from a vanilla stage (Addressables key). Default: Aphelion Sanctuary's sunset.");
            ShopWhite = config.Bind("Deadlock Map", "Shop White Base Cost", 25, "Curiosity shop on the map: base price of a white (a small chest's base cost; scaled by difficulty like chests).");
            ShopGreen = config.Bind("Deadlock Map", "Shop Green Base Cost", 50, "Base price of a green (a large chest's base cost).");
            ShopRed = config.Bind("Deadlock Map", "Shop Red Base Cost", 400, "Base price of a red (a legendary chest's base cost).");
            Exposure = config.Bind("Deadlock Map", "Lightmap Exposure", 1f,
                "Brightness of Deadlock's own baked lighting on the map (1 = as baked).");
            DynamicLight = config.Bind("Deadlock Map", "Dynamic Light Share", 0.15f,
                "How much the game's own lights (the sun, explosions, glows) light the map on top of Deadlock's baked lighting (0 = none).");
            Weight = config.Bind("Deadlock Map", "Sunken Plaza Weight", 1f,
                "How likely Sunken Plaza is as stage 3, against each vanilla stage 3 (1 = as likely as each of them, 0 = never on its own).");
            _scenes = LoadBundle("cac_stagescenes");
            _assets = LoadBundle("cac_stageassets");
            BarrierMaterial = _assets?.LoadAsset<Material>("matBarrier");
            GlassMaterial = _assets?.LoadAsset<Material>("matGlass");
            WorldMaterial = _assets?.LoadAsset<Material>("matWorld");

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

        internal const string NewtPrefab = "RoR2/Base/NewtStatue/NewtStatue.prefab";

        [ConCommand(commandName = "cac_map_newt", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: put a newt altar where you stand, facing your way (host; to try a spot out).")]
        private static void CCNewt(ConCommandArgs args)
        {
            CharacterBody? body = args.senderBody;
            if (body == null) throw new ConCommandException("Needs your body.");
            Vector3 aim = body.inputBank != null ? body.inputBank.aimDirection : body.transform.forward;
            GameObject prefab = Addressables.LoadAssetAsync<GameObject>(NewtPrefab).WaitForCompletion();
            GameObject altar = UnityEngine.Object.Instantiate(prefab, body.footPosition, Quaternion.Euler(0f, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, 0f));
            SitOnFloor(altar, body.footPosition.y);
            NetworkServer.Spawn(altar);
        }

        /// <summary>Lift a spawned prop so the bottom of its visible model rests at floorY (the newt altar's pivot sits well above its base).</summary>
        internal static void SitOnFloor(GameObject go, float floorY)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            float bottom = float.MaxValue;
            foreach (Renderer r in renderers) if (r.enabled && !(r is ParticleSystemRenderer)) bottom = Mathf.Min(bottom, r.bounds.min.y);
            if (bottom == float.MaxValue) return;
            go.transform.position += Vector3.up * (floorY - bottom);
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
            var (ground, air, spawns) = NavBuilder.Get(conv.NavFile, conv.Fingerprint ?? "", world.transform, section.Inside, section.NoWalk, area);

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
                wait.AddComponent<DirectorWhenStageReady>().Spawns = spawns;
            }
            Log($"Sunken Plaza populated in {watch.ElapsedMilliseconds} ms ({ground.GetNodeCount()} ground / {air.GetNodeCount()} air nodes; " +
                $"manager: {string.Join(", ", Array.ConvertAll(manager.GetComponentsInChildren<Component>(), c => c.GetType().Name))})");
        }

        /// <summary>Server: starts the vanilla Director (scene, combat) once Stage.instance exists.</summary>
        private class DirectorWhenStageReady : MonoBehaviour
        {
            public List<(Vector3 Pos, float Yaw)> Spawns = new List<(Vector3, float)>();

            private void Update()
            {
                if (Stage.instance == null) return;
                // Our own player spawns (open, roofless, facing a clear view) before the director looks for any.
                foreach (var (pos, yaw) in Spawns) SpawnPoint.AddSpawnPoint(pos, Quaternion.Euler(0f, yaw, 0f));
                GameObject director = Instantiate(Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/Director.prefab").WaitForCompletion());
                SceneManager.MoveGameObjectToScene(director, gameObject.scene);
                SceneDirector? sd = director.GetComponent<SceneDirector>();
                if (sd != null)
                {
                    if (sd.teleporterSpawnCard == null)
                        sd.teleporterSpawnCard = Addressables.LoadAssetAsync<SpawnCard>("RoR2/Base/Teleporters/iscTeleporter.asset").WaitForCompletion();
                    sd.usePlacedSpawnPoints = Spawns.Count > 0;
                }
                SceneDirector.onTeleporterPlaced += LogTeleporter;
                SceneDirector.onPrePopulateSceneServer += SeedCamps;
                SpawnNewtAltars(gameObject.scene);
                SpawnShops();
                Log($"director started (stage ready), {Spawns.Count} player spawns placed");
                Destroy(gameObject);
            }

            private static void SpawnNewtAltars(Scene scene)
            {
                float[] a = MapSectionDef.SunkenPlaza.NewtAltars;
                GameObject? world = GameObject.Find("DeadlockMap");
                if (a.Length < 4 || world == null) return;
                GameObject prefab = Addressables.LoadAssetAsync<GameObject>(NewtPrefab).WaitForCompletion();
                for (int i = 0; i + 3 < a.Length; i += 4)
                {
                    Vector3 at = world.transform.TransformPoint(new Vector3(a[i], a[i + 1], a[i + 2]));
                    GameObject altar = Instantiate(prefab, at, Quaternion.Euler(0f, a[i + 3] + world.transform.eulerAngles.y, 0f));
                    SitOnFloor(altar, at.y);
                    NetworkServer.Spawn(altar);
                }
                Log($"{a.Length / 4} newt altars placed");
            }

            /// <summary>
            /// A Curiosity shop at each of Deadlock's shop counters in the section: open to everyone, bought from as often
            /// as you like, priced like chests (small / large / legendary base costs, scaled by difficulty the same way). Deadlock's
            /// own items only.
            /// </summary>
            private static void SpawnShops()
            {
                GameObject? world = GameObject.Find("DeadlockMap");
                MapBuilder.Built? t = DeadlockMaps.Template();
                if (world == null || t == null) return;
                int[] b = MapSectionDef.SunkenPlaza.Shops;
                int placed = 0;
                for (int i = 0; i + 5 < b.Length; i += 6)
                {
                    // The counter's trigger box: its centre, dropped onto the floor beneath it.
                    var centre = new Vector3((b[i] + b[i + 3]) / 2f, (b[i + 1] + b[i + 4]) / 2f, b[i + 5]);
                    Vector3 top = world.transform.TransformPoint(MapCache.ToUnity(centre, t.OriginUnits));
                    if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 12f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Items.CuriosityCatalog.SpawnShop(hit.point, requiresCatalog: false,
                            ShopWhite?.Value ?? 25, ShopGreen?.Value ?? 50, ShopRed?.Value ?? 400, deadlockOnly: true) != null)
                        placed++;
                }
                Log($"{placed} curiosity shops placed at Deadlock's shop counters");
            }

            /// <summary>
            /// Before the director spends its interactable credits at random: one interactable at each of Deadlock's neutral
            /// camps (its spawn points merged into clusters), from the stage's own interactable deck and paid from the same
            /// budget, using at most 60% of it so the rest of the map still gets its share.
            /// </summary>
            private static void SeedCamps(SceneDirector sd)
            {
                SceneDirector.onPrePopulateSceneServer -= SeedCamps;
                GameObject? world = GameObject.Find("DeadlockMap");
                MapBuilder.Built? t = DeadlockMaps.Template();
                ClassicStageInfo? csi = ClassicStageInfo.instance;
                if (world == null || t == null || csi == null || csi.interactableCategories == null || DirectorCore.instance == null) return;
                var section = new SectionSpace(MapSectionDef.SunkenPlaza, t.OriginUnits);

                // Camp clusters: spawn points within ~6 m of each other are one camp.
                int[] sp = MapSectionDef.SunkenPlaza.InteractableSpots;
                var camps = new List<Vector3>();
                for (int i = 0; i + 2 < sp.Length; i += 3)
                {
                    Vector3 local = section.ToSection(new Vector3(sp[i], sp[i + 1], sp[i + 2]));
                    if (!section.Inside(local)) continue;
                    Vector3 p = world.transform.TransformPoint(local);
                    if (camps.Exists(c => (c - p).sqrMagnitude < 36f)) continue;
                    camps.Add(p);
                }
                var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
                Util.ShuffleList(camps, rng);
                WeightedSelection<DirectorCard> deck = csi.interactableCategories.GenerateDirectorCardWeightedSelection();
                int budget = (int)(sd.interactableCredit * 0.6f), spent = 0, placed = 0;
                var anchor = new GameObject("CampAnchor");
                foreach (Vector3 camp in camps)
                {
                    if (deck.Count == 0) break;
                    DirectorCard? card = null;
                    for (int tries = 0; tries < 8 && card == null; tries++)
                    {
                        DirectorCard c = deck.Evaluate(rng.nextNormalizedFloat);
                        if (c != null && c.IsAvailable() && c.cost <= budget - spent && c.cost <= sd.interactableCredit) card = c;
                    }
                    if (card == null) continue;
                    anchor.transform.position = camp;
                    var rule = new DirectorPlacementRule
                    {
                        placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                        spawnOnTarget = anchor.transform,
                        minDistance = 0f,
                        maxDistance = 8f,
                    };
                    GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card.GetSpawnCard(), rule, rng));
                    if (spawned == null) continue;
                    spent += card.cost;
                    sd.interactableCredit -= card.cost;
                    placed++;
                }
                Destroy(anchor);
                Log($"camps seeded: {placed} interactables at {camps.Count} Deadlock camps ({spent} credits; {sd.interactableCredit} left for the rest of the map)");
            }

            private static void LogTeleporter(SceneDirector sd, GameObject tp)
            {
                SceneDirector.onTeleporterPlaced -= LogTeleporter;
                Log(tp != null ? $"teleporter placed at {tp.transform.position}" : "no teleporter placed");
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
