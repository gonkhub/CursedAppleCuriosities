using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Grey Talon's character-select entrance (user): his Guided Owl dives in from high off to one side, speeding up, and
    /// crashes into the pedestal in its green blast; Grey Talon stands in its place.
    /// </summary>
    public class GreyTalonDisplayIntro : DeadlockDisplayIntro
    {
        private const float Flight = 1.1f;        // seconds from the edge of the screen to the pedestal
        private const float BlastScale = 4f;      // metres: the lobby blast, smaller than the real 12 m one
        // Where the owl starts, from the pedestal facing the camera: left, high, a little toward the camera.
        private static readonly Vector3 From = new Vector3(-7f, 8f, 5f);

        protected override IEnumerator Run()
        {
            Lift(-50f); // out of sight until the owl lands
            yield return null; // the Deadlock model attaches on its first frame
            Lift(-50f);

            var owl = new GameObject("CursedAppleGreyTalonIntroOwl");
            owl.transform.localScale = Vector3.one * Scale;
            GreyTalonOwlVisual.Build(owl.transform);
            DeadlockVoice? hum = DeadlockSounds.Loop("greytalon_owl_loop", owl, Flight + 0.3f);
            Vector3 start = Home + HomeRotation * From * Scale;
            Vector3 end = Home + Vector3.up * (0.6f * Scale);
            Vector3 last = start;
            for (float t = 0f; t < Flight; t += Time.deltaTime)
            {
                float u = t / Flight;
                // Diving: it speeds up as it comes, and arcs down onto the pedestal.
                float e = u * u;
                Vector3 at = Vector3.Lerp(start, end, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 1.2f * Scale);
                owl.transform.position = at;
                if ((at - last).sqrMagnitude > 1e-6f) owl.transform.rotation = Quaternion.LookRotation(at - last);
                last = at;
                yield return null;
            }

            DeadlockSounds.Stop(hum);
            Destroy(owl);
            var blast = new EffectData { origin = end, scale = BlastScale * Scale };
            if (GreyTalonSurvivor.OwlBlastEffect != null) EffectManager.SpawnEffect(GreyTalonSurvivor.OwlBlastEffect, blast, false);
            if (GreyTalonSurvivor.OwlBlastWave != null) EffectManager.SpawnEffect(GreyTalonSurvivor.OwlBlastWave, blast, false);
            DeadlockSounds.Play("greytalon_owl_explode", Home);
            Lift(0f);
            Play("owl_outro"); // standing up out of the blast
            yield return new WaitForSeconds(0.6f);
        }
    }
}
