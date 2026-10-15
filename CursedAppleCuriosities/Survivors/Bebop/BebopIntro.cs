using System.Collections;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// Bebop's character-select entrance (user 2.5): he charges Hyper Beam (hyperbeam_charge), fires it and sweeps it sideways across
    /// the lobby, turned away from the camera so it never fills the screen, then winds down (hyperbeam_fire) and settles to his idle.
    /// </summary>
    public class BebopDisplayIntro : DeadlockDisplayIntro
    {
        private const float Charge = 1f;                    // Deadlock's charge time
        private const float Sweep = 2.2f;                   // seconds of beam
        private const float FromYaw = -70f, ToYaw = -290f;  // screen left, round behind him, to screen right
        private const float Length = 40f, Width = 0.9f;     // metres

        private LineRenderer? _beam;

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            Transform? muzzle = null;
            for (float t = 0f; t < 3f && (muzzle = FindMuzzle()) == null; t += Time.deltaTime) yield return null;

            // Turn to the start of the sweep while it charges.
            Play("hyper_charge");
            DeadlockSounds.Play("bebop_hyper_prep", Home);
            DeadlockSounds.Play("bebop_hyper_prep_jazz", Home);
            for (float t = 0f; t < Charge; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(0f, FromYaw, t / Charge));
                yield return null;
            }

            DeadlockSounds.Play("bebop_hyper_launch", Home);
            DeadlockSounds.Play("bebop_hyper_launch_jazz", Home);
            DeadlockVoice? loop = DeadlockSounds.Loop("bebop_hyper_loop", gameObject, Sweep);
            _beam = Beam();
            for (float t = 0f; t < Sweep; t += Time.deltaTime)
            {
                Face(Mathf.Lerp(FromYaw, ToYaw, Mathf.SmoothStep(0f, 1f, t / Sweep)));
                Draw(muzzle);
                yield return null;
            }
            DeadlockSounds.Stop(loop);
            Destroy(_beam.gameObject);
            _beam = null;

            Play("hyper_end", 0.15f);
            DeadlockSounds.Play("bebop_hyper_end", Home);
            DeadlockSounds.Play("bebop_hyper_end_jazz", Home);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(ToYaw, -360f, t / 0.6f)); // back round to face the camera
                yield return null;
            }
            Face(0f);
            Play("idle", 0.3f);
            yield return new WaitForSeconds(0.4f);
        }

        private Transform? FindMuzzle()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == BebopData.MuzzleBone) return t;
            return null;
        }

        private static LineRenderer Beam()
        {
            var go = new GameObject("BebopIntroBeam");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            LineRenderer? source = BebopSurvivor.HyperBeamPrefab != null ? BebopSurvivor.HyperBeamPrefab.GetComponentInChildren<LineRenderer>(true) : null;
            line.material = source != null && source.sharedMaterial != null ? source.sharedMaterial : BebopSurvivor.ChainMaterial;
            line.colorGradient = source != null ? source.colorGradient : new Gradient();
            line.textureMode = LineTextureMode.Tile;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private void Draw(Transform? muzzle)
        {
            if (_beam == null) return;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.6f * Scale;
            _beam.SetPosition(0, from);
            _beam.SetPosition(1, from + transform.forward * Length * Scale);
            _beam.widthMultiplier = Width * Scale * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 20f, 0f));
        }

        private void OnDestroy()
        {
            if (_beam != null) Destroy(_beam.gameObject);
        }
    }
}
