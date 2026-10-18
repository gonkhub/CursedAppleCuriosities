using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    /// <summary>
    /// Riposte's counter (user 3.0): a parry no longer dashes on its own. It opens a window (RiposteCounterWindow) with one
    /// extra Riposte charge; while it's open a reticle sits on the enemy nearest the crosshair within 50 m, and pressing Riposte
    /// again dashes into the counter on that enemy (the Riposte state reads <see cref="Open"/>). Unused, the charge goes away.
    /// Owner's machine only (movement and targeting are the owner's).
    /// </summary>
    public class ApolloCounter : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private GenericSkill? _slot;
        private bool _pending;
        private float _until;
        private Indicator? _indicator;
        private static GameObject? _red;

        public bool Open => _pending && Time.time < _until;

        private void Awake() => _body = GetComponent<CharacterBody>();

        /// <summary>The enemy the counter would go for now (nearest the crosshair, in reach and in sight).</summary>
        public static CharacterBody? Target(CharacterBody body) =>
            EquipmentTargeting.FindEnemy(body, ApolloData.RiposteCounterRange * MysticExpansion.RadiusMultiplier(body), ApolloData.RiposteCounterConeDeg, _ => true);

        /// <summary>A parry landed: one more Riposte charge for the counter, for a while.</summary>
        public void Arm()
        {
            _slot = _body.skillLocator != null ? _body.skillLocator.utility : null;
            if (_slot == null) return;
            if (!_pending) _slot.AddOneStock();
            _pending = true;
            _until = Time.time + ApolloData.RiposteCounterWindow;
        }

        /// <summary>The counter was spent (its charge with it).</summary>
        public void Use() => _pending = false;

        private void Update()
        {
            if (_pending && Time.time >= _until)
            {
                _pending = false;
                if (_slot != null && _slot.stock > 0) _slot.DeductStock(1);
            }
            Transform? at = null;
            if (Open && _body.hasEffectiveAuthority && _body.isPlayerControlled)
            {
                CharacterBody? t = Target(_body);
                if (t != null) at = t.mainHurtBox != null ? t.mainHurtBox.transform : t.coreTransform;
            }
            if (_indicator == null)
            {
                if (at == null) return;
                _red ??= Kit.Recoloured(EquipmentTargeting.IndicatorPrefab, "CursedAppleApolloCounterIndicator", 0f, effect: false) ?? EquipmentTargeting.IndicatorPrefab;
                _indicator = new Indicator(gameObject, _red);
            }
            _indicator.targetTransform = at;
            _indicator.active = at != null;
        }

        private void OnDestroy() { if (_indicator != null) _indicator.active = false; }
    }
}
