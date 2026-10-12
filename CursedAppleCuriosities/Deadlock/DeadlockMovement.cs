using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using R2API;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// One Deadlock hero's movement numbers and hooks (from Deadlock's hero data).
    /// Defaults are Deadlock's; a hero overrides what playtesting changed. Every Deadlock survivor shares the same
    /// states (DeadlockMain, DeadlockDash, DeadlockSlide) and reads its own profile off its DeadlockMover.
    /// </summary>
    public class DeadlockMovementProfile
    {
        /// <summary>Sound ids are "{Hero}_{key}" (jump, dash, vo_effort, vo_efforts, slide, slide_loop, slide_exit, wall_jump); unregistered ones stay silent.</summary>
        public string Hero = "";
        /// <summary>Plays a model animation state on the body (parry, parry_air, jump, air_jump, dash_ground, dash_air_forward/back/left/right, slide_start, slide_getup, in_air, wall_jump).</summary>
        public Action<GameObject, string>? Anim;

        // Stamina and jumps
        public int StaminaPips = 3;                     // Deadlock EStamina (Lash is the exception with more)
        public float StaminaRegenSeconds = 4.5f;        // 1 / EStaminaRegenPerSecond
        public float QuailStaminaRegen = 0.25f;         // Wax Quail: faster stamina regen per stack (they never sprint-jump)
        public int AirJumps = 1;                        // one stamina each; extra jumps from items are extra stamina instead
        // Dash
        public bool GroundDash = true;
        public float GroundDashMetres = 10f, GroundDashSeconds = 0.68f;   // the burst: metres covered in seconds
        // Deadlock's ground dash lasts 0.68 s. A hero with a faster burst eases from burst speed down to a run for the
        // rest of it (Deadlock's own dash is fast, then slow): the dash-jump window keeps Deadlock's real timing.
        public float GroundDashTotalSeconds = 0.68f, GroundDashTailDecay = 20f; // 1/s: how fast the tail sheds speed
        public int AirDashesPerAir = 1;
        public float AirDashMetres = 8f, AirDashSeconds = 0.47f, AirDashEndSpeed = 0.2f;
        // Dash-jump: jump late in a ground dash. Deadlock: the window opens 0.3 s into its 0.68 s dash for 0.25 s and
        // the stamina flashes blue; it costs a second stamina pip and sets a fixed launch, whatever the speed before.
        // A ground dash can't be jumped out of otherwise: a press outside the window does nothing but say "too early"
        // or "too late", and locks the dash-jump out for that dash. (A share of the dash halved the window on a 0.3 s
        // dash: it is in seconds, as in Deadlock, and the dash lasts GroundDashTotalSeconds.)
        public float DashJumpWindowOpen = 0.3f, DashJumpWindowLength = 0.25f; // seconds into the ground dash (Deadlock)
        public float DashJumpSpeed = 30f, DashJumpUp = 10.8f;
        public int DashJumpCost = 1;                    // extra pips on top of the dash's one
        // Slide
        public KeyCode SlideKey = KeyCode.LeftControl;  // Deadlock's crouch
        public ConfigEntry<KeyboardShortcut>? SlideKeyEntry; // live: rebinding in the settings menu applies at once
        public float SlideDashSpeed = 16.5f, SlideActivation = 8.89f, SlideSustain = 4.32f, SlideMaxStart = 16.5f;
        public float SlideFriction = 0.8f, SlideFrictionSlope = 0.15f, SlideSlopeMinDeg = 8f, SlideSlopeFacingDeg = 75f;
        public float SlideAccMinSlopeDeg = 5f, SlideAccMaxSlopeDeg = 45f, SlideAccMin = 12.7f, SlideAccMax = 21.59f;
        public float SlideMaxSpeedMin = 30.48f, SlideMaxSpeed = 38.1f, SlideTurnDegPerSec = 90f;
        // Vyper's Slither: no flat-ground friction for the first seconds of a slide (Deadlock m_flFlatGroundFrictionGraceTime),
        // uphill slopes don't slow the slide, and a share of slide distance on top (MODIFIER_VALUE_MOVEMENT_SLIDE_DISTANCE_SCALE).
        public float SlideFrictionGrace = 0f, SlideUphillLoss = 1f, SlideDistanceBonus = 0f;
        // Wall jump
        public float WallJumpUp = 13.3f, WallJumpOutMax = 7.9f, WallJumpOutMin = 5.7f, WallJumpRecharge = 1.25f, WallJumpPowerMin = 0.2f;
        // Air: momentum is kept; input steers it at AirAccel (RoR2's own is 20 m/s2 toward a walk), speed above a run bleeds off at AirDrag.
        public float AirAccel = 50f, AirDrag = 3f;
        // Sprint: out of combat and moving, a real RoR2 sprint (sprint items work) building up to Deadlock's ESprintSpeed.
        public float OutOfCombatSpeed = 2.1f / 7.2f, SprintRampSeconds = 2f;
        /// <summary>
        /// While this is true the hero keeps sprinting in combat (shooting, dealing damage) and only taking damage stops it
        /// (RoR2's outOfDanger instead of outOfCombat). Seven's Power Surge ("Electric Current").
        /// </summary>
        public Func<CharacterBody, bool>? SprintInCombat;

        public int JumpCount => 1 + AirJumps;
        public string Sound(string key) => Hero + "_" + key;
        public void Play(GameObject body, string state) => Anim?.Invoke(body, state);

        /// <summary>The player-facing knobs, in the hero's config section.</summary>
        public void Bind(ConfigFile c, string section)
        {
            StaminaPips = c.Bind(section, "Stamina", StaminaPips, "Stamina pips for dashes and air jumps.").Value;
            GroundDash = c.Bind(section, "Ground Dash", GroundDash, "Sprint key on the ground dashes (one stamina). Off = no ground dash.").Value;
            GroundDashSeconds = c.Bind(section, "Ground Dash - Duration", GroundDashSeconds, $"Seconds to cover the {GroundDashMetres:0} m ground dash burst (lower = faster). The dash then eases to a run until {GroundDashTotalSeconds} s, as in Deadlock.").Value;
            DashJumpSpeed = c.Bind(section, "Dash Jump - Forward Speed", DashJumpSpeed, "Forward launch speed of a timed dash-jump (m/s).").Value;
            DashJumpUp = c.Bind(section, "Dash Jump - Up Speed", DashJumpUp, "Upward launch speed of a timed dash-jump (m/s).").Value;
            // (Seconds now; the old "Window Opens/Closes" keys held shares of the dash.)
            DashJumpWindowOpen = c.Bind(section, "Dash Jump - Window Opens At", DashJumpWindowOpen, "Seconds into a ground dash the dash-jump window opens (Deadlock 0.3). Jumping before it: too early.").Value;
            DashJumpWindowLength = c.Bind(section, "Dash Jump - Window Length", DashJumpWindowLength, "Seconds the dash-jump window stays open (Deadlock 0.25). Jumping after it, still dashing: too late.").Value;
            SprintRampSeconds = c.Bind(section, "Sprint - Build Up Time", SprintRampSeconds, "Seconds for the out-of-combat sprint to build up to full speed.").Value;
            QuailStaminaRegen = c.Bind(section, "Wax Quail - Stamina Regen", QuailStaminaRegen, "Faster stamina refill per Wax Quail (0.25 = +25% per stack).").Value;
            SlideKeyEntry = c.Bind(section, "Slide Key", new KeyboardShortcut(SlideKey), "Hold on the ground while fast (sprint, dash) or on a downhill slope to slide; hold in the air to slide on landing. Deadlock's crouch.");
            SlideKey = SlideKeyEntry.Value.MainKey;
            SlideFriction = c.Bind(section, "Slide - Friction", SlideFriction, "Share of slide speed lost per second on flat ground.").Value;
            AirAccel = c.Bind(section, "Air Control", AirAccel, "How hard you steer in the air (m/s2). Momentum is kept either way.").Value;
            WallJumpUp = c.Bind(section, "Wall Jump - Up Speed", WallJumpUp, $"Upward launch speed off a wall (m/s). Chained wall jumps within {WallJumpRecharge} s start at {WallJumpPowerMin * 100f:0}% of it.").Value;
        }
    }

    /// <summary>Wires Deadlock movement onto a survivor body.</summary>
    public static class DeadlockMovement
    {
        private static bool _registered;
        private static readonly System.Collections.Generic.Dictionary<string, DeadlockMovementProfile> Profiles = new System.Collections.Generic.Dictionary<string, DeadlockMovementProfile>();
        private static readonly DeadlockMovementProfile Default = new DeadlockMovementProfile();

        /// <summary>A hero's profile by its Hero key (the hero defaults if it was never applied).</summary>
        public static DeadlockMovementProfile ProfileFor(string hero) => Profiles.TryGetValue(hero, out DeadlockMovementProfile p) ? p : Default;

        /// <summary>Once for every Deadlock hero: the shared states and the stamina HUD.</summary>
        public static void Init()
        {
            if (_registered) return;
            _registered = true;
            foreach (Type t in new[] { typeof(EntityStates.CursedApple.Deadlock.DeadlockMain), typeof(EntityStates.CursedApple.Deadlock.DeadlockDash), typeof(EntityStates.CursedApple.Deadlock.DeadlockSlide) })
                ContentAddition.AddEntityState(t, out _);
            DeadlockStaminaHud.Hook();
        }

        /// <summary>
        /// Give a body prefab Deadlock movement: stamina, jumps, its own air control, and DeadlockMain as
        /// the Body machine's main state (pass a subclass of it for a hero that adds to it).
        /// </summary>
        public static DeadlockMover Apply(GameObject body, DeadlockMovementProfile profile, Type? mainState = null)
        {
            Init();
            var cb = body.GetComponent<CharacterBody>();
            cb.baseJumpCount = profile.JumpCount;
            cb.bodyFlags |= CharacterBody.BodyFlags.SprintAnyDirection;  // RoR2 drops sprint input off-forward: dashes go any way
            cb.sprintingSpeedMultiplier = 1f;                            // the Deadlock sprint builds this up out of combat
            cb.useSprintCrosshair = false;                               // they sprint on their own: keep the weapon crosshair
            body.GetComponent<CharacterMotor>().airControl = 0f;         // DeadlockMain steers in the air itself (keeps momentum)
            var mover = body.AddComponent<DeadlockMover>();
            Profiles[profile.Hero] = profile;
            mover.HeroKey = profile.Hero;
            foreach (EntityStateMachine esm in body.GetComponents<EntityStateMachine>())
                if (esm.customName == "Body") esm.mainStateType = new EntityStates.SerializableEntityStateType(mainState ?? typeof(EntityStates.CursedApple.Deadlock.DeadlockMain));
            return mover;
        }
    }

    /// <summary>
    /// Deadlock stamina and movement bookkeeping on a hero's body: pips that dashes and air jumps spend, refilling
    /// one at a time. Movement is owner-authoritative in RoR2, so only the owning player's copy matters.
    /// </summary>
    public class DeadlockMover : MonoBehaviour
    {
        /// <summary>
        /// The hero's profile key. Only this string survives Unity cloning the body prefab (a plain C# object field
        /// would come back as a fresh default profile on every spawned body), so the profile is looked up by it.
        /// </summary>
        public string HeroKey = "";
        public DeadlockMovementProfile Profile => DeadlockMovement.ProfileFor(HeroKey);
        public int Max;
        private bool _warded;
        public int Pips = -1;
        public int AirDashes;
        public float LastWallJump = -10f;
        public float SprintRamp;    // 0-1: how far the out-of-combat sprint has built up
        public bool DashJumpWindow; // a ground dash's dash-jump window is open (and affordable): the HUD flashes blue
        public string DashJumpMessage = ""; // "too early" / "too late" / "not enough stamina", shown under the pips for a moment
        public float DashJumpMessageTime = -10f;
        /// <summary>A jump press until this time was already used by an ability (Grey Talon cancelling his glide): no jump from it.</summary>
        public float SuppressJumpUntil = -10f;
        private float _regen;
        private CharacterBody _body = null!;

        /// <summary>0-1 progress of the next pip refilling.</summary>
        public float RegenProgress => Pips < Max ? Mathf.Clamp01(_regen / Profile.StaminaRegenSeconds) : 0f;

        public bool SlideHeld => Input.GetKey(Profile.SlideKeyEntry != null ? Profile.SlideKeyEntry.Value.MainKey : Profile.SlideKey);

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void FixedUpdate()
        {
            ParryOnCounterspell();
            // Extra jumps from items (Hopoo Feather...) become stamina pips instead (playtest).
            Max = Profile.StaminaPips + Mathf.Max(0, _body.maxJumpCount - _body.baseJumpCount);
            if (Pips < 0 || Pips > Max) Pips = Max;
            if (_body.characterMotor != null && _body.characterMotor.isGrounded) AirDashes = 0;
            if (Pips >= Max) { _regen = 0f; return; }
            int quail = _body.inventory != null ? _body.inventory.GetItemCountEffective(RoR2Content.Items.JumpBoost) : 0;
            _regen += Time.fixedDeltaTime * (1f + Profile.QuailStaminaRegen * quail); // Wax Quail: faster stamina
            if (_regen >= Profile.StaminaRegenSeconds) { Pips++; _regen = 0f; }
        }

        /// <summary>
        /// Counterspell (any Deadlock hero): the hero's parry when the ward goes up. Read from the synced buff, so
        /// it plays on every client, not just where the equipment fired.
        /// </summary>
        private void ParryOnCounterspell()
        {
            BuffDef? ward = CursedAppleCuriosities.Items.Counterspell.WardBuffDef;
            bool warded = ward != null && ward.buffIndex != BuffIndex.None && _body.HasBuff(ward.buffIndex);
            if (warded && !_warded)
                Profile.Play(gameObject, _body.characterMotor != null && !_body.characterMotor.isGrounded ? "parry_air" : "parry");
            _warded = warded;
        }

        public bool TrySpend()
        {
            if (Pips <= 0) return false;
            Pips--;
            DeadlockSounds.Play(Profile.Sound("vo_effort"), transform.position, gameObject);
            return true;
        }

        public void Refund(int pips) => Pips = Mathf.Min(Max, Pips + pips);
    }

    /// <summary>Stamina pips under the crosshair (a row of small bars), for any Deadlock hero.</summary>
    public class DeadlockStaminaHud : MonoBehaviour
    {
        private HUD _hud = null!;
        private RectTransform? _root;
        private Image[] _fills = Array.Empty<Image>();
        private int _built = -1;
        private TMPro.TextMeshProUGUI? _message;
        private const float MessageSeconds = 1.2f;
        private const float W = 14f, H = 4f, Gap = 4f;

        public static void Hook()
        {
            On.RoR2.UI.HUD.Awake += (orig, self) => { orig(self); self.gameObject.AddComponent<DeadlockStaminaHud>()._hud = self; };
        }

        private void Build(int max)
        {
            if (_root != null) Destroy(_root.gameObject);
            var go = new GameObject("DeadlockStamina", typeof(RectTransform));
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

            // The dash-jump fail message ("too early" / "too late") under the pips, in the HUD's own font.
            var label = new GameObject("DashJumpMessage", typeof(RectTransform));
            var lrt = (RectTransform)label.transform;
            lrt.SetParent(_root, false);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0f, -16f);
            lrt.sizeDelta = new Vector2(400f, 24f);
            _message = label.AddComponent<TMPro.TextMeshProUGUI>();
            TMPro.TextMeshProUGUI? hudText = _hud.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (hudText != null && hudText.font != null) _message.font = hudText.font;
            _message.fontSize = 18f;
            _message.alignment = TMPro.TextAlignmentOptions.Center;
            _message.raycastTarget = false;
            _message.text = "";
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
            DeadlockMover? s = body != null ? body.GetComponent<DeadlockMover>() : null;
            if (s == null || s.Max <= 0) { if (_root != null) _root.gameObject.SetActive(false); return; }
            if (_built != s.Max || _root == null) Build(s.Max);
            _root!.gameObject.SetActive(true);
            for (int i = 0; i < _fills.Length; i++)
            {
                float fill = i < s.Pips ? 1f : i == s.Pips ? s.RegenProgress : 0f;
                _fills[i].rectTransform.sizeDelta = new Vector2(W * fill, H);
                // Deadlock: the pips a dash-jump would spend flash blue while its window is open.
                bool flash = s.DashJumpWindow && i >= s.Pips - s.Profile.DashJumpCost && i < s.Pips;
                _fills[i].color = flash ? new Color(0.35f, 0.75f, 1f, 1f) : i < s.Pips ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
            }
            if (_message != null)
            {
                float age = Time.time - s.DashJumpMessageTime;
                bool show = age >= 0f && age < MessageSeconds;
                _message.text = show ? s.DashJumpMessage : "";
                if (show) _message.color = new Color(1f, 0.45f, 0.35f, Mathf.Clamp01((MessageSeconds - age) / 0.3f)); // fades out at the end
            }
        }
    }
}

