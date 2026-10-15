using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Items;
using EntityStates;
using EntityStates.CursedApple.Framework;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Framework
{
    /// <summary>
    /// A Deadlock hero waiting for its kit (user, 2026-10-14: frame Bebop, Ivy and Apollo now, design them later). Everything
    /// that doesn't depend on design decisions is real: model, animations, movement, sounds, VO, pings, icons, stats and the
    /// Deadlock gun. The four abilities are stubs that play Deadlock's cast animation and line and do nothing else. When a
    /// hero's kit is decided it graduates to its own files, like Vyper's.
    /// </summary>
    public class FrameworkHero
    {
        public string Hero = "", Name = "", Subtitle = "", Vo = "", Description = "";
        public string Token => "CURSEDAPPLE_" + Hero.ToUpperInvariant() + "_";
        public string BodyName => "CursedApple" + Name + "Body";
        public string MuzzleName => Name + "Muzzle";
        public Color Color = Color.white;
        public float SortPosition = 120f;

        // Stats (Deadlock's, scaled like the other heroes: about 1 RoR2 health per 7.5 Deadlock health).
        public float BaseHealth = 100f, HealthPerLevel = 30f, BaseArmor, BaseSpeed = 7f, BaseRegen = 2f;
        public DeadlockMovementProfile Movement = new DeadlockMovementProfile();

        // The Deadlock gun: hitscan, Shots a burst, Pellets a shot.
        public float GunDamage = 0.5f, GunCycle = 0.2f, GunSpread = 0.5f, GunProc = 1f;
        public int Pellets = 1, Burst = 1;
        public float BurstInterval;
        public float FalloffStart = 20f, FalloffEnd = 50f, FalloffMin = 0.1f;
        public string GunSound = "";

        public FrameworkAbility[] Abilities = Array.Empty<FrameworkAbility>(); // Deadlock's signature 1-4, in order

        public DeadlockHeroDef Def = new DeadlockHeroDef();
        public DeadlockSound[] Sounds = Array.Empty<DeadlockSound>();
        public string IconPortrait = "";

        // Built
        public GameObject? BodyPrefab, DisplayPrefab;
        public SurvivorDef? Survivor;
        internal SkillDef? Gun;

        internal static readonly Dictionary<string, FrameworkHero> ByBody = new Dictionary<string, FrameworkHero>();
        internal static GameObject? Tracer, Hit, Muzzle;

        public static FrameworkHero? Of(GameObject body) =>
            body != null && body.TryGetComponent(out CharacterBody b) && ByBody.TryGetValue(BodyCatalog.GetBodyName(b.bodyIndex) ?? "", out FrameworkHero h) ? h : null;

        public void Init(ConfigFile config, GameObject host)
        {
            Movement.Bind(config, Name);
            BaseHealth = config.Bind(Name, "Base Health", BaseHealth, $"{Name} max health at level 1.").Value;
            foreach (DeadlockSound s in Sounds) DeadlockSounds.Register(s);
            DeadlockInstall.WantIcon(IconPortrait);
            foreach (FrameworkAbility a in Abilities) if (a.Icon != "") DeadlockInstall.WantIcon(a.Icon);
            DeadlockHeroes.Register(Def);
            DeadlockInstall.WantModel(Def.ModelRequest());
            ByBody[BodyName] = this;

            Tracer ??= Kit.TryLoad<GameObject>(Name, "RoR2/Base/Commando/TracerCommandoDefault.prefab");
            Hit ??= Kit.TryLoad<GameObject>(Name, "RoR2/Base/Commando/HitsparkCommando.prefab");
            Muzzle ??= Kit.TryLoad<GameObject>(Name, "RoR2/Base/Bandit2/MuzzleflashBandit2.prefab");
            FrameworkStates.Register();

            Gun = Kit.Skill<SkillDef>("CursedApple" + Name, Token, "Gun", typeof(FrameworkGun), "Weapon", 0f, InterruptPriority.Any);
            Gun.cancelSprintingOnActivation = true;
            Gun.mustKeyPress = false;
            for (int i = 0; i < Abilities.Length; i++)
            {
                FrameworkAbility a = Abilities[i];
                a.Skill = Kit.Skill<SkillDef>("CursedApple" + Name, Token, "Ability" + (i + 1), FrameworkStates.AbilityState(i), "Weapon", a.Cooldown, InterruptPriority.Skill);
                a.Skill.mustKeyPress = true;
                a.Skill.cancelSprintingOnActivation = false;
            }
            BodyPrefab = CreateBody();
            DisplayPrefab = CreateDisplay();
            CreateSurvivor();
            AddLanguage();
            DeadlockHeroVoice.Track(BodyName, Hero, Vo);
            DeadlockPingVoice.Track(BodyName, Hero, Vo);
            host.AddComponent<FrameworkIcons>().Hero = this;
        }

        private SkillDef[] Slot(params int[] indices)
        {
            var l = new List<SkillDef>();
            foreach (int i in indices) if (i < Abilities.Length && Abilities[i].Skill != null) l.Add(Abilities[i].Skill!);
            return l.ToArray();
        }

        private GameObject CreateBody()
        {
            GameObject body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().InstantiateClone(BodyName, true);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = Token + "NAME";
            cb.subtitleNameToken = Token + "SUBTITLE";
            cb.bodyColor = Color;
            cb.baseMaxHealth = BaseHealth;
            cb.levelMaxHealth = HealthPerLevel;
            cb.baseArmor = BaseArmor;
            cb.baseDamage = 12f;
            cb.levelDamage = 2.4f;
            cb.baseMoveSpeed = BaseSpeed;
            cb.baseRegen = BaseRegen;
            cb.levelRegen = BaseRegen * 0.2f;

            foreach (GenericSkill old in body.GetComponents<GenericSkill>()) UnityEngine.Object.DestroyImmediate(old);
            var locator = body.GetComponent<SkillLocator>();
            // Until the kit is designed: abilities 1 and 3 share the secondary slot, 2 is the utility, 4 the special.
            locator.primary = Kit.Slot(body, BodyName, "Primary", Gun!);
            locator.secondary = Kit.Slot(body, BodyName, "Secondary", Slot(0, 2));
            locator.utility = Kit.Slot(body, BodyName, "Utility", Slot(1));
            locator.special = Kit.Slot(body, BodyName, "Special", Slot(3));
            locator.passiveSkill = new SkillLocator.PassiveSkill { enabled = false };
            DeadlockMovement.Apply(body, Movement);

            Transform model = body.GetComponent<ModelLocator>().modelTransform;
            model.gameObject.AddComponent<DeadlockModelSwap>().HeroKey = Hero;
            Kit.MuteStandIn(body, model.gameObject, Hero);
            var children = model.GetComponent<ChildLocator>();
            if (children != null && children.FindChild(MuzzleName) == null) children.AddChild(MuzzleName, model);
            Kit.OwnSkin(model.gameObject, "skinCursedApple" + Name + "DEFAULT", Token + "SKIN_DEFAULT");
            ContentAddition.AddBody(body);
            return body;
        }

        private GameObject CreateDisplay()
        {
            GameObject display = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion().InstantiateClone("CursedApple" + Name + "Display", false);
            Kit.MuteDisplay(display);
            var cm = display.GetComponentInChildren<CharacterModel>(true);
            if (cm != null)
            {
                var swap = cm.gameObject.AddComponent<DeadlockModelSwap>();
                swap.HeroKey = Hero;
                swap.IsDisplay = true;
                Kit.OwnSkin(cm.gameObject, "skinCursedApple" + Name + "DISPLAY", Token + "SKIN_DEFAULT");
            }
            return display;
        }

        private void CreateSurvivor()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "CursedApple" + Name;
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab;
            Survivor.displayNameToken = Token + "NAME";
            Survivor.descriptionToken = Token + "DESCRIPTION";
            Survivor.outroFlavorToken = Token + "OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = Token + "FAILURE";
            Survivor.primaryColor = Color;
            Survivor.desiredSortPosition = SortPosition;
            ContentAddition.AddSurvivorDef(Survivor);
        }

        private void AddLanguage()
        {
            LanguageAPI.Add(Token + "NAME", Name);
            LanguageAPI.Add(Token + "SUBTITLE", Subtitle);
            LanguageAPI.Add(Token + "SKIN_DEFAULT", "Default");
            LanguageAPI.Add(Token + "DESCRIPTION", Description + "<color=#CCD3E0>\n\n< ! > Work in progress: the abilities are placeholders until the kit is designed.</color>\n");
            LanguageAPI.Add(Token + "OUTRO", "..and so they left.");
            LanguageAPI.Add(Token + "FAILURE", "..and so they vanished.");
            LanguageAPI.Add(Token + "GUN_NAME", "Weapon");
            LanguageAPI.Add(Token + "GUN_DESC", $"Shoot for <style=cIsDamage>{GunDamage * 100f:0}% damage</style>{(Pellets > 1 ? $" x{Pellets} pellets" : "")}{(Burst > 1 ? $" in {Burst}-round bursts" : "")}.");
            for (int i = 0; i < Abilities.Length; i++)
            {
                LanguageAPI.Add(Token + "ABILITY" + (i + 1) + "_NAME", Abilities[i].Name);
                LanguageAPI.Add(Token + "ABILITY" + (i + 1) + "_DESC", "<style=cIsUtility>Not designed yet.</style> " + Abilities[i].Deadlock);
            }
        }

        internal void ApplyIcons()
        {
            Texture2D? portrait = DeadlockInstall.Icon(IconPortrait);
            if (portrait != null && BodyPrefab != null) BodyPrefab.GetComponent<CharacterBody>().portraitIcon = portrait;
            if (portrait != null && Gun != null) Gun.icon = Kit.Sprite(portrait);
            foreach (FrameworkAbility a in Abilities)
            {
                Texture2D? t = a.Icon != "" ? DeadlockInstall.Icon(a.Icon) : null;
                if (t != null && a.Skill != null) a.Skill.icon = Kit.Sprite(t);
            }
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[{Name}] icons: {(portrait != null ? "from Deadlock" : "placeholders (no Deadlock icons cached)")}");
        }
    }

    /// <summary>One Deadlock ability awaiting design: its name, Deadlock's description of it, icon, animation state, VO and cast sound.</summary>
    public class FrameworkAbility
    {
        public string Name = "", Deadlock = "", Icon = "", Anim = "", Vo = "", Sound = "";
        public float Cooldown = 8f;
        internal SkillDef? Skill;
    }

    public class FrameworkIcons : MonoBehaviour
    {
        public FrameworkHero? Hero;

        private void Update()
        {
            if (!DeadlockInstall.FilesDone) return;
            try { Hero?.ApplyIcons(); }
            catch (Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[{Hero?.Name}] icons: " + e.Message); }
            Destroy(this);
        }
    }
}

