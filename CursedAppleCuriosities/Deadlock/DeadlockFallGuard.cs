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
        private bool _oneLanding;
        private System.Action? _onLand;

        /// <summary>Open (or extend) the window. A landing state passes landed: true to END it this long after impact,
        /// so a long dive window doesn't leave the hero fall-proof for the next ledge.</summary>
        public static void Protect(GameObject body, float seconds, bool landed = false)
        {
            var guard = body.GetComponent<DeadlockFallGuard>() ?? body.AddComponent<DeadlockFallGuard>();
            guard._until = landed ? Time.time + seconds : Mathf.Max(guard._until, Time.time + seconds);
        }

        /// <summary>Server: no fall damage on the next landing (within maxSeconds); the window closes right after it.
        /// For launches (Majestic Leap) where the landing, not a slam state, ends the protection.</summary>
        public static void ProtectNextLanding(GameObject body, float maxSeconds, System.Action? onLand = null)
        {
            var guard = body.GetComponent<DeadlockFallGuard>() ?? body.AddComponent<DeadlockFallGuard>();
            guard._until = Mathf.Max(guard._until, Time.time + maxSeconds);
            guard._oneLanding = true;
            guard._onLand = onLand;
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
                {
                    info.velocity = Vector3.zero; // no impact speed, no fall damage (other landing effects still run)
                    if (guard._oneLanding)
                    {
                        guard._oneLanding = false;
                        guard._until = Time.time + 0.2f;
                        System.Action? land = guard._onLand;
                        guard._onLand = null;
                        land?.Invoke();
                    }
                }
                orig(self, body, info);
            };
        }
    }
}
