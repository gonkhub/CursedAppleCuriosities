using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Aimed equipment (Focus Lens, Cultist Sacrifice): pick the enemy nearest the crosshair, and show the local
    /// player which one that is with the same kind of reticle Royal Capacitor uses.
    /// </summary>
    public static class EquipmentTargeting
    {
        public class Rule
        {
            public EquipmentDef? Def;
            public float Range;
            public float Angle;
            public Func<CharacterBody, bool> Allow = _ => true;
        }

        private static readonly List<Rule> _rules = new List<Rule>();
        private static bool _hooked;
        private static GameObject? _indicatorPrefab;

        public static void Register(Rule rule)
        {
            _rules.Add(rule);
            if (_hooked) return;
            _hooked = true;
            On.RoR2.EquipmentSlot.Update += EquipmentSlot_Update;
        }

        /// <summary>
        /// The enemy closest (by angle) to the crosshair within range and the cone, in line of sight. If the cone is
        /// empty, whatever enemy the crosshair itself is on (a fat spherecast), so a target right under it is never missed.
        /// </summary>
        public static CharacterBody? FindEnemy(CharacterBody body, float range, float angle, Func<CharacterBody, bool> allow)
        {
            if (body.inputBank == null || body.teamComponent == null) return null;
            var aim     = body.inputBank.GetAimRay();
            var enemies = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);

            var search = new BullseyeSearch
            {
                teamMaskFilter    = enemies,
                filterByLoS       = true,
                searchOrigin      = aim.origin,
                searchDirection   = aim.direction,
                maxDistanceFilter = range,
                maxAngleFilter    = angle,
                sortMode          = BullseyeSearch.SortMode.Angle,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(body.gameObject);
            var hit = search.GetResults()
                .Select(hb => hb != null && hb.healthComponent != null ? hb.healthComponent.body : null)
                .FirstOrDefault(b => Valid(b, allow));
            if (hit != null) return hit;

            foreach (var rh in Physics.SphereCastAll(aim, 1.5f, range, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide)
                         .OrderBy(r => r.distance))
            {
                var hb = rh.collider != null ? rh.collider.GetComponent<HurtBox>() : null;
                var b  = hb != null && hb.healthComponent != null ? hb.healthComponent.body : null;
                if (b == null || b == body || b.teamComponent == null || !enemies.HasTeam(b.teamComponent.teamIndex)) continue;
                if (Valid(b, allow)) return b;
            }
            return null;
        }

        private static bool Valid(CharacterBody? b, Func<CharacterBody, bool> allow)
            => b != null && b.healthComponent != null && b.healthComponent.alive && allow(b);

        // Local player only: point a reticle at what the held equipment would hit right now.
        private static void EquipmentSlot_Update(On.RoR2.EquipmentSlot.orig_Update orig, EquipmentSlot self)
        {
            orig(self);
            var body = self.characterBody;
            if (body == null || !body.hasEffectiveAuthority || !body.isPlayerControlled) return;

            Rule? rule = null;
            foreach (var r in _rules)
                if (r.Def != null && r.Def.equipmentIndex == self.equipmentIndex) { rule = r; break; }

            var reticle = self.GetComponent<EquipmentTargetReticle>();
            if (rule == null)
            {
                if (reticle != null) reticle.Show(null);
                return;
            }
            if (reticle == null) reticle = self.gameObject.AddComponent<EquipmentTargetReticle>();

            var target = self.stock > 0 ? FindEnemy(body, rule.Range, rule.Angle, rule.Allow) : null;
            reticle.Show(target != null ? (target.mainHurtBox != null ? target.mainHurtBox.transform : target.coreTransform) : null);
        }

        internal static GameObject? IndicatorPrefab
        {
            get
            {
                if (_indicatorPrefab == null) _indicatorPrefab = EquipmentFx.Prefab("RoR2/DLC1/Equipment/BossHunter/BossHunterIndicator.prefab"); // Trophy Hunter's Tricorn reticle
                return _indicatorPrefab;
            }
        }
    }

    public class EquipmentTargetReticle : MonoBehaviour
    {
        private Indicator? _indicator;

        public void Show(Transform? target)
        {
            if (_indicator == null)
            {
                if (target == null) return;
                _indicator = new Indicator(gameObject, EquipmentTargeting.IndicatorPrefab);
            }
            _indicator.targetTransform = target;
            _indicator.active          = target != null;
        }

        private void OnDestroy()
        {
            if (_indicator != null) _indicator.active = false;
        }
    }
}
