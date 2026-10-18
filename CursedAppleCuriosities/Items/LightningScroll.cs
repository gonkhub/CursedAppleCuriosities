using System.Collections.Generic;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Lightning Scroll (Deadlock spirit item, 1.8 user): the first time each enemy is hit by your Special, it is marked;
    /// 3 s later a purple bolt strikes it for 500% damage and a 2 s stun.
    /// </summary>
    public class LightningScroll : ItemBase
    {
        public override string ItemName => "Lightning Scroll";
        public override string ItemLangTokenName => "LIGHTNINGSCROLL";

        public override string ItemPickupDesc => "The first time your Special hits an enemy, lightning strikes it shortly after.";
        public override string ItemFullDescription =>
            $"The first time your <style=cIsDamage>special skill</style> hits an enemy, it is marked. After " +
            $"<style=cIsUtility>{Delay:0.#}s</style>, a bolt of lightning strikes it for " +
            $"<style=cIsDamage>{BaseDamage * 100:0}% damage</style> <style=cStack>(+{DamagePerStack * 100:0}% per stack)</style> " +
            $"and <style=cIsUtility>stuns</style> it for <style=cIsUtility>{StunDuration:0.#}s</style>.";

        public override string ItemLore => "Read it out loud. Then duck.";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new[] { ItemTag.Damage };
        protected override string IconFileName => "icon_lightningscroll.png";

        public const float Delay        = 3f;
        public const float StunDuration = 2f;
        public const float ProcCoefficient = 1f;
        public static float BaseDamage;
        public static float DamagePerStack;

        public static ItemDef? StaticItemDef;
        public static BuffDef? MarkBuffDef;
        internal static GameObject? BoltEffect;

        private const float Hue = 0.78f; // purple

        public override void Init(ConfigFile config)
        {
            BaseDamage = 5f;
            DamagePerStack = 2.5f;

            var color             = new Color(0.65f, 0.35f, 1f);
            MarkBuffDef           = ScriptableObject.CreateInstance<BuffDef>();
            MarkBuffDef.name      = "CursedApple_LightningScroll";
            MarkBuffDef.canStack  = false;
            MarkBuffDef.isDebuff  = true;
            MarkBuffDef.buffColor = color;
            MarkBuffDef.iconSprite = BuffIcon(IconFileName, color);
            ContentAddition.AddBuffDef(MarkBuffDef);

            try
            {
                var bolt = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LightningStrikeOnHit/SimpleLightningStrikeImpact.prefab").WaitForCompletion();
                BoltEffect = Kit.Recoloured(bolt, "CursedAppleLightningScrollBolt", Hue, effect: true);
            }
            catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Lightning Scroll] bolt effect: {e.Message}"); }

            CreateLang();
            CreateItem();
            StaticItemDef = ItemDef;
            Hooks();
        }

        public override void Hooks()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += OnHitEnemy;
        }

        private void OnHitEnemy(
            On.RoR2.GlobalEventManager.orig_OnHitEnemy orig,
            GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            if (!NetworkServer.active) return;
            if (damageInfo?.attacker == null || victim == null) return;
            if (damageInfo.procCoefficient <= 0f || damageInfo.rejected) return;
            if (((DamageSource)damageInfo.damageType & DamageSource.Special) == 0) return;

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            int count = attackerBody != null && attackerBody.inventory != null ? attackerBody.inventory.GetItemCountEffective(ItemDef) : 0;
            if (count <= 0) return;

            var victimBody = victim.GetComponent<CharacterBody>();
            if (victimBody == null || victimBody.healthComponent == null || !victimBody.healthComponent.alive) return;

            var mark = victimBody.GetComponent<LightningScrollMark>() ?? victimBody.gameObject.AddComponent<LightningScrollMark>();
            mark.TryMark(attackerBody!, attackerBody!.damage * (BaseDamage + DamagePerStack * (count - 1)));
        }
    }

    /// <summary>On the enemy (server): who has marked it (each attacker once, ever) and the strikes still to come.</summary>
    public class LightningScrollMark : MonoBehaviour
    {
        private class Pending
        {
            public CharacterBody? Attacker;
            public GameObject? AttackerObject;
            public float Damage;
            public float TimeLeft;
        }

        private CharacterBody _body = null!;
        private readonly HashSet<GameObject> _markedBy = new HashSet<GameObject>();
        private readonly List<Pending> _pending = new List<Pending>();

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void TryMark(CharacterBody attacker, float damage)
        {
            if (!_markedBy.Add(attacker.gameObject)) return; // the first hit only
            _pending.Add(new Pending { Attacker = attacker, AttackerObject = attacker.gameObject, Damage = damage, TimeLeft = LightningScroll.Delay });
            if (LightningScroll.MarkBuffDef != null)
                using (DurationExtender.Exact()) // the delay is fixed: the buff shows it
                    _body.AddTimedBuff(LightningScroll.MarkBuffDef, LightningScroll.Delay);
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _pending.Count == 0) return;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                p.TimeLeft -= Time.fixedDeltaTime;
                if (p.TimeLeft > 0f) continue;
                _pending.RemoveAt(i);
                Strike(p);
            }
        }

        private void Strike(Pending p)
        {
            HealthComponent? hc = _body != null ? _body.healthComponent : null;
            if (hc == null || !hc.alive) return;
            Vector3 at = _body.corePosition;
            CursedAppleCuriositiesPlugin.Logger?.LogDebug($"[Lightning Scroll] strike on {_body.name}: {p.Damage:0} damage");
            if (LightningScroll.BoltEffect != null)
                EffectManager.SpawnEffect(LightningScroll.BoltEffect, new EffectData { origin = at, scale = Mathf.Max(1f, _body.radius) }, true);

            var info = new DamageInfo
            {
                attacker         = p.AttackerObject,
                inflictor        = p.AttackerObject,
                damage           = p.Damage,
                crit             = p.Attacker != null && p.Attacker.RollCrit(),
                procCoefficient  = LightningScroll.ProcCoefficient,
                position         = at,
                damageColorIndex = DamageColorIndex.Item,
                damageType       = DamageType.Generic,
            };
            using (DurationExtender.From(p.Attacker)) // the attacker's Duration Extender lengthens the stun
            {
                hc.TakeDamage(info);
                if (!info.rejected)
                {
                    GlobalEventManager.instance.OnHitEnemy(info, hc.gameObject);
                    GlobalEventManager.instance.OnHitAll(info, hc.gameObject);
                    var state = _body.GetComponent<SetStateOnHurt>();
                    if (state != null && state.canBeStunned && hc.alive) state.SetStun(LightningScroll.StunDuration);
                }
            }
        }
    }
}
