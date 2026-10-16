using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class TrophyCollector : ItemBase
    {
        public override string ItemName => "Trophy Collector";
        public override string ItemLangTokenName => "TROPHYCOLLECTOR";

        public override string ItemPickupDesc =>
            "Killing elites grants Trophies that generate gold and increase sprint speed. " +
            "<style=cIsVoid>Corrupts all Sprint Boots</style>.";
        public override string ItemFullDescription =>
            $"<style=cIsVoid>Corrupts all Sprint Boots</style>. " +
            $"Killing an <style=cIsDamage>elite</style> grants a " +
            $"<style=cIsDamage>Trophy</style> " +
            $"(max <style=cIsUtility>{TrophyCapBase}</style> " +
            $"<style=cStack>(+{TrophyCapPerStack} per stack)</style>). " +
            $"Each Trophy generates " +
            $"<style=cIsUtility>${MoneyPerMinuteBase:0}/min</style> (scaling with difficulty, like chest prices), " +
            $"<style=cIsUtility>+{BonusPerExtraTrophy * 100:0}%</style> for every Trophy you hold beyond the first, and grants " +
            $"<style=cIsUtility>+{SprintSpeedPerTrophy * 100:0}% sprint speed</style>. " +
            "Trophies are lost when the teleporter charges or you leave the stage.";

        public override string ItemLore =>
            "If you like that, I got some other stuff that fell off the truck.";

        public override ItemTier Tier => ItemTier.VoidTier1;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_trophycollector.png";

        public static float MoneyPerMinuteBase;
        public static float BonusPerExtraTrophy; // every Trophy's gold, per Trophy held beyond the first
        public static int   TrophyCapBase;      // max trophies with a single copy
        public static int   TrophyCapPerStack;  // +max trophies per extra copy
        public static float SprintSpeedPerTrophy;

        public static ItemDef? StaticItemDef;
        public static BuffDef? TrophyBuffDef;

        // True from the moment the teleporter finishes charging until the next stage starts.
        // While set, Trophies cannot be held or earned, and they pay out no gold.
        public static bool TeleporterCharged;

        public override void Init(ConfigFile config)
        {
            var color              = new Color(1f, 0.85f, 0.1f);
            TrophyBuffDef          = ScriptableObject.CreateInstance<BuffDef>();
            TrophyBuffDef.name     = "CursedApple_Trophy";
            TrophyBuffDef.canStack = true;
            TrophyBuffDef.isDebuff = false;
            TrophyBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(TrophyBuffDef);

            // One copy used to carry a run's economy ($50/min per Trophy, flat: two small chests a minute on every stage). Now a
            // low base that grows with every Trophy held, so buying and filling several copies is what pays (user 3.0).
            MoneyPerMinuteBase = 10f; // per Trophy at 1 Trophy held, before difficulty scaling (was 50)
            BonusPerExtraTrophy = 0.5f; // +50% to every Trophy's gold per Trophy held beyond the first
            TrophyCapBase = 1;
            TrophyCapPerStack = 1;
            SprintSpeedPerTrophy = 0.1f;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            TrophyBuffDef.iconSprite = ItemDef?.pickupIconSprite ?? BuffIcon(null, color);
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged    += OnInventoryChanged;
            On.RoR2.GlobalEventManager.OnCharacterDeath += OnCharacterDeath;
            On.RoR2.ItemCatalog.SetItemRelationships    += InjectVoidPair;
            RecalculateStatsAPI.GetStatCoefficients     += GetStatCoefficients;
            // When the teleporter finishes charging the game drains player gold
            // before allowing the stage transition. Clear stacks immediately so
            // Trophy Collector stops generating cash and the drain can complete.
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
            // Every way off a stage (teleporter, portals) goes through a SceneExitController, which turns leftover gold
            // into XP until none is left: trophies still paying out would keep it from ever finishing (user, 1.8).
            SceneExitController.onBeginExit += _ => OnTeleporterCharged(null);
            // Belt-and-suspenders: also clear at stage start in case stacks survive.
            On.RoR2.Stage.Start += (orig, self) =>
            {
                TeleporterCharged = false;
                var result = orig(self);
                if (NetworkServer.active && TrophyBuffDef != null)
                    self.StartCoroutine(ClearTrophiesNextFrame());
                return result;
            };
        }

        // Same registration as Spirit Burn's Tankbuster pair.
        private void InjectVoidPair(
            On.RoR2.ItemCatalog.orig_SetItemRelationships orig,
            ItemRelationshipProvider[] providers)
        {
            if (SprintBoots.StaticItemDef != null && ItemDef != null)
            {
                var provider = ScriptableObject.CreateInstance<ItemRelationshipProvider>();
                provider.name             = "CursedApple_TrophyCollectorVoidPair";
                provider.relationshipType = DLC1Content.ItemRelationshipTypes.ContagiousItem;
                provider.relationships    = new[]
                {
                    new ItemDef.Pair { itemDef1 = SprintBoots.StaticItemDef, itemDef2 = ItemDef }
                };
                providers = (providers ?? System.Array.Empty<ItemRelationshipProvider>())
                            .Append(provider).ToArray();
            }
            orig(providers);
        }

        // Trophy buff stacks are networked, so this runs the same on server and clients. The game
        // recalculates stats whenever sprinting starts/stops or a buff stack changes.
        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (!sender.isSprinting || sender.inventory == null || TrophyBuffDef == null) return;
            int trophies = TrophyCount(sender, sender.inventory.GetItemCountEffective(ItemDef));
            if (trophies > 0)
                args.moveSpeedMultAdd += SprintSpeedPerTrophy * trophies;
        }

        // Trophies held, clamped to the current cap in case copies were dropped.
        public static int TrophyCount(CharacterBody body, int itemCount)
        {
            if (itemCount <= 0 || TrophyBuffDef == null) return 0;
            int cap = TrophyCapBase + TrophyCapPerStack * (itemCount - 1);
            return Mathf.Min(body.GetBuffCount(TrophyBuffDef.buffIndex), cap);
        }

        private static void OnTeleporterCharged(TeleporterInteraction? teleporter)
        {
            if (!NetworkServer.active || TrophyBuffDef == null) return;
            TeleporterCharged = true;
            foreach (var pcmc in PlayerCharacterMasterController.instances)
                ClearTrophies(pcmc?.master?.GetBody());
        }

        public static void ClearTrophies(CharacterBody? body)
        {
            if (body == null || TrophyBuffDef == null) return;
            int stacks = body.GetBuffCount(TrophyBuffDef.buffIndex);
            for (int i = 0; i < stacks; i++)
                body.RemoveBuff(TrophyBuffDef.buffIndex);
        }

        private void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;
            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<TrophyCollectorTracker>();
            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<TrophyCollectorTracker>();
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private void OnCharacterDeath(
            On.RoR2.GlobalEventManager.orig_OnCharacterDeath orig,
            GlobalEventManager self, DamageReport report)
        {
            orig(self, report);

            if (!NetworkServer.active) return;
            if (report?.victimBody == null || !report.victimBody.isElite) return;
            if (report.attackerBody == null) return;

            // Defer the AddBuff call to the next frame so it doesn't fire in the
            // middle of the death-event chain (which can disrupt TeleporterInteraction).
            report.attackerBody.GetComponent<TrophyCollectorTracker>()?.QueueTrophy();
        }

        private static IEnumerator ClearTrophiesNextFrame()
        {
            yield return null; // wait until bodies have spawned
            if (TrophyBuffDef == null) yield break;
            foreach (var pcmc in PlayerCharacterMasterController.instances)
                ClearTrophies(pcmc?.master?.GetBody());
        }
    }

    public class TrophyCollectorTracker : MonoBehaviour
    {
        private CharacterBody _body;
        private bool          _pendingTrophy;
        private float         _moneyTimer;
        private float         _accumulatedMoney;

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
            // Ensure a fresh body starts with zero trophy stacks.
            if (NetworkServer.active)
                TrophyCollector.ClearTrophies(_body);
        }

        // Called from OnCharacterDeath; the actual buff add is deferred to Update.
        public void QueueTrophy() => _pendingTrophy = true;

        private void Update()
        {
            // --- Trophy grant (deferred from OnCharacterDeath) ---
            if (_pendingTrophy)
            {
                _pendingTrophy = false;
                GrantTrophy();
            }

            // --- Passive money income (once per second to avoid rapid GiveMoney calls) ---
            if (!NetworkServer.active || _body?.master == null) return;
            if (TrophyCollector.TrophyBuffDef == null || TrophyCollector.StaticItemDef == null) return;

            // After the teleporter is charged, no Trophies may exist and none pay out —
            // otherwise the trickle of gold keeps converting to XP and blocks the stage exit.
            if (TrophyCollector.TeleporterCharged)
            {
                TrophyCollector.ClearTrophies(_body);
                _moneyTimer       = 0f;
                _accumulatedMoney = 0f;
                return;
            }

            _moneyTimer += Time.deltaTime;
            if (_moneyTimer < 1f) return;
            _moneyTimer -= 1f;

            // Copies raise the Trophy cap; every Trophy beyond the first raises every Trophy's gold (user 3.0).
            int itemCount = _body.inventory?.GetItemCountEffective(TrophyCollector.StaticItemDef) ?? 0;
            int buffCount = TrophyCollector.TrophyCount(_body, itemCount);
            if (buffCount <= 0) return;

            float scale = Run.instance != null ? Mathf.Pow(Run.instance.difficultyCoefficient, 1.25f) : 1f; // chest-price scaling
            float perTrophy = TrophyCollector.MoneyPerMinuteBase * (1f + TrophyCollector.BonusPerExtraTrophy * (buffCount - 1));
            float moneyPerSec = perTrophy * scale * buffCount / 60f;
            _accumulatedMoney += moneyPerSec;
            uint toGive = (uint)_accumulatedMoney;
            if (toGive > 0)
            {
                _body.master.GiveMoney(toGive);
                _accumulatedMoney -= toGive;
            }
        }

        private void GrantTrophy()
        {
            if (!NetworkServer.active || _body?.inventory == null) return;
            if (TrophyCollector.TrophyBuffDef == null || TrophyCollector.StaticItemDef == null) return;
            if (TrophyCollector.TeleporterCharged) return;

            int itemCount = _body.inventory.GetItemCountEffective(TrophyCollector.StaticItemDef);
            if (itemCount <= 0) return;

            int currentStacks = _body.GetBuffCount(TrophyCollector.TrophyBuffDef.buffIndex);
            int cap           = TrophyCollector.TrophyCapBase + TrophyCollector.TrophyCapPerStack * (itemCount - 1);
            if (currentStacks < cap)
                _body.AddBuff(TrophyCollector.TrophyBuffDef);
        }
    }
}
