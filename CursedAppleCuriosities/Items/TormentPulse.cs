using BepInEx.Configuration;
using R2API;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class TormentPulse : ItemBase
    {
        public override string ItemName => "Torment Pulse";
        public override string ItemLangTokenName => "TORMENTPULSE";

        public override string ItemPickupDesc => "Periodically damage nearby enemies.";
        public override string ItemFullDescription =>
            $"Every <style=cIsUtility>{PulseInterval}s</style>, deal " +
            $"<style=cIsDamage>{DamageCoefficient * 100:0}% base damage</style> " +
            $"to up to <style=cIsUtility>{BaseTargets} enemies</style> " +
            $"<style=cStack>(+{TargetsPerStack} per stack)</style> " +
            $"within <style=cIsUtility>{BaseRange}m</style> " +
            $"<style=cStack>(+{RangePerStack}m per stack)</style>.";

        public override string ItemLore =>
            "A little bit of the divine, right here in your hot little hands.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_tormentpulse.png";

        public const float PulseInterval    = 1.5f;
        public const float BaseRange        = 12f;
        public const float RangePerStack    = 10f;
        public static float DamageCoefficient;
        public const int   BaseTargets      = 2;
        public const int   TargetsPerStack  = 1;

        public override void Init(ConfigFile config)
        {
            DamageCoefficient = config.Bind("Green", "Torment Pulse - Damage Coefficient", 2.0f,
                "Damage per pulse as a fraction of base damage (2.0 = 200%).").Value;
            CreateLang();
            CreateItem();
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += CharacterBody_OnInventoryChanged;
        }

        private void CharacterBody_OnInventoryChanged(
            On.RoR2.CharacterBody.orig_OnInventoryChanged orig,
            CharacterBody self)
        {
            orig(self);

            if (self.inventory == null) return;

            int count = self.inventory.GetItemCountEffective(ItemDef);
            var controller = self.GetComponent<TormentPulseController>();

            if (count > 0 && controller == null)
                self.gameObject.AddComponent<TormentPulseController>().Initialize(this);
            if (count > 0 && self.GetComponent<TormentPulseRing>() == null)
                self.gameObject.AddComponent<TormentPulseRing>().Item = ItemDef;
            else if (count <= 0 && controller != null)
                UnityEngine.Object.Destroy(controller);
        }
    }

    /// <summary>Client visual: a faint ring at the pulse's reach around the local player (grows with Mystic Expansion).</summary>
    public class TormentPulseRing : MonoBehaviour
    {
        private const int Segments = 64;
        private static readonly Color RingColor = new Color(0.75f, 0.15f, 0.2f, 0.2f); // torment red, faint

        public ItemDef? Item;
        private CharacterBody _body = null!;
        private LineRenderer? _ring;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            int count = _body != null && _body.inventory != null && Item != null ? _body.inventory.GetItemCountEffective(Item) : 0;
            if (count <= 0) { Destroy(this); return; }

            bool show = _body.hasEffectiveAuthority && _body.isPlayerControlled
                && _body.healthComponent != null && _body.healthComponent.alive && SpiritSnatch.RingMaterial != null;
            if (!show) { if (_ring != null) _ring.enabled = false; return; }

            if (_ring == null) _ring = MakeRing();
            _ring.enabled = true;
            float r = (TormentPulse.BaseRange + (count - 1) * TormentPulse.RangePerStack) * MysticExpansion.RadiusMultiplier(_body);
            Vector3 c = _body.footPosition + Vector3.up * 0.25f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        private static LineRenderer MakeRing()
        {
            var lr = new GameObject("CursedApple_TormentPulseRing").AddComponent<LineRenderer>();
            lr.material = SpiritSnatch.RingMaterial; // the shared laser-beam material Spirit Snatch loads
            if (lr.material.HasProperty("_TintColor")) lr.material.SetColor("_TintColor", RingColor);
            lr.startColor = lr.endColor = RingColor;
            lr.startWidth = lr.endWidth = 0.08f;
            lr.positionCount = Segments;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void OnDestroy()
        {
            if (_ring != null) Destroy(_ring.gameObject);
        }
    }

    public class TormentPulseController : MonoBehaviour
    {
        private float _timer;
        private CharacterBody _body;
        private TormentPulse _item;

        public void Initialize(TormentPulse item) => _item = item;

        private void Awake()
        {
            _body  = GetComponent<CharacterBody>();
            _timer = TormentPulse.PulseInterval;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _body == null || _item == null) return;
            if (_body.teamComponent == null) return; // Pulse() reads teamComponent.teamIndex

            _timer -= Time.fixedDeltaTime;
            if (_timer > 0f) return;
            _timer = TormentPulse.PulseInterval;

            int itemCount = _body.inventory?.GetItemCountEffective(_item.ItemDef) ?? 0;
            if (itemCount <= 0) return;

            int   maxTargets = TormentPulse.BaseTargets + (itemCount - 1) * TormentPulse.TargetsPerStack;
            float range      = TormentPulse.BaseRange   + (itemCount - 1) * TormentPulse.RangePerStack;
            Pulse(maxTargets, range);
        }

        private void Pulse(int maxTargets, float range)
        {
            var search = new SphereSearch();
            search.origin = _body.transform.position;
            search.radius = range * MysticExpansion.RadiusMultiplier(_body);
            search.mask   = LayerIndex.entityPrecise.mask;

            var hurtBoxes = new List<HurtBox>();
            search
                .RefreshCandidates()
                .FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(_body.teamComponent.teamIndex))
                .OrderCandidatesByDistance()
                .FilterCandidatesByDistinctHurtBoxEntities()
                .GetHurtBoxes(hurtBoxes);

            int hit = 0;
            foreach (var hurtBox in hurtBoxes)
            {
                if (hit >= maxTargets) break;
                if (hurtBox?.healthComponent == null) continue;

                var dmg = new DamageInfo();
                dmg.attacker         = _body.gameObject;
                dmg.inflictor        = _body.gameObject;
                dmg.damage           = _body.damage * TormentPulse.DamageCoefficient;
                dmg.crit             = _body.RollCrit();
                dmg.position         = hurtBox.transform.position;
                dmg.procCoefficient  = 0.5f;
                dmg.damageColorIndex = DamageColorIndex.Item;

                hurtBox.healthComponent.TakeDamage(dmg);
                if (!dmg.rejected)
                {
                    GlobalEventManager.instance.OnHitEnemy(dmg, hurtBox.healthComponent.gameObject);
                    GlobalEventManager.instance.OnHitAll(dmg, hurtBox.healthComponent.gameObject);
                }
                hit++;
            }
        }
    }
}
