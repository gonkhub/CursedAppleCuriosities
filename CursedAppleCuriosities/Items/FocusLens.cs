using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class FocusLens : EquipmentBase
    {
        public override string EquipmentName          => "Focus Lens";
        public override string EquipmentLangTokenName => "FOCUSLENS";

        public override string PickupDesc => "Silence an enemy. Half the damage it takes comes back when the silence ends.";
        public override string FullDescription =>
            $"<style=cIsUtility>Focus</style> the enemy nearest your crosshair within <style=cIsUtility>{Range}m</style>, " +
            $"<style=cIsUtility>silencing</style> it for <style=cIsUtility>{MarkDuration}s</style> (it can't use skills). " +
            $"When the silence ends, it takes <style=cIsDamage>{EchoFraction * 100:0}%</style> of all damage it took " +
            $"during the silence <style=cIsDamage>again</style>. Works on bosses.";

        public override string Lore =>
            "Look closer. No, closer. There. Now hold still.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_focuslens.png";

        public static float CooldownSeconds;
        public static float MarkDuration;
        public static float EchoFraction;
        public const float Range    = 60f;
        public const float AimAngle = 20f;

        public static BuffDef? FocusedBuffDef;
        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Focus Lens - Cooldown", 45f,
                "Equipment cooldown in seconds.").Value;
            MarkDuration = config.Bind("Equipment", "Focus Lens - Silence Duration", 4.5f,
                "Seconds the target is silenced and the damage it takes is recorded.").Value;
            EchoFraction = config.Bind("Equipment", "Focus Lens - Echo Fraction", 0.5f,
                "Share of the recorded damage dealt again when the silence ends (0.5 = 50%).").Value;

            var color                 = new Color(0.65f, 0.4f, 1f);
            FocusedBuffDef            = ScriptableObject.CreateInstance<BuffDef>();
            FocusedBuffDef.name       = "CursedApple_FocusLens";
            FocusedBuffDef.canStack   = false;
            FocusedBuffDef.isDebuff   = true;
            FocusedBuffDef.buffColor  = color;
            FocusedBuffDef.iconSprite = ItemBase.BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(FocusedBuffDef);

            SkillSilence.Register(FocusedBuffDef);
            EquipmentFx.RegisterBuffFx(FocusedBuffDef, "RoR2/Base/Items/DeathMark/DeathMarkEffect.prefab", "focuslens_loop");

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            EquipmentTargeting.Register(new EquipmentTargeting.Rule { Def = EquipmentDef, Range = Range, Angle = AimAngle });
            Hooks();
        }

        public override void RegisterSounds()
        {
            EquipmentFx.RegisterSound("focuslens_cast", "sounds/mods/tech/focus_lens/focus_lens_cast");
            EquipmentFx.RegisterSound("focuslens_start", "sounds/mods/tech/focus_lens/focus_lens_modifier_start", range: 60f, fallback: "Play_item_lunar_focusedConvergence");
            EquipmentFx.RegisterSound("focuslens_loop", "sounds/mods/tech/focus_lens/focus_lens_modifier_lp", volume: 0.5f, range: 30f);
            EquipmentFx.RegisterSound("focuslens_explode", "sounds/mods/tech/focus_lens/focus_lens_modifier_explo", range: 70f, fallback: "Play_item_proc_crit_cooldown");
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null) return false;

            var target = EquipmentTargeting.FindEnemy(body, Range * MysticExpansion.RadiusMultiplier(body), AimAngle, _ => true);
            if (target == null) return false; // no target → no cooldown

            var mark = target.GetComponent<FocusLensMark>() ?? target.gameObject.AddComponent<FocusLensMark>();
            mark.Begin(body, MarkDuration * DurationExtender.Current); // the user's, from the equipment scope
            EquipmentFx.Sound("focuslens_cast", body.corePosition, body.gameObject);
            EquipmentFx.Sound("focuslens_start", target.corePosition, target.gameObject);
            EquipmentFx.Effect("RoR2/Base/Characters/Huntress/Skills/HuntressBlinkEffect.prefab", target.corePosition, target.radius * 2f);
            return true;
        }

        // Record after orig so rejected or blocked hits don't count; the echo itself is never recorded.
        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            orig(self, damageInfo);
            if (!NetworkServer.active || damageInfo == null || damageInfo.rejected || self.body == null) return;

            var mark = self.body.GetComponent<FocusLensMark>();
            if (mark != null) mark.Record(damageInfo);
        }
    }

    // Server-side, on the focused enemy: silence buff, damage tally, and the echo.
    public class FocusLensMark : MonoBehaviour
    {
        private CharacterBody  _body = null!;
        private CharacterBody? _owner;
        private float          _timer;
        private float          _recorded;
        private bool           _echoing;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Begin(CharacterBody owner, float duration)
        {
            if (!NetworkServer.active || _body == null) return;
            _owner = owner;
            _timer = duration; // recasting on the same target restarts the window and keeps the tally
            if (FocusLens.FocusedBuffDef != null) using (DurationExtender.Exact()) _body.AddTimedBuff(FocusLens.FocusedBuffDef, duration);
        }

        // Every source counts (allies, DoTs, other enemies); the enemy's own self-damage does not.
        public void Record(DamageInfo damageInfo)
        {
            if (_timer <= 0f || _echoing || damageInfo.damage <= 0f) return;
            if (damageInfo.attacker == _body.gameObject) return;
            _recorded += damageInfo.damage;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _timer <= 0f) return;
            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;

            float echo = _recorded * FocusLens.EchoFraction;
            _recorded  = 0f;
            if (echo <= 0f || _body.healthComponent == null || !_body.healthComponent.alive) return;

            _echoing = true;
            _body.healthComponent.TakeDamage(new DamageInfo
            {
                attacker         = _owner != null ? _owner.gameObject : null,
                inflictor        = _owner != null ? _owner.gameObject : null,
                damage           = echo,
                procCoefficient  = 0f,
                position         = _body.corePosition,
                damageType       = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Equipment),
                damageColorIndex = DamageColorIndex.WeakPoint,
            });
            _echoing = false;
            EquipmentFx.Sound("focuslens_explode", _body.corePosition, _body.gameObject);
            EquipmentFx.Effect("RoR2/DLC1/Items/CritGlassesVoid/CritGlassesVoidExecuteEffect.prefab", _body.corePosition, _body.radius * 2f);
        }
    }
}
