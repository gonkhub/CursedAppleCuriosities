using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Vyper
{
    /// <summary>
    /// Vyper's character-select entrance: she Slithers in from the back right of the view (behind where she ends up
    /// facing, a little to screen right: user), along the floor toward the camera, slowing onto the pedestal, then gets
    /// up turning to face the camera.
    /// </summary>
    public class VyperDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 10f; // metres back from the pedestal
        private const float RightShare = 0.5f;   // how far toward screen right the start sits, per metre back
        private const float Slide = 0.8f;        // seconds to the pedestal, slowing down
        private const float TurnBack = 0.35f;    // seconds to face the camera while getting up

        protected override IEnumerator Run()
        {
            // The display faces the camera: its back is away from the view, its left is screen right. She starts back and
            // to screen right and slides head first toward the pedestal, so she faces down her path (yaw from the display).
            Vector3 local = new Vector3(-RightShare, 0f, -1f).normalized;
            Vector3 side = HomeRotation * local;
            float yaw = Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg;
            Move(side * StartDistance);
            Face(yaw);
            yield return null; // the Deadlock model attaches on its first frame
            Move(side * StartDistance);
            Face(yaw);
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
                Face(Mathf.SmoothStep(yaw, 0f, t / TurnBack));
                yield return null;
            }
        }
    }
}
