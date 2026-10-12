using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// The Goose Egg family: one equipment per yield (Grey → whites, Green → greens, Red → reds, Golden → boss items,
    /// Void → void items). Each egg gains a stack every 2:30 while held, uncapped; activate to hatch every stack's
    /// worth (nothing happens at 0 stacks). -10% damage while held. Progress is per egg kind for the whole run, so a
    /// dropped egg keeps it.
    /// Eggs share one slot in the drop pool: only the Grey Egg is droppable, and every drop table rolls it into a
    /// weighted egg. Holding a Void Egg, picking up any other egg feeds it a stack instead ("corruption").
    /// </summary>
    public abstract class GooseEgg : EquipmentBase
    {
        public enum Yield { White, Green, Red, Boss, Void }

        protected abstract Yield Kind { get; }
        protected abstract Color Tint { get; }
        protected abstract int DefaultPerStack { get; }
        protected abstract float DefaultDropWeight { get; }
        protected abstract string YieldText { get; }

        public int PerStack;
        public float DropWeight;
        public float Accumulated;           // seconds held this run (or Void stacks fed in)
        public BuffDef? StackBuff;
        private float _lastTick = -1f;

        public static float SecondsPerStack;
        public static float DamagePenalty;
        public static readonly List<GooseEgg> All = new List<GooseEgg>();
        public static readonly Dictionary<EquipmentDef, GooseEgg> ByDef = new Dictionary<EquipmentDef, GooseEgg>();
        public static GooseEgg? Grey, Void;

        public int Stacks => Mathf.FloorToInt(Accumulated / Mathf.Max(1f, SecondsPerStack));
        public static bool IsEgg(EquipmentDef? def) => def != null && ByDef.ContainsKey(def);

        public override string PickupDesc => $"Lower your damage while held. Matures over time; hatch it for {YieldText}.";
        public override string FullDescription =>
            $"While held, deal <style=cIsHealth>-{DamagePenalty * 100:0}% damage</style>. " +
            $"Gains a stack every <style=cIsUtility>{Mathf.FloorToInt(SecondsPerStack / 60f)}:{Mathf.FloorToInt(SecondsPerStack % 60f):00}</style> while held. " +
            $"<style=cIsUtility>Activate</style> to hatch, yielding <style=cIsDamage>{PerStack} {YieldText}</style> per stack. " +
            "Progress is retained if dropped.";
        public override string Lore =>
            "I won't lie, I don't know what that does, but my kid says all the mystics are buying it!";

        public override float Cooldown => 1f; // consumed on use
        protected override string? IconFileName => "icon_goldengooseegg.png";

        public override void Init(ConfigFile config)
        {
            if (All.Count == 0)
            {
                SecondsPerStack = config.Bind("Equipment", "Goose Eggs - Seconds Per Stack", 150f,
                    "Seconds an egg must be held to gain a stack.").Value;
                DamagePenalty = config.Bind("Equipment", "Goose Eggs - Damage Penalty", 0.10f,
                    "Damage reduction while any egg is held.").Value;
            }
            PerStack = config.Bind("Equipment", $"{EquipmentName} - Items Per Stack", DefaultPerStack,
                "Items hatched per stack.").Value;
            DropWeight = config.Bind("Equipment", $"{EquipmentName} - Drop Weight", DefaultDropWeight,
                "Relative chance an egg drop is this egg.").Value;

            StackBuff = ScriptableObject.CreateInstance<BuffDef>();
            StackBuff.name = "CursedApple_Maturation" + Kind;
            StackBuff.canStack = true;
            StackBuff.isDebuff = false;
            StackBuff.buffColor = Tint;
            ContentAddition.AddBuffDef(StackBuff);

            CreateLang();
            CreateEquipment();
            if (EquipmentDef != null)
            {
                EquipmentDef.enigmaCompatible = false;      // Enigma/random triggers would waste the stacks
                EquipmentDef.canBeRandomlyTriggered = false;
                EquipmentDef.canDrop = Kind == Yield.White; // the one egg slot in the pool, rolled on drop
                ByDef[EquipmentDef] = this;
            }
            StackBuff.iconSprite = EquipmentDef?.pickupIconSprite ?? ItemBase.CreatePlaceholderSprite(Tint);

            All.Add(this);
            if (Kind == Yield.White) Grey = this;
            if (Kind == Yield.Void) Void = this;
            if (All.Count == 1) HookShared();
        }

        /// <summary>The golden egg's art, recoloured for the other eggs.</summary>
        protected override Sprite? CreateIcon()
        {
            Sprite? gold = base.CreateIcon();
            if (gold == null || Kind == Yield.Boss) return gold;
            Texture2D src = gold.texture;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            Color[] px = src.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float lum = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
                px[i] = new Color(Mathf.Clamp01(lum * Tint.r * 1.6f), Mathf.Clamp01(lum * Tint.g * 1.6f), Mathf.Clamp01(lum * Tint.b * 1.6f), px[i].a);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        public override void RegisterSounds()
        {
            if (Kind == Yield.Boss)
                EquipmentFx.RegisterSound("gooseegg_hatch", "sounds/mods/tech/golden_egg/golden_egg_activate", fallback: "Play_UI_coin");
        }

        public override void Hooks() { } // shared hooks: HookShared, once for the whole family

        /// <summary>Server: advance this egg once per physics step, however many bodies hold it.</summary>
        public void Tick()
        {
            if (Time.fixedTime == _lastTick) return;
            _lastTick = Time.fixedTime;
            Accumulated += Time.fixedDeltaTime;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            if (!NetworkServer.active) return false;
            var body = slot?.characterBody;
            if (body?.inventory == null) return false;
            int stacks = Stacks;
            if (stacks <= 0) return false; // not ready: keep the egg, no cooldown

            SpawnItems(body, stacks * PerStack);
            EquipmentFx.Sound("gooseegg_hatch", body.corePosition, body.gameObject);
            EquipmentFx.Effect("RoR2/Base/BonusGoldPackOnKill/MoneyPackPickupEffect.prefab", body.corePosition, body.radius * 2f);
            EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUpEffect.prefab", body.corePosition, body.radius);

            body.inventory.SetEquipmentIndex(EquipmentIndex.None);
            Accumulated = 0f;
            return true;
        }

        private void SpawnItems(CharacterBody body, int count)
        {
            Run? run = Run.instance;
            if (run == null) return;
            var rng = new Xoroshiro128Plus(run.treasureRng.nextUlong);
            List<PickupIndex>? list = Kind switch
            {
                Yield.Green => run.availableTier2DropList,
                Yield.Red => run.availableTier3DropList,
                Yield.Boss => run.availableBossDropList,
                Yield.Void => null,
                _ => run.availableTier1DropList,
            };
            Vector3 origin = body.transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < count; i++)
            {
                PickupIndex pickup = list != null
                    ? (list.Count > 0 ? list[rng.RangeInt(0, list.Count)] : PickupIndex.none)
                    : VoidPickup(rng);
                if (pickup == PickupIndex.none) continue;
                // A fountain: spread evenly around the holder, so big hatches don't land in one pile.
                Vector3 around = Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward;
                Vector3 velocity = Vector3.up * 15f + around * (4f + 3f * (i % 3));
                PickupDropletController.CreatePickupDroplet(
                    new GenericPickupController.CreatePickupInfo { pickupIndex = pickup, rotation = Quaternion.identity },
                    origin, velocity);
            }
        }

        // ---------------- void yield: Void Cradle's own drop table ----------------

        private static PickupDropTable? _voidTable;
        private static bool _voidTableLoaded;

        private static PickupIndex VoidPickup(Xoroshiro128Plus rng)
        {
            if (!_voidTableLoaded)
            {
                _voidTableLoaded = true;
                try { _voidTable = Addressables.LoadAssetAsync<PickupDropTable>("RoR2/Base/Common/dtVoidChest.asset").WaitForCompletion(); }
                catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[GooseEgg] dtVoidChest: " + e.Message); }
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[GooseEgg] void yield from {(_voidTable != null ? "Void Cradle's drop table" : "fallback weights")}");
            }
            if (_voidTable != null)
            {
                PickupIndex p = _voidTable.GeneratePickup(rng).pickupIndex;
                if (p != PickupIndex.none) return p;
            }
            Run run = Run.instance;
            float roll = rng.nextNormalizedFloat; // fallback: white-heavy, like the cradle
            List<PickupIndex> list = roll < 0.6f ? run.availableVoidTier1DropList : roll < 0.9f ? run.availableVoidTier2DropList : run.availableVoidTier3DropList;
            return list.Count > 0 ? list[rng.RangeInt(0, list.Count)] : PickupIndex.none;
        }

        // ---------------- shared hooks ----------------

        private static void HookShared()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients += (sender, args) =>
            {
                if (sender.inventory != null && IsEgg(EquipmentCatalog.GetEquipmentDef(sender.inventory.currentEquipmentIndex)))
                    args.damageMultAdd -= DamagePenalty;
            };
            Run.onRunStartGlobal += _ => { foreach (GooseEgg e in All) e.Accumulated = 0f; };
            On.RoR2.GenericPickupController.AttemptGrant += AttemptGrant;

            // One egg slot in the pool: whatever table drops the Grey Egg, it becomes a weighted egg.
            On.RoR2.PickupDropTable.GeneratePickup += (orig, self, rng) => Roll(orig(self, rng), rng);
            On.RoR2.PickupDropTable.GenerateDistinctPickups += (orig, self, dest, count, rng, loop) =>
            {
                orig(self, dest, count, rng, loop);
                for (int i = 0; i < dest.Count; i++) dest[i] = Roll(dest[i], rng);
            };
#pragma warning disable CS0618 // older callers still use these
            On.RoR2.PickupDropTable.GenerateDrop += (orig, self, rng) => Roll(orig(self, rng), rng);
            On.RoR2.PickupDropTable.GenerateUniqueDrops += (orig, self, max, rng) =>
            {
                PickupIndex[] drops = orig(self, max, rng);
                for (int i = 0; i < drops.Length; i++) drops[i] = Roll(drops[i], rng);
                return drops;
            };
#pragma warning restore CS0618
        }

        private static UniquePickup Roll(UniquePickup p, Xoroshiro128Plus rng) => p.WithPickupIndex(Roll(p.pickupIndex, rng));

        private static PickupIndex Roll(PickupIndex p, Xoroshiro128Plus rng)
        {
            if (Grey?.EquipmentDef == null || p == PickupIndex.none || p != PickupCatalog.FindPickupIndex(Grey.EquipmentDef.equipmentIndex)) return p;
            float total = 0f;
            foreach (GooseEgg e in All) total += Mathf.Max(0f, e.DropWeight);
            float roll = rng.nextNormalizedFloat * total;
            foreach (GooseEgg e in All)
            {
                roll -= Mathf.Max(0f, e.DropWeight);
                if (roll < 0f && e.EquipmentDef != null) return PickupCatalog.FindPickupIndex(e.EquipmentDef.equipmentIndex);
            }
            return p;
        }

        /// <summary>Holding a Void Egg, another egg is absorbed into it as one stack instead of being picked up.</summary>
        private static void AttemptGrant(On.RoR2.GenericPickupController.orig_AttemptGrant orig, GenericPickupController self, CharacterBody body)
        {
            if (NetworkServer.active && Void?.EquipmentDef != null && body != null && body.inventory != null
                && body.inventory.currentEquipmentIndex == Void.EquipmentDef.equipmentIndex)
            {
                PickupDef? def = PickupCatalog.GetPickupDef(self.pickupIndex);
                EquipmentDef? eq = def != null ? EquipmentCatalog.GetEquipmentDef(def.equipmentIndex) : null;
                if (eq != null && eq != Void.EquipmentDef && IsEgg(eq))
                {
                    Void.Accumulated += SecondsPerStack;
                    EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUpEffect.prefab", body.corePosition, body.radius);
                    UnityEngine.Object.Destroy(self.gameObject);
                    return;
                }
            }
            orig(self, body);
        }

        private static void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (!NetworkServer.active || self.inventory == null) return;
            EquipmentDef? held = EquipmentCatalog.GetEquipmentDef(self.inventory.currentEquipmentIndex);
            GooseEgg? egg = held != null && ByDef.TryGetValue(held, out GooseEgg e) ? e : null;
            bool validHolder = self.isPlayerControlled || self.name.StartsWith("EquipmentDroneBody", System.StringComparison.Ordinal);

            var tracker = self.GetComponent<GooseEggTracker>();
            if (tracker != null && (egg == null || !validHolder || tracker.Egg != egg)) { UnityEngine.Object.Destroy(tracker); tracker = null; }
            if (tracker == null && egg != null && validHolder) self.gameObject.AddComponent<GooseEggTracker>().Egg = egg;
        }
    }

    public class GreyGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Grey Goose Egg";
        public override string EquipmentLangTokenName => "GREYGOOSEEGG";
        protected override Yield Kind => Yield.White;
        protected override Color Tint => new Color(0.85f, 0.85f, 0.85f);
        protected override int DefaultPerStack => 15;
        protected override float DefaultDropWeight => 45f;
        protected override string YieldText => "white items";
    }

    public class GreenGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Green Goose Egg";
        public override string EquipmentLangTokenName => "GREENGOOSEEGG";
        protected override Yield Kind => Yield.Green;
        protected override Color Tint => new Color(0.45f, 0.95f, 0.35f);
        protected override int DefaultPerStack => 5;
        protected override float DefaultDropWeight => 27f;
        protected override string YieldText => "green items";
    }

    public class RedGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Red Goose Egg";
        public override string EquipmentLangTokenName => "REDGOOSEEGG";
        protected override Yield Kind => Yield.Red;
        protected override Color Tint => new Color(1f, 0.3f, 0.25f);
        protected override int DefaultPerStack => 1;
        protected override float DefaultDropWeight => 12f;
        protected override string YieldText => "red items";
    }

    public class GoldenGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Golden Goose Egg";
        public override string EquipmentLangTokenName => "GOLDENGOOSEEGG";
        protected override Yield Kind => Yield.Boss;
        protected override Color Tint => new Color(1f, 0.85f, 0.1f);
        protected override int DefaultPerStack => 2;
        protected override float DefaultDropWeight => 8f;
        protected override string YieldText => "yellow items";
    }

    public class VoidGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Void Goose Egg";
        public override string EquipmentLangTokenName => "VOIDGOOSEEGG";
        protected override Yield Kind => Yield.Void;
        protected override Color Tint => new Color(0.8f, 0.4f, 1f);
        protected override int DefaultPerStack => 2;
        protected override float DefaultDropWeight => 8f;
        protected override string YieldText => "void items";
        public override string FullDescription => base.FullDescription +
            " <style=cIsVoid>Corrupts all Goose Eggs</style>: while you hold it, other eggs you pick up are absorbed as a stack.";
    }

    /// <summary>Server: ticks the held egg and shows its stacks as a buff.</summary>
    public class GooseEggTracker : MonoBehaviour
    {
        public GooseEgg? Egg;
        private CharacterBody _body = null!;
        private int _shown = -1;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            if (!NetworkServer.active || Egg == null) return;
            Egg.Tick();
            int stacks = Egg.Stacks;
            if (stacks == _shown || Egg.StackBuff == null || Egg.StackBuff.buffIndex == BuffIndex.None) return;
            _shown = stacks;
            _body.SetBuffCount(Egg.StackBuff.buffIndex, stacks);
        }

        private void OnDestroy()
        {
            if (_body == null || Egg?.StackBuff == null || Egg.StackBuff.buffIndex == BuffIndex.None) return;
            _body.SetBuffCount(Egg.StackBuff.buffIndex, 0);
        }
    }
}
