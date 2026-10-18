using System.Collections;
using System.Reflection;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// Rose Buckler on Deadlock heroes (user 3.0: it did nothing). Vanilla gives its armor while sprinting, but a Deadlock hero only
    /// sprints out of combat and stops the moment they're hit, so it never counted when it mattered. On them it counts while they
    /// travel faster than their walk speed (dashing, sliding, launched, falling, or a timed speed boost such as Victor's Jumpstart:
    /// user 3.0, so movement abilities pay off in a fight). Walk speed includes permanent speed items (user: those must not count),
    /// so it is their move speed as last seen with no temporary buff or debuff on them. Judged from how far the body
    /// actually travels (positions are synced), so the host, which applies armor, sees it too.
    /// </summary>
    public class DeadlockRoseBuckler : MonoBehaviour
    {
        private const float OverWalk = 1.05f;      // a little over walk speed, so walking itself (and its jitter) never counts
        private const float ArmorPerStack = 30f;   // vanilla's

        private CharacterBody _body = null!;
        private Vector3 _last;
        private float _slow, _walk;
        // The server's own list of timed buffs (private; clients only get the counts). Damage, and so armor, is the server's.
        private static readonly FieldInfo? TimedBuffs = typeof(CharacterBody).GetField("timedBuffs", BindingFlags.NonPublic | BindingFlags.Instance);
        public bool Moving; // travelling faster than walk speed

        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RecalculateStatsAPI.GetStatCoefficients += (body, args) =>
            {
                if (body.isSprinting || body.inventory == null) return; // sprinting: vanilla already counts it
                int n = body.inventory.GetItemCountEffective(RoR2Content.Items.SprintArmor);
                if (n > 0 && body.TryGetComponent(out DeadlockRoseBuckler rb) && rb.Moving) args.armorAdd += ArmorPerStack * n;
            };
        }

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
            _last = transform.position;
        }

        /// <summary>Any timed buff or debuff on them, item cooldowns aside (only the server keeps the list: elsewhere, never).</summary>
        private bool Temporary()
        {
            if (TimedBuffs?.GetValue(_body) is not IList list) return false;
            foreach (object o in list)
                if (o is CharacterBody.TimedBuff t && BuffCatalog.GetBuffDef(t.buffIndex) is BuffDef def && !def.isCooldown) return true;
            return false;
        }

        private void FixedUpdate()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _last;
            _last = p;
            // Walk speed with their items: refreshed whenever nothing temporary is on them (moveSpeed carries the sprint
            // multiplier while sprinting, so that comes back out). While a timed boost or slow runs, the last calm value holds.
            if (_walk <= 0f || !Temporary())
                _walk = _body.moveSpeed / (_body.isSprinting ? Mathf.Max(1f, _body.sprintingSpeedMultiplier) : 1f);
            bool fast = d.magnitude / Time.fixedDeltaTime > _walk * OverWalk;
            // A short grace so a single still frame (a turn, a landing) doesn't flicker the armor off and on.
            _slow = fast ? 0f : _slow + Time.fixedDeltaTime;
            bool moving = fast || _slow < 0.25f;
            if (moving == Moving) return;
            Moving = moving;
            if (_body.inventory != null && _body.inventory.GetItemCountEffective(RoR2Content.Items.SprintArmor) > 0) _body.MarkAllStatsDirty();
        }
    }
}
