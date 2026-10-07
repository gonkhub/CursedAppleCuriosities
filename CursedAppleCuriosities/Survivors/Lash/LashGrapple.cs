using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Lash
{
    /// <summary>
    /// Grapple's target, as Deadlock picks it: the enemy (or friendly player, LashFriendlies) closest to the
    /// crosshair within range and cone, in line of sight. Shown with Huntress's tracking marker.
    /// </summary>
    [RequireComponent(typeof(CharacterBody))]
    public class LashGrappleTracker : MonoBehaviour
    {
        private HurtBox? _target;
        private CharacterBody _body = null!;
        private InputBankTest _input = null!;
        private TeamComponent _team = null!;
        private Indicator? _indicator;
        private float _stopwatch;
        private readonly BullseyeSearch _search = new BullseyeSearch();

        public HurtBox? Target => _target;

        private void Awake()
        {
            _indicator = new Indicator(gameObject, LashSurvivor.TrackingIndicator);
        }

        private void Start()
        {
            _body = GetComponent<CharacterBody>();
            _input = GetComponent<InputBankTest>();
            _team = GetComponent<TeamComponent>();
        }

        private void OnEnable() { if (_indicator != null) _indicator.active = true; }
        private void OnDisable() { if (_indicator != null) _indicator.active = false; }

        private void FixedUpdate()
        {
            _stopwatch += Time.fixedDeltaTime;
            if (_stopwatch < 0.1f || _input == null) return;
            _stopwatch = 0f;
            var aim = new Ray(_input.aimOrigin, _input.aimDirection);
            HurtBox? enemy = Search(aim, enemies: true);
            HurtBox? ally = Search(aim, enemies: false);
            _target = enemy;
            if (ally != null && (enemy == null || Angle(aim, ally) < Angle(aim, enemy))) _target = ally;
            if (_indicator != null) _indicator.targetTransform = _target != null ? _target.transform : null;
        }

        private static float Angle(Ray aim, HurtBox h) => Vector3.Angle(aim.direction, h.transform.position - aim.origin);

        private HurtBox? Search(Ray aim, bool enemies)
        {
            if (enemies) { _search.teamMaskFilter = TeamMask.all; _search.teamMaskFilter.RemoveTeam(_team.teamIndex); }
            else { _search.teamMaskFilter = TeamMask.none; _search.teamMaskFilter.AddTeam(_team.teamIndex); }
            _search.filterByLoS = true;
            _search.filterByDistinctEntity = true;
            _search.searchOrigin = aim.origin;
            _search.searchDirection = aim.direction;
            _search.sortMode = BullseyeSearch.SortMode.Angle;
            _search.maxDistanceFilter = LashData.GrappleRange;
            _search.maxAngleFilter = LashData.GrappleConeDeg * 0.5f;
            _search.RefreshCandidates();
            _search.FilterOutGameObject(gameObject);
            foreach (HurtBox h in _search.GetResults())
            {
                CharacterBody? b = h.healthComponent != null ? h.healthComponent.body : null;
                if (b == null) continue;
                if (!enemies && !b.isPlayerControlled) continue; // allies: other players only, as in Deadlock
                return h;
            }
            return null;
        }
    }

    /// <summary>Grapple is only ready with a target, so a whiff never spends the cooldown (as in Deadlock).</summary>
    public class LashGrappleSkillDef : SkillDef
    {
        protected class InstanceData : BaseSkillInstanceData
        {
            public LashGrappleTracker? Tracker;
        }

        public override BaseSkillInstanceData OnAssigned(GenericSkill skillSlot) =>
            new InstanceData { Tracker = skillSlot.GetComponent<LashGrappleTracker>() };

        private static bool HasTarget(GenericSkill slot) =>
            slot.skillInstanceData is InstanceData d && d.Tracker != null && d.Tracker.Target != null;

        public override bool CanExecute(GenericSkill skillSlot) => HasTarget(skillSlot) && base.CanExecute(skillSlot);

        public override bool IsReady(GenericSkill skillSlot) => base.IsReady(skillSlot) && HasTarget(skillSlot);
    }
}
