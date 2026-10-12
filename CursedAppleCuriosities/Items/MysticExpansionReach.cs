using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Items;
using RoR2.Orbs;
using RoR2.Projectile;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Mystic Expansion on vanilla content: the ranges of Ukulele, Tesla Coil, N'kuhanna's Opinion, Razorwire and Focus
    /// Crystal, the wards of Warbanner, Bustling Fungus and Mercurial Rachis, and explosion visuals grown to match
    /// the bigger blasts. Each hook is independent: one that can't find its spot logs and leaves that item alone.
    /// </summary>
    internal static class MysticExpansionReach
    {
        private static float M(CharacterBody? body) => MysticExpansion.RadiusMultiplier(body);
        private static float M(GameObject? owner) => owner != null ? M(owner.GetComponent<CharacterBody>()) : 1f;

        public static void Hook()
        {
            // Ukulele and Tesla Coil: the lightning's search range (set once per orb, before its first target pick).
            On.RoR2.Orbs.LightningOrb.PickNextTarget += (orig, self, position) =>
            {
                if ((self.lightningType == LightningOrb.LightningType.Ukulele || self.lightningType == LightningOrb.LightningType.Tesla)
                    && Scaled.TryGetValue(self, out _) == false)
                {
                    Scaled.Add(self, Marker);
                    self.range *= M(self.attacker);
                }
                return orig(self, position);
            };
            // N'kuhanna's Opinion: the skull's 40 m target search.
            On.RoR2.Orbs.DevilOrb.PickNextTarget += (orig, self, position, range) =>
                orig(self, position, self.effectType == DevilOrb.EffectType.Skull ? range * M(self.attacker) : range);

            IL.RoR2.HealthComponent.TakeDamageProcess += TakeDamageProcess;
            IL.RoR2.Items.WardOnLevelManager.OnCharacterLevelUp += il =>
                ScaleWard(il, "Warbanner", c => c.Emit(OpCodes.Ldarg_0), typeof(BuffWard));
            IL.RoR2.Items.MushroomBodyBehavior.FixedUpdate += il =>
                ScaleWard(il, "Bustling Fungus", EmitItemBody, typeof(HealingWard));
            IL.RoR2.Items.RandomDamageZoneBodyBehavior.FixedUpdate += il =>
                ScaleWard(il, "Mercurial Rachis", EmitItemBody, typeof(BuffWard));
            IL.RoR2.Items.WarCryOnCombatBodyBehavior.ActivateWarCryAura += il =>
                ScaleWard(il, "Berzerkers Pauldron", EmitItemBody, typeof(BuffWard));
            IL.RoR2.Items.JumpDamageStrikeBodyBehavior.UpdateAura += il =>
                ScaleWard(il, "Faraday Spur", EmitItemBody, typeof(SphereZone));
            // Frost Relic: its blast already grows (BlastAttack), so its visible ward must match.
            IL.RoR2.IcicleAuraController.FixedUpdate += il => ScaleWard(il, "Frost Relic", c =>
            {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<IcicleAuraController, CharacterBody?>>(a => a.owner != null ? a.owner.GetComponent<CharacterBody>() : null);
            }, typeof(BuffWard));
            // The teleporter's Warbanner and the level-up cripple ward: the owner is the body local the loop just read.
            IL.RoR2.TeleporterInteraction.ChargingState.OnEnter += il => ScaleWardFromLocal(il, "Warbanner (teleporter)",
                i => i.MatchCallOrCallvirt<TeamComponent>("get_body"));
            IL.RoR2.Items.CrippleWardOnLevelManager.onRunAmbientLevelUp += il => ScaleWardFromLocal(il, "cripple ward",
                i => i.MatchCallOrCallvirt<CharacterMaster>("GetBody"));
            // Focus Crystal: its ring is a fixed-size attachment, so grow it on every client.
            On.RoR2.NetworkedBodyAttachment.OnStartClient += (orig, self) =>
            {
                orig(self);
                if (self.name.StartsWith("NearbyDamageBonusIndicator", StringComparison.Ordinal) && self.GetComponent<MysticIndicatorScale>() == null)
                    self.gameObject.AddComponent<MysticIndicatorScale>();
            };

            // Explosion visuals: projectile explosions spawn their effect before the blast, so grow the radius there
            // (once, for both); other blasts grow effects spawned right with them at their old size.
            On.RoR2.Projectile.ProjectileExplosion.DetonateServer += DetonateServer;
            On.RoR2.EffectManager.SpawnEffect_EffectIndex_EffectData_bool += SpawnEffect;
        }

        private static readonly ConditionalWeakTable<LightningOrb, object> Scaled = new ConditionalWeakTable<LightningOrb, object>();
        private static readonly object Marker = new object();

        private static void EmitItemBody(ILCursor c)
        {
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<BaseItemBodyBehavior, CharacterBody>>(b => b.body);
        }

        /// <summary>Scale the radius a ward is given (its Networkradius setter) by its owner's Mystic Expansion.</summary>
        private static void ScaleWard(ILContext il, string what, Action<ILCursor> emitOwner, Type ward)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(i => i.MatchCallOrCallvirt(ward, "set_Networkradius")))
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[MysticExpansion] {what}: radius not found, left unscaled.");
                return;
            }
            emitOwner(c);
            c.EmitDelegate<Func<float, CharacterBody, float>>((r, body) => r * M(body));
        }

        /// <summary>
        /// A ward spawned in a loop over bodies: the body is the local stored right after the getter (the first store
        /// after it), and the ward's radius setter follows.
        /// </summary>
        private static void ScaleWardFromLocal(ILContext il, string what, Func<Instruction, bool> bodyGetter)
        {
            var c = new ILCursor(il);
            int local = -1;
            if (!c.TryGotoNext(MoveType.After, bodyGetter) || !c.TryGotoNext(i => i.MatchStloc(out local))
                || !c.TryGotoNext(i => i.MatchCallOrCallvirt<BuffWard>("set_Networkradius")))
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[MysticExpansion] {what}: radius not found, left unscaled.");
                return;
            }
            c.Emit(OpCodes.Ldloc, local);
            c.EmitDelegate<Func<float, CharacterBody, float>>((r, body) => r * M(body));
        }

        private static void TakeDamageProcess(ILContext il)
        {
            var c = new ILCursor(il);
            // Focus Crystal: "within 13 m" is a squared-distance check against 169.
            if (c.TryGotoNext(MoveType.After, i => i.MatchLdcR4(169f)))
            {
                c.Emit(OpCodes.Ldarg_1);
                c.EmitDelegate<Func<float, DamageInfo, float>>((sq, info) => { float m = M(info?.attacker); return sq * m * m; });
            }
            else CursedAppleCuriositiesPlugin.Logger?.LogWarning("[MysticExpansion] Focus Crystal: range not found, left unscaled.");

            // Razorwire: radius = 25 + 10 per extra stack, around the one being hit.
            c.Index = 0;
            if (c.TryGotoNext(MoveType.After, i => i.MatchLdcR4(25f), i => i.MatchLdcR4(10f)) && c.TryGotoNext(i => i.MatchStloc(out _)))
            {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float, HealthComponent, float>>((r, hc) => r * M(hc.body));
            }
            else CursedAppleCuriositiesPlugin.Logger?.LogWarning("[MysticExpansion] Razorwire: radius not found, left unscaled.");
        }

        // ---------------- explosion visuals ----------------

        [ThreadStatic] internal static bool SkipBlastScale;

        private struct Blast { public Vector3 At; public float Raw, Mult; }
        private static readonly List<Blast> Recent = new List<Blast>();
        private static int _recentFrame = -1;

        /// <summary>Called by Mystic Expansion's BlastAttack hook after it grew a blast.</summary>
        internal static void Remember(Vector3 at, float raw, float mult)
        {
            if (_recentFrame != Time.frameCount) { Recent.Clear(); _recentFrame = Time.frameCount; }
            Recent.Add(new Blast { At = at, Raw = raw, Mult = mult });
        }

        private static void DetonateServer(On.RoR2.Projectile.ProjectileExplosion.orig_DetonateServer orig, ProjectileExplosion self)
        {
            var pc = self.GetComponent<ProjectileController>();
            float m = M(pc != null ? pc.owner : null);
            if (m == 1f) { orig(self); return; }
            float raw = self.blastRadius;
            self.blastRadius = raw * m;
            SkipBlastScale = true; // the blast is already grown
            try { orig(self); }
            finally { SkipBlastScale = false; self.blastRadius = raw; }
        }

        private static void SpawnEffect(On.RoR2.EffectManager.orig_SpawnEffect_EffectIndex_EffectData_bool orig, EffectIndex index, EffectData data, bool transmit)
        {
            if (data != null && _recentFrame == Time.frameCount)
                foreach (Blast b in Recent)
                {
                    // An effect where a blast just went off, sized to the blast's old radius (not already grown).
                    if ((data.origin - b.At).sqrMagnitude > 4f) continue;
                    if (Mathf.Abs(data.scale - b.Raw) >= Mathf.Abs(data.scale - b.Raw * b.Mult)) continue;
                    if (Mathf.Abs(data.scale - b.Raw) > b.Raw * 0.5f + 0.5f) continue;
                    data.scale *= b.Mult;
                    break;
                }
            orig(index, data, transmit);
        }
    }
    /// <summary>Every client: grows a fixed-size range indicator (Focus Crystal's) with its body's Mystic Expansion.</summary>
    public class MysticIndicatorScale : MonoBehaviour
    {
        private NetworkedBodyAttachment _attachment = null!;
        private Vector3 _base;

        private void Awake()
        {
            _attachment = GetComponent<NetworkedBodyAttachment>();
            _base = transform.localScale;
        }

        private void LateUpdate()
        {
            CharacterBody? body = _attachment != null ? _attachment.attachedBody : null;
            transform.localScale = _base * MysticExpansion.RadiusMultiplier(body);
        }
    }
}
