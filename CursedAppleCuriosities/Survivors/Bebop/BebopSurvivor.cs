using System;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Bebop;
using R2API;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Bebop
{
    /// <summary>
    /// Bebop from Deadlock (hero_bebop) as a RoR2 survivor (same structure as Vyper). Kit by the user (2026-10-14): his beam, Sticky Bomb
    /// with permanent stacks (alt: the hook), a wide Exploding Uppercut, Hyper Beam.
    /// </summary>
    public static class BebopSurvivor
    {
        public const string Token = "CURSEDAPPLE_BEBOP_";
        public const string BodyName = "CursedAppleBebopBody";
        public const string MuzzleName = "BebopMuzzle";
        public static readonly Color Color = new Color(0.85f, 0.55f, 0.15f); // character select / UI: his orange paint

        public static GameObject? BodyPrefab, DisplayPrefab, GunBeamPrefab, HyperBeamPrefab, HookHead;
        public static SurvivorDef? Survivor;
        private static Sprite GoldPlatedSkinIcon => _goldPlatedSkinIcon ??= Kit.SkinIcon(new Color(0.93f, 0.72f, 0.2f), new Color(0.2f, 0.2f, 0.21f), new Color(0.75f, 0.55f, 0.15f), new Color(0.55f, 0.12f, 0.12f));
        private static Sprite? _goldPlatedSkinIcon;
        private static Sprite ChromeSkinIcon => _chromeSkinIcon ??= Kit.SkinIcon(new Color(0.78f, 0.8f, 0.84f), new Color(0.2f, 0.2f, 0.21f), new Color(0.6f, 0.62f, 0.66f), new Color(0.3f, 0.85f, 0.4f));
        private static Sprite? _chromeSkinIcon;
        public static BuffDef? StickyStacksBuff, HookAmpBuff, UppercutBuff, HyperSlow, StickySpeedBuff;
        public static GameObject? BeamHitEffect, StickyEffect, PunchEffect, LandEffect;
        public static Material? ChainMaterial;
        internal static SkillDef? StickySkill;

        private static SkillDef? _beam, _sticky, _hook, _uppercut, _hyper;

        private static T? TryLoad<T>(params string[] keys) where T : UnityEngine.Object => Kit.TryLoad<T>("Bebop", keys);

        public static void Init(ConfigFile config, GameObject host)
        {
            BebopData.Bind(config);
            foreach (DeadlockSound s in BebopData.Sounds) DeadlockSounds.Register(s);
            foreach (string icon in new[] { BebopData.IconPortrait, BebopData.IconUppercut, BebopData.IconSticky, BebopData.IconHook, BebopData.IconHyper })
                DeadlockInstall.WantIcon(icon);
            DeadlockHeroes.Register(BebopModel.Def);
            DeadlockInstall.WantModel(BebopModel.Def.ModelRequest());

            LoadAssets();
            CreateBuffs();
            RegisterStates();
            CreateSkills();
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            CreateSurvivor();
            BebopInvader.Build(BodyPrefab); // the hostile Bebop the Sunken Plaza pit wakes (user 3.0)
            AddLanguage();
            BebopNet.Register();
            DeadlockHeroVoice.Track(BodyName, BebopData.Hero, "bebop");
            DeadlockPingVoice.Track(BodyName, BebopData.Hero, "bebop");
            host.AddComponent<BebopIcons>();
        }

        private static void LoadAssets()
        {
            GunBeamPrefab = TryLoad<GameObject>("RoR2/Base/Engi/LaserEngiTurret.prefab");     // Engi's turret laser: a beam with a movable end
            HyperBeamPrefab = TryLoad<GameObject>("RoR2/Base/Titan/LaserTitan.prefab");       // the Stone Titan's laser, for the big one
            BeamHitEffect = TryLoad<GameObject>("RoR2/Base/Engi/LaserCutterHitVFX.prefab");
            StickyEffect = TryLoad<GameObject>("RoR2/Base/Commando/OmniExplosionVFXCommandoGrenade.prefab");
            PunchEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniImpactVFXSlash.prefab");
            LandEffect = TryLoad<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab");
            ChainMaterial = TryLoad<Material>("RoR2/Base/Common/VFX/matLaserPointerBeam.mat");
            GameObject? ghost = TryLoad<GameObject>("RoR2/Base/Loader/LoaderHookGhost.prefab");
            if (ghost != null)
            {
                // A plain copy of Loader's hook as Bebop's flying hand: no projectile ghost logic (it would destroy itself).
                HookHead = ghost.InstantiateClone("CursedAppleBebopHookHead", false);
                foreach (RoR2.Projectile.ProjectileGhostController g in HookHead.GetComponentsInChildren<RoR2.Projectile.ProjectileGhostController>(true)) UnityEngine.Object.DestroyImmediate(g);
                foreach (EffectComponent e in HookHead.GetComponentsInChildren<EffectComponent>(true)) UnityEngine.Object.DestroyImmediate(e);
            }
        }

        private static void CreateBuffs()
        {
            StickyStacksBuff = Buff("CursedAppleBebopStickyStacks", new Color(1f, 0.6f, 0.15f), debuff: false, stack: true);
            HookAmpBuff = Buff("CursedAppleBebopHooked", new Color(0.9f, 0.5f, 0.2f), debuff: true, stack: false);
            UppercutBuff = Buff("CursedAppleBebopUppercut", new Color(1f, 0.75f, 0.3f), debuff: false, stack: false);
            HyperSlow = Buff("CursedAppleBebopHyperSlow", Color.gray, debuff: true, stack: false, hidden: true);
            StickySpeedBuff = Buff("CursedAppleBebopStickySpeed", new Color(1f, 0.3f, 0.2f), debuff: false, stack: true); // stacks: the share of the boost left
        }

        private static BuffDef Buff(string name, Color color, bool debuff, bool stack, bool hidden = false)
        {
            var b = ScriptableObject.CreateInstance<BuffDef>();
            b.name = name;
            b.buffColor = color;
            b.canStack = stack;
            b.isDebuff = debuff;
            b.isHidden = hidden;
            b.iconSprite = ItemBase.CreatePlaceholderSprite(color);
            ContentAddition.AddBuffDef(b);
            return b;
        }

        private static void RegisterStates()
        {
            foreach (Type t in new[] { typeof(FireBeam), typeof(ThrowStickyBomb), typeof(FireHook), typeof(Uppercut), typeof(HyperBeam) })
                ContentAddition.AddEntityState(t, out _);
        }

        private static T Skill<T>(string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef =>
            Kit.Skill<T>("CursedAppleBebop", Token, id, state, machine, cooldown, priority);

        private static void CreateSkills()
        {
            _beam = Skill<SkillDef>("Beam", typeof(FireBeam), "Weapon", 0f, InterruptPriority.Any);
            _beam.cancelSprintingOnActivation = true;
            _beam.mustKeyPress = false;

            _sticky = Skill<SkillDef>("Sticky", typeof(ThrowStickyBomb), "Weapon", BebopData.StickyCooldown, InterruptPriority.Skill);
            _sticky.mustKeyPress = true;
            _sticky.cancelSprintingOnActivation = false;
            StickySkill = _sticky;

            _hook = Skill<SkillDef>("Hook", typeof(FireHook), "Weapon", BebopData.HookCooldown, InterruptPriority.Skill);
            _hook.mustKeyPress = true;
            _hook.cancelSprintingOnActivation = false;

            _uppercut = Skill<SkillDef>("Uppercut", typeof(Uppercut), "Weapon", BebopData.UppercutCooldown, InterruptPriority.Skill);
            _uppercut.mustKeyPress = true;
            _uppercut.cancelSprintingOnActivation = false;

            _hyper = Skill<SkillDef>("Hyper", typeof(HyperBeam), "Body", BebopData.HyperCooldown, InterruptPriority.PrioritySkill);
            _hyper.mustKeyPress = true;
            _hyper.cancelSprintingOnActivation = true;
            _hyper.beginSkillCooldownOnSkillEnd = true;
        }

        private static GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = BebopData.BaseHealth;
            cb.levelMaxHealth = BebopData.HealthPerLevel;
            cb.baseArmor = BebopData.BaseArmor;
            cb.baseDamage = BebopData.BaseDamage;
            cb.levelDamage = BebopData.DamagePerLevel;
            cb.baseMoveSpeed = BebopData.BaseSpeed;
            cb.baseRegen = BebopData.BaseRegen;
            cb.levelRegen = BebopData.BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            locator.primary = Kit.Slot(body, BodyName, "Primary", _beam!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", _sticky!, _hook!);
            locator.utility = Kit.Slot(body, BodyName, "Utility", _uppercut!);
            locator.special = Kit.Slot(body, BodyName, "Special", _hyper!);
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = true, skillNameToken = Token + "PASSIVE_NAME", skillDescriptionToken = Token + "PASSIVE_DESC", icon = ItemBase.CreatePlaceholderSprite(Color) }; // his/her Deadlock movement (user 2.5: every survivor shows it)
            DeadlockMovement.Apply(body, BebopData.Movement);

            var gun = body.AddComponent<BebopGunBeam>();
            gun.Prefab = GunBeamPrefab;
            gun.Range = BebopData.BeamRange;
            gun.Width = 2.4f;
            gun.Solid = new Color(1f, 0.08f, 0.06f); // red (user 2.5)
            gun.Simple = true; // a plain red ray from his gun to the crosshair, stopped by the first thing in the way (user 3.0)
            var hyper = body.AddComponent<BebopHyperBeamFx>();
            hyper.Prefab = HyperBeamPrefab;
            hyper.Range = BebopData.HyperLength;
            hyper.Width = 8f; // Deadlock BeamWidth 2.9 m: a wall of light
            hyper.HitEnemies = false; // it goes through everyone
            body.AddComponent<BebopStickyReticle>();
            Kit.BiggerCamera(body, "ccpCursedAppleBebop", 0.9f, 1.4f); // he's almost Abrams' height (user 2.5): pivot up (m), distance (x Commando's)

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = BebopData.Hero;
            Kit.MuteStandIn(body, model.gameObject, BebopData.Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedAppleBebopDEFAULT", Token + "SKIN_DEFAULT", null,
                ("skinCursedAppleBebopGOLDPLATED", Token + "SKIN_GOLDPLATED", GoldPlatedSkinIcon), ("skinCursedAppleBebopCHROME", Token + "SKIN_CHROME", ChromeSkinIcon));
            ContentAddition.AddBody(body);
            return body;
        }

        private static GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedAppleBebopDisplay", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = BebopData.Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedAppleBebopDISPLAY", Token + "SKIN_DEFAULT", null,
                    ("skinCursedAppleBebopDISPLAYGOLDPLATED", Token + "SKIN_GOLDPLATED", GoldPlatedSkinIcon), ("skinCursedAppleBebopDISPLAYCHROME", Token + "SKIN_CHROME", ChromeSkinIcon));
            }
            display.AddComponent<BebopDisplayIntro>(); // lobby entrance (user 2.5)
            return display;
        }

        private static void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedAppleBebop";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = 111f;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        internal static void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(BebopData.IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            Sprite? Set(SkillDef? s, string path)
            {
                Texture2D? t = DeadlockInstall.Icon(path);
                if (t == null) return null;
                Sprite sprite = Kit.Sprite(t);
                if (s != null) s.icon = sprite;
                return sprite;
            }
            Sprite? sticky = Set(_sticky, BebopData.IconSticky);
            if (sticky != null && StickyStacksBuff != null) StickyStacksBuff.iconSprite = sticky;
            Sprite? hook = Set(_hook, BebopData.IconHook);
            if (hook != null && HookAmpBuff != null) HookAmpBuff.iconSprite = hook;
            Sprite? upper = Set(_uppercut, BebopData.IconUppercut);
            if (upper != null && UppercutBuff != null) UppercutBuff.iconSprite = upper;
            Set(_hyper, BebopData.IconHyper);
            if (portrait != null) _beam!.icon = Kit.Sprite(portrait);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Bebop] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }

        private static void AddLanguage()
        {
            string D(float coeff) => $"{coeff * 100f:0}%";
            LanguageAPI.Add(Token + "NAME", "Bebop");
            LanguageAPI.Add(Token + "PASSIVE_NAME", "Deadlock Movement");
            LanguageAPI.Add(Token + "PASSIVE_DESC", Kit.MovementLine(BebopData.Movement, alone: true));
            LanguageAPI.Add(Token + "SUBTITLE", "The Wrecking Ball");
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "SKIN_GOLDPLATED", "Gold Plated");
            LanguageAPI.Add(Token + "SKIN_CHROME", "Chrome");
            LanguageAPI.Add(Token + "DESCRIPTION",
                "Bebop was built by a widowed junkyard attendant who raised him on jazz. Now she's sick, and he's out paying for her medicine.<color=#CCD3E0>\n\n" +
                "< ! > Humble Pie is a beam: keep it on target. It reaches 32 m and hits the first thing it touches.\n\n" +
                "< ! > Every enemy a Sticky Bomb hurts makes all your future Sticky Bombs stronger, kills most of all. Dying loses all of it.\n\n" +
                "< ! > Hook an enemy and it takes extra beam damage. Press again to let go.\n\n" +
                "< ! > Uppercut sends a whole group flying the way you look; each one explodes where it lands.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so he left, the demolition complete.");
            LanguageAPI.Add(Token + "FAILURE", "..and so he vanished, in pieces.");
            LanguageAPI.Add(Token + "BEAM_NAME", "Humble Pie");
            LanguageAPI.Add(Token + "BEAM_DESC",
                $"Fire a continuous beam that deals <style=cIsDamage>{D(BebopData.BeamDamage / BebopData.BeamTick)} damage per second</style> to the first enemy it touches, up to {BebopData.BeamRange:0} m. Barely procs, but <style=cIsDamage>always procs</style> on a hooked enemy.");
            LanguageAPI.Add(Token + "STICKY_NAME", "Sticky Bomb");
            LanguageAPI.Add(Token + "STICKY_DESC",
                $"Stick a bomb on a nearby enemy or ally (hold: on yourself) and gain <style=cIsUtility>+{BebopData.StickySpeed:0} m/s</style> briefly. After {BebopData.StickyFuse:0.#}s it explodes for <style=cIsDamage>{D(BebopData.StickyDamage)} damage</style>. Each enemy it hits gives Sticky Bomb " +
                $"<style=cIsDamage>+{BebopData.StickyPerHit:0}%</style> damage, each kill <style=cIsDamage>+{BebopData.StickyPerKill:0}%</style> (elites x{BebopData.StickyEliteScale:0.#}), <style=cIsHealth>lost on death</style>.");
            LanguageAPI.Add(Token + "HOOK_NAME", "Grapple Arm");
            LanguageAPI.Add(Token + "HOOK_DESC",
                $"Launch your hand up to {BebopData.HookRange:0} m. The first enemy hit takes <style=cIsDamage>{D(BebopData.HookDamage)} damage</style>, is <style=cIsUtility>pulled in</style> and takes <style=cIsDamage>+{D(BebopData.HookAmp)}</style> beam damage for {BebopData.HookAmpSeconds:0}s. Press again to cancel.");
            LanguageAPI.Add(Token + "UPPERCUT_NAME", "Exploding Uppercut");
            LanguageAPI.Add(Token + "UPPERCUT_DESC",
                $"Uppercut enemies in a wide cone for <style=cIsDamage>{D(BebopData.UppercutDamage)} damage</style>, launching them where you look. Each <style=cIsDamage>explodes for {D(BebopData.UppercutLandDamage)}</style> on landing. " +
                $"Landing a hit gives <style=cIsDamage>+{D(BebopData.UppercutBuffDamage)}</style> beam damage and <style=cIsHealing>heals {D(BebopData.UppercutHeal)} of missing health</style>.");
            LanguageAPI.Add(Token + "HYPER_NAME", "Hyper Beam");
            LanguageAPI.Add(Token + "HYPER_DESC",
                $"Charge, then channel a huge beam for {BebopData.HyperDuration:0}s that deals <style=cIsDamage>{D(BebopData.HyperDps)} damage per second</style> to everything in it and around where it lands, <style=cIsUtility>slowing</style> them and <style=cIsHealing>healing you for all of it</style>. No procs. You move slowly.");
        }
    }

    /// <summary>Humble Pie's beam drawer, and when it last stopped (Deadlock's quick wind-up if it starts again soon).</summary>
    public class BebopGunBeam : BebopBeamFx { public float LastStop = -10f; }

    /// <summary>Hyper Beam's beam drawer.</summary>
    public class BebopHyperBeamFx : BebopBeamFx { }

    /// <summary>The owner's reticle on the enemy a Sticky Bomb would stick to (Seven's Static Charge reticle).</summary>
    public class BebopStickyReticle : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private Indicator? _indicator;
        private static GameObject? _red;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            Transform? at = null;
            GenericSkill? slot = _body.skillLocator != null ? _body.skillLocator.secondary : null;
            if (_body.hasEffectiveAuthority && _body.isPlayerControlled && slot != null && slot.skillDef == BebopSurvivor.StickySkill && slot.stock > 0
                && _body.healthComponent != null && _body.healthComponent.alive)
            {
                CharacterBody? target = ThrowStickyBomb.Target(_body);
                if (target != null) at = target.mainHurtBox != null ? target.mainHurtBox.transform : target.coreTransform;
            }
            if (_indicator == null)
            {
                if (at == null) return;
                _red ??= Kit.Recoloured(EquipmentTargeting.IndicatorPrefab, "CursedAppleBebopStickyIndicator", 0f, effect: false) ?? EquipmentTargeting.IndicatorPrefab; // red (user 2.5)
                _indicator = new Indicator(gameObject, _red);
            }
            _indicator.targetTransform = at;
            _indicator.active = at != null;
        }

        private void OnDestroy() { if (_indicator != null) _indicator.active = false; }
    }

    public class BebopIcons : MonoBehaviour
    {
        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { BebopSurvivor.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Bebop] icons: " + e.Message); }
            Destroy(this);
        }
    }
}