namespace EntityStates.CursedApple.Deadlock
{
    /// <summary>
    /// Every Deadlock jump counts as a RoR2 jump (playtest), including the ones that set their own velocity (wall
    /// jumps, dash-jumps, jumps out of a slide): the jump dust and the global jump event on-jump items listen for.
    /// </summary>
    public static class DeadlockJump
    {
        public static GameObject? LandEffect;

        public static void Fired(CharacterBody body, CharacterMotor motor)
        {
            if (LandEffect == null) LandEffect = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/CharacterLandImpact.prefab").WaitForCompletion();
            if (!motor.isGrounded && LandEffect != null)
                EffectManager.SpawnEffect(LandEffect, new EffectData { origin = body.footPosition, scale = body.radius }, true);
            body.TriggerJumpEventGlobally();
        }
    }

    /// <summary>
    /// A Deadlock hero's main state: RoR2 movement plus Deadlock's. Sprint key = dash (ground or air, one stamina
    /// each). Air jumps cost one stamina each; jumping beside a wall kicks off it for free. The slide key slides when
    /// fast or on a downhill slope, or on landing while held. Heroes can subclass it.
    /// </summary>
    public class DeadlockMain : GenericCharacterMain
    {
        protected DeadlockMover? _mover;
        private bool _wasGrounded = true;
        private Vector3 _airVel;

