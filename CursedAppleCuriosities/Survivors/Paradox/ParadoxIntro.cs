using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CursedAppleCuriosities.Deadlock;
using RoR2;
using RoR2.SurvivorMannequins;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Paradox
{
    /// <summary>
    /// Paradox's character-select entrance (user): the survivor picked before her stays on the pedestal; from off screen she
    /// fires her swap shot at them and they trade places, passing each other, so she ends up on the pedestal and they leave.
    /// </summary>
    public class ParadoxDisplayIntro : DeadlockDisplayIntro
    {
        private const float Away = 11f;     // metres to screen right where she starts
        private const float Shot = 0.25f;   // seconds for the shot to cross
        private const float Swap = 0.85f;   // seconds for the swap itself

        /// <summary>The display prefab of the survivor shown in this lobby slot before Paradox was picked (set by the hook below).</summary>
        private static GameObject? _previous;
        private static readonly FieldInfo? CurrentDef = typeof(SurvivorMannequinSlotController).GetField("_currentSurvivorDef", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Dictionary<SurvivorMannequinSlotController, SurvivorDef?> LastShown = new Dictionary<SurvivorMannequinSlotController, SurvivorDef?>();

        public static void Hook()
        {
            On.RoR2.SurvivorMannequins.SurvivorMannequinSlotController.RebuildMannequinInstance += (orig, self) =>
            {
                LastShown.TryGetValue(self, out SurvivorDef? before);
                SurvivorDef? now = CurrentDef?.GetValue(self) as SurvivorDef;
                _previous = now == ParadoxSurvivor.Survivor && before != null && before != now ? before.displayPrefab : null;
                orig(self);
                LastShown[self] = now;
            };
        }

        protected override IEnumerator Run()
        {
            GameObject? previousPrefab = _previous;
            _previous = null;
            Vector3 side = HomeRotation * Vector3.left; // the display faces the camera: its left is screen right
            Move(side * Away);
            Face(90f);
            yield return null; // the Deadlock model attaches on its first frame
            Move(side * Away);
            Face(90f);

            GameObject? stayer = previousPrefab != null ? Own(Leftover(previousPrefab)) : null;
            yield return new WaitForSeconds(0.25f);

            // The shot: her swap projectile's glow, from her to the pedestal.
            Play("swap_cast");
            DeadlockSounds.Play("paradox_swap_cast", Home);
            GameObject? shot = ShotVisual();
            if (shot != null) Own(shot);
            for (float t = 0f; t < Shot; t += Time.deltaTime)
            {
                if (shot != null) shot.transform.position = Home + (side * Away * (1f - t / Shot) + Vector3.up * 1.2f) * Scale;
                yield return null;
            }
            if (shot != null) Destroy(shot);
            Own(Flash(Home + Vector3.up * Scale));
            Own(Flash(Home + (side * Away + Vector3.up) * Scale));
            DeadlockSounds.Play("paradox_swap_go", Home);

            // The swap: both at once on mirrored arcs (ParadoxSwap.PathAt), passing each other.
            Play("swap");
            Vector3 from = side * Away;
            for (float t = 0f; t < Swap; t += Time.deltaTime)
            {
                Move(ParadoxSwap.PathAt(from, Vector3.zero, t, Swap, 1f));
                if (stayer != null) stayer.transform.position = Home + ParadoxSwap.PathAt(Vector3.zero, from, t, Swap, -1f) * Scale;
                yield return null;
            }
            Move(Vector3.zero);
            if (stayer != null) Destroy(stayer);
            Play("swap_end");
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                Face(Mathf.SmoothStep(90f, 0f, t / 0.3f));
                yield return null;
            }
            DeadlockSounds.Play("paradox_swap_end", Home);
        }

        /// <summary>A quiet copy of the last pick standing on the pedestal (its own intro and select line switched off).</summary>
        private GameObject Leftover(GameObject prefab)
        {
            var holder = new GameObject("ParadoxIntroHolder");
            holder.SetActive(false);
            GameObject copy = Instantiate(prefab, holder.transform, false);
            foreach (DeadlockDisplayIntro intro in copy.GetComponentsInChildren<DeadlockDisplayIntro>(true)) DestroyImmediate(intro);
            foreach (DeadlockModelSwap swap in copy.GetComponentsInChildren<DeadlockModelSwap>(true)) swap.Silent = true;
            copy.transform.SetParent(transform.parent, false);
            copy.transform.SetPositionAndRotation(Home, HomeRotation);
            copy.transform.localScale = transform.localScale;
            Destroy(holder);
            return copy;
        }

        private static GameObject? ShotVisual()
        {
            var pc = ParadoxSurvivor.SwapProjectile != null ? ParadoxSurvivor.SwapProjectile.GetComponent<RoR2.Projectile.ProjectileController>() : null;
            if (pc == null || pc.ghostPrefab == null) return null;
            var holder = new GameObject("ParadoxIntroShotHolder");
            holder.SetActive(false);
            GameObject ghost = Instantiate(pc.ghostPrefab, holder.transform, false);
            foreach (RoR2.Projectile.ProjectileGhostController g in ghost.GetComponentsInChildren<RoR2.Projectile.ProjectileGhostController>(true)) DestroyImmediate(g);
            ghost.transform.SetParent(null, false);
            Destroy(holder);
            return ghost;
        }

        private static GameObject? Flash(Vector3 at)
        {
            if (ParadoxSurvivor.SwapHitEffect == null) return null;
            GameObject fx = Instantiate(ParadoxSurvivor.SwapHitEffect, at, Quaternion.identity);
            if (fx.TryGetComponent(out EffectComponent ec)) ec.enabled = false; // a plain copy: no EffectData in the lobby
            Destroy(fx, 3f);
            return fx;
        }
    }
}
