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

        public override string PickupDesc => "Grow huge, resisting damage and slowing nearby enemies.";
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

        /// <summary>
        /// How much bigger Colossus makes this body (1 without it), from the synced buff count so every machine agrees:
        /// the base step for stack 1, then a geometrically decaying step per extra stack.
        /// </summary>
        public static float SizeMultiplier(CharacterBody? body)
        {
            int stacks = body != null && ColossusBuffDef != null && ColossusBuffDef.buffIndex != BuffIndex.None ? body.GetBuffCount(ColossusBuffDef.buffIndex) : 0;
            return SizeForStacks(stacks);
        }

        public static float SizeForStacks(int stacks)
        {
            float growth = 0f;
            float step = ScaleFactor - 1f;
            for (int i = 0; i < stacks; i++)
            {
                growth += step;
                step *= ScaleDecay;
            }
            return 1f + growth;
        }

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
            EquipmentFx.RegisterBuffFx(ColossusBuffDef, "RoR2/Base/GainArmor/ElephantDefense.prefab", "colossus_loop"); // Jade Elephant shell

            CreateLang();
            CreateEquipment();
            Hooks();
        }

        public override void RegisterSounds()
        {
            EquipmentFx.RegisterSound("colossus_cast", "sounds/mods/armor/colossus/colossus_cast", range: 60f);
            EquipmentFx.RegisterSound("colossus_loop", "sounds/mods/armor/colossus/colossus_lp", volume: 0.5f, range: 30f);
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            // Runs on every machine whenever buffs change: start the growth wherever the Colossus buff shows up.
            RecalculateStatsAPI.GetStatCoefficients += (sender, args) =>
            {
                if (ColossusBuffDef != null && sender.HasBuff(ColossusBuffDef) && sender.GetComponent<ColossusScale>() == null)
                    sender.gameObject.AddComponent<ColossusScale>();
            };
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

            EquipmentFx.Sound("colossus_cast", body.corePosition, body.gameObject);
            EquipmentFx.Effect("RoR2/Base/Parent/ParentSlamEffect.prefab", body.footPosition, SlowRadius * 0.5f * MysticExpansion.RadiusMultiplier(body));
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
    // The growth itself is ColossusScale, which every machine runs from the synced buff count.
    public class ColossusEffect : MonoBehaviour
    {
        private CharacterBody _body;
        private float         _timer;
        private float         _slowTimer;
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
            _timer  = Colossus.Duration * DurationExtender.Multiplier(_body);

            SyncBuff();
            _body?.RecalculateStats();
        }

        // A repeat activation while already transformed: grow another (diminishing) step,
        // refresh the duration, and re-apply stats so the new resistance takes effect.
        public void AddStack()
        {
            if (_stacks < Mathf.Max(1, Colossus.MaxStacks)) _stacks++;
            _timer = Colossus.Duration * DurationExtender.Multiplier(_body);

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
            ClearBuff();
            _body?.RecalculateStats();
        }

        private void PulseSlow()
        {
            if (_body.teamComponent == null) return;
            if (RoR2Content.Buffs.Slow80 == null) return;

            var search    = new SphereSearch();
            search.origin = _body.transform.position;
            search.radius = Colossus.SlowRadius * MysticExpansion.RadiusMultiplier(_body);
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

    /// <summary>
    /// Colossus' growth, on every machine (the transformation itself is server-only): sized from the synced Colossus buff
    /// count, so remote players see each other grow. Scaling modelTransform also grows the hurtboxes under it.
    /// </summary>
    public class ColossusScale : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private Transform?    _model;
        private Vector3       _originalScale;
        private int           _shownStacks;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            int stacks = _body != null && Colossus.ColossusBuffDef != null && Colossus.ColossusBuffDef.buffIndex != BuffIndex.None
                ? _body.GetBuffCount(Colossus.ColossusBuffDef.buffIndex) : 0;
            if (stacks == _shownStacks) return;
            if (stacks <= 0) { Destroy(this); return; }
            if (_model == null)
            {
                _model = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
                if (_model == null) return;
                _originalScale = _model.localScale;
            }
            _shownStacks = stacks;
            _model.localScale = _originalScale * Colossus.SizeForStacks(stacks); // from the original scale, never compounded
        }

        private void OnDestroy()
        {
            if (_model != null && _shownStacks > 0) _model.localScale = _originalScale;
        }
    }
}