        protected DeadlockMovementProfile P => _mover!.Profile;

        public override void OnEnter()
        {
            base.OnEnter();
            _mover = GetComponent<DeadlockMover>();
            _wasGrounded = characterMotor == null || characterMotor.isGrounded;
        }

        public override void FixedUpdate()
        {
            if (isAuthority && _mover != null && inputBank != null && characterMotor != null)
            {
                if (SprintKeyPressed() && TryDash()) return;
                bool grounded = characterMotor.isGrounded;
                Vector3 v = characterMotor.velocity;
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                bool justLanded = grounded && !_wasGrounded;
                _wasGrounded = grounded;
                if (!grounded) _airVel = flat;
                if (_mover.SlideHeld && grounded)
                {
                    // Deadlock: hold crouch in the air and you slide the moment you land, keeping your air speed.
                    if (justLanded && _airVel.magnitude >= P.SlideSustain) { outer.SetNextState(new DeadlockSlide { Dir = _airVel.normalized, Speed = Mathf.Min(_airVel.magnitude, P.SlideMaxStart) }); return; }
                    if (TrySlide(flat)) return;
                }
            }
            base.FixedUpdate();
            if (isAuthority && _mover != null) DeadlockSprint();
            if (isAuthority && _mover != null && characterMotor != null && !characterMotor.isGrounded) AirSteer(characterBody, characterMotor, characterMotor.moveDirection, P);
        }

