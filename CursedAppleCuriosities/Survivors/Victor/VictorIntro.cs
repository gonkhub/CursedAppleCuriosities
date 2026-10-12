using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Victor
{
    /// <summary>
    /// Victor's character-select entrance (user: Shocking Reanimation): his body parts float together on the pedestal
    /// (Deadlock's reanimation model, <see cref="VictorReassembly"/>), green zaps crackling over them, then he gets up in
    /// the reanimation blast. Until that model is converted, he lies on the pedestal in his knockdown pose instead.
    /// </summary>
    public class VictorDisplayIntro : DeadlockDisplayIntro
    {
        private const float Down = 3f;    // seconds of reassembly before he gets up (as long as the channel in a run)
        private const int Sparks = 4;     // zaps over him while he comes together

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            yield return null;
            VictorReassembly? parts = VictorReassembly.Spawn(gameObject, Down);
            if (parts == null) Play("revive_hold");
            DeadlockSounds.Play("victor_revive_start", Home);
            float nextSpark = 0.3f;
            int sparks = 0;
            for (float t = 0f; t < Down; t += Time.deltaTime)
            {
                if (parts == null && Current != "revive_hold") Play("revive_hold"); // clips that finished loading mid-intro
                if (sparks < Sparks && t >= nextSpark)
                {
                    sparks++;
                    nextSpark += (Down - 0.5f) / Sparks;
                    Spark();
                }
                yield return null;
            }

            if (parts != null) parts.Finish();
            Play("revive_up");
            DeadlockSounds.Play("victor_revive_blast", Home);
            if (VictorSurvivor.BlastEffect != null)
                EffectManager.SpawnEffect(VictorSurvivor.BlastEffect, new EffectData { origin = Home, scale = 3f * Scale }, false);
        }

        /// <summary>A green zap somewhere over him.</summary>
        private void Spark()
        {
            if (VictorSurvivor.ZapEffect == null) return;
            Vector2 d = Random.insideUnitCircle * 0.8f;
            Vector3 at = Home + new Vector3(d.x, Random.Range(0.3f, 1.8f), d.y) * Scale;
            EffectManager.SpawnEffect(VictorSurvivor.ZapEffect, new EffectData { origin = at, scale = Scale }, false);
        }
    }
}
