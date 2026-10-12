using System.Collections;
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

        protected abstract IEnumerator Run();

        private void Start()
        {
            Home = transform.position;
            HomeRotation = transform.rotation;
            Scale = Mathf.Max(0.01f, transform.lossyScale.y);
            _swap = GetComponentInChildren<DeadlockModelSwap>(true);
            StartCoroutine(RunThenSpeak());
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

        /// <summary>The animation state playing ("" on the stand-in, or while the clips are still loading).</summary>
        protected string Current => _swap?.Animator?.Current ?? "";
    }
}
