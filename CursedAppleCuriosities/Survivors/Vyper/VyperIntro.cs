using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Vyper's character-select entrance: she Slithers in from off the side of the screen along the floor, slowing onto
    /// the pedestal, then gets up turning to face the camera.
    /// </summary>
    public class VyperDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 10f; // metres to the side: off the edge of the lobby camera
        private const float Slide = 0.8f;        // seconds to the pedestal, slowing down
        private const float TurnBack = 0.35f;    // seconds to face the camera while getting up

        protected override IEnumerator Run()
        {
            // She comes from the display's right (screen left), sliding head first toward its left.
            Vector3 side = HomeRotation * Vector3.right;
            Move(side * StartDistance);
            Face(-90f);
            yield return null; // the Deadlock model attaches on its first frame
            Move(side * StartDistance);
            Face(-90f);
            yield return new WaitForSeconds(0.1f);

            Play("slide_start");
            DeadlockSounds.Play("vyper_slide", Home);
            DeadlockVoice? loop = DeadlockSounds.Loop("vyper_slide_loop", gameObject, Slide + 0.3f);
            for (float t = 0f; t < Slide; t += Time.deltaTime)
            {
                float u = t / Slide;
                Move(side * (StartDistance * (1f - u) * (1f - u))); // fast, then easing to a stop
                if (Current != "slide_start" && Current != "slide") Play("slide"); // clips that finished loading mid-slide
                yield return null;
            }

            Move(Vector3.zero);
            DeadlockSounds.Stop(loop);
            Play("slide_getup");
            DeadlockSounds.Play("vyper_slide_exit", Home);
            for (float t = 0f; t < TurnBack; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(-90f, 0f, t / TurnBack));
                yield return null;
            }
        }
    }
}
