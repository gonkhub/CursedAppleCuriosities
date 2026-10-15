using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Celeste
{
    /// <summary>
    /// Celeste's character-select entrance (user): she ground-dashes in from the back right of the view, as Vyper slides in,
    /// stopping on the pedestal, then turns to face the camera, reloads as she does in Deadlock (user, 2.0) and says her
    /// select line.
    /// </summary>
    public class CelesteDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 9f;  // metres back from the pedestal
        private const float RightShare = 0.5f;   // how far toward screen right the start sits, per metre back
        private const float Dash = 0.55f;        // seconds to the pedestal: a fast burst, easing to a stop
        private const float Settle = 0.45f;      // the rest of her dash clip on the pedestal before she turns
        private const float TurnBack = 0.35f;    // seconds to face the camera

        protected override IEnumerator Run()
        {
            // The display faces the camera: back is away from the view, its left is screen right. She starts back and to
            // screen right and dashes toward the pedestal, facing down her path (yaw from the display).
            Vector3 local = new Vector3(-RightShare, 0f, -1f).normalized;
            Vector3 side = HomeRotation * local;
            float yaw = Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg;
            Move(side * StartDistance);
            Face(yaw);
            yield return null; // the Deadlock model attaches on its first frame
            Move(side * StartDistance);
            Face(yaw);
            yield return new WaitForSeconds(0.1f);

            Play("dash_ground");
            DeadlockSounds.Play("celeste_dash", Home);
            DeadlockSounds.Play("celeste_vo_effort", Home);
            for (float t = 0f; t < Dash; t += Time.deltaTime)
            {
                float u = t / Dash;
                Move(side * (StartDistance * (1f - u) * (1f - u))); // fast, then easing to a stop
                if (t < 0.2f && Current != "dash_ground") Play("dash_ground"); // clips that finished loading just now
                yield return null;
            }

            Move(Vector3.zero);
            yield return new WaitForSeconds(Settle);
            for (float t = 0f; t < TurnBack; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(yaw, 0f, t / TurnBack));
                yield return null;
            }

            Play("reload");
            DeadlockSounds.Play("celeste_reload", Home);
        }
    }
}
