using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Colossus : EquipmentBase
    {
        public override string EquipmentName          => "Colossus";
        public override string EquipmentLangTokenName => "COLOSSUS";

        public override string FullDescription =>
            $"<style=cIsUtility>Grow to massive size</style> for <style=cIsUtility>{Duration}s</style>. " +
            $"Gain <style=cIsHealing>{DamageResist * 100:0}% damage resistance</style> and " +
            $"<style=cIsUtility>slow</style> all enemies within <style=cIsUtility>{SlowRadius}m</style>. " +
            $"<style=cStack>Activating again while transformed refreshes the duration and stacks — " +
            $"growing larger and adding resistance with diminishing returns (up to {MaxResist * 100:0}%).</style>";

        public override string Lore =>
            "Objectively speaking, turning into a giant punching machine is a thing of beauty";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_colossus.png";

        public static float CooldownSeconds;
        public const float Duration         = 8f;
        public static float DamageResist;
        public static float MaxResist;      // cap on stacked resistance (prevents full immunity)
        public static int   MaxStacks;      // max stacked activations
        public const float ScaleFactor      = 1.75f;
        public const float ScaleDecay       = 0.6f;   // each extra stack grows less (diminishing size)
        public const float SlowRadius       = 20f;
        public const float SlowPulseInterval = 0.5f;

        public static BuffDef? ColossusBuffDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Colossus - Cooldown", 35f,
                "Equipment cooldown in seconds.").Value;
            DamageResist = config.Bind("Equipment", "Colossus - Damage Resistance", 0.65f,
                "Damage resistance from a single activation. Extra activations stack multiplicatively (diminishing returns).").Value;
            MaxResist = config.Bind("Equipment", "Colossus - Max Damage Resistance", 0.90f,
                "Maximum damage resistance reachable by stacking activations.").Value;
            MaxStacks = config.Bind("Equipment", "Colossus - Max Stacks", 5,
                "Maximum number of stacked activations (each further grows size and resistance).").Value;

            var color              = new Color(0.55f, 0.25f, 0.9f);
            ColossusBuffDef        = ScriptableObject.CreateInstance<BuffDef>();
            ColossusBuffDef.name   = "CursedApple_Colossus";
            ColossusBuffDef.canStack  = true;
            ColossusBuffDef.isDebuff  = false;
            ColossusBuffDef.buffColor = color;
            ColossusBuffDef.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(ColossusBuffDef);

            CreateLang();
            CreateEquipment();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot?.characterBody;
            if (body == null) return false;

            // Already transformed → stack another activation (bigger + tougher, diminishing).
            // Otherwise start a fresh transformation (Awake seeds the first stack).
            var effect = body.GetComponent<ColossusEffect>();
            if (effect != null)
                effect.AddStack();
            else
                body.gameObject.AddComponent<ColossusEffect>();

            return true;
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active && damageInfo != null && self.body != null)
            {
                var effect = self.body.GetComponent<ColossusEffect>();
                if (effect != null)
                    damageInfo.damage *= effect.DamageMultiplier;
            }
            orig(self, damageInfo);
        }

    }

    // Manages the active Colossus transformation: scales the model, pulses the slow,
    // and tears itself down cleanly when the timer expires or the body is destroyed.
    // Scale is applied to modelTransform, which cascades to HurtBox child colliders —
    // so enemies genuinely have a larger target. CharacterMotor's own capsule is on
    // the root and is NOT a child, so movement physics are unaffected.
    // NOTE: The scale change runs server-side. In hosted multiplayer the host sees it
    // correctly; remote clients see the buff indicator but not the visual growth.
    public class ColossusEffect : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timer;
        private float         _slowTimer;
        private Vector3       _originalScale;
        private bool          _scaled;
        private int           _stacks;

        // Incoming-damage multiplier from the current stack count. Resistance stacks
        // multiplicatively (each stack negates DamageResist of what's left → diminishing
        // returns), capped at MaxResist so it can never reach full immunity.
        public float DamageMultiplier
        {
            get
            {
                float resist = 1f - Mathf.Pow(1f - Colossus.DamageResist, _stacks);
                return 1f - Mathf.Min(Colossus.MaxResist, resist);
            }
        }

        private void Awake()
        {
            _body   = GetComponent<CharacterBody>();
            _stacks = 1;
            _timer  = Colossus.Duration;

            CaptureOriginalScale();
            UpdateScale();
            SyncBuff();
            _body?.RecalculateStats();
        }

        // A repeat activation while already transformed: grow another (diminishing) step,
        // refresh the duration, and re-apply stats so the new resistance takes effect.
        public void AddStack()
        {
            if (_stacks < Mathf.Max(1, Colossus.MaxStacks)) _stacks++;
            _timer = Colossus.Duration;

            UpdateScale();
            SyncBuff();
            _body?.RecalculateStats();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null) return;

            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f)
            {
                Destroy(this);
                return;
            }

            _slowTimer -= Time.fixedDeltaTime;
            if (_slowTimer <= 0f)
            {
                _slowTimer = Colossus.SlowPulseInterval;
                PulseSlow();
            }
        }

        private void OnDestroy()
        {
            RestoreScale();
            ClearBuff();
            _body?.RecalculateStats();
        }

        private void CaptureOriginalScale()
        {
            if (_scaled || _body?.modelLocator?.modelTransform == null) return;
            _originalScale = _body.modelLocator.modelTransform.localScale;
            _scaled = true;
        }

        // Set size from the original scale (never compound onto the current value, or repeated
        // updates would runaway). Growth is the base step for stack 1, plus a geometrically
        // decaying step per extra stack → the model keeps growing but by less each time.
        private void UpdateScale()
        {
            if (!_scaled || _body?.modelLocator?.modelTransform == null) return;

            float growth = 0f;
            float step   = Colossus.ScaleFactor - 1f;
            for (int i = 0; i < _stacks; i++)
            {
                growth += step;
                step   *= Colossus.ScaleDecay;
            }
            _body.modelLocator.modelTransform.localScale = _originalScale * (1f + growth);
        }

        private void RestoreScale()
        {
            if (!_scaled) return;
            if (_body?.modelLocator?.modelTransform != null)
                _body.modelLocator.modelTransform.localScale = _originalScale;
            _scaled = false;
        }

        private void PulseSlow()
        {
            if (_body.teamComponent == null) return;
            if (RoR2Content.Buffs.Slow80 == null) return;

            var search    = new SphereSearch();
            search.origin = _body.transform.position;
            search.radius = Colossus.SlowRadius;
            search.mask   = LayerIndex.entityPrecise.mask;

            var hurtBoxes = new List<HurtBox>();
            search
                .RefreshCandidates()
                .FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(_body.teamComponent.teamIndex))
                .OrderCandidatesByDistance()
                .FilterCandidatesByDistinctHurtBoxEntities()
                .GetHurtBoxes(hurtBoxes);

            // Buffer the slow duration slightly beyond the pulse interval so it never
            // flickers off between pulses (avoids a debuff that vanishes for one frame).
            foreach (var hb in hurtBoxes)
                hb?.healthComponent?.body?.AddTimedBuff(RoR2Content.Buffs.Slow80, Colossus.SlowPulseInterval + 0.1f);
        }

        // Buff stack count mirrors the activation stacks, so the player sees how many are layered.
        private void SyncBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (Colossus.ColossusBuffDef == null || Colossus.ColossusBuffDef.buffIndex == BuffIndex.None) return;
            _body.SetBuffCount(Colossus.ColossusBuffDef.buffIndex, _stacks);
        }

        private void ClearBuff()
        {
            if (!NetworkServer.active || _body == null) return;
            if (Colossus.ColossusBuffDef == null || Colossus.ColossusBuffDef.buffIndex == BuffIndex.None) return;
            _body.SetBuffCount(Colossus.ColossusBuffDef.buffIndex, 0);
        }
    }
}
