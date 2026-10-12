using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Seven
{
    /// <summary>
    /// Seven's character-select entrance: he sinks onto the pedestal from above the screen in his Storm Cloud float,
    /// lightning striking the floor around him on the way down, and ends the storm as he touches down. While he floats he
    /// wears Storm Cloud's look, as in a run: only his glowing orange skeleton shows (SevenStormLook).
    /// </summary>
    public class SevenDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartHeight = 7f;   // metres: just above the top of the lobby camera
        private const float Descent = 2.6f;     // seconds to the ground, slowing as he arrives
        private const int Bolts = 5;            // lightning strikes around him on the way down
        private const float BoltRingMin = 2f, BoltRingMax = 4.5f; // metres from the pedestal

        protected override IEnumerator Run()
        {
            Lift(StartHeight);
            yield return null; // the Deadlock model attaches on its first frame
            Lift(StartHeight);

            SevenStormLook? look = GetComponentInChildren<SevenStormLook>(true);
            if (look != null) look.ForceStorm = true;
            Play("storm_start");
            DeadlockSounds.Play("seven_storm_cast", Home);
            DeadlockVoice? loop = DeadlockSounds.Loop("seven_storm_loop", gameObject, Descent + 0.5f);
            float nextBolt = 0.3f;
            int bolts = 0;
            for (float t = 0f; t < Descent; t += Time.deltaTime)
            {
                float u = t / Descent;
                Lift(StartHeight * (1f - u) * (1f - u)); // easing in to land
                if (Current != "storm_start" && Current != "storm") Play("storm"); // clips that finished loading mid-descent
                if (bolts < Bolts && t >= nextBolt)
                {
                    bolts++;
                    nextBolt += (Descent - 0.5f) / Bolts;
                    Bolt();
                }
                yield return null;
            }

            Lift(0f);
            DeadlockSounds.Stop(loop);
            if (look != null) look.ForceStorm = false;
            Play("storm_end");
            DeadlockSounds.Play("seven_storm_end", Home);
        }

        /// <summary>A lightning strike on the floor somewhere around the pedestal.</summary>
        private void Bolt()
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float r = Random.Range(BoltRingMin, BoltRingMax) * Scale;
            Vector3 at = Home + new Vector3(dir.x, 0f, dir.y) * r;
            if (SevenSurvivor.BoltEffect != null)
                EffectManager.SpawnEffect(SevenSurvivor.BoltEffect, new EffectData { origin = at, scale = Scale }, false);
            DeadlockSounds.Play("seven_strike_explode", at);
        }
    }
}
