using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Lash;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Deadlock stamina (ported from HeroBrew): pips that dashes and air jumps spend, refilling one at a time.
    /// Movement is owner-authoritative in RoR2, so only the owning player's copy matters.
    /// </summary>
    public class LashStamina : MonoBehaviour
    {
        public int Max = LashData.StaminaPips;
        public int Pips = LashData.StaminaPips;
        public int AirDashes;
        public float LastWallJump = -10f;
        public float SprintRamp; // 0-1: how far the out-of-combat sprint has built up
        private float _regen;
        private CharacterBody _body = null!;

        /// <summary>0-1 progress of the next pip refilling.</summary>
        public float RegenProgress => Pips < Max ? Mathf.Clamp01(_regen / LashData.StaminaRegenSeconds) : 0f;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            // Extra jumps from items (Hopoo Feather...) become stamina pips instead (playtest).
            Max = LashData.StaminaPips + Mathf.Max(0, _body.maxJumpCount - _body.baseJumpCount);
            if (Pips > Max) Pips = Max;
            if (_body.characterMotor != null && _body.characterMotor.isGrounded) AirDashes = 0;
            if (Pips >= Max) { _regen = 0f; return; }
            int quail = _body.inventory != null ? _body.inventory.GetItemCountEffective(RoR2Content.Items.JumpBoost) : 0;
            _regen += Time.fixedDeltaTime * (1f + LashData.QuailStaminaRegen * quail); // Wax Quail: faster stamina
            if (_regen >= LashData.StaminaRegenSeconds) { Pips++; _regen = 0f; }
        }

        public bool TrySpend()
        {
            if (Pips <= 0) return false;
            Pips--;
            DeadlockSounds.Play("lash_vo_effort", transform.position, gameObject);
            return true;
        }

        public void Refund(int pips) => Pips = Mathf.Min(Max, Pips + pips);
    }

    /// <summary>Stamina pips under the crosshair (HeroBrew style: a row of small bars).</summary>
    public class LashStaminaHud : MonoBehaviour
    {
        private HUD _hud = null!;
        private RectTransform? _root;
        private Image[] _fills = System.Array.Empty<Image>();
        private int _built = -1;
        private const float W = 14f, H = 4f, Gap = 4f;

        public static void Hook()
        {
            On.RoR2.UI.HUD.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<LashStaminaHud>()._hud = self; };
        }

        private void Build(int max)
        {
            if (_root != null) Destroy(_root.gameObject);
            var go = new GameObject("LashStamina", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(_hud.mainContainer != null ? _hud.mainContainer.transform : _hud.transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = new Vector2(0f, -42f);
            _root.sizeDelta = new Vector2(max * (W + Gap), H);
            _fills = new Image[max];
            float left = -(max * W + (max - 1) * Gap) / 2f;
            for (int i = 0; i < max; i++)
            {
                Image back = Box(_root, new Vector2(left + i * (W + Gap), 0f), new Color(0f, 0f, 0f, 0.55f));
                _fills[i] = Box(back.rectTransform, Vector2.zero, new Color(1f, 1f, 1f, 0.9f));
                _fills[i].rectTransform.anchorMin = _fills[i].rectTransform.anchorMax = new Vector2(0f, 0.5f);
                _fills[i].rectTransform.pivot = new Vector2(0f, 0.5f);
            }
            _built = max;
        }

        private static Image Box(Transform parent, Vector2 pos, Color c)
        {
            var go = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(W, H);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private void Update()
        {
            GameObject? body = _hud.targetBodyObject;
            LashStamina? s = body != null ? body.GetComponent<LashStamina>() : null;
            if (s == null) { if (_root != null) _root.gameObject.SetActive(false); return; }
            if (_built != s.Max || _root == null) Build(s.Max);
            _root!.gameObject.SetActive(true);
            for (int i = 0; i < _fills.Length; i++)
            {
                float fill = i < s.Pips ? 1f : i == s.Pips ? s.RegenProgress : 0f;
                _fills[i].rectTransform.sizeDelta = new Vector2(W * fill, H);
                _fills[i].color = i < s.Pips ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
            }
        }
    }
}

namespace EntityStates.CursedApple.Lash
{
    /// <summary>
    /// Every Lash jump counts as a RoR2 jump (playtest), including the ones that set their own velocity (wall
    /// jumps, dash-jumps, jumps out of a slide): the jump dust and the global jump event on-jump items listen for.
    /// (Wax Quail's jump boost needs sprinting, which Lash never does; Quail speeds up his stamina instead.)
    /// </summary>
    internal static class LashJump
    {
        public static GameObject? LandEffect;

        public static void Fired(CharacterBody body, CharacterMotor motor)
        {
            if (!motor.isGrounded && LandEffect != null)
                EffectManager.SpawnEffect(LandEffect, new EffectData { origin = body.footPosition, scale = body.radius }, true);
            body.TriggerJumpEventGlobally();
        }
    }

    /// <summary>
    /// Lash's main state: RoR2 movement plus Deadlock's. Sprint key = dash (ground or air, one stamina each; keep
    /// holding to sprint, as in Deadlock). Air jumps cost one stamina each; jumping beside a wall kicks off it for
    /// free. The slide key slides when fast or on a downhill slope, or on landing while held.
    /// </summary>
    public class LashMain : GenericCharacterMain
    {
        private LashStamina? _stamina;
        private bool _wasGrounded = true;
        private Vector3 _airVel;

        public static bool SlideHeld => Input.GetKey(LashData.SlideKey);

        public override void OnEnter()
        {
            base.OnEnter();
            _stamina = GetComponent<LashStamina>();
            _wasGrounded = characterMotor == null || characterMotor.isGrounded;
        }

        public override void FixedUpdate()
        {
            if (isAuthority && _stamina != null && inputBank != null && characterMotor != null)
            {
                if (SprintKeyPressed() && TryDash()) return;
                bool grounded = characterMotor.isGrounded;
                Vector3 v = characterMotor.velocity;
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                bool justLanded = grounded && !_wasGrounded;
                _wasGrounded = grounded;
                if (!grounded) _airVel = flat;
                if (SlideHeld && grounded)
                {
                    // Deadlock: hold crouch in the air and you slide the moment you land, keeping your air speed.
                    if (justLanded && _airVel.magnitude >= LashData.SlideSustain) { outer.SetNextState(new LashSlide { Dir = _airVel.normalized, Speed = Mathf.Min(_airVel.magnitude, LashData.SlideMaxStart) }); return; }
                    if (TrySlide(flat)) return;
                }
            }
            base.FixedUpdate();
            if (isAuthority) DeadlockSprint();
            if (isAuthority && characterMotor != null && !characterMotor.isGrounded) AirSteer(characterBody, characterMotor, characterMotor.moveDirection);
        }

        /// <summary>
        /// Deadlock-style air movement (Lash's body has RoR2 air control off): momentum is kept, the movement input
        /// steers it toward its direction without slowing you below what you had, and speed above a run sheds slowly.
        /// A grapple launch flies untouched until you jump or dash.
        /// </summary>
        public static void AirSteer(CharacterBody characterBody, CharacterMotor characterMotor, Vector3 wish)
        {
            if (characterMotor.disableAirControlUntilCollision) return;
            float dt = Time.fixedDeltaTime;
            Vector3 v = characterMotor.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            wish.y = 0f;
            float run = characterBody.moveSpeed;
            if (wish.sqrMagnitude > 0.01f)
            {
                Vector3 target = wish.normalized * Mathf.Max(run * Mathf.Min(1f, wish.magnitude), flat.magnitude);
                flat = Vector3.MoveTowards(flat, target, LashData.AirAccel * dt);
            }
            if (flat.magnitude > run) flat = flat.normalized * Mathf.Max(run, flat.magnitude - LashData.AirDrag * dt);
            characterMotor.velocity = new Vector3(flat.x, v.y, flat.z);
        }

        /// <summary>RoR2's jumps replace horizontal speed with a walk in the input direction: keep the momentum instead.</summary>
        private void KeepMomentum(Vector3 before)
        {
            Vector3 v = characterMotor.velocity;
            Vector3 jumped = new Vector3(v.x, 0f, v.z);
            float speed = Mathf.Max(before.magnitude, jumped.magnitude);
            Vector3 dir = jumped.sqrMagnitude > 0.01f ? jumped.normalized : before.sqrMagnitude > 0.01f ? before.normalized : Vector3.zero;
            characterMotor.velocity = dir * speed + Vector3.up * v.y;
        }

        /// <summary>
        /// The sprint key, read straight from the player's input (Rewired action 18, honours rebinds). RoR2's sprint
        /// button state follows isSprinting, so reading that would dash every time Lash starts sprinting.
        /// </summary>
        private bool SprintKeyPressed()
        {
            var player = characterBody.master != null && characterBody.master.playerCharacterMasterController != null
                ? characterBody.master.playerCharacterMasterController.networkUser?.localUser?.inputPlayer : null;
            return player != null ? player.GetButtonDown(RewiredConsts.Action.Sprint) : inputBank.sprint.justPressed;
        }

        /// <summary>
        /// Deadlock's sprint: out of combat and moving, Lash sprints (a real RoR2 sprint, so sprint items work), and
        /// his sprint speed builds up from a run to +29% over SprintRampSeconds. The sprint key never sprints.
        /// </summary>
        private void DeadlockSprint()
        {
            bool want = characterBody.outOfCombat && inputBank.moveVector.sqrMagnitude > 0.01f;
            float ramp = want ? Mathf.Min(1f, _stamina!.SprintRamp + Time.fixedDeltaTime / Mathf.Max(0.01f, LashData.SprintRampSeconds)) : 0f;
            _stamina!.SprintRamp = ramp;
            characterBody.isSprinting = want;
            float mult = 1f + LashData.OutOfCombatSpeed * ramp;
            if (Mathf.Abs(characterBody.sprintingSpeedMultiplier - mult) > 0.01f || (ramp >= 1f && characterBody.sprintingSpeedMultiplier != mult))
            {
                characterBody.sprintingSpeedMultiplier = mult;
                characterBody.RecalculateStats();
            }
        }

        /// <summary>Slide when fast enough (a sprint is) or on a downhill slope while heading down it.</summary>
        private bool TrySlide(Vector3 flat)
        {
            float slope = LashSlide.Slope(characterMotor, out Vector3 downhill);
            Vector3 heading = flat.sqrMagnitude > 0.25f ? flat.normalized : FlatAim();
            bool fast = flat.magnitude >= LashData.SlideActivation || characterBody.isSprinting && flat.magnitude >= characterBody.moveSpeed * 0.8f;
            bool downSlope = slope >= LashData.SlideSlopeMinDeg && downhill != Vector3.zero && Vector3.Angle(heading, downhill) <= LashData.SlideSlopeFacingDeg;
            if (!fast && !downSlope) return false;
            outer.SetNextState(new LashSlide { Dir = heading, Speed = Mathf.Min(Mathf.Max(flat.magnitude, LashData.SlideSustain + 0.5f), LashData.SlideMaxStart) });
            return true;
        }

        private Vector3 FlatAim()
        {
            Vector3 d = inputBank.aimDirection;
            d.y = 0f;
            return d.sqrMagnitude > 0.001f ? d.normalized : transform.forward;
        }

        private bool TryDash()
        {
            bool air = !characterMotor.isGrounded;
            if (air && _stamina!.AirDashes >= LashData.AirDashesPerAir) return false;
            if (!air && !LashData.GroundDash) return false;
            if (!_stamina!.TrySpend()) return false;
            if (air) _stamina.AirDashes++;
            Vector3 dir = inputBank.moveVector;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = FlatAim();
            outer.SetNextState(new LashDash { Air = air, Dir = dir.normalized });
            return true;
        }

        public override void ProcessJump()
        {
            if (jumpInputReceived && characterBody != null && characterMotor != null && !characterMotor.isGrounded && _stamina != null)
            {
                // Deadlock order: ground jump, else kick off a wall (free), else an air jump.
                if (TryWall(out Vector3 normal)) { WallJump(normal); jumpInputReceived = false; return; }
                if (characterMotor.jumpCount == 0) characterMotor.jumpCount = 1; // walked off a ledge: the next jump is an air jump
                int jumpsBefore = characterMotor.jumpCount;
                // Lash's two air jumps cost one stamina each. Extra jumps from items are extra stamina instead.
                if (jumpsBefore >= LashData.JumpCount || !_stamina.TrySpend()) { jumpInputReceived = false; return; }
                Vector3 before = characterMotor.velocity;
                before.y = 0f;
                base.ProcessJump();
                if (characterMotor.jumpCount > jumpsBefore)
                {
                    KeepMomentum(before);
                    DeadlockSounds.Play("lash_jump", transform.position, gameObject);
                    DeadlockSounds.Play("lash_vo_efforts", transform.position, gameObject);
                    characterMotor.disableAirControlUntilCollision = false; // steering back after a grapple launch
                    LashModelSwap.Play(gameObject, "air_jump");
                }
                return;
            }
            if (jumpInputReceived && characterMotor != null && characterMotor.isGrounded)
            {
                Vector3 before = characterMotor.velocity;
                before.y = 0f;
                int jumps = characterMotor.jumpCount;
                base.ProcessJump();
                if (characterMotor.jumpCount > jumps) KeepMomentum(before);
                return;
            }
            base.ProcessJump();
        }

        private bool TryWall(out Vector3 normal)
        {
            normal = Vector3.zero;
            Vector3 origin = characterBody.footPosition + Vector3.up * 1f;
            float reach = characterBody.radius + 0.6f, best = float.MaxValue;
            for (int i = 0; i < 12; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
                if (!Physics.Raycast(origin, dir, out RaycastHit hit, reach, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.5f || hit.distance >= best) continue; // walls only
                best = hit.distance;
                normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            }
            return best < float.MaxValue;
        }

        /// <summary>Deadlock wall jump: up and out, keeping speed along the wall; chained ones are weaker for 1.25 s.</summary>
        private void WallJump(Vector3 normal)
        {
            float power = Mathf.Lerp(LashData.WallJumpPowerMin, 1f, Mathf.Clamp01((Time.time - _stamina!.LastWallJump) / LashData.WallJumpRecharge));
            _stamina.LastWallJump = Time.time;
            Vector3 v = characterMotor.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float outSpeed = Mathf.Lerp(LashData.WallJumpOutMin, LashData.WallJumpOutMax, Mathf.Clamp01(flat.magnitude / (2f * LashData.BaseSpeed)));
            Vector3 h = normal * outSpeed + Vector3.ProjectOnPlane(flat, normal);
            characterMotor.Motor.ForceUnground();
            characterMotor.velocity = new Vector3(h.x, LashData.WallJumpUp * power, h.z);
            characterMotor.disableAirControlUntilCollision = false;
            LashJump.Fired(characterBody, characterMotor);
            LashModelSwap.Play(gameObject, "wall_jump");
            DeadlockSounds.Play("lash_wall_jump", transform.position, gameObject);
        }
    }

    /// <summary>
    /// RoR2 only reads skill buttons in the main state. Slide and dash are their own states, so they read them
    /// here: Lash can shoot (and use skills) while sliding or dashing, as in Deadlock.
    /// </summary>
    internal static class LashSkillInput
    {
        public static void Run(BaseState s, InputBankTest input, SkillLocator locator)
        {
            if (input == null || locator == null) return;
            Press(locator.primary, input.skill1, true);
            Press(locator.secondary, input.skill2, false);
            Press(locator.utility, input.skill3, false);
            Press(locator.special, input.skill4, false);
        }

        private static void Press(GenericSkill? skill, InputBankTest.ButtonState button, bool hold)
        {
            if (skill == null || !(hold ? button.down : button.justPressed)) return;
            if (skill.skillDef != null && skill.skillDef.mustKeyPress && !button.justPressed) return;
            skill.ExecuteIfReady();
        }

        /// <summary>Face where you aim while firing, otherwise the way you're moving.</summary>
        public static Vector3 Facing(InputBankTest input, Vector3 moving)
        {
            if (input == null || !input.skill1.down) return moving;
            Vector3 aim = input.aimDirection;
            aim.y = 0f;
            return aim.sqrMagnitude > 0.01f ? aim.normalized : moving;
        }
    }

    /// <summary>
    /// Deadlock slide (ported from HeroBrew): keeps your speed, Source-style friction on flat ground, Deadlock's
    /// slope acceleration downhill, steer toward your movement input, jump out to keep the speed. Ends when the
    /// slide key is let go, you slow below 4.3 m/s, or you slide off a ledge (keeping momentum).
    /// </summary>
    public class LashSlide : BaseState
    {
        public Vector3 Dir = Vector3.forward;
        public float Speed;
        private float _airborne;

        /// <summary>Ground steepness in degrees and the flat downhill direction.</summary>
        public static float Slope(CharacterMotor motor, out Vector3 downhill)
        {
            Vector3 n = motor.estimatedGroundNormal;
            if (n.sqrMagnitude < 0.01f) n = Vector3.up;
            downhill = Vector3.ProjectOnPlane(Vector3.down, n);
            downhill.y = 0f;
            downhill = downhill.sqrMagnitude > 1e-4f ? downhill.normalized : Vector3.zero;
            return Vector3.Angle(n, Vector3.up);
        }

        public override void OnEnter()
        {
            base.OnEnter();
            LashModelSwap.Play(gameObject, "slide_start");
            DeadlockSounds.Play("lash_slide", transform.position, gameObject);
            _loop = DeadlockSounds.Loop("lash_slide_loop", gameObject, 30f);
            if (characterBody != null) characterBody.isSprinting = false;
        }

        private DeadlockVoice? _loop;

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;
            if (!characterMotor.isGrounded) _airborne += dt; else _airborne = 0f;
            if (_airborne > 0.15f) { characterMotor.velocity = Dir * Speed + Vector3.up * characterMotor.velocity.y; outer.SetNextStateToMain(); return; } // off a ledge
            if (!LashMain.SlideHeld) { End(); return; }
            if (inputBank.jump.justPressed)
            {
                // Jump out of a slide and keep the speed.
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = Dir * Speed + Vector3.up * characterBody.jumpPower;
                characterMotor.jumpCount = 1;
                LashJump.Fired(characterBody, characterMotor);
                LashModelSwap.Play(gameObject, "jump");
                DeadlockSounds.Play("lash_jump", transform.position, gameObject);
                outer.SetNextStateToMain();
                return;
            }

            Vector3 input = inputBank.moveVector;
            input.y = 0f;
            if (input.sqrMagnitude > 0.01f) Dir = Vector3.RotateTowards(Dir, input.normalized, LashData.SlideTurnDegPerSec * Mathf.Deg2Rad * dt, 0f);

            float slopeDeg = Slope(characterMotor, out Vector3 downhill);
            float along = downhill == Vector3.zero ? 0f : Vector3.Dot(Dir, downhill);
            bool onSlope = slopeDeg >= LashData.SlideSlopeMinDeg;
            if (along > 0f && slopeDeg >= LashData.SlideAccMinSlopeDeg)
            {
                // Downhill: Deadlock's slope acceleration and top speed, stronger on steeper slopes.
                float t = Mathf.InverseLerp(LashData.SlideAccMinSlopeDeg, LashData.SlideAccMaxSlopeDeg, slopeDeg);
                float cap = Mathf.Lerp(LashData.SlideMaxSpeedMin, LashData.SlideMaxSpeed, t);
                if (Speed < cap) Speed = Mathf.Min(cap, Speed + Mathf.Lerp(LashData.SlideAccMin, LashData.SlideAccMax, t) * along * dt);
            }
            else if (along < 0f) Speed += Mathf.Abs(Physics.gravity.y) * Mathf.Sin(slopeDeg * Mathf.Deg2Rad) * along * dt; // uphill slows you
            Speed -= Speed * (onSlope ? LashData.SlideFrictionSlope : LashData.SlideFriction) * dt;                     // Source-style friction
            if (Speed < LashData.SlideSustain && !(onSlope && along > 0f)) { End(); return; }

            // Move by root motion along the ground (RoR2's motor would otherwise pull the speed back to a walk).
            Vector3 n = characterMotor.estimatedGroundNormal.sqrMagnitude > 0.01f ? characterMotor.estimatedGroundNormal : Vector3.up;
            characterMotor.rootMotion += Vector3.ProjectOnPlane(Dir, n).normalized * Speed * dt;
            characterMotor.velocity = new Vector3(0f, Mathf.Min(characterMotor.velocity.y, 0f), 0f);
            characterMotor.moveDirection = Vector3.zero;
            if (characterDirection != null) characterDirection.moveVector = LashSkillInput.Facing(inputBank, Dir);
            LashSkillInput.Run(this, inputBank, skillLocator);
        }

        private void End()
        {
            characterMotor.velocity = Dir * Mathf.Min(Speed, characterBody.moveSpeed) + Vector3.up * Mathf.Min(characterMotor.velocity.y, 0f);
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            LashModelSwap.Play(gameObject, characterMotor == null || characterMotor.isGrounded ? "slide_getup" : "in_air");
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play("lash_slide_exit", transform.position, gameObject);
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Dir); writer.Write(Speed); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Dir = reader.ReadVector3(); Speed = reader.ReadSingle(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    /// <summary>
    /// A Deadlock dash. Air: flat (no gravity), 8 m in 0.47 s, keeping 20% of the speed after. Ground: 10 m in
    /// 0.68 s; jump late in it (0.3-0.55 s) for Deadlock's dash-jump.
    /// </summary>
    public class LashDash : BaseState
    {
        public bool Air;
        public Vector3 Dir = Vector3.forward;
        private float Duration => Air ? LashData.AirDashSeconds : LashData.GroundDashSeconds;
        private float Speed => (Air ? LashData.AirDashMetres : LashData.GroundDashMetres) / Duration;

        public override void OnEnter()
        {
            base.OnEnter();
            DeadlockSounds.Play("lash_dash", transform.position, gameObject);
            if (!Air) LashModelSwap.Play(gameObject, "dash_ground");
            else
            {
                Vector3 fwd = characterDirection != null ? characterDirection.forward : transform.forward;
                float rel = Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), Dir, Vector3.up);
                LashModelSwap.Play(gameObject, Mathf.Abs(rel) <= 45f ? "dash_air_forward" : Mathf.Abs(rel) >= 135f ? "dash_air_back" : rel > 0f ? "dash_air_right" : "dash_air_left");
            }
            if (isAuthority) characterMotor.disableAirControlUntilCollision = false; // a dash out of a grapple arc steers again
            if (isAuthority && Air) characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (characterDirection != null) characterDirection.moveVector = LashSkillInput.Facing(inputBank, Dir);
            LashSkillInput.Run(this, inputBank, skillLocator);
            characterMotor.rootMotion += Dir * Speed * Time.fixedDeltaTime;
            Vector3 v = characterMotor.velocity;
            characterMotor.velocity = Air ? Vector3.zero : new Vector3(0f, Mathf.Min(v.y, 0f), 0f);

            if (!Air && LashMain.SlideHeld && fixedAge > 0.05f)
            {
                outer.SetNextState(new LashSlide { Dir = Dir, Speed = LashData.SlideDashSpeed });
                return;
            }
            if (!Air && inputBank.jump.justPressed)
            {
                float vy, h;
                if (fixedAge >= LashData.DashJumpWindowStart && fixedAge <= LashData.DashJumpWindowEnd)
                {
                    vy = LashData.DashJumpVertical;
                    h = LashData.DashJumpMetres / (2f * vy / Mathf.Max(1f, -Physics.gravity.y));
                }
                else { vy = characterBody.jumpPower; h = characterBody.moveSpeed; }
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = Dir * h + Vector3.up * vy;
                characterMotor.jumpCount = 1;
                LashJump.Fired(characterBody, characterMotor);
                LashModelSwap.Play(gameObject, "jump");
                DeadlockSounds.Play("lash_jump", transform.position, gameObject);
                outer.SetNextStateToMain();
                return;
            }
            if (fixedAge < Duration) return;
            characterMotor.velocity = Air ? Dir * Speed * LashData.AirDashEndSpeed : Dir * characterBody.moveSpeed + Vector3.up * Mathf.Min(v.y, 0f);
            outer.SetNextStateToMain();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Air); writer.Write(Dir); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Air = reader.ReadBoolean(); Dir = reader.ReadVector3(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
