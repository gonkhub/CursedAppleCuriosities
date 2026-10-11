using System;
using System.Reflection;
using EntityStates;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// The pieces every Deadlock survivor builds the same way: skills and skill slots, its own copy of the stand-in's
    /// skin, an extra entity state machine, and the stand-in's own sounds muted.
    /// </summary>
    public static class DeadlockSurvivorKit
    {
        public static T Skill<T>(string prefix, string token, string id, Type state, string machine, float cooldown, InterruptPriority priority) where T : SkillDef
        {
            var s = ScriptableObject.CreateInstance<T>();
            s.skillName = prefix + id;
            ((ScriptableObject)s).name = s.skillName;
            s.skillNameToken = token + id.ToUpperInvariant() + "_NAME";
            s.skillDescriptionToken = token + id.ToUpperInvariant() + "_DESC";
            s.activationState = new SerializableEntityStateType(state);
            s.activationStateMachineName = machine;
            s.baseRechargeInterval = cooldown;
            s.interruptPriority = priority;
            s.icon = Items.ItemBase.CreatePlaceholderSprite(Color.gray);
            ContentAddition.AddSkillDef(s);
            return s;
        }

        private static readonly FieldInfo SkillFamilyField = typeof(GenericSkill).GetField("_skillFamily", BindingFlags.NonPublic | BindingFlags.Instance);

        public static GenericSkill Slot(GameObject body, string bodyName, string name, params SkillDef[] defs)
        {
            var family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = bodyName + name + "Family";
            var variants = new SkillFamily.Variant[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                variants[i] = new SkillFamily.Variant { skillDef = defs[i], viewableNode = new ViewablesCatalog.Node(defs[i].skillNameToken, false, null) };
            family.variants = variants;
            ContentAddition.AddSkillFamily(family);
            var slot = body.AddComponent<GenericSkill>();
            slot.skillName = name;
            SkillFamilyField.SetValue(slot, family);
            return slot;
        }

        /// <summary>The hero's own copy of the stand-in's default skin (sharing its SkinDef would mix up skin indices).</summary>
        public static void OwnSkin(GameObject model, string skinName, string nameToken)
        {
            var msc = model.GetComponent<ModelSkinController>();
            if (msc == null || msc.skins == null || msc.skins.Length == 0) return;
            SkinDef skin = UnityEngine.Object.Instantiate(msc.skins[0]);
            skin.name = skinName;
            skin.nameToken = nameToken; // rootObject stays the stand-in's: the skin's transform paths are built from it
            msc.skins = new[] { skin };
        }

        private static readonly FieldInfo NetMachines = typeof(NetworkStateMachine).GetField("stateMachines", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// One more entity state machine on a body prefab (e.g. for a skill that runs alongside everything else),
        /// networked, and reset to idle on death.
        /// </summary>
        public static EntityStateMachine AddMachine(GameObject body, string name)
        {
            var esm = body.AddComponent<EntityStateMachine>();
            esm.customName = name;
            esm.initialStateType = new SerializableEntityStateType(typeof(Idle));
            esm.mainStateType = new SerializableEntityStateType(typeof(Idle));
            var net = body.GetComponent<NetworkStateMachine>();
            if (net != null && NetMachines != null)
            {
                var list = (EntityStateMachine[])NetMachines.GetValue(net);
                NetMachines.SetValue(net, Append(list, esm));
            }
            var death = body.GetComponent<CharacterDeathBehavior>();
            if (death != null) death.idleStateMachine = Append(death.idleStateMachine, esm);
            return esm;
        }

        private static EntityStateMachine[] Append(EntityStateMachine[]? list, EntityStateMachine esm)
        {
            var l = new System.Collections.Generic.List<EntityStateMachine>(list ?? Array.Empty<EntityStateMachine>()) { esm };
            return l.ToArray();
        }

        /// <summary>
        /// The stand-in's own Wwise jump, landing, death and footstep sounds would play under the hero's: blank them.
        /// The stand-in's footstep animation events stay (with their dust) and drive the hero's footsteps.
        /// </summary>
        public static void MuteStandIn(GameObject body, GameObject model, string hero)
        {
            var sfx = body.GetComponent<SfxLocator>();
            if (sfx != null) { sfx.jumpSound = ""; sfx.landingSound = ""; sfx.deathSound = ""; sfx.barkSound = ""; }
            var steps = model.GetComponent<FootstepHandler>();
            if (steps != null)
            {
                steps.baseFootstepString = "";
                steps.sprintFootstepOverrideString = "";
                steps.baseFootliftString = "";
                steps.sprintFootliftOverrideString = "";
            }
            model.AddComponent<DeadlockFootsteps>().HeroKey = hero;
        }

        /// <summary>
        /// The stand-in's lobby display twirls its weapon with a sound and spark trails, which would show through the
        /// hero. Its animation events (sounds, spawned effects) are switched off and its particles and trails
        /// stripped; the animation itself keeps running, since lobby item displays hang off that skeleton.
        /// </summary>
        public static void MuteDisplay(GameObject display)
        {
            foreach (Animator a in display.GetComponentsInChildren<Animator>(true)) a.fireEvents = false;
            foreach (ParticleSystem ps in display.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                UnityEngine.Object.DestroyImmediate(ps);
                if (r != null) UnityEngine.Object.DestroyImmediate(r);
            }
            foreach (TrailRenderer t in display.GetComponentsInChildren<TrailRenderer>(true)) UnityEngine.Object.DestroyImmediate(t);
            foreach (MonoBehaviour b in display.GetComponentsInChildren<MonoBehaviour>(true))
                if (b is AnimationEvents || b is PlaySoundOnEvent || b.GetType().Name == "AkEvent" || b.GetType().Name == "AkAmbient") b.enabled = false;
            display.AddComponent<SilentStandInAnimator>();
        }

        /// <summary>
        /// The twirl sound is a PlaySoundOnEnter on the stand-in's animator controller, which is shared with the
        /// vanilla survivor. Unity gives each Animator its own copies of those behaviours: blank only this display's.
        /// </summary>
        private class SilentStandInAnimator : MonoBehaviour
        {
            private void Awake() => Silence();
            private void OnEnable() => Silence();

            private void Silence()
            {
                foreach (Animator a in GetComponentsInChildren<Animator>(true))
                    foreach (RoR2.Mecanim.PlaySoundOnEnter s in a.GetBehaviours<RoR2.Mecanim.PlaySoundOnEnter>())
                    { s.soundString = ""; s.stopSoundString = ""; }
            }
        }

        public static Sprite Sprite(Texture2D t) => UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
    }
}
