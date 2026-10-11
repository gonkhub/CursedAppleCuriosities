using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// Deadlock heroes' slam skills (Abrams' Seismic Impact, Lash's Ground Strike) drive them into the ground far faster
    /// than a fall; the landing must not count as one. Fall damage is dealt by the server when the landing is reported,
    /// and entity states run OnEnter on every copy of the body, so each slam state opens a short window everywhere and
    /// the server skips fall damage inside it.
    /// </summary>
    public class DeadlockFallGuard : MonoBehaviour
    {
        private float _until;

        /// <summary>Open (or extend) the window. A landing state passes landed: true to END it this long after impact,
        /// so a long dive window doesn't leave the hero fall-proof for the next ledge.</summary>
        public static void Protect(GameObject body, float seconds, bool landed = false)
        {
            var guard = body.GetComponent<DeadlockFallGuard>() ?? body.AddComponent<DeadlockFallGuard>();
            guard._until = landed ? Time.time + seconds : Mathf.Max(guard._until, Time.time + seconds);
        }

        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.GlobalEventManager.OnCharacterHitGroundServer += (orig, self, body, info) =>
            {
                var guard = body != null ? body.GetComponent<DeadlockFallGuard>() : null;
                if (guard != null && Time.time < guard._until)
                    info.velocity = Vector3.zero; // no impact speed, no fall damage (other landing effects still run)
                orig(self, body, info);
            };
        }
    }
}