namespace EntityStates.CursedApple.Framework
{
    using CursedAppleCuriosities.Survivors.Framework;

    internal static class FrameworkStates
    {
        private static bool _registered;
        private static readonly Type[] Abilities = { typeof(FrameworkAbility1), typeof(FrameworkAbility2), typeof(FrameworkAbility3), typeof(FrameworkAbility4) };

        public static Type AbilityState(int i) => Abilities[Mathf.Clamp(i, 0, Abilities.Length - 1)];

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            ContentAddition.AddEntityState(typeof(FrameworkGun), out _);
            foreach (Type t in Abilities) ContentAddition.AddEntityState(t, out _);
        }
    }

    /// <summary>A framework hero's Deadlock gun: hitscan pellets in bursts, with Deadlock's falloff. No reload, like the others.</summary>
    public class FrameworkGun : BaseSkillState
    {
        private FrameworkHero? _h;
        private float _duration;
        private int _fired;
        private string _muzzle = "MuzzleRight";

        public override void OnEnter()
        {
            base.OnEnter();
            _h = FrameworkHero.Of(gameObject);
            if (_h == null) { if (isAuthority) outer.SetNextStateToMain(); return; }
            _duration = (_h.BurstInterval * (_h.Burst - 1) + _h.GunCycle) / attackSpeedStat;
            Transform? gun = FindModelChild(_h.MuzzleName);
            if (gun != null && gun.name == _h.Def.MuzzleBone) _muzzle = _h.MuzzleName;
            StartAimMode(2f);
            Fire();
        }

        private void Fire()
        {
            FrameworkHero h = _h!;
            _fired++;
            DeadlockModelSwap.Play(gameObject, "shoot");
            if (h.GunSound != "") DeadlockSounds.Play(h.GunSound, transform.position, gameObject);
            if (FrameworkHero.Muzzle != null) EffectManager.SimpleMuzzleFlash(FrameworkHero.Muzzle, gameObject, _muzzle, false);
            AddRecoil(-0.4f, -0.6f, -0.2f, 0.2f);
            if (!isAuthority) return;
            Ray aim = GetAimRay();
            new BulletAttack
            {
                owner = gameObject,
                weapon = gameObject,
                origin = aim.origin,
                aimVector = aim.direction,
                bulletCount = (uint)Mathf.Max(1, h.Pellets),
                minSpread = 0f,
                maxSpread = h.GunSpread,
                damage = h.GunDamage * damageStat,
                procCoefficient = h.GunProc,
                force = 80f,
                tracerEffectPrefab = FrameworkHero.Tracer,
                hitEffectPrefab = FrameworkHero.Hit,
                muzzleName = _muzzle,
                isCrit = RollCrit(),
                radius = 0.15f,
                smartCollision = true,
                falloffModel = BulletAttack.FalloffModel.None,
                modifyOutgoingDamageCallback = (BulletAttack _, ref BulletAttack.BulletHit hit, DamageInfo info) =>
                    info.damage *= CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit.Falloff(hit.distance, h.FalloffStart, h.FalloffEnd, h.FalloffMin),
                damageType = DamageSource.Primary,
            }.Fire();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (_h == null) return;
            if (_fired < _h.Burst && fixedAge >= _h.BurstInterval / attackSpeedStat * _fired) Fire();
            if (fixedAge >= _duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    /// <summary>An ability waiting for its design: Deadlock's cast animation, line and sound, nothing else.</summary>
    public abstract class FrameworkAbilityBase : BaseSkillState
    {
        protected abstract int Index { get; }

        public override void OnEnter()
        {
            base.OnEnter();
            FrameworkHero? h = FrameworkHero.Of(gameObject);
            FrameworkAbility? a = h != null && Index < h.Abilities.Length ? h.Abilities[Index] : null;
            if (a != null)
            {
                if (a.Anim != "") DeadlockModelSwap.Play(gameObject, a.Anim);
                if (a.Sound != "") DeadlockSounds.Play(a.Sound, transform.position, gameObject);
                if (a.Vo != "") DeadlockHeroVoice.Power(a.Vo, gameObject);
                if (isAuthority) CursedAppleCuriosities.CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[{h!.Name}] {a.Name}: placeholder (kit not designed yet)");
            }
            if (isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    public class FrameworkAbility1 : FrameworkAbilityBase { protected override int Index => 0; }
    public class FrameworkAbility2 : FrameworkAbilityBase { protected override int Index => 1; }
    public class FrameworkAbility3 : FrameworkAbilityBase { protected override int Index => 2; }
    public class FrameworkAbility4 : FrameworkAbilityBase { protected override int Index => 3; }
}
