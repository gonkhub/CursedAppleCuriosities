using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor's character-select entrance (user: Shocking Reanimation): he lies dead on the pedestal while green sparks
    /// crackle over him, then the charge takes and he gets up in the reanimation blast, as in a run.
    /// </summary>
    public class VictorDisplayIntro : DeadlockDisplayIntro
    {
        private const float Down = 1.4f;  // seconds lying still before he comes back (shorter than the 3 s in a run)
        private const int Sparks = 3;     // zaps over his body while he lies there

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            Play("revive_hold");
            DeadlockSounds.Play("victor_revive_start", Home);
            float nextSpark = 0.2f;
            int sparks = 0;
            for (float t = 0f; t < Down; t += Time.deltaTime)
            {
                if (Current != "revive_hold") Play("revive_hold"); // clips that finished loading mid-intro
                if (sparks < Sparks && t >= nextSpark)
                {
                    sparks++;
                    nextSpark += (Down - 0.3f) / Sparks;
                    Spark();
                }
                yield return null;
            }

            Play("revive_up");
            DeadlockSounds.Play("victor_revive_blast", Home);
            if (VictorSurvivor.BlastEffect != null)
                EffectManager.SpawnEffect(VictorSurvivor.BlastEffect, new EffectData { origin = Home, scale = 3f * Scale }, false);
        }

        /// <summary>A green zap somewhere over his body.</summary>
        private void Spark()
        {
            if (VictorSurvivor.ZapEffect == null) return;
            Vector2 d = Random.insideUnitCircle * 0.8f;
            Vector3 at = Home + new Vector3(d.x, 0.3f, d.y) * Scale;
            EffectManager.SpawnEffect(VictorSurvivor.ZapEffect, new EffectData { origin = at, scale = Scale }, false);
        }
    }
}
