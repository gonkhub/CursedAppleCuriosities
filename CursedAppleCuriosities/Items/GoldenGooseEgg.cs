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
    /// Void → void items). Each egg gains a stack every 5:00 (config) while held, uncapped; activate to hatch (nothing
    /// happens at 0 stacks). Each stack yields more than the last (<see cref="Hatched"/>), so holding pays off. -25% damage while held. Progress belongs to each individual egg:
    /// kept per equipment slot while held (<see cref="GooseEggProgress"/>) and carried on the pickup when the egg is
    /// swapped out (<see cref="GooseEggPickupProgress"/>), so a dropped egg keeps its own.
    /// Eggs share one slot in the drop pool: only the Grey Egg is droppable, and every drop table rolls it into a
    /// weighted egg. Holding a Void Egg, picking up any other egg feeds it a stack instead ("corruption").
    /// </summary>
    public abstract class GooseEgg : EquipmentBase
    {
        public enum Yield { White, Green, Red, Boss, Void }

        protected abstract Yield Kind { get; }
        protected abstract Color Tint { get; }
        protected abstract int DefaultFirstStack { get; }
        protected abstract float DefaultExtraPerStack { get; }
        protected abstract float DefaultDropWeight { get; }
        protected abstract string YieldText { get; }

        public int FirstStack;
        public float ExtraPerStack;
        public float DropWeight;
        public BuffDef? StackBuff;

        public static float SecondsPerStack;
        public static float DamagePenalty;
        public static readonly List<GooseEgg> All = new List<GooseEgg>();
        public static readonly Dictionary<EquipmentDef, GooseEgg> ByDef = new Dictionary<EquipmentDef, GooseEgg>();
        public static GooseEgg? Grey, Void;

        public static int StacksFor(float seconds) => Mathf.FloorToInt(seconds / Mathf.Max(1f, SecondsPerStack));
        public static bool IsEgg(EquipmentDef? def) => def != null && ByDef.ContainsKey(def);

        /// <summary>Items hatched at this many stacks: stack k yields FirstStack + (k-1)·ExtraPerStack, summed.</summary>
        public int Hatched(int stacks) =>
            stacks <= 0 ? 0 : Mathf.Max(0, Mathf.FloorToInt(FirstStack * stacks + ExtraPerStack * stacks * (stacks - 1) / 2f + 0.001f));

        public override string PickupDesc => $"Lower your damage while held. Matures over time; hatch it for {YieldText}.";
        public override string FullDescription =>
            $"While held, deal <style=cIsHealth>-{DamagePenalty * 100:0}% damage</style>. " +
            $"Gains a stack every <style=cIsUtility>{Mathf.FloorToInt(SecondsPerStack / 60f)}:{Mathf.FloorToInt(SecondsPerStack % 60f):00}</style> while held. " +
            $"<style=cIsUtility>Activate</style> to hatch, yielding <style=cIsDamage>{Hatched(1)} {YieldText}</style> at 1 stack, " +
            $"<style=cIsDamage>{Hatched(2)}</style> at 2 and <style=cIsDamage>{Hatched(3)}</style> at 3: each stack is worth more than the last. " +
            "Progress is retained if dropped.";
        public override string Lore =>
            "I won't lie, I don't know what that does, but my kid says all the mystics are buying it!";

        public override float Cooldown => 1f; // consumed on use
        protected override string? IconFileName => "icon_goldengooseegg.png";

        public override void Init(ConfigFile config)
        {
            if (All.Count == 0)
            {
                SecondsPerStack = config.Bind("Equipment", "Goose Eggs - Seconds Per Stack", 300f, // 150 at first; 5 min by playtest
                    "Seconds an egg must be held to gain a stack.").Value;
                DamagePenalty = config.Bind("Equipment", "Goose Eggs - Damage Penalty", 0.25f,
                    "Damage reduction while any egg is held.").Value;
            }
            FirstStack = config.Bind("Equipment", $"{EquipmentName} - First Stack Items", DefaultFirstStack,
                "Items the first stack yields.").Value;
            ExtraPerStack = config.Bind("Equipment", $"{EquipmentName} - Extra Items Per Stack", DefaultExtraPerStack,
                "Each stack after the first yields this many more items than the one before (fractions add up; the hatch rounds down).").Value;
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

        public override bool OnUse(EquipmentSlot slot)
        {
            if (!NetworkServer.active) return false;
            var body = slot?.characterBody;
            if (body?.inventory == null) return false;
            Inventory inventory = body.inventory;
            EquipmentLocation location = inventory.activeEquipmentLocation;
            int stacks = StacksFor(GooseEggProgress.For(inventory).Get(location));
            if (stacks <= 0) return false; // not ready: keep the egg, no cooldown

            SpawnItems(body, Hatched(stacks));
            EquipmentFx.Sound("gooseegg_hatch", body.corePosition, body.gameObject);
            EquipmentFx.Effect("RoR2/Base/BonusGoldPackOnKill/MoneyPackPickupEffect.prefab", body.corePosition, body.radius * 2f);
            EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUpEffect.prefab", body.corePosition, body.radius);

            inventory.SetEquipmentIndex(EquipmentIndex.None, in location);
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
            GooseEgg? egg = PickWeighted(rng);
            return egg?.EquipmentDef != null ? PickupCatalog.FindPickupIndex(egg.EquipmentDef.equipmentIndex) : p;
        }

        /// <summary>A random egg by the configured drop weights (null if every weight is 0).</summary>
        public static GooseEgg? PickWeighted(Xoroshiro128Plus rng)
        {
            float total = 0f;
            foreach (GooseEgg e in All) total += Mathf.Max(0f, e.DropWeight);
            if (total <= 0f) return null;
            float roll = rng.nextNormalizedFloat * total;
            foreach (GooseEgg e in All)
            {
                roll -= Mathf.Max(0f, e.DropWeight);
                if (roll < 0f && e.EquipmentDef != null) return e;
            }
            return null;
        }

        /// <summary>
        /// Carries each egg's own progress across a pickup: the incoming egg brings the pickup's progress into the slot,
        /// and the equipment swapped out leaves that slot's progress on the pickup. Holding a Void Egg, another egg is
        /// absorbed into it as one stack instead of being picked up.
        /// </summary>
        private static void AttemptGrant(On.RoR2.GenericPickupController.orig_AttemptGrant orig, GenericPickupController self, CharacterBody body)
        {
            PickupDef? def = PickupCatalog.GetPickupDef(self.pickupIndex);
            if (!NetworkServer.active || body == null || body.inventory == null || def == null || def.equipmentIndex == EquipmentIndex.None)
            {
                orig(self, body);
                return;
            }
            Inventory inventory = body.inventory;
            GooseEggProgress progress = GooseEggProgress.For(inventory);
            EquipmentDef? incoming = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);

            if (Void?.EquipmentDef != null && inventory.currentEquipmentIndex == Void.EquipmentDef.equipmentIndex
                && incoming != Void.EquipmentDef && IsEgg(incoming))
            {
                EquipmentLocation active = inventory.activeEquipmentLocation;
                progress.Set(active, progress.Get(active) + SecondsPerStack);
                EquipmentFx.Effect("RoR2/Base/Common/VFX/LevelUpEffect.prefab", body.corePosition, body.radius);
                UnityEngine.Object.Destroy(self.gameObject);
                return;
            }

            // The same location the game's own equipment grant picks.
            EquipmentLocation location = inventory.FindBestEquipmentLocationOrCurrent(in Inventory.EquipmentLocationQueryArgs.defaultPickup);
            float outgoing = progress.Get(location);
            float carried = GooseEggPickupProgress.Get(self);
            orig(self, body);
            if (inventory.GetEquipmentIndex(in location) == def.equipmentIndex) progress.Set(location, carried);
            if (self != null) GooseEggPickupProgress.Set(self, outgoing); // the swapped-out equipment now lies on this pickup
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
        protected override int DefaultFirstStack => 4;
        protected override float DefaultExtraPerStack => 2f;
        protected override float DefaultDropWeight => 45f;
        protected override string YieldText => "white items";
    }

    public class GreenGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Green Goose Egg";
        public override string EquipmentLangTokenName => "GREENGOOSEEGG";
        protected override Yield Kind => Yield.Green;
        protected override Color Tint => new Color(0.45f, 0.95f, 0.35f);
        protected override int DefaultFirstStack => 2;
        protected override float DefaultExtraPerStack => 1f;
        protected override float DefaultDropWeight => 27f;
        protected override string YieldText => "green items";
    }

    public class RedGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Red Goose Egg";
        public override string EquipmentLangTokenName => "REDGOOSEEGG";
        protected override Yield Kind => Yield.Red;
        protected override Color Tint => new Color(1f, 0.3f, 0.25f);
        protected override int DefaultFirstStack => 1;
        protected override float DefaultExtraPerStack => 0.5f;
        protected override float DefaultDropWeight => 12f;
        protected override string YieldText => "red items";
    }

    public class GoldenGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Golden Goose Egg";
        public override string EquipmentLangTokenName => "GOLDENGOOSEEGG";
        protected override Yield Kind => Yield.Boss;
        protected override Color Tint => new Color(1f, 0.85f, 0.1f);
        protected override int DefaultFirstStack => 1;
        protected override float DefaultExtraPerStack => 1f;
        protected override float DefaultDropWeight => 8f;
        protected override string YieldText => "yellow items";
    }

    public class VoidGooseEgg : GooseEgg
    {
        public override string EquipmentName => "Void Goose Egg";
        public override string EquipmentLangTokenName => "VOIDGOOSEEGG";
        protected override Yield Kind => Yield.Void;
        protected override Color Tint => new Color(0.8f, 0.4f, 1f);
        protected override int DefaultFirstStack => 2;
        protected override float DefaultExtraPerStack => 1f;
        protected override float DefaultDropWeight => 8f;
        protected override string YieldText => "void items";
        public override string FullDescription => base.FullDescription +
            " <style=cIsVoid>Corrupts all Goose Eggs</style>: while you hold it, other eggs you pick up are absorbed as a stack.";
    }

    /// <summary>Server: matures the egg in the holder's active equipment slot and shows its stacks as a buff.</summary>
    public class GooseEggTracker : MonoBehaviour
    {
        public GooseEgg? Egg;
        private CharacterBody _body = null!;
        private GooseEggProgress? _progress;
        private int _shown = -1;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            Inventory? inventory = _body.inventory;
            if (!NetworkServer.active || Egg?.EquipmentDef == null || inventory == null) return;
            EquipmentLocation location = inventory.activeEquipmentLocation;
            if (inventory.GetEquipmentIndex(in location) != Egg.EquipmentDef.equipmentIndex) return;
            _progress ??= GooseEggProgress.For(inventory);
            // Eggs mature on the run's clock: not while it's stopped (the Bazaar, Gilded Coast, between stages...; user, 1.7),
            // and only while the holder is alive (user, 2.0). Holding is checked above: only the active slot matures.
            Run? run = Run.instance;
            bool paused = run == null || run.isRunStopwatchPaused || run.isGameOverServer
                || _body.healthComponent == null || !_body.healthComponent.alive;
            float seconds = _progress.Get(location) + (paused ? 0f : Time.fixedDeltaTime);
            _progress.Set(location, seconds);
            int stacks = GooseEgg.StacksFor(seconds);
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

    /// <summary>Server: how long each egg in this inventory has matured, per equipment location (slot, set).</summary>
    public class GooseEggProgress : MonoBehaviour
    {
        private readonly Dictionary<(uint, uint), (EquipmentIndex egg, float seconds)> _eggs = new Dictionary<(uint, uint), (EquipmentIndex, float)>();
        private Inventory _inventory = null!;

        public static GooseEggProgress For(Inventory inventory)
        {
            var progress = inventory.GetComponent<GooseEggProgress>();
            if (progress == null)
            {
                progress = inventory.gameObject.AddComponent<GooseEggProgress>();
                progress._inventory = inventory;
            }
            return progress;
        }

        /// <summary>Seconds matured by the egg now at this location (0 if the entry belongs to equipment since replaced).</summary>
        public float Get(in EquipmentLocation location) =>
            _eggs.TryGetValue((location.slot, location.set), out var e) && e.egg == _inventory.GetEquipmentIndex(in location) ? e.seconds : 0f;

        public void Set(in EquipmentLocation location, float seconds) =>
            _eggs[(location.slot, location.set)] = (_inventory.GetEquipmentIndex(in location), seconds);
    }

    /// <summary>Server: the progress of an egg lying on the ground, left by whoever swapped it out.</summary>
    public class GooseEggPickupProgress : MonoBehaviour
    {
        private EquipmentIndex _egg = EquipmentIndex.None;
        private float _seconds;

        private static EquipmentIndex EquipmentOf(GenericPickupController pickup) =>
            PickupCatalog.GetPickupDef(pickup.pickupIndex)?.equipmentIndex ?? EquipmentIndex.None;

        /// <summary>0 for a fresh egg, or if the pickup has since become something else (e.g. recycled).</summary>
        public static float Get(GenericPickupController pickup)
        {
            var p = pickup.GetComponent<GooseEggPickupProgress>();
            return p != null && p._egg == EquipmentOf(pickup) ? p._seconds : 0f;
        }

        public static void Set(GenericPickupController pickup, float seconds)
        {
            var p = pickup.GetComponent<GooseEggPickupProgress>();
            if (p == null) p = pickup.gameObject.AddComponent<GooseEggPickupProgress>();
            p._egg = EquipmentOf(pickup);
            p._seconds = seconds;
        }
    }
}
