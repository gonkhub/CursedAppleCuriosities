using System;
using RoR2;
using RoR2.Achievements;

namespace CursedAppleCuriosities.Unlocks.Challenges
{
    /// <summary>
    /// Victor's unlock (user, 2026-10-15): "Pain Battery", in a single run take 1,000 damage from yourself, as any survivor.
    /// Only damage from your own body counts, as its attacker or (with no attacker) its source (REX's health costs, Helfire Tincture, Blood Tribute, the Cursed
    /// Broker's self-damage, Victor's and Bebop's own): Shrine of Blood (its attacker is the shrine) and fall damage (no
    /// attacker) don't, by the user's call. Counted as health actually lost, on the server; the total starts over each run.
    /// </summary>
    [RegisterAchievement(Identifier, "CursedApple.Survivors.Victor", null, 0, typeof(Server))]
    public class VictorPainBattery : BaseAchievement
    {
        public const string Identifier = "CursedAppleVictorPainBattery";
        public const float Damage = 1000f;

        public override void OnInstall()
        {
            base.OnInstall();
            SetServerTracked(true);
        }

        public override void OnUninstall()
        {
            SetServerTracked(false);
            base.OnUninstall();
        }

        private class Server : BaseServerAchievement
        {
            private float _total;

            public override void OnInstall()
            {
                base.OnInstall();
                SelfDamage.Init();
                SelfDamage.OnSelfDamage += Add;
                Run.onRunStartGlobal += Reset;
            }

            public override void OnUninstall()
            {
                SelfDamage.OnSelfDamage -= Add;
                Run.onRunStartGlobal -= Reset;
                base.OnUninstall();
            }

            private void Reset(Run run) => _total = 0f;

            private void Add(CharacterBody body, float amount)
            {
                if (!IsCurrentBody(body)) return;
                _total += amount;
                if (_total >= Damage) Grant();
            }
        }
    }

    /// <summary>Server: health a body lost to a hit from itself (health + shield + barrier, after armour).</summary>
    internal static class SelfDamage
    {
        public static event Action<CharacterBody, float>? OnSelfDamage;
        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            On.RoR2.HealthComponent.TakeDamage += TakeDamage;
        }

        private static void TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo info)
        {
            CharacterBody? body = self != null ? self.body : null;
            // Your own hit: you're its attacker, or it has no attacker and you're its source (Blood Tribute's drain, Victor's
            // payments). Falls (no attacker) and Shrines of Blood (the shrine is the attacker) never count.
            bool own = body != null && info != null && body.isPlayerControlled
                && (info.damageType.damageType & DamageType.FallDamage) == 0
                && (info.attacker == body.gameObject || (info.attacker == null && info.inflictor == body.gameObject));
            float before = own ? self!.health + self.shield + self.barrier : 0f;
            orig(self!, info!);
            if (!own) return;
            float lost = before - (self!.health + self.shield + self.barrier);
            if (lost > 0f) OnSelfDamage?.Invoke(body!, lost);
        }
    }
}
