using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>
    /// Mina's character-select entrance (user): a swarm of bats flies in from the back of the view onto the pedestal and she
    /// reforms out of it, as Sanguine Retreat ends.
    /// </summary>
    public class MinaDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartDistance = 12f; // metres back and up from the pedestal
        private const float Flight = 1.1f;      // seconds for the swarm to arrive

        protected override IEnumerator Run()
        {
            Vector3 start = HomeRotation * new Vector3(0.5f, 0.35f, -1f).normalized * StartDistance; // behind her, up and to screen left
            Lift(-200f); // she isn't there yet: she is the bats
            yield return null;
            Lift(-200f);

            var path = Own(new GameObject("MinaIntroSwarm")).transform;
            path.position = Home + start * Scale;
            GameObject cloud = Own(MinaBatVisual.Cloud(path, 14)); // its own root: clicked off mid-flight, the bats stayed (user 3.0)
            DeadlockSounds.Play("mina_retreat_start", Home);
            DeadlockVoice? loop = DeadlockSounds.Loop("mina_retreat_loop", path.gameObject, Flight + 0.3f);
            for (float t = 0f; t < Flight; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / Flight);
                // A curve in: swing out to the side and come down onto the pedestal.
                Vector3 bend = HomeRotation * Vector3.right * Mathf.Sin(u * Mathf.PI) * 2.5f;
                path.position = Home + (start * (1f - u) + bend + Vector3.up * 0.2f * (1f - u)) * Scale;
                yield return null;
            }

            DeadlockSounds.Stop(loop);
            DeadlockSounds.Play("mina_retreat_end_final", Home);
            if (MinaSurvivor.BlinkEffect != null)
            {
                GameObject fx = Own(Object.Instantiate(MinaSurvivor.BlinkEffect, Home + Vector3.up * Scale, Quaternion.identity));
                if (fx.TryGetComponent(out EffectComponent ec)) ec.enabled = false; // a plain copy: no EffectData here
                Object.Destroy(fx, 3f);
            }
            Destroy(cloud);
            Destroy(path.gameObject);
            Move(Vector3.zero);
            Play("retreat_end", 0.05f);
            yield return new WaitForSeconds(0.6f);
        }
    }
}
