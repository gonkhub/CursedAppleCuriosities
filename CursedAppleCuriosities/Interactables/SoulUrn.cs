using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Interactables
{
    /// <summary>
    /// The Soul Urn (user, 2.0): 30% of stages have one lying on the ground, well away from the teleporter. Pick it up
    /// and carry it to the teleporter within 30 s without taking damage; getting hit, running out of time or the carrier
    /// dying sends it back to where it spawned. A delivery showers the carrier with items at chest rarity weights,
    /// more the further into the run.
    /// <para>The server owns the state; every machine (host included) learns it from <see cref="SoulUrnMessage"/> and
    /// raises the events below, so they suit client-side sound and HUD.</para>
    /// </summary>
    public static class SoulUrn
    {
        public static bool Enabled;
        public static float SpawnChance;
        public static float TimeLimit;
        public static float MinTeleporterDistance;
        public static float MaxTeleporterDistance;
        public static float DeliveryRadius;
        public static int BaseItems;
        public static float ItemsPerStage;
        public static int MaxItems;
        public static float WhiteWeight, GreenWeight, RedWeight;
        public static bool DebugAlwaysSpawn;

        /// <summary>Every machine: the urn is on the ground at its spawn point (it just spawned, or came back).</summary>
        public static event Action<SoulUrnController>? Waiting;
        /// <summary>Every machine: someone picked the urn up.</summary>
        public static event Action<SoulUrnController, CharacterBody>? PickedUp;
        /// <summary>Every machine: the carrier lost the urn (hit, timer ran out, died); it is back home right after.</summary>
        public static event Action<SoulUrnController, CharacterBody?, SoulUrnDropReason>? Dropped;
        /// <summary>Every machine: the urn reached the teleporter (the rewards are dropping).</summary>
        public static event Action<SoulUrnController, CharacterBody?>? Delivered;

        internal static void RaiseWaiting(SoulUrnController u) => Safe(() => Waiting?.Invoke(u));
        internal static void RaisePickedUp(SoulUrnController u, CharacterBody b) => Safe(() => PickedUp?.Invoke(u, b));
        internal static void RaiseDropped(SoulUrnController u, CharacterBody? b, SoulUrnDropReason r) => Safe(() => Dropped?.Invoke(u, b, r));
        internal static void RaiseDelivered(SoulUrnController u, CharacterBody? b) => Safe(() => Delivered?.Invoke(u, b));

        private static void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogError($"[SoulUrn] Event handler failed: {e}"); }
        }

        internal static readonly Color SoulColor = new Color(0.45f, 0.9f, 1f, 1f);
        internal const string SoulColorHex = "#73E6FF";

        private static GameObject? _prefab;
        private static InteractableSpawnCard? _card;
        internal static GameObject? UseEffect;

        internal const string NameToken = "CURSEDAPPLE_SOULURN_NAME";
        internal const string ContextToken = "CURSEDAPPLE_SOULURN_CONTEXT";

        public static void Init(ConfigFile config)
        {
            const string S = "Soul Urn";
            Enabled = config.Bind(S, "Enabled", true,
                "Spawn the Soul Urn: carry it to the teleporter in time, without taking damage, for a pile of items.").Value;
            SpawnChance = config.Bind(S, "Spawn Chance", 0.3f, "Chance that a Soul Urn appears on any stage (0.3 = 30%).").Value;
            TimeLimit = config.Bind(S, "Time Limit", 30f, "Seconds to reach the teleporter after picking the urn up.").Value;
            MinTeleporterDistance = config.Bind(S, "Min Teleporter Distance", 60f,
                "The urn always spawns at least this far (m) from the teleporter.").Value;
            MaxTeleporterDistance = config.Bind(S, "Max Teleporter Distance", 160f,
                "The urn prefers to spawn within this distance (m) of the teleporter, so it can be delivered in time. Further out only when the map has no room closer.").Value;
            DeliveryRadius = config.Bind(S, "Delivery Radius", 12f, "How close (m) to the teleporter the carrier must get.").Value;
            BaseItems = config.Bind(S, "Base Items", 3, "Items for a delivery on stage 1.").Value;
            ItemsPerStage = config.Bind(S, "Items Per Stage", 1f, "Extra items per stage already cleared this run (fractions add up and round down).").Value;
            MaxItems = config.Bind(S, "Max Items", 30, "Most items a single delivery can drop.").Value;
            WhiteWeight = config.Bind(S, "White Weight", 0.8f, "Relative chance for each reward to be white (common), as a small chest.").Value;
            GreenWeight = config.Bind(S, "Green Weight", 0.2f, "Relative chance for each reward to be green (uncommon).").Value;
            RedWeight = config.Bind(S, "Red Weight", 0.01f, "Relative chance for each reward to be red (legendary).").Value;
            DebugAlwaysSpawn = config.Bind(S, "Debug - Spawn Every Stage", false, "Testing: a Soul Urn spawns on every stage.").Value;

            LanguageAPI.Add(NameToken, "Soul Urn");
            LanguageAPI.Add(ContextToken, "Pick up the Soul Urn");

            NetworkingAPI.RegisterMessageType<SoulUrnMessage>();

            BuildPrefab();
            if (_prefab == null) return;

            SceneDirector.onPostPopulateSceneServer += OnPostPopulateScene;
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
            SoulUrnHud.Hook();
        }

        // ───────────────────────────────────── prefab ─────────────────────────────────────

        private static void BuildPrefab()
        {
            GameObject? model = LoadAsset<GameObject>("RoR2/Base/SiphonOnLowHealth/PickupSiphonOnLowHealth.prefab");
            UseEffect = LoadAsset<GameObject>("RoR2/Base/Common/VFX/ShrineUseEffect.prefab");
            if (model == null) CursedAppleCuriositiesPlugin.Logger?.LogWarning("[SoulUrn] Mired Urn model not found; the urn is only a light.");

            var temp = new GameObject("CursedAppleSoulUrn");
            temp.AddComponent<NetworkIdentity>();
            GameObject p = temp.InstantiateClone("CursedAppleSoulUrn", true);
            UnityEngine.Object.Destroy(temp);

            var interaction = p.AddComponent<GenericInteraction>();
            interaction.contextToken = ContextToken;
            interaction.interactability = Interactability.Available;
            interaction.shouldShowOnScanner = true;
            interaction.shouldProximityHighlight = true;
            interaction.onActivation = new GenericInteraction.InteractorUnityEvent();
            p.AddComponent<GenericDisplayNameProvider>().displayToken = NameToken;

            // The visual: the Mired Urn's pickup model, bobbing and spinning like an item on the ground.
            var visual = new GameObject("Model");
            visual.transform.SetParent(p.transform, false);
            Renderer? mainRenderer = null;
            if (model != null)
            {
                GameObject m = UnityEngine.Object.Instantiate(model, visual.transform, false);
                m.name = "Urn";
                m.transform.localPosition = Vector3.zero;
                foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (Rigidbody rb in m.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
                foreach (MonoBehaviour mb in m.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
                mainRenderer = m.GetComponentInChildren<MeshRenderer>(true) ?? (Renderer?)m.GetComponentInChildren<SkinnedMeshRenderer>(true);
            }
            var glow = new GameObject("SoulGlow");
            glow.transform.SetParent(visual.transform, false);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = SoulColor;
            light.range = 14f;
            light.intensity = 8f;
            light.shadows = LightShadows.None;

            // What the interactor's raycast hits: a trigger on the pickups layer that points back at the urn.
            var hit = new GameObject("Interact");
            hit.transform.SetParent(p.transform, false);
            hit.layer = LayerIndex.pickups.intVal;
            var sphere = hit.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 1.4f;
            sphere.center = new Vector3(0f, 1f, 0f);
            hit.AddComponent<EntityLocator>().entity = p;

            if (mainRenderer != null)
            {
                var highlight = p.AddComponent<Highlight>();
                highlight.targetRenderer = mainRenderer;
                highlight.highlightColor = Highlight.HighlightColor.interactive;
                highlight.strength = 1f;
            }

            p.AddComponent<SoulUrnController>();
            _prefab = p;

            _card = ScriptableObject.CreateInstance<InteractableSpawnCard>();
            _card.name = "iscCursedAppleSoulUrn";
            _card.prefab = p;
            _card.sendOverNetwork = true;
            _card.hullSize = HullClassification.Human;
            _card.nodeGraphType = RoR2.Navigation.MapNodeGroup.GraphType.Ground;
            _card.requiredFlags = RoR2.Navigation.NodeFlags.None;
            _card.forbiddenFlags = RoR2.Navigation.NodeFlags.NoChestSpawn;
            _card.directorCreditCost = 0;
            _card.occupyPosition = true;
            _card.orientToFloor = false;
        }

        private static T? LoadAsset<T>(string key) where T : UnityEngine.Object
        {
            try { return Addressables.LoadAssetAsync<T>(key).WaitForCompletion(); }
            catch { return null; }
        }

        // ───────────────────────────────────── spawning ─────────────────────────────────────

        private static void OnPostPopulateScene(SceneDirector director)
        {
            Run? run = Run.instance;
            if (!Enabled || !CuriositiesExpansion.Enabled || !NetworkServer.active || run == null || _card == null || DirectorCore.instance == null) return;
            SceneDef? scene = SceneCatalog.mostRecentSceneDef;
            if (scene == null || scene.sceneType != SceneType.Stage || scene.isFinalStage) return;
            TeleporterInteraction? tp = TeleporterInteraction.instance;
            if (tp == null) return; // nowhere to deliver it

            var rng = new Xoroshiro128Plus(run.seed ^ (ulong)(run.stageClearCount + 1) * 0xA24BAED4963EE407UL);
            if (!DebugAlwaysSpawn && rng.nextNormalizedFloat >= SpawnChance) return;

            // Nearest band first, so the urn is far from the teleporter but still deliverable in time.
            float min = Mathf.Max(0f, MinTeleporterDistance);
            float max = Mathf.Max(min + 10f, MaxTeleporterDistance);
            foreach (float far in new[] { max, max * 2f, 10000f })
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(_card, new DirectorPlacementRule
                    {
                        placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                        minDistance = min,
                        maxDistance = far,
                        spawnOnTarget = tp.transform,
                    }, rng));
                    if (spawned == null) continue;
                    if (Vector3.Distance(spawned.transform.position, tp.transform.position) < min)
                    {
                        NetworkServer.Destroy(spawned); // node distance and straight-line distance can disagree
                        continue;
                    }
                    CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[SoulUrn] Spawned at {spawned.transform.position}, " +
                        $"{Vector3.Distance(spawned.transform.position, tp.transform.position):0} m from the teleporter.");
                    return;
                }
            }
            CursedAppleCuriositiesPlugin.Logger?.LogWarning("[SoulUrn] No spot far enough from the teleporter for the urn this stage.");
        }

        [ConCommand(commandName = "cac_urn_spawn", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: spawn a Soul Urn near you (ignores the teleporter distance).")]
        private static void CCUrnSpawn(ConCommandArgs args)
        {
            if (_card == null || DirectorCore.instance == null) throw new ConCommandException("No director here, or the Soul Urn failed to load.");
            CharacterBody? body = args.senderBody;
            if (body == null) throw new ConCommandException("You need a living body.");
            if (args.Count > 0 && args.GetArgString(0) == "here" && _prefab != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(body.inputBank != null ? body.inputBank.aimDirection : body.transform.forward, Vector3.up).normalized;
                GameObject go = UnityEngine.Object.Instantiate(_prefab, body.footPosition + fwd * 2f, Quaternion.identity);
                NetworkServer.Spawn(go);
                Debug.Log($"[SoulUrn] Spawned at your feet, {go.transform.position}.");
                return;
            }
            GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(_card, new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                minDistance = 4f,
                maxDistance = 20f,
                spawnOnTarget = body.transform,
            }, new Xoroshiro128Plus((ulong)UnityEngine.Random.Range(1, int.MaxValue))));
            Debug.Log(spawned != null ? $"[SoulUrn] Spawned at {spawned.transform.position}." : "[SoulUrn] No room to place an urn nearby.");
        }

        [ConCommand(commandName = "cac_urn_tp", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: teleport yourself next to the teleporter (Soul Urn testing).")]
        private static void CCUrnTeleport(ConCommandArgs args)
        {
            CharacterBody? body = args.senderBody;
            TeleporterInteraction? tp = TeleporterInteraction.instance;
            if (body == null || tp == null) throw new ConCommandException("Needs a living body and a teleporter.");
            TeleportHelper.TeleportBody(body, tp.transform.position + Vector3.up * 2f + tp.transform.forward * 4f, false);
        }

        // ───────────────────────────────────── rules (server) ─────────────────────────────────────

        private static void OnServerDamageDealt(DamageReport report)
        {
            CharacterBody? victim = report.victimBody;
            if (victim == null || report.damageDealt <= 0f) return;
            foreach (SoulUrnController urn in SoulUrnController.Instances)
                if (urn.Carrier == victim) urn.DropServer(SoulUrnDropReason.Damaged);
        }

        /// <summary>The number of items a delivery drops right now.</summary>
        public static int RewardCount(Run run)
        {
            int n = BaseItems + Mathf.FloorToInt(ItemsPerStage * run.stageClearCount);
            return Mathf.Clamp(n, 1, Mathf.Max(1, MaxItems));
        }

        internal static void DropRewards(Vector3 origin)
        {
            Run? run = Run.instance;
            if (run == null) return;
            var rng = new Xoroshiro128Plus(run.treasureRng.nextUlong);
            int count = RewardCount(run);
            int dropped = 0;
            for (int i = 0; i < count; i++)
            {
                PickupIndex pickup = RollPickup(run, rng);
                if (pickup == PickupIndex.none) continue;
                // A fountain around the carrier, like a boss drop.
                Vector3 around = Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward;
                Vector3 velocity = Vector3.up * 18f + around * (4f + 3f * (i % 3));
                PickupDropletController.CreatePickupDroplet(new UniquePickup { pickupIndex = pickup }, origin, velocity, false);
                dropped++;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[SoulUrn] Delivered: {dropped} items (stage {run.stageClearCount + 1}).");
        }

        /// <summary>White/green/red by the configured weights (a small chest's by default), then any item of that tier this run allows.</summary>
        private static PickupIndex RollPickup(Run run, Xoroshiro128Plus rng)
        {
            var tiers = new (float Weight, List<PickupIndex> List)[]
            {
                (WhiteWeight, run.availableTier1DropList),
                (GreenWeight, run.availableTier2DropList),
                (RedWeight, run.availableTier3DropList),
            };
            float total = 0f;
            foreach (var t in tiers) if (t.List.Count > 0) total += Mathf.Max(0f, t.Weight);
            if (total <= 0f) return PickupIndex.none;
            float roll = rng.nextNormalizedFloat * total;
            foreach (var t in tiers)
            {
                if (t.List.Count == 0 || t.Weight <= 0f) continue;
                if (roll < t.Weight) return t.List[rng.RangeInt(0, t.List.Count)];
                roll -= t.Weight;
            }
            foreach (var t in tiers) if (t.List.Count > 0 && t.Weight > 0f) return t.List[rng.RangeInt(0, t.List.Count)];
            return PickupIndex.none;
        }

        internal static void Broadcast(string text) => Chat.SendBroadcastChat(new Chat.SimpleChatMessage
        {
            baseToken = $"<color={SoulColorHex}>{text}</color>",
        });

        internal static string NameOf(CharacterBody? body)
        {
            if (body == null) return "Someone";
            CharacterMaster? m = body.master;
            return m != null && m.playerCharacterMasterController != null ? m.playerCharacterMasterController.GetDisplayName() : body.GetDisplayName();
        }
    }

    public enum SoulUrnDropReason : byte { Damaged, Expired, CarrierLost }

    public enum SoulUrnState : byte { Waiting, Carried, Dropped, Delivered }

    /// <summary>
    /// One urn, on every machine. The root never moves (it marks the spawn point and holds the interaction); only the
    /// "Model" child follows the carrier, positioned locally from the synced carrier, so no transform sync is needed.
    /// </summary>
    public class SoulUrnController : MonoBehaviour
    {
        public static readonly List<SoulUrnController> Instances = new List<SoulUrnController>();

        /// <summary>The body carrying the urn, or null while it waits.</summary>
        public CharacterBody? Carrier { get; private set; }
        public bool IsDelivered { get; private set; }
        public bool IsWaiting => Carrier == null && !IsDelivered;
        /// <summary>The spawn point, where it waits and returns to.</summary>
        public Vector3 Home => transform.position;
        /// <summary>Seconds left to deliver (0 when not carried).</summary>
        public float Remaining => Carrier != null ? Mathf.Max(0f, _deadline - Time.time) : 0f;

        private float _deadline;
        private bool _carried; // Carrier alone can't tell: a destroyed body compares equal to null
        private GenericInteraction? _interaction;
        private Transform? _model;
        private float _groundScale = 1f;
        private Vector3 _modelPos;
        private float _deliveredAt;

        private const float GroundHeight = 1.4f; // the urn's size on the ground (m)
        private const float CarriedScale = 0.75f;

        private void Awake()
        {
            _interaction = GetComponent<GenericInteraction>();
            _model = transform.Find("Model");
            if (_interaction != null) _interaction.onActivation.AddListener(OnActivated);
        }

        private void OnEnable() => Instances.Add(this);
        private void OnDisable() => Instances.Remove(this);

        private void Start()
        {
            // Bounds only exist once the model is live: scale it to a fixed size whatever the pickup model's units.
            if (_model != null)
            {
                Bounds? b = null;
                foreach (Renderer r in _model.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (b == null) b = r.bounds;
                    else { Bounds bb = b.Value; bb.Encapsulate(r.bounds); b = bb; }
                }
                float size = b.HasValue ? Mathf.Max(b.Value.size.x, b.Value.size.y, b.Value.size.z) : 0f;
                _groundScale = size > 0.01f ? GroundHeight / size : 1f;
                _model.localScale = Vector3.one * _groundScale;
                _modelPos = transform.position + Vector3.up * 0.9f;
            }
            SoulUrn.RaiseWaiting(this);
        }

        private void Update()
        {
            if (NetworkServer.active) ServerTick();
            if (_model == null) return;

            if (IsDelivered)
            {
                // Rises and shrinks away into the teleporter.
                float t = Mathf.Clamp01((Time.time - _deliveredAt) / 1f);
                _model.localScale = Vector3.one * _groundScale * CarriedScale * (1f - t);
                _modelPos += Vector3.up * 4f * Time.deltaTime;
                _model.position = _modelPos;
                return;
            }

            Vector3 target;
            float scale;
            if (Carrier != null)
            {
                Collider? c = Carrier.GetComponent<Collider>();
                float top = c != null ? c.bounds.max.y : Carrier.corePosition.y + 1f;
                target = new Vector3(Carrier.corePosition.x, top + 0.9f, Carrier.corePosition.z);
                scale = _groundScale * CarriedScale;
                _modelPos = Vector3.Lerp(_modelPos, target, 1f - Mathf.Exp(-25f * Time.deltaTime));
            }
            else
            {
                target = transform.position + Vector3.up * (0.9f + 0.2f * Mathf.Sin(Time.time * 2f));
                scale = _groundScale;
                _modelPos = target;
            }
            _model.position = _modelPos;
            _model.rotation = Quaternion.Euler(0f, Time.time * 60f % 360f, 0f);
            _model.localScale = Vector3.Lerp(_model.localScale, Vector3.one * scale, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        // ───────────────────────────────────── server ─────────────────────────────────────

        private void OnActivated(Interactor activator)
        {
            if (!NetworkServer.active || IsDelivered || _carried || activator == null) return;
            CharacterBody? body = activator.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive) return;

            _deadline = Time.time + SoulUrn.TimeLimit;
            SetCarrier(body);
            if (_interaction != null) _interaction.Networkinteractability = Interactability.Disabled;
            Send(SoulUrnState.Carried, body, SoulUrn.TimeLimit);
            SoulUrn.Broadcast($"{SoulUrn.NameOf(body)} picked up the Soul Urn.");
            Effect(transform.position + Vector3.up);
        }

        private void ServerTick()
        {
            if (!_carried || IsDelivered) return;
            if (Carrier == null || Carrier.healthComponent == null || !Carrier.healthComponent.alive)
            {
                DropServer(SoulUrnDropReason.CarrierLost);
                return;
            }
            TeleporterInteraction? tp = TeleporterInteraction.instance;
            if (tp != null && Vector3.Distance(Carrier.footPosition, tp.transform.position) <= SoulUrn.DeliveryRadius)
            {
                DeliverServer();
                return;
            }
            if (Time.time >= _deadline) DropServer(SoulUrnDropReason.Expired);
        }

        internal void DropServer(SoulUrnDropReason reason)
        {
            if (!NetworkServer.active || !_carried || IsDelivered) return;
            CharacterBody? who = Carrier ? Carrier : null;
            Send(SoulUrnState.Dropped, who, 0f, reason);
            ApplyDropped(who, reason);
            if (_interaction != null) _interaction.Networkinteractability = Interactability.Available;
            SoulUrn.Broadcast($"{SoulUrn.NameOf(who)} dropped the Soul Urn.");
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[SoulUrn] Dropped ({reason}); back at {transform.position}.");
        }

        private void DeliverServer()
        {
            CharacterBody? who = Carrier;
            Vector3 origin = _modelPos;
            Send(SoulUrnState.Delivered, who, 0f);
            ApplyDelivered(who);
            if (_interaction != null) _interaction.Networkinteractability = Interactability.Disabled;
            SoulUrn.Broadcast($"{SoulUrn.NameOf(who)} delivered the Soul Urn.");
            Effect(origin);
            SoulUrn.DropRewards(origin);
            Destroy(gameObject, 3f); // after the clients have played the delivery
        }

        private void Send(SoulUrnState state, CharacterBody? carrier, float remaining, SoulUrnDropReason reason = default) =>
            new SoulUrnMessage
            {
                Urn = gameObject,
                Carrier = carrier != null ? carrier.gameObject : null,
                State = state,
                Reason = reason,
                Remaining = remaining,
            }.Send(NetworkDestination.Clients);

        private static void Effect(Vector3 at)
        {
            if (SoulUrn.UseEffect == null) return;
            EffectManager.SpawnEffect(SoulUrn.UseEffect, new EffectData
            {
                origin = at,
                rotation = Quaternion.identity,
                scale = 1.5f,
                color = new Color32(115, 230, 255, 255),
            }, transmit: true);
        }

        // ───────────────────────────────────── every machine ─────────────────────────────────────

        private void SetCarrier(CharacterBody body)
        {
            if (_carried && Carrier == body) return;
            Carrier = body;
            _carried = true;
            SoulUrn.RaisePickedUp(this, body);
        }

        private void ApplyDropped(CharacterBody? who, SoulUrnDropReason reason)
        {
            if (!_carried) return;
            Carrier = null;
            _carried = false;
            _modelPos = transform.position + Vector3.up * 0.9f;
            SoulUrn.RaiseDropped(this, who, reason);
            SoulUrn.RaiseWaiting(this);
        }

        private void ApplyDelivered(CharacterBody? who)
        {
            if (IsDelivered) return;
            IsDelivered = true;
            Carrier = null;
            _carried = false;
            _deliveredAt = Time.time;
            SoulUrn.RaiseDelivered(this, who);
        }

        internal void OnMessage(SoulUrnMessage msg)
        {
            if (NetworkServer.active) return; // the server already applied it
            CharacterBody? body = msg.Carrier != null ? msg.Carrier.GetComponent<CharacterBody>() : null;
            switch (msg.State)
            {
                case SoulUrnState.Carried:
                    if (body == null) return;
                    _deadline = Time.time + msg.Remaining;
                    SetCarrier(body);
                    break;
                case SoulUrnState.Dropped:
                    ApplyDropped(body != null ? body : Carrier, msg.Reason);
                    break;
                case SoulUrnState.Delivered:
                    ApplyDelivered(body);
                    break;
            }
        }
    }

    public class SoulUrnMessage : INetMessage
    {
        public GameObject? Urn;
        public GameObject? Carrier;
        public SoulUrnState State;
        public SoulUrnDropReason Reason;
        public float Remaining;

        public void Serialize(NetworkWriter w)
        {
            w.Write(Urn);
            w.Write(Carrier);
            w.Write((byte)State);
            w.Write((byte)Reason);
            w.Write(Remaining);
        }

        public void Deserialize(NetworkReader r)
        {
            Urn = r.ReadGameObject();
            Carrier = r.ReadGameObject();
            State = (SoulUrnState)r.ReadByte();
            Reason = (SoulUrnDropReason)r.ReadByte();
            Remaining = r.ReadSingle();
        }

        public void OnReceived()
        {
            if (Urn != null && Urn.TryGetComponent(out SoulUrnController urn)) urn.OnMessage(this);
        }
    }

    /// <summary>The delivery countdown above the crosshair, for every player while anyone carries an urn.</summary>
    public class SoulUrnHud : MonoBehaviour
    {
        private HUD _hud = null!;
        private TMPro.TextMeshProUGUI? _text;

        public static void Hook()
        {
            On.RoR2.UI.HUD.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<SoulUrnHud>()._hud = self; };
        }

        private void Build()
        {
            var go = new GameObject("SoulUrnTimer", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_hud.mainContainer != null ? _hud.mainContainer.transform : _hud.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 170f);
            rt.sizeDelta = new Vector2(700f, 80f);
            _text = go.AddComponent<TMPro.TextMeshProUGUI>();
            TMPro.TextMeshProUGUI? hudText = _hud.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (hudText != null && hudText.font != null) _text.font = hudText.font;
            _text.fontSize = 34f;
            _text.alignment = TMPro.TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.richText = true;
        }

        private void Update()
        {
            SoulUrnController? carried = null;
            foreach (SoulUrnController u in SoulUrnController.Instances)
                if (u.Carrier != null) { carried = u; break; }
            if (carried == null)
            {
                if (_text != null) _text.gameObject.SetActive(false);
                return;
            }
            if (_text == null) Build();
            _text!.gameObject.SetActive(true);

            float left = carried.Remaining;
            bool mine = carried.Carrier!.gameObject == _hud.targetBodyObject;
            Color c = left > 10f ? SoulUrn.SoulColor : Color.Lerp(new Color(1f, 0.3f, 0.25f), SoulUrn.SoulColor, left / 10f);
            string hint = mine ? "Bring it to the Teleporter. Don't get hit!" : $"{SoulUrn.NameOf(carried.Carrier)} carries the Soul Urn";
            _text.color = c;
            _text.text = $"SOUL URN  {left:0.0}\n<size=55%><color=#FFFFFF>{hint}</color></size>";
        }
    }
}
