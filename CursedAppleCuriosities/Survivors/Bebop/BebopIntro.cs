using System.Collections;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// Bebop's character-select entrance (user 2.5): he charges Hyper Beam (hyperbeam_charge, held on its last frame), fires it in his
    /// firing stance (hyperbeam_fire_idle) and sweeps it across the screen in front of him, angled down onto the floor before the
    /// pedestal, then winds down (hyperbeam_fire) and settles to his idle. No jazz sting at the end (user 2.5).
    /// </summary>
    public class BebopDisplayIntro : DeadlockDisplayIntro
    {
        private const float Charge = 1f;                  // Deadlock's charge time
        private const float Sweep = 2.4f;                 // seconds of beam
        private const float FromYaw = -60f, ToYaw = 60f;  // across the screen, through straight ahead (toward the camera)
        private const float Pitch = 28f;                  // degrees down: onto the floor in front of him, not into the camera
        private const float Length = 40f, Width = 0.9f;   // metres

        private LineRenderer? _beam, _glow;

        protected override IEnumerator Run()
        {
            yield return null; // the Deadlock model attaches on its first frame
            Transform? muzzle = null;
            for (float t = 0f; t < 3f && (muzzle = FindMuzzle()) == null; t += Time.deltaTime) yield return null;

            Play("hyper_charge");
            DeadlockSounds.Play("bebop_hyper_prep", Home);
            DeadlockSounds.Play("bebop_hyper_prep_jazz", Home);
            for (float t = 0f; t < Charge; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(0f, FromYaw, t / Charge));
                yield return null;
            }

            Play("hyper_fire", 0.1f);
            DeadlockSounds.Play("bebop_hyper_launch", Home);
            DeadlockVoice? loop = DeadlockSounds.Loop("bebop_hyper_loop", gameObject, Sweep);
            Build();
            for (float t = 0f; t < Sweep; t += Time.deltaTime)
            {
                Face(Mathf.Lerp(FromYaw, ToYaw, Mathf.SmoothStep(0f, 1f, t / Sweep)));
                Draw(muzzle);
                yield return null;
            }
            DeadlockSounds.Stop(loop);
            Clear();

            Play("hyper_end", 0.15f);
            DeadlockSounds.Play("bebop_hyper_end", Home);
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(ToYaw, 0f, t / 0.5f));
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

        private void Build()
        {
            LineRenderer? source = BebopSurvivor.HyperBeamPrefab != null ? BebopSurvivor.HyperBeamPrefab.GetComponentInChildren<LineRenderer>(true) : null;
            _beam = Line("BebopIntroBeam", source != null && source.sharedMaterial != null ? source.sharedMaterial : BebopSurvivor.ChainMaterial);
            _beam.colorGradient = source != null ? source.colorGradient : new Gradient();
            _beam.textureMode = LineTextureMode.Tile;
            // A solid red glow around it, so it reads end to end.
            _glow = Line("BebopIntroBeamGlow", CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.LineMaterial());
            _glow.startColor = _glow.endColor = new Color(1f, 0.15f, 0.08f, 0.35f);
        }

        private static LineRenderer Line(string name, Material? material)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.material = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private void Draw(Transform? muzzle)
        {
            if (_beam == null || _glow == null) return;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.6f * Scale;
            Vector3 dir = Quaternion.AngleAxis(Pitch, transform.right) * transform.forward;
            float length = Length * Scale;
            Vector3 to = Physics.Raycast(from, dir, out RaycastHit hit, length, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : from + dir * length;
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 20f, 0f);
            foreach (LineRenderer l in new[] { _beam, _glow })
            {
                l.SetPosition(0, from);
                l.SetPosition(1, to);
            }
            _beam.widthMultiplier = Width * Scale * flicker;
            _glow.widthMultiplier = Width * 1.8f * Scale * flicker;
        }

        private void Clear()
        {
            if (_beam != null) Destroy(_beam.gameObject);
            if (_glow != null) Destroy(_glow.gameObject);
            _beam = _glow = null;
        }

        private void OnDestroy() => Clear();
    }
}
