using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// Shiv's character-select entrance (user): he Slice'n'Dices onto the pedestal from screen left, then plays Deadlock's
    /// reload, flicking the break-action shotgun shut.
    /// </summary>
    public class ShivDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 9f; // metres from the pedestal
        private const float Dash = 0.22f;       // seconds: Slice'n'Dice's 61 m/s, a touch slower to read on screen

        protected override IEnumerator Run()
        {
            // The display faces the camera, so its right is screen left: he dashes in from there, facing down his path.
            Vector3 side = HomeRotation * Vector3.right;
            Move(side * StartDistance);
            Face(-90f);
            yield return null; // the Deadlock model attaches on its first frame
            Move(side * StartDistance);
            Face(-90f);
            yield return new WaitForSeconds(0.15f);

            Play("slice");
            DeadlockSounds.Play("shiv_dash_cast", Home);
            for (float t = 0f; t < Dash; t += Time.deltaTime)
            {
                Move(side * (StartDistance * (1f - t / Dash)));
                yield return null;
            }
            Move(Vector3.zero);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(-90f, 0f, t / 0.25f));
                yield return null;
            }
            Face(0f);

            // The reload: break the shotgun open, load, flick it shut.
            Play("reload", 0.15f);
            DeadlockSounds.Play("shiv_reload_start", Home);
            yield return new WaitForSeconds(0.45f);
            DeadlockSounds.Play("shiv_reload_mid", Home);
            yield return new WaitForSeconds(0.45f);
            DeadlockSounds.Play("shiv_reload_end", Home);
            yield return new WaitForSeconds(0.3f);
        }
    }
}
