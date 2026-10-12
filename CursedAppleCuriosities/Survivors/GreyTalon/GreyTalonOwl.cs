using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// The Guided Owl as its owner flies it (Deadlock ability_guided_arrow). It moves on the owner's machine so steering
    /// is immediate: it turns toward where he looks at Deadlock's track rate, cruises at 15 m/s and speeds up to 36 m/s
    /// while forward is held, and bursts on the first terrain or enemy it touches (or when its 20 s run out). The camera
    /// rides behind it (an override camera that keeps the player's own look rotation, so looking around steers it) and
    /// holds on the blast for a moment. Released, it flies on straight and the camera goes back to Grey Talon.
    /// </summary>
    public class GreyTalonOwl : MonoBehaviour, ICameraStateProvider
    {
        private CharacterBody _by = null!;
        private bool _crit, _released, _exploded;
        private Vector3 _pos, _prevPos, _dir, _wish, _blastAt;
        private float _speed, _age, _flight, _holdUntil, _sendT;
        private bool _boost;
        private CameraRigController? _rig;
        private DeadlockVoice? _hum;

        /// <summary>Over: burst (and the camera's hold on the blast is done), or released (the state can end).</summary>
        public bool Done => _exploded && Time.time >= _holdUntil;

        public static GreyTalonOwl Launch(CharacterBody by, Vector3 at, Vector3 dir, bool crit)
        {
            var go = new GameObject("CursedAppleGreyTalonOwl");
            go.transform.position = at;
            var owl = go.AddComponent<GreyTalonOwl>();
            owl._by = by;
            owl._crit = crit;
            owl._pos = owl._prevPos = at;
            owl._dir = owl._wish = dir.normalized;
            owl._speed = GreyTalonData.OwlSpeed;
            owl._flight = GreyTalonData.OwlFlight * DurationExtender.Multiplier(by);
            GreyTalonOwlVisual.Build(go.transform);
            owl._hum = DeadlockSounds.Loop("greytalon_owl_loop", go, owl._flight + 1f);
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
                if (rig.target == by.gameObject && rig.localUserViewer != null) { owl._rig = rig; rig.SetOverrideCam(owl, 0.25f); break; }
            return owl;
        }

        /// <summary>Where the player is looking (the camera's own look rotation; the override only moves its position).</summary>
        public Vector3 LookDirection(CharacterBody body) =>
            _rig != null && _rig.sceneCam != null ? _rig.sceneCam.transform.forward : body.inputBank != null ? body.inputBank.aimDirection : _dir;

        public void Steer(Vector3 look, bool boost)
        {
            if (_released) return;
            _wish = look.normalized;
            _boost = boost;
        }

        /// <summary>Primary while steering (user): the owl bursts right where it is, in mid-air.</summary>
        public void Detonate()
        {
            if (!_exploded) Explode(_pos);
        }

        public void Release()
        {
            _released = true;
            _boost = false;
            ClearCamera(0.3f);
        }

        private void FixedUpdate()
        {
            if (_exploded) { if (Time.time >= _holdUntil) Destroy(gameObject); return; }
            float dt = Time.fixedDeltaTime;
            _age += dt;
            if (!_released) _dir = Vector3.RotateTowards(_dir, _wish, GreyTalonData.OwlTurnDegPerSec * Mathf.Deg2Rad * dt, 0f).normalized;
            _speed = _boost ? Mathf.MoveTowards(_speed, GreyTalonData.OwlMaxSpeed, GreyTalonData.OwlAccel * dt)
                            : Mathf.MoveTowards(_speed, GreyTalonData.OwlSpeed, GreyTalonData.OwlDecel * dt);
            float step = _speed * dt;
            if (_age > 0.05f && Touches(_pos, _dir, step, out Vector3 hitAt)) { Explode(hitAt); return; }
            _prevPos = _pos;
            _pos += _dir * step;
            if (_age >= _flight) { Explode(_pos); return; }
            _sendT -= dt;
            if (_sendT <= 0f)
            {
                _sendT = 0.05f;
                GreyTalonNet.ToServer(new GreyTalonOwlMoveMessage { Attacker = _by != null ? _by.gameObject : null, Pos = _pos, Dir = _dir });
            }
        }

        /// <summary>The first terrain or enemy in the owl's path this tick (Deadlock m_flTriggerRadius).</summary>
        private bool Touches(Vector3 from, Vector3 dir, float step, out Vector3 at)
        {
            at = from;
            TeamIndex team = _by != null && _by.teamComponent != null ? _by.teamComponent.teamIndex : TeamIndex.Player;
            RaycastHit[] hits = Physics.SphereCastAll(from, GreyTalonData.OwlHitRadius, dir, step, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (RaycastHit h in hits)
            {
                if (((1 << h.collider.gameObject.layer) & LayerIndex.world.mask) == 0)
                {
                    HurtBox? box = h.collider.GetComponent<HurtBox>();
                    if (box == null || box.teamIndex == team || box.healthComponent == null || !box.healthComponent.alive) continue;
                }
                if (h.distance < best) { best = h.distance; found = true; at = h.point != Vector3.zero ? h.point : from + dir * h.distance; }
            }
            return found;
        }

        private void Explode(Vector3 at)
        {
            _exploded = true;
            _blastAt = at;
            _pos = at;
            DeadlockSounds.Stop(_hum);
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            GameObject? by = _by != null ? _by.gameObject : null;
            GreyTalonNet.ToServer(new GreyTalonOwlBlastMessage { Attacker = by, Point = at, Crit = _crit });
            GreyTalonNet.ToServer(new GreyTalonOwlMoveMessage { Attacker = by, Pos = at, Dir = _dir, Gone = true });
            _holdUntil = Time.time + (_released ? 0f : GreyTalonData.OwlCameraHold); // Deadlock m_flCameraHoldAtExplosion
        }

        private void Update()
        {
            if (_exploded) { transform.position = _blastAt; return; }
            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            transform.position = Vector3.Lerp(_prevPos, _pos, t);
            transform.rotation = Quaternion.LookRotation(_dir);
        }

        private void OnDestroy()
        {
            DeadlockSounds.Stop(_hum);
            ClearCamera(0.3f);
        }

        private void ClearCamera(float lerp)
        {
            if (_rig != null && _rig.IsOverrideCam(this)) _rig.SetOverrideCam(null, lerp);
            _rig = null;
        }

        // ---- the camera behind the owl ----

        public void GetCameraState(CameraRigController rig, ref CameraState state)
        {
            Vector3 look = state.rotation * Vector3.forward;
            Vector3 anchor = transform.position;
            Vector3 want = anchor - look * GreyTalonData.OwlCameraBack + Vector3.up * GreyTalonData.OwlCameraUp;
            // Keep the camera out of walls between it and the owl.
            Vector3 back = want - anchor;
            if (Physics.SphereCast(anchor, 0.25f, back.normalized, out RaycastHit hit, back.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                want = anchor + back.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
            state.position = want;
        }

        public bool IsUserLookAllowed(CameraRigController rig) => true;
        public bool IsUserControlAllowed(CameraRigController rig) => true;
        public bool IsHudAllowed(CameraRigController rig) => true;
    }

    /// <summary>Everyone but the owner: the owl as its owner reports it (smoothed between updates).</summary>
    public class GreyTalonOwlGhost : MonoBehaviour
    {
        private static readonly Dictionary<GameObject, GreyTalonOwlGhost> Live = new Dictionary<GameObject, GreyTalonOwlGhost>();
        private Vector3 _target, _dir;
        private float _lastHeard;
        private DeadlockVoice? _hum;

        public static void Update(GameObject owner, Vector3 pos, Vector3 dir, bool gone)
        {
            Live.TryGetValue(owner, out GreyTalonOwlGhost ghost);
            if (gone)
            {
                if (ghost != null) Destroy(ghost.gameObject);
                Live.Remove(owner);
                return;
            }
            if (ghost == null)
            {
                var go = new GameObject("CursedAppleGreyTalonOwlGhost");
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(dir);
                ghost = go.AddComponent<GreyTalonOwlGhost>();
                GreyTalonOwlVisual.Build(go.transform);
                ghost._hum = DeadlockSounds.Loop("greytalon_owl_loop", go, GreyTalonData.OwlFlight + 2f);
                Live[owner] = ghost;
            }
            ghost._target = pos;
            ghost._dir = dir;
            ghost._lastHeard = Time.time;
        }

        private void Update()
        {
            transform.position = Vector3.Lerp(transform.position, _target, 1f - Mathf.Exp(-20f * Time.deltaTime));
            if (_dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_dir), 1f - Mathf.Exp(-15f * Time.deltaTime));
            if (Time.time - _lastHeard > 2f) Destroy(gameObject); // its owner left or the last message got lost
        }

        private void OnDestroy() => DeadlockSounds.Stop(_hum);
    }

    /// <summary>What the owl looks like: Deadlock's own spirit owl (archer_guided_arrow) when converted, inside the Engi harpoon's glow and trail.</summary>
    public static class GreyTalonOwlVisual
    {
        public const float ModelScale = 1.4f;

        public static void Build(Transform parent)
        {
            if (GreyTalonSurvivor.OwlGlow != null)
            {
                GameObject glow = Object.Instantiate(GreyTalonSurvivor.OwlGlow, parent, false);
                glow.transform.localPosition = Vector3.zero;
                glow.transform.localRotation = Quaternion.identity;
                glow.SetActive(true);
            }
            GlbModel? model = DeadlockInstall.Model(GreyTalonData.OwlKey);
            if (model == null) return;
            GameObject owl = Object.Instantiate(model.Root, parent, false);
            owl.name = "GreyTalonOwlModel";
            // The mesh runs 1.3 m along +Z from its tail: centred on the flight point, pointing ahead.
            owl.transform.localScale = Vector3.one * ModelScale;
            owl.transform.localRotation = Quaternion.identity;
            owl.transform.localPosition = new Vector3(0f, 0f, -0.65f * ModelScale);
            owl.SetActive(true);
        }
    }
}
