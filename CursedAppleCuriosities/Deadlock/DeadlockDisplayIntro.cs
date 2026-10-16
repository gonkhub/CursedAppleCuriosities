using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// A hero's character-select entrance, like the vanilla survivors' lobby intros. Sits on the display prefab's root
    /// and runs once each time the hero is picked (the lobby makes a new display every time). The hero's intro moves
    /// the whole display (Deadlock model, hidden stand-in and item displays together), so it also plays on the
    /// stand-in for players without Deadlock; animation states only play when the Deadlock model is there.
    /// </summary>
    public abstract class DeadlockDisplayIntro : MonoBehaviour
    {
        /// <summary>Where the display was put on its pedestal.</summary>
        protected Vector3 Home { get; private set; }

        /// <summary>How the display was turned on its pedestal (facing the lobby camera).</summary>
        protected Quaternion HomeRotation { get; private set; }

        /// <summary>Metres to world units: the lobby display can be scaled.</summary>
        protected float Scale { get; private set; } = 1f;

        private DeadlockModelSwap? _swap;
        private readonly List<GameObject> _owned = new List<GameObject>();

        protected abstract IEnumerator Run();

        private void Start()
        {
            Home = transform.position;
            HomeRotation = transform.rotation;
            Scale = Mathf.Max(0.01f, transform.lossyScale.y);
            _swap = GetComponentInChildren<DeadlockModelSwap>(true);
            StartCoroutine(Owned(RunThenSpeak()));
        }

        /// <summary>
        /// Runs the intro (and any IEnumerator it yields) with this display as DeadlockSounds.Owner, so every sound it starts, the
        /// select line included, can be silenced the moment the hero is clicked off (user 3.0).
        /// </summary>
        private IEnumerator Owned(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                object? before = DeadlockSounds.Owner;
                bool more;
                DeadlockSounds.Owner = this;
                try { more = top.MoveNext(); }
                finally { DeadlockSounds.Owner = before; }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return top.Current;
            }
        }

        /// <summary>Something the intro made outside the display (a swarm, a projectile, an effect): removed with it if the hero is clicked off.</summary>
        protected T Own<T>(T obj) where T : Object
        {
            if (obj is GameObject go) _owned.Add(go);
            else if (obj is Component c && c != null) _owned.Add(c.gameObject);
            return obj;
        }

        /// <summary>
        /// A vanilla effect for the intro: an unpooled copy given its EffectData (no networking in the lobby), owned by the intro.
        /// Use instead of EffectManager.SpawnEffect, whose pooled instances can't be taken back.
        /// </summary>
        protected void Effect(GameObject? prefab, EffectData data)
        {
            if (prefab == null) return;
            var holder = new GameObject("CursedAppleIntroEffectHolder");
            holder.SetActive(false);
            GameObject fx = Instantiate(prefab, data.origin, data.rotation, holder.transform);
            foreach (EffectManagerHelper helper in fx.GetComponentsInChildren<EffectManagerHelper>(true)) DestroyImmediate(helper);
            if (fx.TryGetComponent(out EffectComponent ec)) ec.effectData = data;
            fx.transform.SetParent(null, true);
            Destroy(holder);
            Own(fx);
            Destroy(fx, 8f); // in case it has no timer of its own
        }

        /// <summary>Clicked off (the lobby destroys or hides the display): silence the hero and clear what the intro left around.</summary>
        private void OnDisable()
        {
            DeadlockSounds.StopOwned(this);
            if (_swap != null) DeadlockSounds.StopOwned(_swap);
            foreach (GameObject go in _owned) if (go != null) Destroy(go);
            _owned.Clear();
        }

        /// <summary>The select line waits for the intro: it plays where the hero ends up, close to the lobby camera.</summary>
        private IEnumerator RunThenSpeak()
        {
            yield return Run();
            transform.SetPositionAndRotation(Home, HomeRotation);
            _swap?.PlaySelectVoice(Home);
        }

        /// <summary>Put the display this many metres above its pedestal.</summary>
        protected void Lift(float metres) => Move(Vector3.up * metres);

        /// <summary>Put the display this far (metres, world directions) from its pedestal.</summary>
        protected void Move(Vector3 metres) => transform.position = Home + metres * Scale;

        /// <summary>Turn the display this many degrees from facing the camera (positive = clockwise from above).</summary>
        protected void Face(float yaw) => transform.rotation = HomeRotation * Quaternion.Euler(0f, yaw, 0f);

        /// <summary>Play one of the hero's animation states (nothing on the stand-in).</summary>
        protected void Play(string state) => _swap?.Animator?.Play(state);

        /// <summary>Play a full-body state with a crossfade this many seconds long.</summary>
        protected void Play(string state, float blend) => _swap?.Animator?.Play(state, blend);

        /// <summary>The animation state playing ("" on the stand-in, or while the clips are still loading).</summary>
        protected string Current => _swap?.Animator?.Current ?? "";
    }
}