        /// <summary>
        /// Deadlock-style air movement (the body has RoR2 air control off): momentum is kept, the movement input
        /// steers it toward its direction without slowing you below what you had, and speed above a run sheds slowly.
        /// A launch with disableAirControlUntilCollision flies untouched until you jump or dash.
        /// </summary>
        public static void AirSteer(CharacterBody characterBody, CharacterMotor characterMotor, Vector3 wish, DeadlockMovementProfile p)
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
                flat = Vector3.MoveTowards(flat, target, p.AirAccel * dt);
            }
            if (flat.magnitude > run) flat = flat.normalized * Mathf.Max(run, flat.magnitude - p.AirDrag * dt);
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
        /// button state follows isSprinting, so reading that would dash every time the hero starts sprinting.
        /// </summary>
        private bool SprintKeyPressed() => SprintKeyPressed(characterBody, inputBank);

        /// <summary>The dash key this frame, for any state on a Deadlock hero (Grey Talon dashes inside Rain of Arrows' glide).</summary>
        public static bool SprintKeyPressed(CharacterBody body, InputBankTest bank)
        {
            var player = body.master != null && body.master.playerCharacterMasterController != null
                ? body.master.playerCharacterMasterController.networkUser?.localUser?.inputPlayer : null;
            return player != null ? player.GetButtonDown(RewiredConsts.Action.Sprint) : bank.sprint.justPressed;
        }

