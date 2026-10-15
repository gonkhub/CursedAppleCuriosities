using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Apollo's character-select entrance (user 2.5): Disengaging Sigil's backflip (ability_sigil_jump) carries him back onto the
    /// pedestal from in front of it, then he flourishes his blade with Riposte's stance (ability_riposte) and settles.
    /// </summary>
    public class ApolloDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 5f; // metres in front of the pedestal (toward the camera)
        private const float Flip = 0.7f;        // seconds in the air
        private const float Height = 2.2f;      // metres at the top of the arc

        protected override IEnumerator Run()
        {
            Vector3 front = HomeRotation * Vector3.forward; // the display faces the camera
            Move(front * StartDistance);
            yield return null; // the Deadlock model attaches on its first frame
            Move(front * StartDistance);
            Play("sigil_jump");
            DeadlockSounds.Play("apollo_sigil_cast", Home);
            for (float t = 0f; t < Flip; t += Time.deltaTime)
            {
                float s = t / Flip;
                Move(front * (StartDistance * (1f - Mathf.SmoothStep(0f, 1f, s))) + Vector3.up * (Height * 4f * s * (1f - s)));
                yield return null;
            }
            Move(Vector3.zero);
            yield return new WaitForSeconds(0.15f);

            Play("riposte", 0.15f);
            DeadlockSounds.Play("apollo_riposte_cast", Home);
            yield return new WaitForSeconds(1.1f);
            Play("idle", 0.3f);
            yield return new WaitForSeconds(0.3f);
        }
    }
}
