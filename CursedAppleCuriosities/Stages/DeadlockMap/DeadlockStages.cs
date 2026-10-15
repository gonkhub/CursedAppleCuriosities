using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// What every Deadlock district stage shares: the shell bundles (no Valve content), the map shaders, the lighting and shop
    /// settings, and the run hooks that offer a district in its stage slot only when every player in the run can load it
    /// (<see cref="PartyCheck"/>), checked again right before loading.
    /// </summary>
    public static class DeadlockStages
    {
        public static ConfigEntry<int>? ShopWhite, ShopGreen, ShopRed;
        public static ConfigEntry<float>? Exposure, DynamicLight;
        private static AssetBundle? _scenes, _assets;
        internal static Material? BarrierMaterial, GlassMaterial, WorldMaterial;
        internal const string NewtPrefab = "RoR2/Base/NewtStatue/NewtStatue.prefab";

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[DeadlockMap] " + s);

        public static void Init(ConfigFile config, IReadOnlyList<District> districts)
        {
            ShopWhite = config.Bind("Deadlock Map", "Shop White Base Cost", 25, "Curiosity shop on the map: base price of a white (a small chest's base cost; scaled by difficulty like chests).");
            ShopGreen = config.Bind("Deadlock Map", "Shop Green Base Cost", 50, "Base price of a green (a large chest's base cost).");
            ShopRed = config.Bind("Deadlock Map", "Shop Red Base Cost", 400, "Base price of a red (a legendary chest's base cost).");
            Exposure = config.Bind("Deadlock Map", "Lightmap Exposure", 1f,
                "Brightness of Deadlock's own baked lighting on the map (1 = as baked).");
            DynamicLight = config.Bind("Deadlock Map", "Dynamic Light Share", 0.15f,
                "How much the game's own lights (the sun, explosions, glows) light the map on top of Deadlock's baked lighting (0 = none).");
            WalkerTeleporter = config.Bind("Deadlock Map", "Walker Teleporter Chance", 0.5f,
                "Chance (0-1) that a district's Walker spot holds the teleporter; otherwise a major interactable stands there (Large Chest, Large Multishop or Shrine of the Mountain).");
            SecretChance = config.Bind("Deadlock Map", "Unfound Secret Chance", 0.5f,
                "Chance that a district replaces its stage while it still hides a hero someone in the run hasn't unlocked (used instead of the district's own chance when higher; a district set to 0 stays off).");
            ImpPitRadius = config.Bind("Deadlock Map", "Imp Pit Radius", 20f,
                "Sunken Plaza's purple pit: using an interactable within this many metres of it wakes an elite Imp Overlord nearby (once per visit).");
            MapShop.Init();
            HeroSilhouette.Init(districts.Select(d => d.Def));
            _scenes = LoadBundle("cac_stagescenes");
            _assets = LoadBundle("cac_stageassets");
            BarrierMaterial = _assets?.LoadAsset<Material>("matBarrier");
            GlassMaterial = _assets?.LoadAsset<Material>("matGlass");
            WorldMaterial = _assets?.LoadAsset<Material>("matWorld");

            // Registered on every machine whatever the config: scene catalogs must match between host and clients.
            foreach (District d in districts) d.Stage = new DistrictStage(d, config);
            RoR2Application.onLoad += () => { foreach (District d in districts) d.Stage.FinishDef(); };
            On.RoR2.Run.PickNextStageScene += PickNextStageScene;
            On.RoR2.Run.AdvanceStage += AdvanceStage;
            On.RoR2.SceneDirector.PlaceTeleporter += PlaceTeleporter;
            GlobalEventManager.OnInteractionsGlobal += ImpPitInteraction;
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                District? d = DeadlockMaps.ByScene(scene.name);
                if (d != null) d.Stage.Populate(scene);
            };
        }

        public static ConfigEntry<float>? WalkerTeleporter;
        public static ConfigEntry<float>? SecretChance;

        /// <summary>True while the district hides a hero that someone in the run hasn't unlocked yet.</summary>
        private static bool HasUnfoundSecret(MapSectionDef def)
        {
            foreach (SecretSpot s in def.Secrets)
                if (Unlocks.SurvivorUnlocks.Of(s.Hero) != null && !Unlocks.SurvivorUnlocks.EveryoneHas(s.Hero)) return true;
            return false;
        }
        /// <summary>This stage's teleporter spot when it goes to the Walker's (server, set before the director populates).</summary>
        internal static (Vector3 Pos, float Yaw)? _walkerTeleporter;

        /// <summary>The teleporter at the Walker's spot when this stage rolled it; otherwise vanilla's random spot.</summary>
        private static void PlaceTeleporter(On.RoR2.SceneDirector.orig_PlaceTeleporter orig, SceneDirector self)
        {
            (Vector3 Pos, float Yaw)? at = _walkerTeleporter;
            _walkerTeleporter = null;
            if (at == null || self.teleporterInstance != null || self.teleporterSpawnCard == null || DirectorCore.instance == null) { orig(self); return; }
            self.teleporterInstance = SpawnUpright(self.teleporterSpawnCard, at.Value.Pos, at.Value.Yaw, new Xoroshiro128Plus(Run.instance.stageRng.nextUlong));
            if (self.teleporterInstance == null) { Warn("the teleporter didn't fit at the Walker's spot: placed as usual"); orig(self); return; }
            Run.instance.OnServerTeleporterPlaced(self, self.teleporterInstance);
            // Vanilla's event for whoever listens (our log, other mods): only invokable through its backing field.
            var placed = typeof(SceneDirector).GetField("onTeleporterPlaced", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Action<SceneDirector, GameObject>;
            placed?.Invoke(self, self.teleporterInstance);
        }

        /// <summary>A spawn card exactly at a spot, standing upright (no tilt to the floor under it).</summary>
        internal static GameObject? SpawnUpright(SpawnCard card, Vector3 pos, float yaw, Xoroshiro128Plus rng)
        {
            var anchor = new GameObject("UprightAnchor");
            anchor.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (card is InteractableSpawnCard isc && (isc.orientToFloor || isc.slightlyRandomizeOrientation))
            {
                isc = UnityEngine.Object.Instantiate(isc);
                isc.orientToFloor = false;
                isc.slightlyRandomizeOrientation = false;
                card = isc;
            }
            var rule = new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Direct, spawnOnTarget = anchor.transform };
            GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, rule, rng));
            UnityEngine.Object.Destroy(anchor);
            return spawned;
        }

        internal static readonly string[] WalkerInteractables =
        {
            "RoR2/Base/Chest2/iscChest2.asset",                   // Large Chest
            "RoR2/Base/TripleShopLarge/iscTripleShopLarge.asset", // Large Multishop
            "RoR2/Base/ShrineBoss/iscShrineBoss.asset",           // Shrine of the Mountain
        };

        public static ConfigEntry<float>? ImpPitRadius;
        /// <summary>This stage's armed Imp Overlord pit (server): cleared once it has woken.</summary>
        internal static (Stage Stage, Vector3 Pit)? _impPit;

        /// <summary>
        /// The first time anyone uses an interactable in the pit (a chest, a shrine...), an elite Imp Overlord appears nearby:
        /// once per visit. A random tier-1 elite (fire, ice, lightning...), stats and rewards as the combat director gives one.
        /// </summary>
        private static void ImpPitInteraction(Interactor interactor, IInteractable interactable, GameObject used)
        {
            if (!NetworkServer.active || _impPit == null || used == null) return;
            var (stage, pit) = _impPit.Value;
            if (stage != Stage.instance) { _impPit = null; return; }
            if ((used.transform.position - pit).sqrMagnitude > Mathf.Pow(ImpPitRadius?.Value ?? 20f, 2f)) return;
            _impPit = null;
            SpawnImpLord(used.transform.position, pit);
        }

        private static void SpawnImpLord(Vector3 near, Vector3 pit)
        {
            if (Run.instance == null || DirectorCore.instance == null) return;
            SpawnCard? card = Addressables.LoadAssetAsync<SpawnCard>("RoR2/Base/ImpBoss/cscImpBoss.asset").WaitForCompletion();
            if (card == null) { Warn("no Imp Overlord spawn card"); return; }
            var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
            // Vanilla's first elite tier (private): its cost multiplier and a random elite of it available this run.
            var tiers = typeof(CombatDirector).GetField("eliteTiers", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as CombatDirector.EliteTierDef[];
            CombatDirector.EliteTierDef? tier = tiers != null && tiers.Length > 1 ? tiers[1] : null;
            EliteDef? elite = tier?.GetRandomAvailableEliteDef(rng);
            float value = tier?.costMultiplier ?? 1f;
            var anchor = new GameObject("ImpLordAnchor");
            anchor.transform.position = near;
            void Configure(SpawnCard.SpawnResult result)
            {
                CharacterMaster? master = result.success && result.spawnedInstance != null ? result.spawnedInstance.GetComponent<CharacterMaster>() : null;
                if (master == null || master.inventory == null) return;
                if (elite?.eliteEquipmentDef != null) master.inventory.SetEquipmentIndex(elite.eliteEquipmentDef.equipmentIndex);
                master.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, Mathf.RoundToInt(((elite?.healthBoostCoefficient ?? 1f) - 1f) * 10f));
                master.inventory.GiveItemPermanent(RoR2Content.Items.BoostDamage, Mathf.RoundToInt(((elite?.damageBoostCoefficient ?? 1f) - 1f) * 10f));
                DeathRewards? rewards = master.GetBodyObject()?.GetComponent<DeathRewards>();
                if (rewards != null)
                {
                    float worth = card.directorCreditCost * value * 0.2f; // the scene director's exp reward coefficient
                    rewards.spawnValue = (int)Mathf.Max(1f, worth);
                    rewards.expReward = (uint)Mathf.Max(1f, worth * Run.instance.compensatedDifficultyCoefficient);
                    rewards.goldReward = (uint)Mathf.Max(1f, worth * 2f * Run.instance.compensatedDifficultyCoefficient);
                }
                Log($"an elite Imp Overlord ({(elite != null ? elite.name : "no elite available")}) woke in its pit at {master.GetBodyObject()?.transform.position}");
            }
            DirectorSpawnRequest Request(DirectorPlacementRule rule) => new DirectorSpawnRequest(card, rule, rng)
            {
                teamIndexOverride = TeamIndex.Monster,
                ignoreTeamMemberLimit = true,
                onSpawnedServer = Configure,
            };
            // Nearby, not on top of whoever woke it; failing that, right in the pit.
            GameObject? spawned = DirectorCore.instance.TrySpawnObject(Request(new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Approximate, spawnOnTarget = anchor.transform, minDistance = 8f, maxDistance = 25f,
            }));
            if (spawned == null)
            {
                anchor.transform.position = pit;
                spawned = DirectorCore.instance.TrySpawnObject(Request(new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Direct, spawnOnTarget = anchor.transform }));
            }
            if (spawned == null) Warn($"the Imp Overlord couldn't be placed near {near}");
            UnityEngine.Object.Destroy(anchor);
        }

        private static AssetBundle? LoadBundle(string name)
        {
            using Stream? s = typeof(DeadlockStages).Assembly.GetManifestResourceStream(name);
            if (s == null) { Warn($"bundle {name} missing from the DLL"); return null; }
            var bytes = new byte[s.Length];
            s.Read(bytes, 0, bytes.Length);
            return AssetBundle.LoadFromMemory(bytes);
        }

        // ---------------------------------------------------------------- picking (server)

        /// <summary>A stage pick: every district of that stage slot joins the choices when the whole party can load it.</summary>
        private static void PickNextStageScene(On.RoR2.Run.orig_PickNextStageScene orig, Run self, WeightedSelection<SceneDef> choices)
        {
            if (NetworkServer.active)
            {
                int order = SlotOf(choices);
                float vanilla = 0f; // the slot's own stages, before any district joins
                for (int i = 0; i < choices.Count; i++) vanilla += choices.GetChoice(i).weight;
                foreach (District d in DeadlockMaps.Districts)
                {
                    SceneDef? def = d.Stage.Def;
                    float chance = Mathf.Clamp(d.Stage.Chance?.Value ?? 0f, 0f, 0.95f);
                    if (chance > 0f && HasUnfoundSecret(d.Def)) chance = Mathf.Max(chance, Mathf.Clamp(SecretChance?.Value ?? 0f, 0f, 0.95f));
                    if (def == null || chance <= 0f || order != d.Def.StageOrder || Offered(choices, def) || vanilla <= 0f) continue;
                    // A weight that makes it this likely against the slot's vanilla stages: w / (vanilla + w) = chance.
                    if (PartyCheck.AllReady(d, out string why)) choices.AddChoice(def, vanilla * chance / (1f - chance));
                    else Log($"{d.Def.Name} left out of the stage {order} choices: {why}");
                }
            }
            orig(self, choices);
        }

        /// <summary>The vanilla stage slot these choices are for (0 = not a numbered stage pick).</summary>
        private static int SlotOf(WeightedSelection<SceneDef> choices)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                SceneDef d = choices.GetChoice(i).value;
                if (d != null && d.stageOrder >= 1 && d.stageOrder <= 5 && DeadlockMaps.ByScene(d.cachedName) == null) return d.stageOrder;
            }
            return 0;
        }

        private static bool Offered(WeightedSelection<SceneDef> choices, SceneDef def)
        {
            for (int i = 0; i < choices.Count; i++) if (choices.GetChoice(i).value == def) return true;
            return false;
        }

        /// <summary>Last check before loading: if anyone can't load it now (joined since, conversion broke), go elsewhere.</summary>
        private static void AdvanceStage(On.RoR2.Run.orig_AdvanceStage orig, Run self, SceneDef nextScene)
        {
            District? d = nextScene != null ? DeadlockMaps.ByScene(nextScene.cachedName) : null;
            if (d != null && !PartyCheck.AllReady(d, out string why))
            {
                SceneCollection? slot = Addressables.LoadAssetAsync<SceneCollection>($"RoR2/Base/SceneGroups/sgStage{d.Def.StageOrder}.asset").WaitForCompletion();
                var pick = new WeightedSelection<SceneDef>();
                slot?.AddToWeightedSelection(pick, s => DeadlockMaps.ByScene(s.cachedName) == null);
                SceneDef? instead = pick.Count > 0 ? pick.Evaluate(UnityEngine.Random.value) : null;
                Warn($"{d.Def.Name} can't load ({why}): going to {instead?.cachedName ?? "the default next stage"} instead");
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = $"<color=#ffb347>{d.Def.Name} skipped: {why}.</color>" });
                if (instead != null) nextScene = instead;
            }
            orig(self, nextScene);
        }

        /// <summary>The district a command names (its id), or the first one.</summary>
        internal static District Named(ConCommandArgs args, int index)
        {
            if (DeadlockMaps.Districts.Count == 0) throw new ConCommandException("No Deadlock districts.");
            if (args.Count <= index) return DeadlockMaps.Districts[0];
            string id = args.GetArgString(index).ToLowerInvariant();
            return DeadlockMaps.Find(id) ?? throw new ConCommandException(
                $"No district \"{id}\": {string.Join(", ", DeadlockMaps.Districts.ConvertAll(x => x.Id))}");
        }

        [ConCommand(commandName = "cac_map_go", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: go to a Deadlock district now (host; only if the whole party can load it). Optional: its id (default the first).")]
        private static void CCGo(ConCommandArgs args)
        {
            District d = Named(args, 0);
            if (Run.instance == null || d.Stage.Def == null) throw new ConCommandException("Needs a run.");
            if (!PartyCheck.AllReady(d, out string why)) throw new ConCommandException($"{d.Def.Name} can't load: {why}");
            Run.instance.AdvanceStage(d.Stage.Def);
        }

        [ConCommand(commandName = "cac_map_party", flags = ConVarFlags.ExecuteOnServer, helpText = "Cursed Apple: can everyone load each Deadlock district? (host)")]
        private static void CCParty(ConCommandArgs args)
        {
            foreach (District d in DeadlockMaps.Districts)
            {
                bool ok = PartyCheck.AllReady(d, out string why);
                Debug.Log(ok ? $"[DeadlockMap] {d.Id}: everyone can load {d.Def.Name}" : $"[DeadlockMap] {d.Id}: not everyone can load {d.Def.Name}: {why}");
            }
        }

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
    }

    /// <summary>
    /// One district's stage: an empty shell scene that each machine fills at load time from its own converted Deadlock map.
    /// Offered in its stage slot when the whole party has it (see <see cref="DeadlockStages"/>).
    /// </summary>
    public sealed class DistrictStage
    {
        public readonly District District;
        public readonly SceneDef? Def;
        public readonly ConfigEntry<float>? Chance;
        public readonly ConfigEntry<string>? Sky;
        private MapSectionDef D => District.Def;

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[DeadlockMap] " + s);

        internal DistrictStage(District district, ConfigFile config)
        {
            District = district;
            Sky = config.Bind("Deadlock Map", $"{D.Name} Sky", D.Sky,
                "Skybox material borrowed from a vanilla stage (Addressables key).");
            Chance = config.Bind("Deadlock Map", $"{D.Name} Chance", 0.1f,
                $"Chance that {D.Name} replaces the usual stage {D.StageOrder} (0.1 = 10%; 0 = never on its own). Only when everyone in the run can load it.");
            if (Array.IndexOf(MapSectionDef.Scenes, D.Scene) < 0) { Warn($"{D.Id}: scene {D.Scene} isn't in the stage bundle"); return; }
            string token = "CURSEDAPPLE_STAGE_" + D.Id.ToUpperInvariant();
            LanguageAPI.Add(token + "_NAME", D.Name);
            LanguageAPI.Add(token + "_SUBTITLE", D.Subtitle);
            Def = ScriptableObject.CreateInstance<SceneDef>();
            Def.cachedName = D.Scene;
            Def.sceneType = SceneType.Stage;
            Def.stageOrder = D.StageOrder;
            Def.nameToken = token + "_NAME";
            Def.subtitleToken = token + "_SUBTITLE";
            Def.shouldIncludeInLogbook = false;
            Def.validForRandomSelection = false; // random stage orders don't check the party
            Def.filterOutOfBazaar = true;
            Def.environmentColor = new Color(0.45f, 0.55f, 0.7f);
            ContentAddition.AddSceneDef(Def);
        }

        /// <summary>Destinations (the next slot's stages) and music borrowed from vanilla once the catalogs are up.</summary>
        internal void FinishDef()
        {
            if (Def == null) return;
            int next = D.StageOrder % 5 + 1;
            Def.destinationsGroup = Addressables.LoadAssetAsync<SceneCollection>($"RoR2/Base/SceneGroups/sgStage{next}.asset").WaitForCompletion();
            Def.loopedDestinationsGroup = Addressables.LoadAssetAsync<SceneCollection>($"RoR2/Base/SceneGroups/loopSgStage{next}.asset").WaitForCompletion();
            SceneDef? music = Addressables.LoadAssetAsync<SceneDef>(D.MusicFrom).WaitForCompletion();
            if (music != null)
            {
                Def.mainTrack = music.mainTrack;
                Def.bossTrack = music.bossTrack;
                Def.portalMaterial = music.portalMaterial;
                Def.portalMaterialReference = music.portalMaterialReference;
                Def.previewTextureReference = music.previewTextureReference;
            }
        }

        // ---------------------------------------------------------------- building the scene (every machine)

        internal void Populate(Scene scene)
        {
            MapConverter? conv = District.Converter;
            MapBuilder.Built? t = DeadlockMaps.Template(District);
            if (conv == null || t == null)
            {
                // Shouldn't happen (the party check gates loading): a floor so nobody falls forever, and say why.
                Warn($"{D.Name} loaded without a converted map on this machine");
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
            StageDressing.Build(scene, world.transform, section, t.Playable, Sky?.Value ?? "");

            // AI navigation from the geometry (cached after the first time), then the stage's own objects.
            Bounds area = t.Playable;
            Vector3 arrival = world.transform.TransformPoint(District.ArrivalLocal(t.OriginUnits));
            var (ground, air, spawns) = NavBuilder.Get(conv.NavFile, conv.Fingerprint ?? "", world.transform, section.Inside, section.NoWalk, area, arrival);
            // Hand-placed spawns win over the automatic pick.
            float[] hand = D.PlayerSpawns;
            if (hand.Length >= 4)
            {
                spawns = new List<(Vector3 Pos, float Yaw)>();
                for (int i = 0; i + 3 < hand.Length; i += 4)
                    spawns.Add((world.transform.TransformPoint(new Vector3(hand[i], hand[i + 1], hand[i + 2])), hand[i + 3] + world.transform.eulerAngles.y));
            }

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
            csi.SetMonsterDCCSP(Addressables.LoadAssetAsync<DccsPool>(D.MonsterPool).WaitForCompletion());
            csi.SetInteractableDCCSP(Addressables.LoadAssetAsync<DccsPool>(D.InteractablePool).WaitForCompletion());
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
                var dw = wait.AddComponent<DirectorWhenStageReady>();
                dw.District = District;
                dw.Spawns = spawns;
            }
            Log($"{D.Name} populated in {watch.ElapsedMilliseconds} ms ({ground.GetNodeCount()} ground / {air.GetNodeCount()} air nodes; " +
                $"manager: {string.Join(", ", Array.ConvertAll(manager.GetComponentsInChildren<Component>(), c => c.GetType().Name))})");
        }

        /// <summary>Server: starts the vanilla Director (scene, combat) once Stage.instance exists.</summary>
        private class DirectorWhenStageReady : MonoBehaviour
        {
            public District District = null!;
            public List<(Vector3 Pos, float Yaw)> Spawns = new List<(Vector3, float)>();
            private static District? _seeding;

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
                _seeding = District;
                SceneDirector.onTeleporterPlaced += LogTeleporter;
                SceneDirector.onPrePopulateSceneServer += SeedCamps;
                SpawnNewtAltars(District.Def);
                SpawnFixed(District);
                WalkerSlot(District.Def);
                ArmImpPit(District.Def);
                SpawnShops(District);
                GameObject? map = GameObject.Find("DeadlockMap");
                if (map != null) HeroSilhouette.SpawnAll(District.Def, map.transform);
                Log($"director started (stage ready), {Spawns.Count} player spawns placed");
                Destroy(gameObject);
            }

            private static void SpawnNewtAltars(MapSectionDef def)
            {
                float[] a = def.NewtAltars;
                GameObject? world = GameObject.Find("DeadlockMap");
                if (a.Length < 4 || world == null) return;
                GameObject prefab = Addressables.LoadAssetAsync<GameObject>(DeadlockStages.NewtPrefab).WaitForCompletion();
                // As in vanilla stages: the spots are candidates, one altar per stage at one of them (the stage's own RNG).
                int spots = a.Length / 4;
                int i = (Run.instance != null ? Run.instance.stageRng.RangeInt(0, spots) : UnityEngine.Random.Range(0, spots)) * 4;
                Vector3 at = world.transform.TransformPoint(new Vector3(a[i], a[i + 1], a[i + 2]));
                GameObject altar = Instantiate(prefab, at, Quaternion.Euler(0f, a[i + 3] + world.transform.eulerAngles.y, 0f));
                DeadlockStages.SitOnFloor(altar, at.y);
                NetworkServer.Spawn(altar);
                Log($"newt altar placed at spot {i / 4 + 1} of {spots}");
            }

            /// <summary>Arms this stage's Imp Overlord pit (see <see cref="DeadlockStages.ImpPitInteraction"/>).</summary>
            private static void ArmImpPit(MapSectionDef def)
            {
                float[] p = def.ImpLordSpot;
                GameObject? world = GameObject.Find("DeadlockMap");
                DeadlockStages._impPit = null;
                if (p.Length < 4 || world == null || Stage.instance == null) return;
                DeadlockStages._impPit = (Stage.instance, world.transform.TransformPoint(new Vector3(p[0], p[1], p[2])));
                Log("the Imp Overlord's pit is armed");
            }

            /// <summary>
            /// The Walker's spot: the teleporter (placed there by <see cref="PlaceTeleporter"/> when the director populates) or,
            /// the rest of the time, one major interactable.
            /// </summary>
            private static void WalkerSlot(MapSectionDef def)
            {
                float[] w = def.WalkerSlot;
                GameObject? world = GameObject.Find("DeadlockMap");
                if (w.Length < 4 || world == null || Run.instance == null || DirectorCore.instance == null) return;
                Vector3 at = world.transform.TransformPoint(new Vector3(w[0], w[1], w[2]));
                float yaw = w[3] + world.transform.eulerAngles.y;
                if (Run.instance.stageRng.nextNormalizedFloat < (DeadlockStages.WalkerTeleporter?.Value ?? 0.5f))
                {
                    DeadlockStages._walkerTeleporter = (at, yaw);
                    Log("the Walker's spot holds the teleporter this stage");
                    return;
                }
                string key = DeadlockStages.WalkerInteractables[Run.instance.stageRng.RangeInt(0, DeadlockStages.WalkerInteractables.Length)];
                SpawnCard? card = Addressables.LoadAssetAsync<SpawnCard>(key).WaitForCompletion();
                if (card == null) { Warn($"walker spot: no spawn card {key}"); return; }
                GameObject? spawned = DeadlockStages.SpawnUpright(card, at, yaw, new Xoroshiro128Plus(Run.instance.stageRng.nextUlong));
                Log(spawned != null ? $"the Walker's spot holds {card.name} this stage" : $"{card.name} didn't fit at the Walker's spot");
            }

            /// <summary>The district's fixed interactables (a legendary chest on a bell tower...), each exactly at its spot.</summary>
            private static void SpawnFixed(District district)
            {
                MapSectionDef def = district.Def;
                GameObject? world = GameObject.Find("DeadlockMap");
                MapBuilder.Built? t = DeadlockMaps.Template(district);
                if (world == null || t == null || def.FixedInteractables.Length == 0 || DirectorCore.instance == null) return;
                int placed = 0;
                var anchor = new GameObject("FixedAnchor");
                foreach (FixedSpawn f in def.FixedInteractables)
                {
                    SpawnCard? card = Addressables.LoadAssetAsync<SpawnCard>(f.Card).WaitForCompletion();
                    if (card == null) { Warn($"fixed interactable: no spawn card {f.Card}"); continue; }
                    if (f.Atop != null)
                    {
                        if (!HighestFlat(world.transform, t.OriginUnits, f.Atop, out Vector3 top)) { Warn($"fixed interactable {f.Card}: nothing flat on top of its box"); continue; }
                        anchor.transform.position = top;
                    }
                    else anchor.transform.position = world.transform.TransformPoint(new Vector3(f.X, f.Y, f.Z));
                    anchor.transform.rotation = Quaternion.Euler(0f, f.Yaw + world.transform.eulerAngles.y, 0f);
                    var rule = new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Direct, spawnOnTarget = anchor.transform };
                    var rng = new Xoroshiro128Plus(Run.instance != null ? Run.instance.stageRng.nextUlong : 0UL);
                    if (card is InteractableSpawnCard isc && (isc.orientToFloor || isc.slightlyRandomizeOrientation))
                    {
                        // Upright, whatever the roof under it does (a copy of the card that doesn't tilt it to the surface).
                        isc = Instantiate(isc);
                        isc.orientToFloor = false;
                        isc.slightlyRandomizeOrientation = false;
                        card = isc;
                    }
                    if (DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, rule, rng)) != null) placed++;
                    else Warn($"fixed interactable {f.Card} couldn't be placed at {anchor.transform.position}");
                }
                Destroy(anchor);
                Log($"{placed} fixed interactables placed");
            }

            /// <summary>The highest flat (walkable) spot inside a box of Deadlock units, by a grid of rays straight down.</summary>
            private static bool HighestFlat(Transform world, Vector3 originUnits, float[] box, out Vector3 best)
            {
                best = default;
                const int n = 24;
                bool under = box.Length >= 5;
                // Every flat hit, then the highest floor among them.
                var hits = new List<Vector3>();
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n; j++)
                    {
                        float x = Mathf.Lerp(box[0], box[2], i / (float)n), y = Mathf.Lerp(box[1], box[3], j / (float)n);
                        Vector3 from = under
                            ? world.TransformPoint(MapCache.ToUnity(new Vector3(x, y, box[4]), originUnits)) // under the roof
                            : world.TransformPoint(MapCache.ToUnity(new Vector3(x, y, 0f), originUnits)) + Vector3.up * 400f;
                        if (FlatBelow(from, out Vector3 p)) hits.Add(p);
                    }
                if (hits.Count == 0) return false;
                float top = hits.Max(h => h.y);
                List<Vector3> floor = hits.Where(h => h.y > top - 0.5f).ToList();
                if (box.Length >= 7)
                {
                    // Nearest the point that still leaves the chest's footprint (1.5 m around) flat and 3 m clear of the point.
                    Vector3 near = world.TransformPoint(MapCache.ToUnity(new Vector3(box[5], box[6], 0f), originUnits));
                    float Flat2(Vector3 h) => new Vector2(h.x - near.x, h.z - near.z).magnitude;
                    bool Roomy(Vector3 h)
                    {
                        foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                            if (!FlatBelow(h + d * 1.5f + Vector3.up * 1.5f, out Vector3 q) || Mathf.Abs(q.y - h.y) > 0.3f) return false;
                        return true;
                    }
                    List<Vector3> ok = floor.Where(h => Flat2(h) >= 3f && Roomy(h)).OrderBy(Flat2).ToList();
                    best = ok.Count > 0 ? ok[0] : floor.OrderByDescending(Flat2).First();
                }
                else best = floor[0];
                Log($"fixed interactable spot at {best} (top floor {top:0.##}, {floor.Count} spots on it)");
                return true;
            }

            private static bool FlatBelow(Vector3 from, out Vector3 p)
            {
                p = default;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 800f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return false;
                if (hit.normal.y < 0.9f) return false; // a slope or a spire: not somewhere to stand a chest
                p = hit.point;
                return true;
            }

            /// <summary>
            /// A Curiosity shop at each of Deadlock's shop counters in the district: open to everyone, bought from as often
            /// as you like, priced like chests (small / large / legendary base costs, scaled by difficulty the same way). Deadlock's
            /// own items only.
            /// </summary>
            private static void SpawnShops(District district)
            {
                GameObject? world = GameObject.Find("DeadlockMap");
                MapBuilder.Built? t = DeadlockMaps.Template(district);
                if (world == null || t == null) return;
                int[] b = district.Def.Shops;
                int placed = 0;
                for (int i = 0; i + 5 < b.Length; i += 6)
                {
                    // The counter's trigger box: its centre, dropped onto the floor beneath it.
                    var centre = new Vector3((b[i] + b[i + 3]) / 2f, (b[i + 1] + b[i + 4]) / 2f, b[i + 5]);
                    Vector3 top = world.transform.TransformPoint(MapCache.ToUnity(centre, t.OriginUnits));
                    if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 12f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    if (MapShop.Spawn(hit.point,
                            DeadlockStages.ShopWhite?.Value ?? 25, DeadlockStages.ShopGreen?.Value ?? 50, DeadlockStages.ShopRed?.Value ?? 400) != null)
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
                District? district = _seeding;
                GameObject? world = GameObject.Find("DeadlockMap");
                MapBuilder.Built? t = district != null ? DeadlockMaps.Template(district) : null;
                ClassicStageInfo? csi = ClassicStageInfo.instance;
                if (district == null || world == null || t == null || csi == null || csi.interactableCategories == null || DirectorCore.instance == null) return;
                var section = new SectionSpace(district.Def, t.OriginUnits);

                // Camp clusters: spawn points within ~6 m of each other are one camp.
                int[] sp = district.Def.InteractableSpots;
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
        public readonly MapSectionDef Def;
        private readonly Vector3 _origin;
        private readonly Vector2[] _outline;

        public SectionSpace(MapSectionDef def, Vector3 originUnits)
        {
            Def = def;
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
            int[] b = Def.NoWalk;
            for (int i = 0; i + 5 < b.Length; i += 6)
                if (s.x >= b[i] - 32 && s.y >= b[i + 1] - 32 && s.z >= b[i + 2] - 32 && s.x <= b[i + 3] + 32 && s.y <= b[i + 4] + 32 && s.z <= b[i + 5] + 32)
                    return true;
            return false;
        }
    }
}
