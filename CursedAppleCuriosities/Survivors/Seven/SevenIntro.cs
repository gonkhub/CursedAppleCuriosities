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
    /// Deadlock's float spins his pelvis (storm start 36 -> 93 deg, the loop on round, the end clip -75 -> -9 in 0.4 s), which
    /// read as an awkward spin with his legs pointing off as he landed (user): the display counter-turns to keep his hips
    /// square to the camera the whole way down and through the landing, then eases back.
    /// </summary>
    public class SevenDisplayIntro : DeadlockDisplayIntro
    {
        private const float StartHeight = 7f;   // metres: just above the top of the lobby camera
        private const float Descent = 2.6f;     // seconds to the ground, slowing as he arrives
        private const int Bolts = 5;            // lightning strikes around him on the way down
        private const float BoltRingMin = 2f, BoltRingMax = 4.5f; // metres from the pedestal
        private const float Settle = 0.7f;      // seconds after touching down that his hips are still held to the camera
        private const float Ease = 0.4f;        // then seconds to hand the turn back

        private Transform? _hipL, _hipR;
        private float _face;

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
                HipsToCamera();
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
            for (float t = 0f; t < Settle; t += Time.deltaTime) { HipsToCamera(); yield return null; }
            float from = _face; // already within +-180: the short way back (the long way was a whole extra spin, playtest)
            for (float t = 0f; t < Ease; t += Time.deltaTime) { _face = Mathf.LerpAngle(from, 0f, Mathf.SmoothStep(0f, 1f, t / Ease)); Face(_face); yield return null; }
            Face(0f);
        }

        /// <summary>Turn the display so his hip line (left to right thigh) runs across the screen, as it does standing.</summary>
        private void HipsToCamera()
        {
            if (_hipL == null || _hipR == null)
            {
                GameObject? hero = GetComponentInChildren<DeadlockModelSwap>(true)?.HeroObject;
                if (hero == null) return;
                foreach (Transform t in hero.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "leg_upper_L") _hipL = t;
                    else if (t.name == "leg_upper_R") _hipR = t;
                }
                if (_hipL == null || _hipR == null) return;
            }
            Vector3 right = _hipR.position - _hipL.position;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) return;
            float error = Vector3.SignedAngle(right, HomeRotation * Vector3.right, Vector3.up);
            // Half the error a frame (steady, no jitter from the one-frame-old pose), kept within +-180: his float turns
            // one way for ~200 deg, and an unwrapped total eased back the long way round (playtest).
            _face = Mathf.DeltaAngle(0f, _face + error * 0.5f);
            Face(_face);
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
