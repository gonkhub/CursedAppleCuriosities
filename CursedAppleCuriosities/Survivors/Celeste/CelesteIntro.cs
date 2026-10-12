using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Celeste's character-select entrance: Deadlock's own hero-select flourish (ui_hero_select) with Dazzling Trick's cast
    /// sound, then her select line.
    /// </summary>
    public class CelesteDisplayIntro : DeadlockDisplayIntro
    {
        private const float Show = 2.2f; // seconds of the flourish before she speaks

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            yield return null;
            Play("select");
            DeadlockSounds.Play("celeste_prism_cast", Home);
            for (float t = 0f; t < Show; t += Time.deltaTime)
            {
                if (t < 0.5f && Current != "select") Play("select"); // clips that finished loading just now
                yield return null;
            }
        }
    }
}
