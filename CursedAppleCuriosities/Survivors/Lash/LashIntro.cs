using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Lash's character-select entrance: he Ground Strikes onto the pedestal from high above the screen, in his
    /// Ground Strike dive pose, and lands with the leg smash, its impact sound and the slam effect.
    /// </summary>
    public class LashDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartHeight = 30f;  // metres above the pedestal: well off the top of the lobby camera
        private const float Wait = 0.2f;        // out of sight before the dive
        private const float Fall = 0.4f;        // seconds to the ground (~110 m/s at impact, Ground Strike's 90 m/s and then some)
        private const float SlamRadius = 5f;    // the slam effect, smaller than the in-game 10 m: the lobby is close up

        protected override IEnumerator Run()
        {
            Lift(StartHeight);
            yield return null; // the Deadlock model attaches on its first frame
            Lift(StartHeight);
            yield return new WaitForSeconds(Wait);

            Play("gs_drop");
            DeadlockSounds.Play("lash_gs_whoosh", Home);
            for (float t = 0f; t < Fall; t += Time.deltaTime)
            {
                float u = t / Fall;
                Lift(StartHeight * (1f - u * Mathf.Sqrt(u))); // speeding up
                if (Current != "gs_drop" && Current != "gs_dive") Play("gs_dive"); // clips that finished loading mid-fall
                yield return null;
            }

            Lift(0f);
            Play("gs_land");
            DeadlockSounds.Play("lash_gs_impact", Home);
            Effect(LashSurvivor.GroundSlamEffect, new EffectData { origin = Home, scale = SlamRadius * Scale });
        }
    }
}
