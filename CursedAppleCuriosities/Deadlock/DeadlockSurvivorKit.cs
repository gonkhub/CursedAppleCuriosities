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

        /// <summary>
        /// The hero's own copies of the stand-in's default skin (sharing its SkinDef would mix up skin indices): the
        /// default, then any extra skins. They only differ in name and icon here; the look is the hero def's Skins,
        /// which DeadlockModelSwap applies by skin index.
        /// </summary>
        public static void OwnSkin(GameObject model, string skinName, string nameToken, UnityEngine.Sprite? icon = null, params (string Name, string Token, UnityEngine.Sprite Icon)[] extra)
        {
            var msc = model.GetComponent<ModelSkinController>();
            if (msc == null || msc.skins == null || msc.skins.Length == 0) return;
            SkinDef standIn = msc.skins[0];
            var skins = new SkinDef[1 + extra.Length];
            skins[0] = CopySkin(standIn, skinName, nameToken, icon);
            for (int i = 0; i < extra.Length; i++) skins[i + 1] = CopySkin(standIn, extra[i].Name, extra[i].Token, extra[i].Icon);
            msc.skins = skins;
        }

        private static SkinDef CopySkin(SkinDef standIn, string skinName, string nameToken, UnityEngine.Sprite? icon)
        {
            SkinDef skin = UnityEngine.Object.Instantiate(standIn);
            skin.name = skinName;
            skin.nameToken = nameToken; // rootObject stays the stand-in's: the skin's transform paths are built from it
            if (icon != null) skin.icon = icon;
            return skin;
        }

        /// <summary>
        /// A skin icon in RoR2's style: a disc split into four wedges (top, right, bottom, left) by thin dark lines,
        /// ringed in the same dark colour. Hero icons use the hero's own palette instead of the stand-in's.
        /// </summary>
        public static UnityEngine.Sprite SkinIcon(Color top, Color right, Color bottom, Color left)
        {
            const int Size = 128;
            Color line = new Color(0.11f, 0.11f, 0.12f);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "texCursedAppleSkinIcon" };
            var px = new Color[Size * Size];
            float c = (Size - 1) / 2f, outer = Size / 2f - 1f, ring = outer - 7f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color col;
                    if (r >= ring) col = line;
                    else if (Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) < 2.2f) col = line; // the diagonals between wedges
                    else if (Mathf.Abs(dy) > Mathf.Abs(dx)) col = dy > 0 ? top : bottom;    // texture rows run bottom-up
                    else col = dx > 0 ? right : left;
                    col.a = Mathf.Clamp01(outer + 0.5f - r); // anti-aliased edge
                    px[y * Size + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply();
            return UnityEngine.Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
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
        /// The stand-in's footstep animation events stay (with their dust); the hero's own feet drive its footsteps.
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

        private static readonly System.Collections.Generic.Dictionary<float, Texture2D> Ramps = new System.Collections.Generic.Dictionary<float, Texture2D>();

        /// <summary>
        /// A vanilla effect recoloured (a copy: the original is untouched). Most RoR2 VFX take their colour from a remap
        /// ramp texture (_RemapTex), so a tint alone wouldn't do: ramps are replaced by one in the new hue, and tints,
        /// particle colours, trails and lights are hue-shifted (brightness and alpha kept).
        /// </summary>
        public static GameObject? Recoloured(GameObject? source, string name, float hue, bool effect)
        {
            if (source == null) return null;
            GameObject copy = source.InstantiateClone(name, false);
            Recolour(copy, hue);
            if (effect)
            {
                // Cloned effects never get an effect pool: pooled-effect leftovers made every tracer log
                // "Tracer: No owning pool set" (2289 times in one Seven run, playtest 2026-10-12). Spawn them unpooled.
                var vfx = copy.GetComponent<VFXAttributes>() ?? copy.AddComponent<VFXAttributes>();
                vfx.DoNotPool = true;
                foreach (EffectManagerHelper helper in copy.GetComponentsInChildren<EffectManagerHelper>(true)) UnityEngine.Object.DestroyImmediate(helper);
                ContentAddition.AddEffect(copy);
            }
            return copy;
        }

        public static void Recolour(GameObject root, float hue)
        {
            Color Shift(Color c)
            {
                Color.RGBToHSV(c, out _, out float s, out float v);
                Color o = Color.HSVToRGB(hue, Mathf.Max(s, 0.6f), v);
                o.a = c.a;
                return o;
            }
            Gradient ShiftG(Gradient g)
            {
                var keys = g.colorKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color);
                var n = new Gradient();
                n.SetKeys(keys, g.alphaKeys);
                n.mode = g.mode;
                return n;
            }
            ParticleSystem.MinMaxGradient ShiftMM(ParticleSystem.MinMaxGradient m)
            {
                switch (m.mode)
                {
                    case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Shift(m.color));
                    case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Shift(m.colorMin), Shift(m.colorMax));
                    case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(ShiftG(m.gradient));
                    case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(ShiftG(m.gradientMin), ShiftG(m.gradientMax));
                    default: return m;
                }
            }
            if (!Ramps.TryGetValue(hue, out Texture2D ramp))
            {
                // Dark to the hue to near-white, like RoR2's own ramps.
                ramp = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "texRampCursedApple" + hue };
                for (int x = 0; x < 256; x++)
                {
                    float t = x / 255f;
                    Color c = t < 0.6f ? Color.Lerp(Color.black, Color.HSVToRGB(hue, 0.9f, 1f), t / 0.6f) : Color.Lerp(Color.HSVToRGB(hue, 0.9f, 1f), new Color(1f, 0.95f, 0.8f), (t - 0.6f) / 0.4f);
                    c.a = t;
                    ramp.SetPixel(x, 0, c);
                }
                ramp.Apply();
                Ramps[hue] = ramp;
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]);
                    if (m.HasProperty("_RemapTex") && m.GetTexture("_RemapTex") != null) m.SetTexture("_RemapTex", ramp);
                    foreach (string p in new[] { "_TintColor", "_Color", "_EmColor", "_EmissionColor" })
                        if (m.HasProperty(p)) m.SetColor(p, Shift(m.GetColor(p)));
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                if (r is TrailRenderer tr) tr.colorGradient = ShiftG(tr.colorGradient);
                if (r is LineRenderer lr) lr.colorGradient = ShiftG(lr.colorGradient);
            }
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = ShiftMM(main.startColor);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = ShiftMM(col.color);
            }
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) l.color = Shift(l.color);
        }

        /// <summary>The first of several Addressables keys that loads (a missing asset costs its effect, never the hero).</summary>
        public static T? TryLoad<T>(string tag, params string[] keys) where T : UnityEngine.Object
        {
            foreach (string key in keys)
            {
                try
                {
                    T asset = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<T>(key).WaitForCompletion();
                    if (asset != null) return asset;
                }
                catch (Exception) { }
            }
            CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[{tag}] asset not found: {string.Join(" | ", keys)}");
            return null;
        }

        /// <summary>
        /// A hero projectile from a vanilla one: its model (ghost), flight and networking are kept; every other
        /// RoR2.Projectile behaviour (impact, explosion, beams, sticking) is stripped so the hero's own component
        /// decides what a hit does. Triggers pass through enemies (the behaviour looks for walls itself).
        /// </summary>
        /// <param name="steer">Keep pushing it along its facing every tick (lets the behaviour change its speed, as Lightning Ball's
        /// crawl does). Off: launched once and left to fly (Vyper's bola).</param>
        public static GameObject? CloneProjectile(GameObject? source, string name, Type behaviour, float speed, float lifetime, bool gravity, bool triggers, bool steer = true)
        {
            if (source == null) return null;
            GameObject p = source.InstantiateClone(name, true);
            // Strip dependants before what they depend on: a few passes.
            for (int pass = 0; pass < 3; pass++)
                foreach (MonoBehaviour b in p.GetComponents<MonoBehaviour>())
                {
                    if (b == null) continue;
                    // ApplyTorqueOnStart (Commando's grenade) spins it on launch: with the facing steering the velocity, it corkscrewed.
                    bool keep = b is RoR2.Projectile.ProjectileController || b is RoR2.Projectile.ProjectileSimple || b is RoR2.Projectile.ProjectileDamage ||
                                b is RoR2.Projectile.ProjectileNetworkTransform || b.GetType().Namespace != "RoR2.Projectile" && b.GetType().Name != "ApplyTorqueOnStart";
                    if (!keep) UnityEngine.Object.DestroyImmediate(b);
                }
            var simple = p.GetComponent<RoR2.Projectile.ProjectileSimple>() ?? p.AddComponent<RoR2.Projectile.ProjectileSimple>();
            simple.desiredForwardSpeed = speed;
            simple.lifetime = lifetime;
            simple.updateAfterFiring = steer;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) { rb.useGravity = gravity; rb.angularVelocity = Vector3.zero; }
            if (triggers) foreach (Collider c in p.GetComponentsInChildren<Collider>()) c.isTrigger = true;
            p.AddComponent(behaviour);
            ContentAddition.AddProjectile(p);
            return p;
        }
    }
}