        /// <summary>
        /// Deadlock's sprint: out of combat and moving, a real RoR2 sprint (so sprint items work) building up from a
        /// run to OutOfCombatSpeed over SprintRampSeconds. The sprint key never sprints.
        /// </summary>
        private void DeadlockSprint()
        {
            bool calm = characterBody.outOfCombat || P.SprintInCombat != null && P.SprintInCombat(characterBody) && characterBody.outOfDanger;
            bool want = calm && inputBank.moveVector.sqrMagnitude > 0.01f;
            float ramp = want ? Mathf.Min(1f, _mover!.SprintRamp + Time.fixedDeltaTime / Mathf.Max(0.01f, P.SprintRampSeconds)) : 0f;
            _mover!.SprintRamp = ramp;
            characterBody.isSprinting = want;
            float mult = 1f + P.OutOfCombatSpeed * ramp;
            if (Mathf.Abs(characterBody.sprintingSpeedMultiplier - mult) > 0.01f || (ramp >= 1f && characterBody.sprintingSpeedMultiplier != mult))
            {
                characterBody.sprintingSpeedMultiplier = mult;
                characterBody.RecalculateStats();
            }
        }

        /// <summary>Slide when fast enough (a sprint is) or on a downhill slope while heading down it.</summary>
        private bool TrySlide(Vector3 flat)
        {
            float slope = DeadlockSlide.Slope(characterMotor, out Vector3 downhill);
            Vector3 heading = flat.sqrMagnitude > 0.25f ? flat.normalized : FlatAim();
            bool fast = flat.magnitude >= P.SlideActivation || characterBody.isSprinting && flat.magnitude >= characterBody.moveSpeed * 0.8f;
            bool downSlope = slope >= P.SlideSlopeMinDeg && downhill != Vector3.zero && Vector3.Angle(heading, downhill) <= P.SlideSlopeFacingDeg;
            if (!fast && !downSlope) return false;
            outer.SetNextState(new DeadlockSlide { Dir = heading, Speed = Mathf.Min(Mathf.Max(flat.magnitude, P.SlideSustain + 0.5f), P.SlideMaxStart) });
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
            if (air && _mover!.AirDashes >= P.AirDashesPerAir) return false;
            if (!air && !P.GroundDash) return false;
            if (!_mover!.TrySpend()) return false;
            if (air) _mover.AirDashes++;
            Vector3 dir = inputBank.moveVector;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = FlatAim();
            outer.SetNextState(new DeadlockDash { Air = air, Dir = dir.normalized });
            return true;
        }

