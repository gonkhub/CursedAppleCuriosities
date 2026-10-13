using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Broker
{
    /// <summary>
    /// The Cursed Broker: a rare shrine (user, 1.7: 10% on any stage, 50% on stage 3 of every loop, and 50% per Bazaar
    /// visit until one has appeared there this loop). Each player may curse
    /// one non-lunar item in their inventory once per broker. Opening it rolls one penalty per eligible item for
    /// that player (kept for that broker, so reopening can't reroll); the menu's tooltips show what each curse would
    /// improve and its penalty. A curse roughly doubles the item and every copy of it (<see cref="CurseRules"/>) and
    /// applies its penalty for the rest of the run (<see cref="CursePenalties"/>).
    /// </summary>
    public static class CursedBroker
    {
        public static bool Enabled;
        public static float SpawnChance;
        public static float StageThreeChance;
        public static float BazaarChance;
        public static bool DebugAlwaysSpawn;
        private const int StagesPerLoop = 5;

        private static GameObject? _prefab;
        private static InteractableSpawnCard? _card;
        private static GameObject? _useEffect;

        private const string NameToken = "CURSEDAPPLE_BROKER_NAME";
        private const string ContextToken = "CURSEDAPPLE_BROKER_CONTEXT";

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("Cursed Broker", "Enabled", true,
                "Spawn the Cursed Broker, a rare shrine that curses one of your items: a far stronger item, a harsh penalty.").Value;
            SpawnChance = config.Bind("Cursed Broker", "Spawn Chance", 0.1f,
                "Chance that a Cursed Broker appears on any stage (0.1 = 10%). Stage 3 of each loop uses its own chance.").Value;
            StageThreeChance = config.Bind("Cursed Broker", "Stage 3 Chance", 0.5f,
                "Chance that a Cursed Broker appears on stage 3 of every loop (0.5 = 50%).").Value;
            BazaarChance = config.Bind("Cursed Broker", "Bazaar Chance", 0.5f,
                "Chance per visit to the Bazaar Between Time that a Cursed Broker appears there. Once one has, no more appear in the Bazaar until the next loop.").Value;
            DebugAlwaysSpawn = config.Bind("Cursed Broker", "Debug - Spawn Every Stage", false,
                "Testing: a Cursed Broker spawns on every stage, ignoring stage number and chance.").Value;

            LanguageAPI.Add(NameToken, "Cursed Broker");
            LanguageAPI.Add(ContextToken, "Consult the Cursed Broker");

            CurseLedger.Hook();
            CursePenalties.Hook();
            NetworkingAPI.RegisterMessageType<BrokerOfferMessage>();

            BuildPrefab();
            if (_prefab == null) return;

            SceneDirector.onPostPopulateSceneServer += OnPostPopulateScene;
            Stage.onServerStageBegin += OnServerStageBegin;
            Run.onRunStartGlobal += _ => _bazaarBrokerLoop = -1;
            On.RoR2.PickupPickerController.GetContextString += GetContextString;
            On.RoR2.PickupPickerController.GetInteractability += GetInteractability;
            On.RoR2.PickupPickerController.OnInteractionBegin += OnInteractionBegin;
            On.RoR2.PickupPickerController.HandlePickupSelected += HandlePickupSelected;
            On.RoR2.UI.PickupPickerPanel.SetPickupOptions += SetPickupOptions;
        }

        // ───────────────────────────────────── prefab ─────────────────────────────────────

        private static void BuildPrefab()
        {
            GameObject shrine = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ShrineRestack/ShrineRestack.prefab").WaitForCompletion();
            GameObject panel = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Command/CommandPickerPanel.prefab").WaitForCompletion();
            _useEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/ShrineUseEffect.prefab").WaitForCompletion();
            if (shrine == null || panel == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogError("[CursedBroker] Shrine or picker panel prefab not found; the broker is disabled.");
                return;
            }

            GameObject p = shrine.InstantiateClone("CursedAppleBroker", true);

            // A Shrine of Order's body without its purchase: the picker is the interaction. Anything that requires the
            // PurchaseInteraction (ShrineRestackBehavior, InteractionsPerPlayer, ...) has to go first or Unity keeps it,
            // and a kept PurchaseInteraction wins the prompt ("Offer to Shrine of Order").
            StripPurchase(p);
            p.AddComponent<NetworkUIPromptController>();
            var ppc = p.AddComponent<PickupPickerController>();
            ppc.panelPrefab = panel;
            ppc.cutoffDistance = 10f;
            ppc.contextString = ContextToken;
#pragma warning disable CS0618 // still invoked by the game; must not be null
            ppc.onPickupSelected = new PickupPickerController.PickupIndexUnityEvent();
#pragma warning restore CS0618
            ppc.onUniquePickupSelected = new PickupPickerController.UniquePickupUnityEvent();
            ppc.onServerInteractionBegin = new GenericInteraction.InteractorUnityEvent();
            p.AddComponent<CursedBrokerMarker>();

            var names = p.GetComponent<GenericDisplayNameProvider>() ?? p.AddComponent<GenericDisplayNameProvider>();
            names.displayToken = NameToken;

            Recolor(p);

            _prefab = p;

            _card = ScriptableObject.CreateInstance<InteractableSpawnCard>();
            _card.name = "iscCursedAppleBroker";
            _card.prefab = p;
            _card.sendOverNetwork = true;
            _card.hullSize = HullClassification.Golem;
            _card.nodeGraphType = RoR2.Navigation.MapNodeGroup.GraphType.Ground;
            _card.requiredFlags = RoR2.Navigation.NodeFlags.None;
            _card.forbiddenFlags = RoR2.Navigation.NodeFlags.NoShrineSpawn;
            _card.directorCreditCost = 0;
            _card.occupyPosition = true;
            _card.orientToFloor = true;
        }

        private static void StripPurchase(GameObject p)
        {
            for (int pass = 0; pass < 5 && p.GetComponentInChildren<PurchaseInteraction>(true) != null; pass++)
            {
                foreach (MonoBehaviour mb in p.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb is PurchaseInteraction) continue;
                    Type t = mb.GetType();
                    bool needsPurchase = t.Name == "ShrineRestackBehavior" || t.Name == "PurchaseAvailabilityIndicator";
                    foreach (RequireComponent req in t.GetCustomAttributes<RequireComponent>(true))
                        needsPurchase |= IsPurchase(req.m_Type0) || IsPurchase(req.m_Type1) || IsPurchase(req.m_Type2);
                    if (needsPurchase) UnityEngine.Object.DestroyImmediate(mb);
                }
                foreach (PurchaseInteraction pi in p.GetComponentsInChildren<PurchaseInteraction>(true))
                    UnityEngine.Object.DestroyImmediate(pi);
            }
            if (p.GetComponentInChildren<PurchaseInteraction>(true) != null)
                CursedAppleCuriositiesPlugin.Logger?.LogWarning("[CursedBroker] Could not remove the shrine's PurchaseInteraction.");
        }

        private static bool IsPurchase(Type? t) => t != null && t.IsAssignableFrom(typeof(PurchaseInteraction));

        internal static readonly Color CursePurple = new Color(0.62f, 0.2f, 1f, 1f);
        internal static Material? CurseOverlay;

        /// <summary>The look is applied on each spawned broker by <see cref="CursedBrokerLook"/> (every client).</summary>
        private static void Recolor(GameObject p)
        {
            CurseOverlay = LoadAsset<Material>("RoR2/DLC4/Hollowman/matHMCurseOverlay.mat")
                        ?? LoadAsset<Material>("RoR2/DLC1/EliteVoid/matEliteVoidOverlay.mat");
            if (CurseOverlay == null) CursedAppleCuriositiesPlugin.Logger?.LogWarning("[CursedBroker] No curse overlay material found.");
            p.AddComponent<CursedBrokerLook>();
        }

        private static T? LoadAsset<T>(string key) where T : UnityEngine.Object
        {
            try { return Addressables.LoadAssetAsync<T>(key).WaitForCompletion(); }
            catch { return null; }
        }

        // ───────────────────────────────────── spawning ─────────────────────────────────────

        private static bool StageEligible(Run run)
        {
            SceneDef? scene = SceneCatalog.mostRecentSceneDef;
            if (scene == null || scene.sceneType != SceneType.Stage || scene.isFinalStage) return false;
            return true;
        }

        /// <summary>Stage 3 of a loop (stages 3, 8, 13...).</summary>
        private static bool IsLoopStageThree(Run run) => run.stageClearCount % StagesPerLoop == 2;

        private static bool _forceNextStage; // set by cac_broker_stage3

        private static void OnPostPopulateScene(SceneDirector director)
        {
            Run? run = Run.instance;
            if (!Enabled || !NetworkServer.active || run == null || _card == null || DirectorCore.instance == null) return;
            bool forced = _forceNextStage;
            _forceNextStage = false;
            if (!forced && !StageEligible(run)) return;

            var rng = new Xoroshiro128Plus(run.seed ^ (ulong)(run.stageClearCount + 1) * 0x9E3779B97F4A7C15UL);
            float chance = IsLoopStageThree(run) ? StageThreeChance : SpawnChance;
            if (!forced && !DebugAlwaysSpawn && rng.nextNormalizedFloat >= chance) return;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(_card,
                    new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Random }, rng));
                if (spawned != null)
                {
                    CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[CursedBroker] Spawned at {spawned.transform.position}.");
                    return;
                }
            }
            CursedAppleCuriositiesPlugin.Logger?.LogWarning("[CursedBroker] No spot found for the broker this stage.");
        }

        /// <summary>The last loop whose Bazaar already had its broker (-1 = none yet this run).</summary>
        private static int _bazaarBrokerLoop = -1;

        /// <summary>
        /// The Bazaar has no scene population for interactables, so its broker is placed here: BazaarChance per visit,
        /// until one has appeared there this loop. It stands near the Newt's lunar shop (the director's ground nodes when it has them,
        /// else straight onto the floor next to a shop terminal).
        /// </summary>
        private static void OnServerStageBegin(Stage stage)
        {
            Run? run = Run.instance;
            if (!Enabled || run == null || _prefab == null || _card == null) return;
            SceneDef? scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (scene == null || scene.cachedName != "bazaar") return;
            // Rolled per visit (seeded by the stage count, so each visit has its own roll).
            var rng = new Xoroshiro128Plus(run.seed ^ (ulong)(run.stageClearCount + 1) * 0xC2B2AE3D27D4EB4FUL);
            if (!DebugAlwaysSpawn)
            {
                if (_bazaarBrokerLoop >= run.loopClearCount) return;
                if (rng.nextNormalizedFloat >= BazaarChance) return;
            }
            _bazaarBrokerLoop = run.loopClearCount;

            ShopTerminalBehavior? terminal = UnityEngine.Object.FindObjectOfType<ShopTerminalBehavior>();
            Transform? anchor = terminal != null ? terminal.transform : null;

            if (DirectorCore.instance != null)
            {
                var rule = anchor != null
                    ? new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Approximate, minDistance = 6f, maxDistance = 25f, spawnOnTarget = anchor }
                    : new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Random };
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(_card, rule, rng));
                    if (spawned != null)
                    {
                        CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[CursedBroker] Bazaar broker at {spawned.transform.position}.");
                        return;
                    }
                }
            }
            if (anchor == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning("[CursedBroker] No spot found for the Bazaar broker.");
                return;
            }

            // No nodes to place on: walk around the shop terminal until a floor turns up with room to stand.
            for (int i = 0; i < 16; i++)
            {
                float angle = i * 22.5f * Mathf.Deg2Rad;
                Vector3 probe = anchor.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 7f + Vector3.up * 5f;
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 20f, LayerIndex.world.mask)) continue;
                if (Physics.CheckSphere(hit.point + Vector3.up * 2f, 1.5f, LayerIndex.world.mask)) continue;
                GameObject go = UnityEngine.Object.Instantiate(_prefab, hit.point, Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 180f, 0f));
                NetworkServer.Spawn(go);
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[CursedBroker] Bazaar broker placed by hand at {hit.point}.");
                return;
            }
            CursedAppleCuriositiesPlugin.Logger?.LogWarning("[CursedBroker] No floor found next to the Bazaar shop for the broker.");
        }

        // ───────────────────────────────────── debug commands ─────────────────────────────────────

        [ConCommand(commandName = "cac_broker_stage3", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: skip to a stage 3 that has a Cursed Broker. Optional: a scene name (e.g. frozenwall).")]
        private static void CCBrokerStage3(ConCommandArgs args)
        {
            Run? run = Run.instance;
            if (run == null) throw new ConCommandException("Start a run first.");
            if (_card == null) throw new ConCommandException("The Cursed Broker failed to load (see the log).");

            SceneDef? next = null;
            if (args.Count > 0)
            {
                next = SceneCatalog.FindSceneDef(args.GetArgString(0));
                if (next == null) throw new ConCommandException($"No scene named '{args.GetArgString(0)}'.");
            }
            else
            {
                var pool = new List<SceneDef>();
                foreach (SceneDef d in SceneCatalog.allStageSceneDefs)
                    if (d != null && d.stageOrder == 3 && d.sceneType == SceneType.Stage && !d.isFinalStage) pool.Add(d);
                if (pool.Count == 0) throw new ConCommandException("No stage-3 scenes found.");
                next = pool[UnityEngine.Random.Range(0, pool.Count)];
            }

            // AdvanceStage counts the current stage as cleared when it is a normal stage: land on "Stage 3" either way.
            SceneDef? here = SceneCatalog.GetSceneDefForCurrentScene();
            bool counts = here != null && (here.sceneType == SceneType.Stage || here.sceneType == SceneType.UntimedStage) && !here.preventStageAdvanceCounter;
            run.NetworkstageClearCount = counts ? 1 : 2;
            _forceNextStage = true;
            Debug.Log($"[CursedBroker] Skipping to {next.cachedName} (stage 3) with a Cursed Broker.");
            run.AdvanceStage(next);
        }

        [ConCommand(commandName = "cac_broker_spawn", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Cursed Apple: spawn a Cursed Broker near you on the current stage.")]
        private static void CCBrokerSpawn(ConCommandArgs args)
        {
            if (_card == null || DirectorCore.instance == null) throw new ConCommandException("No director here, or the broker failed to load.");
            CharacterBody? body = args.senderBody;
            if (body == null) throw new ConCommandException("You need a living body.");
            GameObject? spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(_card, new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                minDistance = 4f,
                maxDistance = 20f,
                spawnOnTarget = body.transform,
            }, new Xoroshiro128Plus((ulong)UnityEngine.Random.Range(1, int.MaxValue))));
            Debug.Log(spawned != null ? $"[CursedBroker] Spawned at {spawned.transform.position}." : "[CursedBroker] No room to place a broker nearby.");
        }

        // ───────────────────────────────────── offers ─────────────────────────────────────

        /// <summary>
        /// The offers for this player right now: every uncursed eligible item they hold. An item keeps the penalty it
        /// rolled the first time this player saw it at this broker (reopening can't reroll); items picked up since are
        /// rolled now. Nothing is cached for items they don't hold, so an empty visit never locks the broker.
        /// </summary>
        private static BrokerOffer[] CurrentOffers(CharacterMaster master, CursedBrokerMarker marker)
        {
            Inventory inv = master.inventory;
            CurseLedger? ledger = CurseLedger.For(master);
            if (!marker.Offers.TryGetValue(master, out Dictionary<ItemIndex, BrokerOffer> rolled))
                marker.Offers[master] = rolled = new Dictionary<ItemIndex, BrokerOffer>();

            var offers = new List<BrokerOffer>();
            foreach (ItemIndex idx in inv.itemAcquisitionOrder)
            {
                ItemDef def = ItemCatalog.GetItemDef(idx);
                if (!CurseRules.IsEligible(def) || inv.GetItemCountPermanent(idx) <= 0) continue;
                if (ledger != null && ledger.IsCursed(idx)) continue;
                if (offers.Exists(o => o.Item == idx)) continue;
                if (!rolled.TryGetValue(idx, out BrokerOffer offer))
                    rolled[idx] = offer = Roll(master, marker, idx, def, rolled.Values);
                offers.Add(offer);
            }
            return offers.ToArray();
        }

        private static BrokerOffer Roll(CharacterMaster master, CursedBrokerMarker marker, ItemIndex item, ItemDef def, IEnumerable<BrokerOffer> others)
        {
            // Seeded by broker, player and item: the same roll no matter when the item is first seen.
            var rng = new Xoroshiro128Plus(marker.Seed ^ (ulong)master.netId.Value * 0xD1B54A32D192ED03UL ^ (ulong)(int)item * 0x9E3779B97F4A7C15UL);
            // Prefer a penalty no other offer in this menu has, so the choices differ.
            var kinds = new List<CursePenalty>();
            for (int k = 0; k < CursePenalties.Count; k++) kinds.Add(CursePenalties.KindAt(k));
            foreach (BrokerOffer o in others) kinds.Remove(o.Penalty);
            if (kinds.Count == 0) for (int k = 0; k < CursePenalties.Count; k++) kinds.Add(CursePenalties.KindAt(k));
            CursePenalty kind = kinds[rng.RangeInt(0, kinds.Count)];
            return new BrokerOffer { Item = item, Penalty = kind, Value = CursePenalties.Roll(kind, CurseRules.SeverityBand(def), rng) };
        }

        private static PickupPickerController.Option[] Options(BrokerOffer[] offers)
        {
            var options = new PickupPickerController.Option[offers.Length];
            for (int i = 0; i < offers.Length; i++)
                options[i] = new PickupPickerController.Option
                {
                    pickup = new UniquePickup { pickupIndex = PickupCatalog.FindPickupIndex(offers[i].Item) },
                    available = true,
                };
            return options;
        }

        // ───────────────────────────────────── interaction ─────────────────────────────────────

        private static string GetContextString(On.RoR2.PickupPickerController.orig_GetContextString orig, PickupPickerController self, Interactor activator)
        {
            if (self.GetComponent<CursedBrokerMarker>() is CursedBrokerMarker marker)
            {
                CharacterMaster? m = activator != null ? activator.GetComponent<CharacterBody>()?.master : null;
                return m != null && marker.Used.Contains(m) ? "The Broker has already cursed you" : Language.GetString(ContextToken);
            }
            return orig(self, activator);
        }

        private static Interactability GetInteractability(On.RoR2.PickupPickerController.orig_GetInteractability orig, PickupPickerController self, Interactor activator)
        {
            if (self.GetComponent<CursedBrokerMarker>() is CursedBrokerMarker marker && activator != null)
            {
                CharacterMaster? m = activator.GetComponent<CharacterBody>()?.master;
                if (m != null && marker.Used.Contains(m)) return Interactability.Disabled;
            }
            return orig(self, activator);
        }

        private static void OnInteractionBegin(On.RoR2.PickupPickerController.orig_OnInteractionBegin orig, PickupPickerController self, Interactor activator)
        {
            CursedBrokerMarker? marker = self.GetComponent<CursedBrokerMarker>();
            if (marker == null || !NetworkServer.active)
            {
                orig(self, activator);
                return;
            }

            CharacterMaster? master = activator != null ? activator.GetComponent<CharacterBody>()?.master : null;
            if (master == null || master.inventory == null || marker.Used.Contains(master)) return;

            BrokerOffer[] offers = CurrentOffers(master, marker);
            if (offers.Length == 0)
            {
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                {
                    baseToken = $"<color={CurseLedger.CurseColor}>The Cursed Broker finds nothing worth cursing. (Needs an uncursed, non-lunar item.)</color>",
                });
                return;
            }

            self.SetOptionsServer(Options(offers));
            new BrokerOfferMessage { Broker = self.gameObject, Master = master.gameObject, Offers = offers }.Send(NetworkDestination.Clients);
            orig(self, activator);
        }

        private static void HandlePickupSelected(On.RoR2.PickupPickerController.orig_HandlePickupSelected orig, PickupPickerController self, int choiceIndex)
        {
            CursedBrokerMarker? marker = self.GetComponent<CursedBrokerMarker>();
            if (marker == null || !NetworkServer.active)
            {
                orig(self, choiceIndex);
                return;
            }

            // Never call orig: that would hand out the item.
            var prompt = self.GetComponent<NetworkUIPromptController>();
            CharacterMaster? master = prompt != null ? prompt.currentParticipantMaster : null;
            if (master == null || master.inventory == null || marker.Used.Contains(master)) return;
            if ((uint)choiceIndex >= (uint)self.options.Length || !marker.Offers.TryGetValue(master, out Dictionary<ItemIndex, BrokerOffer> rolled)) return;

            PickupDef pickup = PickupCatalog.GetPickupDef(self.options[choiceIndex].pickup.pickupIndex);
            if (pickup == null || !rolled.TryGetValue(pickup.itemIndex, out BrokerOffer offer)) return;
            if (master.inventory.GetItemCountPermanent(pickup.itemIndex) <= 0) return;

            CurseLedger.GetOrAdd(master).AddServer(new CurseEntry { Item = offer.Item, Penalty = offer.Penalty, Value = offer.Value });
            marker.Used.Add(master);
            prompt!.SetParticipantMaster(null);
            new BrokerOfferMessage { Broker = self.gameObject, Master = master.gameObject, Used = true }.Send(NetworkDestination.Clients);

            ItemDef def = ItemCatalog.GetItemDef(offer.Item);
            string who = master.playerCharacterMasterController != null ? master.playerCharacterMasterController.GetDisplayName() : "Someone";
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = $"<color={CurseLedger.CurseColor}>{who} cursed every {Language.GetString(def.nameToken)}.</color> " +
                            $"<style=cIsUtility>{CurseRules.Get(offer.Item).Improves}</style> " +
                            $"<style=cDeath>{CursePenalties.Describe(offer.Penalty, offer.Value)}.</style>",
            });
            if (_useEffect != null)
                EffectManager.SpawnEffect(_useEffect, new EffectData
                {
                    origin = self.transform.position,
                    rotation = Quaternion.identity,
                    scale = 1f,
                    color = new Color32(176, 108, 255, 255),
                }, transmit: true);
        }

        // ───────────────────────────────────── menu tooltips ─────────────────────────────────────

        private static readonly FieldInfo? ButtonAllocator =
            typeof(PickupPickerPanel).GetField("buttonAllocator", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void SetPickupOptions(On.RoR2.UI.PickupPickerPanel.orig_SetPickupOptions orig, PickupPickerPanel self, PickupPickerController.Option[] options)
        {
            orig(self, options);
            CursedBrokerMarker? marker = self.pickerController != null ? self.pickerController.GetComponent<CursedBrokerMarker>() : null;
            if (marker == null) return;
            marker.Panel = self;
            Decorate(self, marker, options);
        }

        internal static void Decorate(PickupPickerPanel panel, CursedBrokerMarker marker, PickupPickerController.Option[] options)
        {
            BrokerOffer[]? offers = null;
            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster? m = user.cachedMaster;
                if (m != null && marker.ClientOffers.TryGetValue(m.gameObject, out offers)) break;
            }
            if (offers == null || !(ButtonAllocator?.GetValue(panel) is UIElementAllocator<MPButton> alloc)) return;

            var buttons = alloc.elements;
            for (int i = 0; i < options.Length && i < buttons.Count; i++)
            {
                PickupDef pickup = PickupCatalog.GetPickupDef(options[i].pickup.pickupIndex);
                if (pickup == null) continue;
                int found = Array.FindIndex(offers, o => o.Item == pickup.itemIndex);
                if (found < 0 || !buttons[i].TryGetComponent(out TooltipProvider tooltip)) continue;
                BrokerOffer offer = offers[found];
                ItemDef def = ItemCatalog.GetItemDef(offer.Item);
                tooltip.enabled = true;
                tooltip.AllowTooltipOnNavigationSelect = true;
                tooltip.SetContent(new TooltipContent
                {
                    titleToken = def.nameToken,
                    overrideTitleText = $"<color={CurseLedger.CurseColor}>Curse</color> {Language.GetString(def.nameToken)}",
                    titleColor = pickup.darkColor,
                    overrideBodyText = Language.GetString(def.pickupToken) + "\n\n" + CurseLedger.CurseText(offer.Item, offer.Penalty, offer.Value, offer: true),
                    bodyColor = pickup.baseColor,
                });
            }
        }
    }

    /// <summary>
    /// Makes a spawned broker look cursed: a curse overlay pass on its meshes, a violet-stained body, a bright purple
    /// symbol and a purple light. Applied after spawn (and again shortly after) because the game can swap shrine
    /// materials for quality variants at runtime, which undid changes made to the prefab.
    /// </summary>
    public class CursedBrokerLook : MonoBehaviour
    {
        private const string Tag = " (Cursed)";
        private bool _logged;

        private System.Collections.IEnumerator Start()
        {
            Apply();
            yield return new WaitForSeconds(0.5f);
            Apply();
            yield return new WaitForSeconds(2f);
            Apply();
        }

        private void Apply()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Material[] mats = r.sharedMaterials;
                if (!_logged)
                    CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[CursedBroker] Renderer {r.name}: " +
                        string.Join(", ", Array.ConvertAll(mats, m => m == null ? "null" : $"{m.name} [{m.shader?.name}]")));
                bool hasOverlay = false;
                var list = new List<Material>();
                foreach (Material m0 in mats)
                {
                    if (m0 == null) continue;
                    if (m0 == CursedBroker.CurseOverlay || m0.name.EndsWith(Tag, StringComparison.Ordinal))
                    {
                        hasOverlay |= m0.name.StartsWith("CurseOverlay", StringComparison.Ordinal);
                        list.Add(m0);
                        continue;
                    }
                    var m = new Material(m0) { name = m0.name + Tag };
                    if (m.name.Contains("Symbol"))
                    {
                        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", CursedBroker.CursePurple * 2f);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", CursedBroker.CursePurple);
                        if (m.HasProperty("_EmColor")) m.SetColor("_EmColor", CursedBroker.CursePurple);
                    }
                    else if (m.HasProperty("_Color"))
                    {
                        m.SetColor("_Color", new Color(0.42f, 0.24f, 0.55f, 1f)); // stone stained violet
                    }
                    list.Add(m);
                }
                if (!hasOverlay && CursedBroker.CurseOverlay != null && !r.name.Contains("Symbol"))
                {
                    var overlay = new Material(CursedBroker.CurseOverlay) { name = "CurseOverlay" + Tag };
                    if (overlay.HasProperty("_TintColor")) overlay.SetColor("_TintColor", CursedBroker.CursePurple);
                    list.Add(overlay);
                }
                r.sharedMaterials = list.ToArray();
            }
            _logged = true;

            if (transform.Find("CurseGlow") == null)
            {
                var glow = new GameObject("CurseGlow");
                glow.transform.SetParent(transform, false);
                glow.transform.localPosition = new Vector3(0f, 3f, 0f);
                var light = glow.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = CursedBroker.CursePurple;
                light.range = 16f;
                light.intensity = 6f;
                light.shadows = LightShadows.None;
            }
        }
    }

    public struct BrokerOffer
    {
        public ItemIndex Item;
        public CursePenalty Penalty;
        public float Value;
    }

    /// <summary>Per-broker state. Server: rolled offers and who has used it. Clients: the offers to show in tooltips.</summary>
    public class CursedBrokerMarker : MonoBehaviour
    {
        public readonly Dictionary<CharacterMaster, Dictionary<ItemIndex, BrokerOffer>> Offers = new Dictionary<CharacterMaster, Dictionary<ItemIndex, BrokerOffer>>();
        public readonly HashSet<CharacterMaster> Used = new HashSet<CharacterMaster>();
        public readonly Dictionary<GameObject, BrokerOffer[]> ClientOffers = new Dictionary<GameObject, BrokerOffer[]>();
        public PickupPickerPanel? Panel;
        public ulong Seed;

        private void Awake() => Seed = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) << 32 ^ (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
    }

    public class BrokerOfferMessage : INetMessage
    {
        public GameObject? Broker;
        public GameObject? Master;
        public BrokerOffer[] Offers = Array.Empty<BrokerOffer>();
        public bool Used; // this player has spent their curse here (so their prompt greys out)

        public void Serialize(NetworkWriter w)
        {
            w.Write(Broker);
            w.Write(Master);
            w.Write(Used);
            w.Write(Offers.Length);
            foreach (BrokerOffer o in Offers)
            {
                w.Write((int)o.Item);
                w.Write((byte)o.Penalty);
                w.Write(o.Value);
            }
        }

        public void Deserialize(NetworkReader r)
        {
            Broker = r.ReadGameObject();
            Master = r.ReadGameObject();
            Used = r.ReadBoolean();
            Offers = new BrokerOffer[r.ReadInt32()];
            for (int i = 0; i < Offers.Length; i++)
                Offers[i] = new BrokerOffer { Item = (ItemIndex)r.ReadInt32(), Penalty = (CursePenalty)r.ReadByte(), Value = r.ReadSingle() };
        }

        public void OnReceived()
        {
            if (Broker == null || Master == null) return;
            CursedBrokerMarker? marker = Broker.GetComponent<CursedBrokerMarker>();
            if (marker == null) return;
            if (Used)
            {
                CharacterMaster? m = Master.GetComponent<CharacterMaster>();
                if (m != null) marker.Used.Add(m);
                return;
            }
            marker.ClientOffers[Master] = Offers;
            // The menu may already be open if the panel beat this message here.
            if (marker.Panel != null && marker.Panel.pickerController != null)
                CursedBroker.Decorate(marker.Panel, marker, marker.Panel.pickerController.options);
        }
    }
}
