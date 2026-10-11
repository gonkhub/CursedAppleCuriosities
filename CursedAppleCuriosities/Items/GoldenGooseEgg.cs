using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class GoldenGooseEgg : EquipmentBase
    {
        public static float DamagePenalty;
        public const float StageInterval = 150f;  // 2.5 min per stage
        public const int   MaxStage      = 12;    // 12 stages × 2.5 min = 30 min total

        // Stages 1-4 → 1-4 whites, 5-8 → 1-4 greens, 9-12 → 1-4 reds.
        private static (ItemTier tier, int count) GetReward(int stage)
        {
            ItemTier tier  = stage <= 4 ? ItemTier.Tier1 : stage <= 8 ? ItemTier.Tier2 : ItemTier.Tier3;
            int      count = ((stage - 1) % 4) + 1;
            return (tier, count);
        }

        // Global for the run — only one egg exists at a time.
        public static float         AccumulatedTime     { get; private set; }
        public static EquipmentDef? StaticEquipmentDef;
        public static BuffDef?      MaturationBuffDef;

        public static void  AddTime(float dt) => AccumulatedTime = Mathf.Min(AccumulatedTime + dt, StageInterval * MaxStage);
        public static void  ResetTime()       => AccumulatedTime = 0f;
        public static int   CurrentStage      => Mathf.Clamp(Mathf.FloorToInt(AccumulatedTime / StageInterval) + 1, 1, MaxStage);

        public override string EquipmentName          => "Golden Goose Egg";
        public override string EquipmentLangTokenName => "GOLDENGOOSEEGG";

        public override string PickupDesc => "Lower your damage while held. Hatch it for items that grow rarer over time.";
        public override string FullDescription =>
            $"While held, deal <style=cIsHealth>-{DamagePenalty * 100:0}% damage</style>. " +
            $"<style=cIsUtility>Activate</style> to hatch: yields " +
            $"<style=cIsUtility>white items</style> in the first 10 minutes, " +
            $"<style=cIsDamage>green items</style> in the next 10, and " +
            $"<style=cIsHealing>red items</style> after 20 minutes — " +
            $"one extra item every <style=cIsUtility>2:30</style>, up to 4. " +
            $"Progress is retained if dropped.";

        public override string Lore =>
            "I won't lie, I don't know what that does, but my kid says all the mystics are buying it!";

        // Cooldown is irrelevant — the equipment is consumed on use.
        public override float Cooldown => 1f;
        protected override string? IconFileName => "icon_goldengooseegg.png";

        public override void Init(ConfigFile config)
        {
            var color = new Color(1f, 0.85f, 0.1f);
            MaturationBuffDef          = ScriptableObject.CreateInstance<BuffDef>();
            MaturationBuffDef.name     = "CursedApple_Maturation";
            MaturationBuffDef.canStack = true;
            MaturationBuffDef.isDebuff = false;
            MaturationBuffDef.buffColor = color;
            ContentAddition.AddBuffDef(MaturationBuffDef);

            DamagePenalty = config.Bind("Equipment", "Golden Goose Egg - Damage Penalty", 0.10f,
                "Damage reduction while the egg is held.").Value;

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            MaturationBuffDef.iconSprite = EquipmentDef?.pickupIconSprite
                                        ?? ItemBase.CreatePlaceholderSprite(color);

            // Prevent Enigma/random triggers wasting accumulated incubation time.
            if (EquipmentDef != null)
            {
                EquipmentDef.enigmaCompatible       = false;
                EquipmentDef.canBeRandomlyTriggered = false;
            }

            Hooks();
        }

        public override void RegisterSounds()
            => EquipmentFx.RegisterSound("gooseegg_hatch", "sounds/mods/tech/golden_egg/golden_egg_activate", fallback: "Play_UI_coin");

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += CharacterBody_OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
            Run.onRunStartGlobal                     += _ => ResetTime();
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (!NetworkServer.active || self.inventory == null) return;

            var  eqIdx    = StaticEquipmentDef?.equipmentIndex ?? EquipmentIndex.None;
            bool holdsEgg = eqIdx != EquipmentIndex.None
                         && self.inventory.currentEquipmentIndex == eqIdx;
            bool isValidHolder = self.isPlayerControlled || IsEquipmentDrone(self);

            var tracker = self.GetComponent<GooseEggTracker>();
            if (holdsEgg && isValidHolder && tracker == null)
                self.gameObject.AddComponent<GooseEggTracker>();
            else if ((!holdsEgg || !isValidHolder) && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        private static bool IsEquipmentDrone(CharacterBody body) =>
            body.name.StartsWith("EquipmentDroneBody", System.StringComparison.Ordinal);

        public override bool OnUse(EquipmentSlot slot)
        {
            if (!NetworkServer.active) return false;
            var body = slot?.characterBody;
            if (body?.inventory == null) return false;

            var (tier, count) = GetReward(CurrentStage);
            SpawnItems(body, tier, count);
            EquipmentFx.Sound("gooseegg_hatch", body.corePosition, body.gameObject);
            EquipmentFx.Effect("RoR2/Base/Items/BonusGoldPackOnKill/MoneyPackPickupEffect.prefab", body.corePosition, body.radius * 2f);
            EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUp/LevelUpEffect.prefab", body.corePosition, body.radius);

            if (MaturationBuffDef != null && MaturationBuffDef.buffIndex != BuffIndex.None)
                body.SetBuffCount(MaturationBuffDef.buffIndex, 0);

            body.inventory.SetEquipmentIndex(EquipmentIndex.None);
            ResetTime();

            return true;
        }

        private static void SpawnItems(CharacterBody body, ItemTier tier, int count)
        {
            var dropList = tier switch
            {
                ItemTier.Tier2 => Run.instance?.availableTier2DropList,
                ItemTier.Tier3 => Run.instance?.availableTier3DropList,
                _              => Run.instance?.availableTier1DropList,
            };

            if (dropList == null || dropList.Count == 0) return;

            var origin = body.transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < count; i++)
            {
                var pickup   = dropList[UnityEngine.Random.Range(0, dropList.Count)];
                var velocity = Vector3.up * 10f + UnityEngine.Random.insideUnitSphere * 5f;
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo
                    {
                        pickupIndex    = pickup,
                        rotation       = Quaternion.identity,
                        prefabOverride = null,
                    },
                    origin, velocity);
            }
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            var eqIdx = StaticEquipmentDef?.equipmentIndex ?? EquipmentIndex.None;
            if (eqIdx == EquipmentIndex.None) return;
            if (sender.inventory.currentEquipmentIndex != eqIdx) return;
            args.damageMultAdd -= DamagePenalty;
        }
    }

    public class GooseEggTracker : MonoBehaviour
    {
        private CharacterBody _body;
        private int           _lastStage;

        private void Awake()
        {
            _body      = GetComponent<CharacterBody>();
            _lastStage = GoldenGooseEgg.CurrentStage;
            SyncBuff(_lastStage);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null) return;

            GoldenGooseEgg.AddTime(Time.fixedDeltaTime);

            int stage = GoldenGooseEgg.CurrentStage;
            if (stage == _lastStage) return;

            _lastStage = stage;
            SyncBuff(stage);
        }

        private void OnDestroy()
        {
            var bd = GoldenGooseEgg.MaturationBuffDef;
            if (_body == null || bd == null || bd.buffIndex == BuffIndex.None) return;
            _body.SetBuffCount(bd.buffIndex, 0);
        }

        private void SyncBuff(int stage)
        {
            var bd = GoldenGooseEgg.MaturationBuffDef;
            if (_body == null || bd == null || bd.buffIndex == BuffIndex.None) return;
            _body.SetBuffCount(bd.buffIndex, stage);
        }
    }
}
