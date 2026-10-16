using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Frenzy : ItemBase
    {
        public override string ItemName => "Frenzy";
        public override string ItemLangTokenName => "FRENZY";

        public override string ItemPickupDesc => "Gain damage and speed the lower your health falls.";
        public override string ItemFullDescription =>
            $"Below <style=cIsHealth>{RampStartBase * 100:0}% health</style> " +
            $"<style=cStack>(+{RampStartPerStack * 100:0}% per stack)</style>, gain " +
            $"<style=cIsDamage>damage</style>, <style=cIsDamage>attack speed</style>, and " +
            $"<style=cIsUtility>movement speed</style> that scale the lower your health falls — up to " +
            $"<style=cIsDamage>+{MaxDamageBase * 100:0}% damage</style>, " +
            $"<style=cIsDamage>+{MaxAttackSpeedBase * 100:0}% attack speed</style>, and " +
            $"<style=cIsUtility>+{MaxMoveSpeedBase * 100:0}% movement speed</style> " +
            $"<style=cStack>(+{DamagePerStack * 100:0}%, +{AttackSpeedPerStack * 100:0}%, and +{MoveSpeedPerStack * 100:0}% per stack)</style> " +
            $"at the brink of death. The bonus <style=cIsUtility>lasts {HoldSeconds:0.#}s</style> at its strongest after you climb back above the threshold.";

        public override string ItemLore =>
            "Hopefully this'll give you the edge to survive, but if it doesn't you're gonna go down swinging!";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage, ItemTag.Utility };
        protected override string IconFileName => "icon_frenzy.png";

        // The bonus ramps linearly from 0 at RampStart health down to its max at 0 health.
        public const float RampStartBase     = 0.50f; // bonus begins below this health fraction
        public const float RampStartPerStack = 0.08f; // ...higher window with more copies
        public const float RampStartCap      = 0.90f; // never let it become effectively always-on
        public static float MaxDamageBase;
        public const float DamagePerStack      = 0.25f;
        public static float MaxAttackSpeedBase;
        public const float AttackSpeedPerStack = 0.25f;
        public static float MaxMoveSpeedBase;
        public static float HoldSeconds = 5f; // the bonus holds at its peak this long after health climbs back (user)
        public const float MoveSpeedPerStack   = 0.15f;

        public static BuffDef? FrenzyBuffDef;

        public override void Init(ConfigFile config)
        {
            var color               = new Color(0.85f, 0.1f, 0.5f);
            FrenzyBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            FrenzyBuffDef.name      = "CursedApple_Frenzy";
            FrenzyBuffDef.canStack  = false;
            FrenzyBuffDef.isDebuff  = false;
            FrenzyBuffDef.buffColor = color;
            FrenzyBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(FrenzyBuffDef);

            MaxDamageBase = 0.40f;
            MaxAttackSpeedBase = 0.50f;
            MaxMoveSpeedBase = 0.25f;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += CharacterBody_OnInventoryChanged;
            RecalculateStatsAPI.GetStatCoefficients  += GetStatCoefficients;
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);
            if (self.inventory == null) return;

            int count   = self.inventory.GetItemCountEffective(ItemDef);
            var tracker = self.GetComponent<FrenzyTracker>();

            if (count > 0 && tracker == null)
                self.gameObject.AddComponent<FrenzyTracker>().Initialize(ItemDef);
            else if (count <= 0 && tracker != null)
                UnityEngine.Object.Destroy(tracker);
        }

        // 0 at RampStart health, 1 at 0 health. Shared by the stat hook and the tracker.
        public static float RampFraction(CharacterBody body, int count)
        {
            float rampStart  = Mathf.Min(RampStartCap, RampStartBase + RampStartPerStack * (count - 1));
            float hpFraction = body.healthComponent?.combinedHealthFraction ?? 1f;
            return Mathf.Clamp01((rampStart - hpFraction) / rampStart);
        }

        private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (sender.inventory == null) return;
            int count = sender.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            // The tracker's value: the live ramp, or the held peak for a while after climbing back above the threshold.
            var tracker = sender.GetComponent<FrenzyTracker>();
            float frac = tracker != null ? tracker.Fraction(count) : RampFraction(sender, count);
            if (frac <= 0f) return;

            float maxDamage      = MaxDamageBase      + DamagePerStack      * (count - 1);
            float maxAttackSpeed = MaxAttackSpeedBase + AttackSpeedPerStack * (count - 1);
            float maxMoveSpeed   = MaxMoveSpeedBase   + MoveSpeedPerStack   * (count - 1);
            args.damageMultAdd      += maxDamage      * frac;
            args.attackSpeedMultAdd += maxAttackSpeed * frac;
            args.moveSpeedMultAdd   += maxMoveSpeed   * frac;
        }
    }

    public class FrenzyTracker : MonoBehaviour
    {
        private ItemDef       _itemDef;
        private CharacterBody _body;
        private float         _checkTimer;
        private bool          _wasActive;
        private float         _lastFrac;
        private float         _heldFrac;    // the strongest ramp reached while below the threshold
        private float         _heldUntil;   // until when it holds (refreshed while below)

        private const float CheckInterval = 0.1f;
        private const float RecalcEpsilon = 0.03f; // re-apply stats when the ramp shifts this much

        public FrenzyTracker Initialize(ItemDef itemDef)
        {
            _itemDef = itemDef;
            return this;
        }

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>
        /// The bonus strength now: the live ramp while below the threshold (its peak is remembered), and that peak for
        /// <see cref="Frenzy.HoldSeconds"/> after health climbs back above it (user: a fixed duration).
        /// </summary>
        public float Fraction(int count)
        {
            float live = Frenzy.RampFraction(_body, count);
            if (live > 0.001f)
            {
                _heldFrac = Mathf.Max(_heldFrac, live);
                _heldUntil = Time.time + Frenzy.HoldSeconds;
            }
            if (Time.time < _heldUntil) return Mathf.Max(live, _heldFrac);
            _heldFrac = 0f;
            return live;
        }

        private void OnDestroy()
        {
            if (_wasActive) SetBuff(false);
        }

        private void FixedUpdate()
        {
            if (_body == null || _itemDef == null) return;

            _checkTimer -= Time.fixedDeltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = CheckInterval;

            int count = _body.inventory?.GetItemCountEffective(_itemDef) ?? 0;
            if (count <= 0) return;

            float frac   = Fraction(count);
            bool  active = frac > 0.001f;

            // The bonus is smooth, so re-apply stats whenever the ramp shifts meaningfully — not
            // just when it switches on/off — so the speed tracks your health in real time.
            if (active != _wasActive || (active && Mathf.Abs(frac - _lastFrac) > RecalcEpsilon))
            {
                _wasActive = active;
                _lastFrac  = frac;
                SetBuff(active);
                _body.RecalculateStats();
            }
        }

        private void SetBuff(bool active)
        {
            if (!NetworkServer.active || _body == null) return;
            if (Frenzy.FrenzyBuffDef == null || Frenzy.FrenzyBuffDef.buffIndex == BuffIndex.None) return;
            var idx = Frenzy.FrenzyBuffDef.buffIndex;
            bool has = _body.HasBuff(idx);
            if (active && !has) _body.AddBuff(idx);
            else if (!active && has) _body.RemoveBuff(idx);
        }
    }
}
