using BepInEx.Configuration;
using R2API;
using RoR2;
using RoR2.Orbs;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Ricochet : ItemBase
    {
        public override string ItemName => "Ricochet";
        public override string ItemLangTokenName => "RICOCHET";

        public override string ItemPickupDesc => "Your hits ricochet to nearby enemies.";
        public override string ItemFullDescription =>
            $"Hits <style=cIsDamage>ricochet</style> to up to " +
            $"<style=cIsDamage>{BaseTargets}</style> <style=cStack>(+{TargetsPerStack} per stack)</style> " +
            $"enemies within <style=cIsUtility>{Range:0}m</style> <style=cStack>(+{RangePerStack:0}m per stack)</style>, dealing " +
            $"<style=cIsDamage>{DamageFraction * 100:0}%</style> of the original hit's damage. " +
            $"Ricochets have a <style=cIsDamage>{ProcTransferChance:0}%</style> chance to carry the hit's " +
            $"on-hit effects, and cannot ricochet again.";

        public override string ItemLore =>
            "Aim? Nah. Just point it at the general vibe of the room.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_ricochet.png";

        public static float DamageFraction;      // ricochet damage as a share of the original hit
        public static int   BaseTargets;
        public static int   TargetsPerStack;
        public static float Range;
        public static float RangePerStack;
        public const float  ProcTransferChance = 50f; // % chance a ricochet keeps the hit's proc coefficient

        // Marks a ricochet hit (and anything it procs, since items copy the proc chain mask
        // into their own attacks) so it can never start another ricochet.
        public static ModdedProcType RicochetProc;

        public override void Init(ConfigFile config)
        {
            RicochetProc = ProcTypeAPI.ReserveProcType();

            DamageFraction = config.Bind("Red", "Ricochet - Damage Fraction", 0.5f,
                "Ricochet damage as a fraction of the original hit (0.5 = half).").Value;
            BaseTargets = config.Bind("Red", "Ricochet - Targets Base", 2,
                "Enemies each hit ricochets to with a single copy.").Value;
            TargetsPerStack = config.Bind("Red", "Ricochet - Targets Per Stack", 1,
                "Additional ricochet targets per extra copy.").Value;
            Range = config.Bind("Red", "Ricochet - Range", 26f,
                "Ricochet search radius around the enemy that was hit, in meters.").Value;
            RangePerStack = config.Bind("Red", "Ricochet - Range Per Stack", 5f,
                "Additional ricochet search radius per extra copy, in meters.").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f || damageInfo.damage <= 0f) return;
            if (ProcTypeAPI.HasModdedProc(damageInfo.procChainMask, RicochetProc)) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null || attackerBody.teamComponent == null) return;
            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            // Low-proc hits (shotgun pellets, fast multi-hits) ricochet proportionally less often.
            if (!Util.CheckRoll(100f * damageInfo.procCoefficient, attackerBody.master)) return;

            var victimHC = victim.GetComponent<HealthComponent>();
            if (victimHC == null) return;

            var mask = damageInfo.procChainMask;
            ProcTypeAPI.AddModdedProc(ref mask, RicochetProc);

            // Shared exclusion list: the struck enemy plus every enemy already picked,
            // so each ricochet lands on a different target.
            var excluded = new List<HealthComponent> { victimHC };
            Vector3 origin = victimHC.body != null ? victimHC.body.corePosition : damageInfo.position;
            int   targets = BaseTargets + TargetsPerStack * (count - 1);
            float range   = (Range + RangePerStack * (count - 1)) * MysticExpansion.RadiusMultiplier(attackerBody);

            for (int i = 0; i < targets; i++)
            {
                // Each ricochet independently rolls to carry the original hit's on-hit effects.
                float procCoefficient = Util.CheckRoll(ProcTransferChance, attackerBody.master)
                    ? damageInfo.procCoefficient : 0f;

                var orb = new LightningOrb
                {
                    origin                     = origin,
                    damageValue                = damageInfo.damage * DamageFraction,
                    isCrit                     = damageInfo.crit,
                    attacker                   = damageInfo.attacker,
                    inflictor                  = damageInfo.attacker,
                    teamIndex                  = attackerBody.teamComponent.teamIndex,
                    procChainMask              = mask,
                    procCoefficient            = procCoefficient,
                    damageColorIndex           = DamageColorIndex.Item,
                    damageType                 = DamageType.Generic,
                    bouncesRemaining           = 0,
                    targetsToFindPerBounce     = 1,
                    range                      = range,
                    damageCoefficientPerBounce = 1f,
                    lightningType              = LightningOrb.LightningType.RazorWire,
                    bouncedObjects             = excluded,
                };

                HurtBox? target = orb.PickNextTarget(origin);
                if (target == null) break; // no enemies left in range
                excluded.Add(target.healthComponent);
                orb.target = target;
                OrbManager.instance.AddOrb(orb);
            }
        }
    }
}
