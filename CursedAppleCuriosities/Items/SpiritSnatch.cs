using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    // Focus Crystal, but barrier: close-range hits shield you. Reworked 2026-10-12 — the old armor steal
    // was invisible in play and its close-range bonus hit overlapped Focus Crystal. Barrier shows on the
    // health bar, so the benefit is readable, and nothing vanilla grants barrier on close hits (Loader's
    // passive aside).
    public class SpiritSnatch : ItemBase
    {
        public override string ItemName => "Spirit Snatch";
        public override string ItemLangTokenName => "SPIRITSNATCH";

        public override string ItemPickupDesc => "Hitting nearby enemies grants barrier.";
        public override string ItemFullDescription =>
            $"Hitting an enemy within <style=cIsUtility>{Range}m</style> grants a " +
            $"<style=cIsHealing>temporary barrier</style> for <style=cIsHealing>{DamageToBarrier * 100:0.#}% of the damage dealt</style> " +
            $"<style=cStack>(+{DamageToBarrier * 100:0.#}% per stack)</style>.";

        public override string ItemLore =>
            "Bought that off a Knickerbocker that was about to get his legs broken.";

        public override ItemTier Tier => ItemTier.Tier2;
        public override ItemTag[] ItemTags => new[] { ItemTag.Utility };
        protected override string IconFileName => "icon_spiritsnatch.png";

        public const float Range = 13f; // Focus Crystal's
        public static float DamageToBarrier; // 3.0 (user): proportional to damage, was 1% max HP per hit x proc (fast guns lived in full barrier)

        public static ItemDef? StaticItemDef;
        internal static Material? RingMaterial;

        public override void Init(ConfigFile config)
        {
            DamageToBarrier = 0.10f;
            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            try
            {
                // Shader.Find is off limits in RoR2: borrow the laser-pointer beam, as Abrams' rings do.
                var laser = Addressables.LoadAssetAsync<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat").WaitForCompletion();
                if (laser != null) RingMaterial = new Material(laser);
            }
            catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Spirit Snatch] ring material: {e.Message}"); }
            Hooks();
        }

        public override void Hooks()
        {
            GlobalEventManager.onServerDamageDealt   += OnDamageDealt;
            On.RoR2.CharacterBody.OnInventoryChanged += OnInventoryChanged;
        }

        private static int Count(CharacterBody? body)
            => body != null && body.inventory != null && StaticItemDef != null
                ? body.inventory.GetItemCountEffective(StaticItemDef) : 0;

        public static float RangeFor(CharacterBody body) => Range * MysticExpansion.RadiusMultiplier(body);

        // Server only (onServerDamageDealt). Proc > 0 keeps DoT ticks and proc-less item damage out; the proc
        // coefficient no longer scales it, since damage already accounts for fire rate.
        private void OnDamageDealt(DamageReport report)
        {
            var damageInfo = report.damageInfo;
            if (damageInfo == null || damageInfo.procCoefficient <= 0f) return;
            if (report.victim == null || report.attackerTeamIndex == report.victimTeamIndex) return;

            var attackerBody = report.attackerBody;
            int count = Count(attackerBody);
            if (count <= 0) return;
            var hc = attackerBody!.healthComponent;
            if (hc == null || !hc.alive) return;

            // Same check as Focus Crystal: attacker's core to the hit point.
            float range = RangeFor(attackerBody);
            if ((attackerBody.corePosition - damageInfo.position).sqrMagnitude > range * range) return;

            float dealt = Mathf.Min(report.damageDealt, report.combinedHealthBeforeDamage); // no overkill
            if (dealt > 0f) hc.AddBarrier(dealt * DamageToBarrier * count);
        }

        // Inventories sync to every client, so each machine can attach the ring itself; it only draws for
        // the local player's own body.
        private void OnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self)
        {
            orig(self);
            if (Count(self) > 0 && self.GetComponent<SpiritSnatchRing>() == null)
                self.gameObject.AddComponent<SpiritSnatchRing>();
        }
    }

    // Client visual: a flat ring at the item's range around the local player's feet.
    public class SpiritSnatchRing : MonoBehaviour
    {
        private const int Segments = 64;
        private static readonly Color RingColor = new Color(0.4f, 0.12f, 0.65f, 0.18f); // dark purple, faint (was barrier yellow at 0.35; playtest)

        private CharacterBody _body = null!;
        private LineRenderer? _ring;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            if (_body == null) return;
            var inv = _body.inventory;
            if (inv == null || SpiritSnatch.StaticItemDef == null || inv.GetItemCountEffective(SpiritSnatch.StaticItemDef) <= 0)
            {
                Destroy(this);
                return;
            }

            bool show = _body.hasEffectiveAuthority && _body.isPlayerControlled
                && _body.healthComponent != null && _body.healthComponent.alive && SpiritSnatch.RingMaterial != null;
            if (!show)
            {
                if (_ring != null) _ring.enabled = false;
                return;
            }

            if (_ring == null) _ring = MakeRing();
            _ring.enabled = true;

            float r = SpiritSnatch.RangeFor(_body);
            Vector3 c = _body.footPosition + Vector3.up * 0.3f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                _ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        private static LineRenderer MakeRing()
        {
            var go = new GameObject("CursedApple_SpiritSnatchRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = SpiritSnatch.RingMaterial;
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
