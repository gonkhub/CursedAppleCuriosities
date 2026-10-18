using System.Collections.Generic;
using CursedAppleCuriosities.Items;
using RoR2;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Shiv
{
    /// <summary>
    /// The local Shiv player's view: a red reticle on every enemy already under Killing Blow's execute threshold (user 2.5), so he can
    /// see who to finish. Health and rage are synced, so it's the same test the lunge uses (ShivNet.Executable).
    /// </summary>
    public class ShivExecuteMarks : MonoBehaviour
    {
        private const float Range = 90f, Rescan = 0.15f;
        private const int Max = 16;
        private static GameObject? _red;
        private CharacterBody _body = null!;
        private readonly Dictionary<HealthComponent, Indicator> _marks = new Dictionary<HealthComponent, Indicator>();
        private readonly List<HealthComponent> _gone = new List<HealthComponent>();
        private float _next;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Rescan;
            bool mine = _body.hasEffectiveAuthority && _body.isPlayerControlled && _body.healthComponent != null && _body.healthComponent.alive;
            var want = new HashSet<HealthComponent>();
            if (mine && _body.teamComponent != null)
            {
                Vector3 me = _body.corePosition;
                foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
                {
                    if (want.Count >= Max) break;
                    if (b == null || b.healthComponent == null || b.teamComponent == null || b.teamComponent.teamIndex == _body.teamComponent.teamIndex) continue;
                    if ((b.corePosition - me).sqrMagnitude > Range * Range || !ShivNet.Executable(_body, b.healthComponent)) continue;
                    want.Add(b.healthComponent);
                }
            }
            _gone.Clear();
            foreach (var kv in _marks) if (!want.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (HealthComponent hc in _gone) { _marks[hc].active = false; _marks.Remove(hc); }
            if (want.Count == 0) return;
            _red ??= Kit.Recoloured(EquipmentTargeting.IndicatorPrefab, "CursedAppleShivExecuteIndicator", 0f, effect: false) ?? EquipmentTargeting.IndicatorPrefab;
            foreach (HealthComponent hc in want)
            {
                if (_marks.ContainsKey(hc) || hc.body == null) continue;
                var ind = new Indicator(gameObject, _red)
                {
                    targetTransform = hc.body.mainHurtBox != null ? hc.body.mainHurtBox.transform : hc.body.coreTransform,
                    active = true,
                };
                _marks[hc] = ind;
            }
        }

        private void OnDestroy()
        {
            foreach (Indicator i in _marks.Values) i.active = false;
            _marks.Clear();
        }
    }
}
