using System.Collections.Generic;
using System.Linq;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Viscous
{
    /// <summary>
    /// The Cube (Deadlock viscous_restorative_goo). Server: a cubed body holds <see cref="ViscousSurvivor.CubeBuff"/>
    /// (synced, so every client draws the cube): it takes no damage, can't move sideways or jump, can't use skills, and
    /// heals 5% of max health a second. After 3 s (Duration Extender: the caster's) or a jump after the first second
    /// (Deadlock BreakoutTime), it ends and the body gets <see cref="ViscousSurvivor.PostCubeBuff"/> for 8 s: +2.5 m/s and
    /// stamina refilling 30% faster (Deadlock heroes; Deadlock's upgrades, user).
    /// </summary>
    public static class ViscousCubes
    {
        private class Entry { public float End, Start, NextHeal, PostSeconds; public bool Break; }
        private static readonly Dictionary<CharacterBody, Entry> Cubes = new Dictionary<CharacterBody, Entry>();

        public static void Hook()
        {
            if (ViscousSurvivor.CubeBuff != null) SkillSilence.Register(ViscousSurvivor.CubeBuff);
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (ViscousSurvivor.CubeBuff != null && self.body != null && self.body.HasBuff(ViscousSurvivor.CubeBuff) && info.dotIndex == DotController.DotIndex.None)
                {
                    // Hits do no damage, but still land (user): not rejected, so their on-hit debuffs and DoTs reach the
                    // one inside, and DoTs (dotIndex set) tick through the cube normally.
                    info.damage = 0f;
                    // Deadlock: hits still shove the cube around (it's slippery). The motor's owner applies it.
                    if (info.force.sqrMagnitude > 0.01f && self.body.characterMotor != null)
                        self.body.characterMotor.ApplyForceImpulse(new PhysForceInfo { force = info.force * ViscousData.CubeShove, ignoreGroundStick = true });
                    info.force = Vector3.zero; // shoved once, above
                }
                orig(self, info);
            };
            // No fall damage inside the cube (user): the server zeroes the landing's impact speed (as DeadlockFallGuard does).
            On.RoR2.GlobalEventManager.OnCharacterHitGroundServer += (orig, self, body, info) =>
            {
                if (body != null && ViscousSurvivor.CubeBuff != null && body.HasBuff(ViscousSurvivor.CubeBuff)) info.velocity = Vector3.zero;
                orig(self, body, info);
            };
            // Slippery (user): no walking, dashing (root motion) or jumping, but the cube keeps whatever momentum it has
            // with almost no friction, slides downhill and falls. The motor's owner runs this.
            On.RoR2.CharacterMotor.UpdateVelocity += (On.RoR2.CharacterMotor.orig_UpdateVelocity orig, CharacterMotor self, ref Vector3 v, float dt) =>
            {
                Vector3 before = v;
                orig(self, ref v, dt);
                if (ViscousSurvivor.CubeBuff == null || ViscousSurvivor.CubeBuff.buffIndex == BuffIndex.None) return;
                if (!self.TryGetComponent(out CharacterBody body) || !body.HasBuff(ViscousSurvivor.CubeBuff.buffIndex)) return;
                Vector3 flat = new Vector3(before.x, 0f, before.z) * Mathf.Max(0f, 1f - ViscousData.CubeFriction * dt);
                if (self.isGrounded)
                {
                    float slope = EntityStates.CursedApple.Deadlock.DeadlockSlide.Slope(self, out Vector3 downhill);
                    if (downhill != Vector3.zero) flat += downhill * Mathf.Abs(Physics.gravity.y) * Mathf.Sin(slope * Mathf.Deg2Rad) * dt;
                }
                v = new Vector3(flat.x, self.isGrounded ? Mathf.Min(v.y, 0f) : v.y, flat.z);
                self.rootMotion = Vector3.zero;
                var mover = body.GetComponent<DeadlockMover>();
                if (mover != null) mover.SuppressJumpUntil = Time.time + 0.2f; // a Deadlock hero can't hop inside it
            };
            R2API.RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (ViscousSurvivor.PostCubeBuff != null && body.HasBuff(ViscousSurvivor.PostCubeBuff)) args.baseMoveSpeedAdd += ViscousData.PostCubeSpeed;
            };
            DeadlockMover.RegenMultipliers.Add(body =>
                ViscousSurvivor.PostCubeBuff != null && body.HasBuff(ViscousSurvivor.PostCubeBuff) ? 1f / Mathf.Max(0.1f, 1f - ViscousData.PostCubeStamina) : 1f);
        }

        /// <summary>The ally nearest the crosshair: in range (Mystic Expansion farther), in the cone, in line of sight.</summary>
        public static CharacterBody? FindAlly(CharacterBody body)
        {
            if (body.inputBank == null || body.teamComponent == null) return null;
            Ray aim = body.inputBank.GetAimRay();
            TeamMask mask = TeamMask.none;
            mask.AddTeam(body.teamComponent.teamIndex);
            var search = new BullseyeSearch
            {
                teamMaskFilter = mask,
                filterByLoS = true,
                searchOrigin = aim.origin,
                searchDirection = aim.direction,
                maxDistanceFilter = ViscousData.CubeRange * MysticExpansion.RadiusMultiplier(body),
                maxAngleFilter = ViscousData.CubeAngle,
                sortMode = BullseyeSearch.SortMode.Angle,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(body.gameObject);
            return search.GetResults()
                .Select(h => h != null && h.healthComponent != null ? h.healthComponent.body : null)
                .FirstOrDefault(b => b != null && b != body && b.healthComponent.alive);
        }

        /// <summary>Server: put a body in the cube.</summary>
        public static void Encase(CharacterBody target, CharacterBody? caster)
        {
            BuffDef? cube = ViscousSurvivor.CubeBuff;
            if (cube == null || target.healthComponent == null || !target.healthComponent.alive) return;
            float mult = DurationExtender.Multiplier(caster); // Duration Extender lengthens the cube (user) and what follows it
            float seconds = ViscousData.CubeDuration * mult;
            target.ClearTimedBuffs(cube);
            // Cast cleanses (user): every debuff, DoT and stun on them goes. What hits the cube afterwards sticks again.
            Util.CleanseBody(target, removeDebuffs: true, removeBuffs: false, removeCooldownBuffs: false, removeDots: true, removeStun: true, removeNearbyProjectiles: false);
            using (DurationExtender.Exact()) target.AddTimedBuff(cube, seconds + 1f); // ended by Finish, not by the timer
            Cubes[target] = new Entry { Start = Time.time, End = Time.time + seconds, NextHeal = Time.time, PostSeconds = ViscousData.PostCubeSeconds * mult };
            EquipmentFx.Sound("viscous_cube_cast", target.corePosition, target.gameObject);
        }

        /// <summary>Server: the cubed player asked to break out (honoured once the breakout time has passed).</summary>
        public static void Breakout(CharacterBody body)
        {
            if (Cubes.TryGetValue(body, out Entry e)) e.Break = true;
        }

        /// <summary>Server, every physics tick: healing, and the end of each cube.</summary>
        public static void Tick()
        {
            if (Cubes.Count == 0) return;
            float now = Time.time;
            foreach (CharacterBody body in Cubes.Keys.ToArray())
            {
                Entry e = Cubes[body];
                if (body == null || body.healthComponent == null || !body.healthComponent.alive) { Cubes.Remove(body!); continue; }
                if (now >= e.NextHeal)
                {
                    e.NextHeal = now + 0.25f;
                    body.healthComponent.Heal(body.healthComponent.fullHealth * ViscousData.CubeHeal * 0.25f, default(ProcChainMask), true);
                }
                if (now >= e.End || e.Break && now - e.Start >= ViscousData.CubeBreakout) Finish(body, e);
            }
        }

        private static void Finish(CharacterBody body, Entry e)
        {
            Cubes.Remove(body);
            BuffDef? cube = ViscousSurvivor.CubeBuff;
            if (cube != null)
            {
                body.ClearTimedBuffs(cube);
                while (body.HasBuff(cube)) body.RemoveBuff(cube);
            }
            // The cube ended mid-air: an ally's landing is safe too. Viscous takes fall damage whenever he isn't in the cube or ball (user).
            if (body.characterMotor != null && !body.characterMotor.isGrounded && !ViscousSurvivor.Is(body))
                DeadlockFallGuard.ProtectNextLanding(body.gameObject, 8f);
            if (ViscousSurvivor.PostCubeBuff != null)
                using (DurationExtender.Exact()) body.AddTimedBuff(ViscousSurvivor.PostCubeBuff, e.PostSeconds);
            EquipmentFx.Sound("viscous_cube_end", body.corePosition, body.gameObject);
            if (ViscousSurvivor.SplashEffect != null)
                EffectManager.SpawnEffect(ViscousSurvivor.SplashEffect, new EffectData { origin = body.footPosition, scale = 3f }, true);
        }

        /// <summary>
        /// The cube's size for whoever is in it (user: proportionate to the target): the box around what is drawn of them,
        /// their CharacterModel's own renderers (not item displays; Deadlock heroes' meshes keep exact bounds every frame),
        /// its tallest or widest side plus a margin. Null when nothing is drawn yet.
        /// </summary>
        public static float? FitSize(GameObject? root, CharacterModel? model = null)
        {
            if (root == null) return null;
            var renderers = new List<Renderer>();
            if (model != null && model.baseRendererInfos != null)
                foreach (CharacterModel.RendererInfo info in model.baseRendererInfos) if (info.renderer != null) renderers.Add(info.renderer);
            if (!Encapsulate(renderers, out Bounds b) && !Encapsulate(root.GetComponentsInChildren<Renderer>(), out b)) return null;
            float side = Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z)) * ViscousData.CubeMargin;
            return Mathf.Clamp(side, 0.8f, 40f);
        }

        /// <summary>The box around the drawn meshes (enabled, active, mesh or skinned) of a renderer list; false when none are drawn.</summary>
        private static bool Encapsulate(IEnumerable<Renderer> renderers, out Bounds b)
        {
            bool any = false;
            b = default;
            foreach (Renderer r in renderers)
            {
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>
        /// A cube to draw: Deadlock's model once it's converted (else a plain green box), its base at the parent's origin,
        /// this tall. Every client, and the lobby intro.
        /// </summary>
        public static GameObject MakeVisual(float height)
        {
            var root = new GameObject("ViscousCube");
            GlbModel? model = DeadlockInstall.Model(ViscousData.CubeKey);
            if (model != null)
            {
                GameObject m = Object.Instantiate(model.Root, root.transform, false);
                m.SetActive(true);
                m.transform.localScale = Vector3.one * (height / ViscousData.CubeModelHeight);
            }
            else
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(box.GetComponent<Collider>());
                box.transform.SetParent(root.transform, false);
                box.transform.localScale = Vector3.one * height;
                box.transform.localPosition = Vector3.up * height * 0.5f;
                var r = box.GetComponent<MeshRenderer>();
                r.sharedMaterial = DeadlockHeroes.MakeMaterial("Viscous", "cube", null);
                r.sharedMaterial.color = ViscousSurvivor.Color;
            }
            return root;
        }
    }

    /// <summary>
    /// On the plugin object: the server's cube ticks; on every client, the cubes drawn around cubed bodies (with their
    /// loop), and the local player's breakout (jump, after the first second).
    /// </summary>
    public class ViscousCubeHost : MonoBehaviour
    {
        private readonly Dictionary<CharacterBody, (GameObject Cube, DeadlockVoice? Loop)> _drawn = new Dictionary<CharacterBody, (GameObject, DeadlockVoice?)>();
        private CharacterBody? _localCubed;
        private float _localSince;
        private bool _asked;

        private void FixedUpdate()
        {
            if (NetworkServer.active) ViscousCubes.Tick();
        }

        private void Update()
        {
            BuffDef? cube = ViscousSurvivor.CubeBuff;
            if (cube == null || cube.buffIndex == BuffIndex.None) return;
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (body != null && body.HasBuff(cube.buffIndex) && !_drawn.ContainsKey(body))
                {
                    CharacterModel? cm = body.modelLocator != null && body.modelLocator.modelTransform != null ? body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
                    float height = ViscousCubes.FitSize(cm != null ? cm.gameObject : null, cm)
                                   ?? Mathf.Max(2.2f, (body.corePosition.y - body.footPosition.y) * 2.6f);
                    _drawn[body] = (ViscousCubes.MakeVisual(height), DeadlockSounds.Loop("viscous_cube_loop", body.gameObject, 30f));
                }
            foreach (CharacterBody body in _drawn.Keys.ToArray())
            {
                var (box, loop) = _drawn[body];
                if (body == null || !body.HasBuff(cube.buffIndex) || box == null)
                {
                    if (box != null) Destroy(box);
                    DeadlockSounds.Stop(loop);
                    _drawn.Remove(body!);
                    continue;
                }
                Vector3 fwd = body.transform.forward;
                fwd.y = 0f;
                box.transform.SetPositionAndRotation(body.footPosition, fwd.sqrMagnitude > 0.01f ? Quaternion.LookRotation(fwd) : Quaternion.identity);
            }
            Breakout(cube);
        }

        /// <summary>The local player in a cube: jump breaks out once the breakout time has passed (the server checks it too).</summary>
        private void Breakout(BuffDef cube)
        {
            CharacterBody? me = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            bool cubed = me != null && me.HasBuff(cube.buffIndex);
            if (!cubed) { _localCubed = null; return; }
            if (_localCubed != me) { _localCubed = me; _localSince = Time.time; _asked = false; }
            if (_asked || Time.time - _localSince < ViscousData.CubeBreakout || me!.inputBank == null || !me.inputBank.jump.justPressed) return;
            _asked = true;
            ViscousNet.ToServer(new ViscousCubeBreakMessage { Body = me.gameObject });
        }
    }

    /// <summary>The local player's Viscous: the Tricorn reticle on the ally a tap of The Cube would cube.</summary>
    public class ViscousCubeReticle : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private Indicator? _indicator;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            Transform? at = null;
            GenericSkill? slot = _body.skillLocator != null ? _body.skillLocator.utility : null;
            if (_body.hasEffectiveAuthority && _body.isPlayerControlled && slot != null && slot.stock > 0 && _body.healthComponent != null && _body.healthComponent.alive)
            {
                CharacterBody? ally = ViscousCubes.FindAlly(_body);
                if (ally != null) at = ally.mainHurtBox != null ? ally.mainHurtBox.transform : ally.coreTransform;
            }
            if (_indicator == null)
            {
                if (at == null) return;
                _indicator = new Indicator(gameObject, EquipmentTargeting.IndicatorPrefab);
            }
            _indicator.targetTransform = at;
            _indicator.active = at != null;
        }

        private void OnDestroy() { if (_indicator != null) _indicator.active = false; }
    }

    /// <summary>Cube a body (from the caster's client).</summary>
    public class ViscousCubeMessage : INetMessage
    {
        public GameObject? Caster, Target;

        public void Serialize(NetworkWriter w) { w.Write(Caster); w.Write(Target); }
        public void Deserialize(NetworkReader r) { Caster = r.ReadGameObject(); Target = r.ReadGameObject(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Target == null) return;
            var target = Target.GetComponent<CharacterBody>();
            if (target != null) ViscousCubes.Encase(target, Caster != null ? Caster.GetComponent<CharacterBody>() : null);
        }
    }

    /// <summary>A cubed player jumped to break out.</summary>
    public class ViscousCubeBreakMessage : INetMessage
    {
        public GameObject? Body;

        public void Serialize(NetworkWriter w) => w.Write(Body);
        public void Deserialize(NetworkReader r) => Body = r.ReadGameObject();

        public void OnReceived()
        {
            if (!NetworkServer.active || Body == null) return;
            var body = Body.GetComponent<CharacterBody>();
            if (body != null) ViscousCubes.Breakout(body);
        }
    }
}
