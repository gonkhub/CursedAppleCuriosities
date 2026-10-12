using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class MajesticLeap : EquipmentBase
    {
        public override string EquipmentName          => "Majestic Leap";
        public override string EquipmentLangTokenName => "MAJESTICLEAP";

        public override string PickupDesc => "Leap forward, high into the air, and gain a barrier.";
        public override string FullDescription =>
            $"<style=cIsUtility>Launch</style> yourself forward and high into the air, gaining a " +
            $"<style=cIsHealing>temporary barrier</style> for <style=cIsHealing>{BarrierFraction * 100:0}% of your maximum health</style>. " +
            "You take no fall damage from the landing.";

        public override string Lore =>
            "Some say it's a cape. Some say it's a lifestyle. Everyone agrees it's a long way down.";

        public override float Cooldown => CooldownSeconds;
        protected override string? IconFileName => "icon_majesticleap.png";

        public static float CooldownSeconds;
        public static float BarrierFraction;
        public static float UpSpeed;
        public static float ForwardSpeed;
        public const float LandingWindow = 10f; // fall damage stays off until the landing, or this long

        public static EquipmentDef? StaticEquipmentDef;

        public override void Init(ConfigFile config)
        {
            CooldownSeconds = config.Bind("Equipment", "Majestic Leap - Cooldown", 45f,
                "Equipment cooldown in seconds.").Value;
            BarrierFraction = config.Bind("Equipment", "Majestic Leap - Barrier", 0.5f,
                "Barrier gained, as a share of max health (0.5 = 50%).").Value;
            UpSpeed = config.Bind("Equipment", "Majestic Leap - Up Speed", 38f,
                "Launch speed upward, metres per second.").Value;
            ForwardSpeed = config.Bind("Equipment", "Majestic Leap - Forward Speed", 22f,
                "Launch speed forward (where you aim, flattened), metres per second.").Value;
            CreateLang();
            CreateEquipment();
            StaticEquipmentDef = EquipmentDef;
            Hooks();
        }

        public override void Hooks() => DeadlockFallGuard.Hook();

        public override void RegisterSounds()
        {
            EquipmentFx.RegisterSound("majesticleap_jump", "sounds/mods/armor/majestic_leap/majestic_leap_jump", range: 60f, fallback: "Play_loader_shift_activate");
            EquipmentFx.RegisterSound("majesticleap_land", "sounds/mods/armor/majestic_leap/majestic_leap_land", range: 50f, fallback: "Play_char_land");
            EquipmentFx.RegisterSound("majesticleap_barrier", "sounds/player/barrier/barrier_activate", volume: 0.7f);
        }

        public override bool OnUse(EquipmentSlot slot)
        {
            if (!NetworkServer.active) return false;
            var body = slot != null ? slot.characterBody : null;
            if (body == null || body.characterMotor == null || body.healthComponent == null) return false;

            Vector3 aim = body.inputBank != null ? body.inputBank.aimDirection : body.transform.forward;
            aim.y = 0f;
            aim = aim.sqrMagnitude > 0.01f ? aim.normalized : body.transform.forward;
            // The motor belongs to the owning client: ApplyForceImpulse sends it there (and runs locally for the host).
            body.characterMotor.ApplyForceImpulse(new PhysForceInfo
            {
                force = aim * ForwardSpeed * MysticExpansion.RadiusMultiplier(body) + Vector3.up * UpSpeed, // Mystic Expansion: leap farther
                massIsOne = true,
                resetVelocity = true,
                ignoreGroundStick = true,
            });

            body.healthComponent.AddBarrier(body.maxHealth * BarrierFraction);
            GameObject who = body.gameObject;
            DeadlockFallGuard.ProtectNextLanding(who, LandingWindow,
                () => { if (who != null) EquipmentFx.Sound("majesticleap_land", who.transform.position, who); });

            EquipmentFx.Effect("RoR2/Base/JumpBoost/BoostJumpEffect.prefab", body.footPosition, 2f);
            EquipmentFx.Sound("majesticleap_jump", body.corePosition, body.gameObject);
            EquipmentFx.Sound("majesticleap_barrier", body.corePosition, body.gameObject);
            return true;
        }
    }
}
