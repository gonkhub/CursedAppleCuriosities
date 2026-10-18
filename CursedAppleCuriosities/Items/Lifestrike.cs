using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    public class Lifestrike : ItemBase
    {
        public override string ItemName => "Lifestrike";
        public override string ItemLangTokenName => "LIFESTRIKE";

        public override string ItemPickupDesc => "Hitting nearby enemies heals you and slows them.";
        public override string ItemFullDescription =>
            $"Hitting an enemy within <style=cIsUtility>{Range}m</style> heals you for " +
            $"<style=cIsHealing>{BaseHealFraction * 100:0}% of the damage dealt</style> " +
            $"<style=cStack>(+{HealFractionPerStack * 100:0}% per stack)</style>, and " +
            $"<style=cIsUtility>slows</style> them for <style=cIsUtility>{SlowDuration}s</style>.";

        public override string ItemLore =>
            "Someone's gonna be bleedin' on the concrete, and it ain't gonna be you.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Healing, ItemTag.Utility };
        protected override string IconFileName => "icon_lifestrike.png";

        public const float Range               = 13f;
        public static ItemDef? StaticItemDef;
        public static float BaseHealFraction;            // of damage dealt
        public const float HealFractionPerStack = 0.04f;
        public const float SlowDuration        = 2f;

        public override void Init(ConfigFile config)
        {
            BaseHealFraction = 0.08f;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
        }

        // Inventories sync to every client, so each machine attaches the range ring itself; it only draws for the local player.
        private void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (self.inventory != null && self.inventory.GetItemCountEffective(ItemDef) > 0 && self.GetComponent<LifestrikeRing>() == null)
                self.gameObject.AddComponent<LifestrikeRing>();
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self,
            DamageInfo damageInfo,
            GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (damageInfo == null || damageInfo.rejected) return; // blocked hits proc nothing (vanilla's own check is inside orig)

            if (!NetworkServer.active) return;
            if (damageInfo == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f || damageInfo.damage <= 0f) return;
            if (damageInfo.attacker == null) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (attackerBody?.inventory == null) return;

            int count = attackerBody.inventory.GetItemCountEffective(ItemDef);
            if (count <= 0) return;

            float distance = Vector3.Distance(damageInfo.attacker.transform.position, victim.transform.position);
            if (distance > Range * MysticExpansion.RadiusMultiplier(attackerBody)) return;

            // Slow the victim.
            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody != null && RoR2Content.Buffs.Slow50 != null)
                victimBody.AddTimedBuff(RoR2Content.Buffs.Slow50, SlowDuration);

            // Heal the attacker for a fraction of the damage dealt.
            float fraction = BaseHealFraction + HealFractionPerStack * (count - 1);
            float heal     = damageInfo.damage * fraction;
            if (heal > 0f)
                attackerBody.healthComponent?.Heal(heal, default(ProcChainMask));
        }
    }

    /// <summary>
    /// Lifestrike's range on the ground around the local player (user 3.0, like Spirit Snatch's ring, in green): hits on enemies
    /// inside it heal. Mystic Expansion widens it with the item.
    /// </summary>
    public class LifestrikeRing : MonoBehaviour
    {
        private const int Segments = 64;
        private static readonly Color RingColor = new Color(0.25f, 0.85f, 0.35f, 0.22f); // green, faint

        private CharacterBody _body = null!;
        private LineRenderer? _ring;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            if (_body == null) return;
            var inv = _body.inventory;
            if (inv == null || Lifestrike.StaticItemDef == null || inv.GetItemCountEffective(Lifestrike.StaticItemDef) <= 0)
            {
                Destroy(this);
                return;
            }
            bool show = _body.hasEffectiveAuthority && _body.isPlayerControlled && _body.healthComponent != null && _body.healthComponent.alive;
            if (!show)
            {
                if (_ring != null) _ring.enabled = false;
                return;
            }
            if (_ring == null) _ring = MakeRing();
            if (_ring == null) return;
            _ring.enabled = true;
            float r = Lifestrike.Range * MysticExpansion.RadiusMultiplier(_body);
            Vector3 c = _body.footPosition + Vector3.up * 0.3f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        private static LineRenderer? MakeRing()
        {
            Material? mat = SpiritSnatch.RingMaterial != null ? SpiritSnatch.RingMaterial : CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.LineMaterial();
            if (mat == null) return null;
            var go = new GameObject("CursedApple_LifestrikeRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat; // an instance: the tint is this ring's alone
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
}
