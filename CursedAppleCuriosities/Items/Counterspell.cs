using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Lash;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Counterspell : EquipmentBase
    {
        public override string EquipmentName => "Counterspell";
        public override string EquipmentLangTokenName => "COUNTERSPELL";

        public override string PickupDesc => "Negate the next instance of damage you take.";
        public override string FullDescription =>
            $"Become <style=cIsHealing>warded</style> for <style=cIsUtility>{WardDuration}s</style>. " +
            $"The next instance of damage you take is <style=cIsHealing>completely negated</style>" +
            (HealFraction > 0f
                ? $", healing you for <style=cIsHealing>{HealFraction * 100:0}% of your max health</style>."
                : ".");

        public override string Lore =>
            "Don't mind the blood on that, it's gently used.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_counterspell.png";

        public const float WardDuration = 2f;
        public static float CooldownSeconds;
        public static float HealFraction; // reward on a successful block; set 0 for pure block

        // Deadlock's own: the parry cast on activation, and m_strSuccessProcSound (Mods.Armor.Spellbreaker.Proc)
        // when the ward blocks something.
        public const string CastSound  = "counterspell_cast";
        public const string BlockSound = "counterspell_block";

        public override void RegisterSounds()
        {
            DeadlockSounds.Register(new DeadlockSound { Id = CastSound, VpkPrefix = "sounds/player/melee/shared/melee_parry_cast", Volume = 0.9f, Range = 30f });
            DeadlockSounds.Register(new DeadlockSound { Id = BlockSound, VpkPrefix = "sounds/mods/armor/spellbreaker/spellbreaker_proc", Volume = 0.8f, Range = 40f, MinGap = 0.05f });
        }

        public static BuffDef? WardBuffDef;
        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = 25f;
            HealFraction = 0.08f;

            var color               = new Color(0.4f, 0.8f, 1f);
            WardBuffDef             = ScriptableObject.CreateInstance<BuffDef>();
            WardBuffDef.name        = "CursedApple_CounterspellWard";
            WardBuffDef.canStack    = false;
            WardBuffDef.isDebuff    = false;
            WardBuffDef.buffColor   = color;
            WardBuffDef.iconSprite  = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(WardBuffDef);
            EquipmentFx.RegisterBuffFx(WardBuffDef, "RoR2/DLC1/BearVoid/BearVoidEffect.prefab"); // Safer Spaces bubble while warded

            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            var body = slot != null ? slot.characterBody : null;
            if (body == null) return false;

            var ward = body.GetComponent<CounterspellWard>()
                ?? body.gameObject.AddComponent<CounterspellWard>();
            ward.Activate(WardDuration * DurationExtender.Multiplier(body));
            SoundMessage.Everyone(CastSound, body.corePosition, body.gameObject);
            EquipmentFx.Effect("RoR2/DLC1/ImmuneToDebuff/ImmuneToDebuffEffect.prefab", body.corePosition, body.radius);
            return true; // start the cooldown
        }

        private void HealthComponent_TakeDamage(
            On.RoR2.HealthComponent.orig_TakeDamage orig,
            HealthComponent self,
            DamageInfo damageInfo)
        {
            if (NetworkServer.active
                && self.body != null
                && damageInfo != null
                && damageInfo.damage > 0f
                // DoT ticks shouldn't waste the ward — it should save a real hit. The mod's own ticks (Bloodletting,
                // Toxic Bullets) carry the DoT flag but no dotIndex.
                && damageInfo.dotIndex == DotController.DotIndex.None
                && (damageInfo.damageType.damageType & DamageType.DoT) == 0)
            {
                var ward = self.body.GetComponent<CounterspellWard>();
                if (ward != null && ward.IsActive)
                {
                    ward.Consume();
                    SoundMessage.Everyone(BlockSound, self.body.corePosition, self.body.gameObject);
                    EquipmentFx.Effect("RoR2/Base/Bear/BearProc.prefab", self.body.corePosition, self.body.radius);

                    if (HealFraction > 0f)
                        self.body.healthComponent?.Heal(self.body.maxHealth * HealFraction, default(ProcChainMask));

                    // Fully negate this instance: rejected, like Tougher Times' block, so its on-hit effects
                    // (debuffs, burns, elite effects) don't land either.
                    damageInfo.damage   = 0f;
                    damageInfo.force    = Vector3.zero;
                    damageInfo.rejected = true;
                }
            }

            orig(self, damageInfo);
        }
    }

    // Lives on a body while Counterspell's ward is up; the TakeDamage hook consumes it on the
    // first real hit. Manages the ward buff icon so the player can see the window is active.
    public class CounterspellWard : MonoBehaviour
    {
        private float         _timer;
        private CharacterBody _body;

        public bool IsActive => _timer > 0f;

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Activate(float duration)
        {
            _timer = duration;
            ShowBuff(true);
        }

        public void Consume()
        {
            _timer = 0f;
            ShowBuff(false);
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) ShowBuff(false);
        }

        private void OnDestroy() => ShowBuff(false);

        private void ShowBuff(bool on)
        {
            if (!NetworkServer.active || _body == null) return;
            if (Counterspell.WardBuffDef == null || Counterspell.WardBuffDef.buffIndex == BuffIndex.None) return;
            var idx = Counterspell.WardBuffDef.buffIndex;
            if (on && !_body.HasBuff(idx)) _body.AddBuff(idx);
            else if (!on && _body.HasBuff(idx)) _body.RemoveBuff(idx);
        }
    }
}