        public override void ProcessJump()
        {
            if (jumpInputReceived && _mover != null && Time.time < _mover.SuppressJumpUntil) jumpInputReceived = false;
            if (jumpInputReceived && characterBody != null && characterMotor != null && !characterMotor.isGrounded && _mover != null)
            {
                // Deadlock order: ground jump, else kick off a wall (free), else an air jump.
                if (TryWall(out Vector3 normal)) { WallJump(normal); jumpInputReceived = false; return; }
                if (characterMotor.jumpCount == 0) characterMotor.jumpCount = 1; // walked off a ledge: the next jump is an air jump
                int jumpsBefore = characterMotor.jumpCount;
                // Air jumps cost one stamina each. Extra jumps from items are extra stamina instead.
                if (jumpsBefore >= P.JumpCount || !_mover.TrySpend()) { jumpInputReceived = false; return; }
                Vector3 before = characterMotor.velocity;
                before.y = 0f;
                base.ProcessJump();
                if (characterMotor.jumpCount > jumpsBefore)
                {
                    KeepMomentum(before);
                    DeadlockSounds.Play(P.Sound("jump"), transform.position, gameObject);
                    DeadlockSounds.Play(P.Sound("vo_efforts"), transform.position, gameObject);
                    characterMotor.disableAirControlUntilCollision = false; // steering back after a launch
                    P.Play(gameObject, "air_jump");
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

        /// <summary>Deadlock wall jump: up and out, keeping speed along the wall; chained ones are weaker for a while.</summary>
        private void WallJump(Vector3 normal)
        {
            float power = Mathf.Lerp(P.WallJumpPowerMin, 1f, Mathf.Clamp01((Time.time - _mover!.LastWallJump) / P.WallJumpRecharge));
            _mover.LastWallJump = Time.time;
            Vector3 v = characterMotor.velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float outSpeed = Mathf.Lerp(P.WallJumpOutMin, P.WallJumpOutMax, Mathf.Clamp01(flat.magnitude / (2f * characterBody.baseMoveSpeed)));
            Vector3 h = normal * outSpeed + Vector3.ProjectOnPlane(flat, normal);
            characterMotor.Motor.ForceUnground();
            characterMotor.velocity = new Vector3(h.x, P.WallJumpUp * power, h.z);
            characterMotor.disableAirControlUntilCollision = false;
            DeadlockJump.Fired(characterBody, characterMotor);
            P.Play(gameObject, "wall_jump");
            DeadlockSounds.Play(P.Sound("wall_jump"), transform.position, gameObject);
        }
    }

    /// <summary>
    /// RoR2 only reads skill buttons in the main state. Slide and dash are their own states, so they read them
    /// here: heroes can shoot (and use skills) while sliding or dashing, as in Deadlock.
    /// </summary>
    public static class DeadlockSkillInput
    {
        public static void Run(InputBankTest input, SkillLocator locator)
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
    /// Deadlock slide: keeps your speed, Source-style friction on flat ground, Deadlock's
    /// slope acceleration downhill, steer toward your movement input, jump out to keep the speed. Ends when the
    /// slide key is let go, you slow below the sustain speed, or you slide off a ledge (keeping momentum).
    /// </summary>
    public class DeadlockSlide : BaseState
    {
        public Vector3 Dir = Vector3.forward;
        public float Speed;
        private float _airborne;
        private DeadlockMover? _mover;
        private DeadlockVoice? _loop;

        private DeadlockMovementProfile P => _mover != null ? _mover.Profile : Fallback;
        private static readonly DeadlockMovementProfile Fallback = new DeadlockMovementProfile();

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
            _mover = GetComponent<DeadlockMover>();
            P.Play(gameObject, "slide_start");
            DeadlockSounds.Play(P.Sound("slide"), transform.position, gameObject);
            _loop = DeadlockSounds.Loop(P.Sound("slide_loop"), gameObject, 30f);
            if (characterBody != null) characterBody.isSprinting = false;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            float dt = Time.fixedDeltaTime;
            if (!characterMotor.isGrounded) _airborne += dt; else _airborne = 0f;
            if (_airborne > 0.15f) { characterMotor.velocity = Dir * Speed + Vector3.up * characterMotor.velocity.y; outer.SetNextStateToMain(); return; } // off a ledge
            if (_mover == null || !_mover.SlideHeld) { End(); return; }
            if (inputBank.jump.justPressed)
            {
                // Jump out of a slide and keep the speed.
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = Dir * Speed + Vector3.up * characterBody.jumpPower;
                characterMotor.jumpCount = 1;
                DeadlockJump.Fired(characterBody, characterMotor);
                P.Play(gameObject, "jump");
                DeadlockSounds.Play(P.Sound("jump"), transform.position, gameObject);
                outer.SetNextStateToMain();
                return;
            }

            Vector3 input = inputBank.moveVector;
            input.y = 0f;
            if (input.sqrMagnitude > 0.01f) Dir = Vector3.RotateTowards(Dir, input.normalized, P.SlideTurnDegPerSec * Mathf.Deg2Rad * dt, 0f);

            float slopeDeg = Slope(characterMotor, out Vector3 downhill);
            float along = downhill == Vector3.zero ? 0f : Vector3.Dot(Dir, downhill);
            bool onSlope = slopeDeg >= P.SlideSlopeMinDeg;
            if (along > 0f && slopeDeg >= P.SlideAccMinSlopeDeg)
            {
                // Downhill: Deadlock's slope acceleration and top speed, stronger on steeper slopes.
                float t = Mathf.InverseLerp(P.SlideAccMinSlopeDeg, P.SlideAccMaxSlopeDeg, slopeDeg);
                float cap = Mathf.Lerp(P.SlideMaxSpeedMin, P.SlideMaxSpeed, t);
                if (Speed < cap) Speed = Mathf.Min(cap, Speed + Mathf.Lerp(P.SlideAccMin, P.SlideAccMax, t) * along * dt);
            }
            else if (along < 0f) Speed += Mathf.Abs(Physics.gravity.y) * Mathf.Sin(slopeDeg * Mathf.Deg2Rad) * along * P.SlideUphillLoss * dt; // uphill slows you
            if (onSlope || fixedAge >= P.SlideFrictionGrace)                                                                 // Source-style friction
                Speed -= Speed * (onSlope ? P.SlideFrictionSlope : P.SlideFriction) / (1f + P.SlideDistanceBonus) * dt;
            if (Speed < P.SlideSustain && !(onSlope && along > 0f)) { End(); return; }

            // Move by root motion along the ground (RoR2's motor would otherwise pull the speed back to a walk).
            Vector3 n = characterMotor.estimatedGroundNormal.sqrMagnitude > 0.01f ? characterMotor.estimatedGroundNormal : Vector3.up;
            characterMotor.rootMotion += Vector3.ProjectOnPlane(Dir, n).normalized * Speed * dt;
            characterMotor.velocity = new Vector3(0f, Mathf.Min(characterMotor.velocity.y, 0f), 0f);
            characterMotor.moveDirection = Vector3.zero;
            if (characterDirection != null) characterDirection.moveVector = DeadlockSkillInput.Facing(inputBank, Dir);
            DeadlockSkillInput.Run(inputBank, skillLocator);
        }

        private void End()
        {
            characterMotor.velocity = Dir * Mathf.Min(Speed, characterBody.moveSpeed) + Vector3.up * Mathf.Min(characterMotor.velocity.y, 0f);
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            P.Play(gameObject, characterMotor == null || characterMotor.isGrounded ? "slide_getup" : "in_air");
            DeadlockSounds.Stop(_loop);
            DeadlockSounds.Play(P.Sound("slide_exit"), transform.position, gameObject);
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Dir); writer.Write(Speed); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Dir = reader.ReadVector3(); Speed = reader.ReadSingle(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    /// <summary>
    /// A Deadlock dash. Air: flat (no gravity), keeping a share of the speed after. Ground: jump late in it (the
    /// stamina pips flash blue) for Deadlock's dash-jump.
    /// </summary>
    public class DeadlockDash : BaseState
    {
        public bool Air;
        public Vector3 Dir = Vector3.forward;
        private DeadlockMover? _mover;
        private bool _lockedOut;

        private DeadlockMovementProfile P => _mover != null ? _mover.Profile : Fallback;
        private static readonly DeadlockMovementProfile Fallback = new DeadlockMovementProfile();
        private float Burst => Air ? P.AirDashSeconds : P.GroundDashSeconds;              // the dash's travel
        private float Duration => Air ? P.AirDashSeconds : Mathf.Max(P.GroundDashSeconds, P.GroundDashTotalSeconds); // the whole dash
        private float Speed => (Air ? P.AirDashMetres : P.GroundDashMetres) / Burst;
        private float WindowOpen => P.DashJumpWindowOpen;
        private float WindowClose => P.DashJumpWindowOpen + P.DashJumpWindowLength;

        /// <summary>Burst speed, then (ground) easing toward a run for the rest of the dash.</summary>
        private float SpeedNow()
        {
            if (fixedAge < Burst) return Speed;
            float run = characterBody.moveSpeed;
            return run + Mathf.Max(0f, Speed - run) * Mathf.Exp(-P.GroundDashTailDecay * (fixedAge - Burst));
        }

        public override void OnEnter()
        {
            base.OnEnter();
            _mover = GetComponent<DeadlockMover>();
            DeadlockSounds.Play(P.Sound("dash"), transform.position, gameObject);
            if (!Air) P.Play(gameObject, "dash_ground");
            else
            {
                Vector3 fwd = characterDirection != null ? characterDirection.forward : transform.forward;
                float rel = Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), Dir, Vector3.up);
                P.Play(gameObject, Mathf.Abs(rel) <= 45f ? "dash_air_forward" : Mathf.Abs(rel) >= 135f ? "dash_air_back" : rel > 0f ? "dash_air_right" : "dash_air_left");
            }
            if (isAuthority) characterMotor.disableAirControlUntilCollision = false; // a dash out of a launch arc steers again
            if (isAuthority && Air) characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (characterDirection != null) characterDirection.moveVector = DeadlockSkillInput.Facing(inputBank, Dir);
            DeadlockSkillInput.Run(inputBank, skillLocator);
            characterMotor.rootMotion += Dir * SpeedNow() * Time.fixedDeltaTime;
            Vector3 v = characterMotor.velocity;
            characterMotor.velocity = Air ? Vector3.zero : new Vector3(0f, Mathf.Min(v.y, 0f), 0f);

            if (!Air && _mover != null && _mover.SlideHeld && fixedAge > 0.05f)
            {
                outer.SetNextState(new DeadlockSlide { Dir = Dir, Speed = P.SlideDashSpeed });
                return;
            }
            bool inWindow = !Air && !_lockedOut && fixedAge >= WindowOpen && fixedAge <= WindowClose;
            bool affordable = _mover != null && _mover.Pips >= P.DashJumpCost;
            if (_mover != null) _mover.DashJumpWindow = inWindow && affordable;
            if (!Air && inputBank.jump.justPressed && !_lockedOut)
            {
                // Deadlock: a jump in the window is a dash-jump (a second stamina pip, a fixed long leap, no matter
                // the speed before). Works grounded or not (Deadlock's coyote dash-jump: off a ledge mid-dash, and
                // the air jumps stay). Outside it there is no jump at all: the dash carries on, the dash-jump is
                // locked out for this dash, and the player is told why. Presses after that are ignored.
                if (inWindow && affordable)
                {
                    for (int i = 0; i < P.DashJumpCost; i++) _mover!.TrySpend();
                    Launch(Dir * P.DashJumpSpeed + Vector3.up * P.DashJumpUp);
                    DeadlockSounds.Play(P.Sound("dash"), transform.position, gameObject);
                    return;
                }
                _lockedOut = true;
                Fail(inWindow ? "Dash-jump: not enough stamina" : fixedAge < WindowOpen ? "Dash-jump: too early" : "Dash-jump: too late");
            }
            if (fixedAge < Duration) return;
            characterMotor.velocity = Air ? Dir * Speed * P.AirDashEndSpeed : Dir * characterBody.moveSpeed + Vector3.up * Mathf.Min(v.y, 0f);
            outer.SetNextStateToMain();
        }

        private void Fail(string message)
        {
            if (_mover == null) return;
            _mover.DashJumpMessage = message;
            _mover.DashJumpMessageTime = Time.time;
            _mover.DashJumpWindow = false;
        }

        private void Launch(Vector3 velocity)
        {
            characterMotor.Motor.ForceUnground();
            characterMotor.velocity = velocity;
            characterMotor.jumpCount = 1; // the air jumps stay (a coyote dash-jump isn't an air action either)
            DeadlockJump.Fired(characterBody, characterMotor);
            P.Play(gameObject, "jump");
            DeadlockSounds.Play(P.Sound("jump"), transform.position, gameObject);
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (_mover != null) _mover.DashJumpWindow = false;
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Air); writer.Write(Dir); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Air = reader.ReadBoolean(); Dir = reader.ReadVector3(); }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
