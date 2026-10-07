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
        public const float BaseRange        = 20f;
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
            else if (count <= 0 && controller != null)
                UnityEngine.Object.Destroy(controller);
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
            search.radius = range;
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
