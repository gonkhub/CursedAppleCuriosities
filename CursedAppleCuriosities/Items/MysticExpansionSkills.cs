using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Mystic Expansion on vanilla survivors' cast ranges (user, 2.0). Every bullet's max distance grows with its owner's
    /// stacks (BulletAttack.Fire); the other ranges (aim arcs and indicators, target searches, trackers, Loader's grapple)
    /// are grown where each method reads its range field, so the same state can never be scaled twice. A field or method
    /// that's gone (a game update) logs and is left alone.
    /// </summary>
    internal static class MysticExpansionSkills
    {
        private static readonly List<ILHook> Hooks = new List<ILHook>();

        /// <summary>(type with the field, field, methods that read it as a range: "Type.Method" or just "Method" on the same type).</summary>
        private static readonly (string type, string field, string[] readers)[] Ranges =
        {
            // Aimed throws and arcs: REX's Tangling Growth/DIRECTIVE: Drill, MUL-T's grenade, Engineer's mines, Captain's airstrikes, ...
            ("EntityStates.AimThrowableBaseAimOnly", "maxDistance", new[] { "UpdateTrajectoryInfo", "EntityStates.Drifter.EmptyBag.OnEnter" }),
            // Area indicators cast at a spot: REX's DIRECTIVE: Harvest pounder and mortar, Seeker's Unseen Hand, Artificer's Ice Wall.
            ("EntityStates.Treebot.Weapon.CreatePounder", "maxDistance", new[] { "UpdateAreaIndicator" }),
            ("EntityStates.Treebot.Weapon.FireMortar2", "maxDistance", new[] { "Fire" }),
            ("EntityStates.Seeker.UnseenHand", "maxDistance", new[] { "UpdateAreaIndicator" }),
            ("EntityStates.Mage.Weapon.PrepWall", "maxDistance", new[] { "UpdateAreaIndicator" }),
            ("EntityStates.Captain.Weapon.SetupSupplyDrop", "maxPlacementDistance", new[] { "GetPlacementInfo" }),
            ("EntityStates.FalseSon.MeridiansWillAim", "maxDistanceForIndicator", new[] { "OnEnter", "UpdateAreaIndicator", "InstantiateNextState" }),
            // Target searches.
            ("EntityStates.Merc.Evis", "maxRadius", new[] { "SearchForTarget" }),
            ("EntityStates.Huntress.BackflipState", "orbRange", new[] { "FireOrbArrow" }),
            ("EntityStates.Huntress.HuntressWeapon.ChargeArrow", "orbRange", new[] { "FireOrbArrow" }),
            ("EntityStates.Huntress.HuntressWeapon.IdleTracking", "maxTrackingDistance", new[] { "FixedUpdate" }),
            ("EntityStates.Huntress.HuntressWeapon.ThrowGlaive", "glaiveBounceRange", new[] { "FireOrbGlaive" }),
            ("EntityStates.Engi.EngiMissilePainter.Paint", "maxDistance", new[] { "GetCurrentTargetInfo" }),
            ("EntityStates.Commando.CommandoWeapon.FireSweepBarrage", "maxDistance", new[] { "OnEnter" }),
            ("EntityStates.Croco.Disease", "orbRange", new[] { "OnEnter" }),
            ("EntityStates.Croco.Disease", "bounceRange", new[] { "OnEnter" }),
            ("EntityStates.VoidSurvivor.Weapon.Suppress", "maxSearchDistance", new[] { "OnEnter" }),
            ("EntityStates.Treebot.Weapon.FireSonicBoom", "maxDistance", new[] { "OnEnter" }),
            // Trackers (the target indicator and what the skill locks onto): Huntress, Seeker, Railgunner, drones, ...
            ("RoR2.HuntressTracker", "maxTrackingDistance", new[] { "SearchForTarget" }),
            ("RoR2.TargetTracker", "maxTrackingDistance", new[] { "SearchForTarget" }),
            ("RoR2.MultiTargetTracker", "maxTrackingDistance", new[] { "SearchForTarget" }),
            ("RoR2.DroneTracker", "maxTrackingDistance", new[] { "SearchForTarget", "EntityStates.Drone.Command.CommandBombingRun.OnEnter" }),
            ("RoR2.DrifterTracker", "maxTrackingDistance", new[] { "SearchForTinkerTarget" }),
            ("RoR2.HuntressContractKiller", "maxTrackingDistance", new[] { "SearchForTarget", "Update" }),
            // Loader's grapples: how far the hook flies.
            ("RoR2.Projectile.ProjectileGrappleController", "maxTravelDistance", new[] { "RoR2.Projectile.ProjectileGrappleController+FlyState.OnEnter" }),
        };

        public static void Hook()
        {
            // Bullets: the shot's reach (Commando, Bandit, Railgunner, MUL-T's nailgun, Artificer's and CHEF's flames, beams, ...).
            // Some states reuse one BulletAttack, so the distance is put back after each shot.
            On.RoR2.BulletAttack.Fire += (orig, self) =>
            {
                float m = MysticExpansion.RadiusMultiplier(self.owner != null ? self.owner.GetComponent<CharacterBody>() : null);
                if (m == 1f) { orig(self); return; }
                float raw = self.maxDistance;
                self.maxDistance = raw * m;
                try { orig(self); }
                finally { self.maxDistance = raw; }
            };

            Assembly game = typeof(CharacterBody).Assembly;
            foreach (var (typeName, fieldName, readers) in Ranges)
            {
                Type? type = game.GetType(typeName);
                FieldInfo? field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (field == null) { Warn($"{typeName}.{fieldName} not found"); continue; }
                foreach (string reader in readers)
                {
                    int dot = reader.LastIndexOf('.');
                    Type? owner = dot < 0 ? type : game.GetType(reader.Substring(0, dot));
                    string methodName = dot < 0 ? reader : reader.Substring(dot + 1);
                    // Every overload of that name (GetMethod threw AmbiguousMatchException once a game update added one, and the
                    // whole item failed to load); an overload that never reads the field just warns.
                    // Static ones too (Captain's SetupSupplyDrop.GetPlacementInfo is static: it was never found).
                    var methods = new List<MethodInfo>();
                    if (owner != null)
                        foreach (MethodInfo m in owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (m.Name == methodName && !m.IsAbstract) methods.Add(m);
                    if (methods.Count == 0) { Warn($"{reader} not found ({fieldName})"); continue; }
                    FieldInfo f = field;
                    int reads = 0; // over every overload: one that never reads the field (UpdateTrajectoryInfo's ref wrapper) is fine
                    foreach (MethodInfo method in methods) Hooks.Add(new ILHook(method, il => reads += ScaleReads(il, f, reader, method.IsStatic)));
                    if (reads == 0) Warn($"{reader}: no read of {fieldName}");
                }
            }
        }

        /// <summary>After every read of the range field, multiply it by the Mystic Expansion of whoever this method runs for.</summary>
        private static int ScaleReads(ILContext il, FieldInfo field, string where, bool isStatic)
        {
            var c = new ILCursor(il);
            // Whoever the method runs for: its instance, or a static method's GameObject argument. Ask the original method:
            // the hook's copy is always static (an instance method's `this` becomes its first parameter, still arg 0), so
            // il.Method.HasThis was false for every one of them and nothing was scaled.
            Mono.Cecil.ParameterDefinition? who = null;
            if (isStatic)
            {
                foreach (Mono.Cecil.ParameterDefinition p in il.Method.Parameters)
                    if (p.ParameterType.FullName == "UnityEngine.GameObject") { who = p; break; }
                if (who == null) { Warn($"{where}: static with no GameObject argument"); return 0; }
            }
            int n = 0;
            while (c.TryGotoNext(MoveType.After, i => (i.OpCode == OpCodes.Ldfld || i.OpCode == OpCodes.Ldsfld) && i.Operand is Mono.Cecil.FieldReference r
                && r.Name == field.Name && r.DeclaringType.FullName.Replace('/', '+') == field.DeclaringType!.FullName))
            {
                if (who != null) c.Emit(OpCodes.Ldarg, who);
                else c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float, object, float>>((range, self) => range * MysticExpansion.RadiusMultiplier(BodyOf(self)));
                n++;
            }
            return n;
        }

        /// <summary>The body a state, tracker or projectile acts for (a projectile's owner).</summary>
        private static CharacterBody? BodyOf(object self)
        {
            GameObject? go = self is EntityStates.EntityState s ? (s.outer != null ? s.outer.gameObject : null) : self as GameObject ?? (self as Component)?.gameObject;
            if (go == null) return null;
            var pc = go.GetComponent<ProjectileController>();
            if (pc != null) return pc.owner != null ? pc.owner.GetComponent<CharacterBody>() : null;
            return go.GetComponent<CharacterBody>();
        }

        private static void Warn(string what) =>
            CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[MysticExpansion] vanilla range {what}, left unscaled.");
    }
}
