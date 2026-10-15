using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Ivy's character-select entrance (user 2.5): she drops onto the pedestal from above in Stone Form (Deadlock's
    /// stone_form_cast_in_air), lands with the slam, then breaks out to her idle.
    /// </summary>
    public class IvyDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartHeight = 9f; // metres above the pedestal
        private const float Fall = 0.4f;      // seconds: a stone drop

        protected override IEnumerator Run()
        {
            Lift(StartHeight);
            yield return null; // the Deadlock model attaches on its first frame
            Lift(StartHeight);
            Play("stone_air");
            DeadlockSounds.Play("ivy_stone_cast", Home);
            yield return new WaitForSeconds(0.25f);
            for (float t = 0f; t < Fall; t += Time.deltaTime)
            {
                float s = t / Fall;
                Lift(StartHeight * (1f - s * s)); // accelerating, like a falling statue
                yield return null;
            }
            Lift(0f);
            DeadlockSounds.Play("ivy_stone_impact", Home);
            yield return new WaitForSeconds(0.7f);
            Play("idle", 0.3f);
            yield return new WaitForSeconds(0.3f);
        }
    }
}
