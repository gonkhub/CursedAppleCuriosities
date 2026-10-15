using System;
using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Unlocks;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// A district's secret (3.0, user): a blacked-out silhouette of a hero standing at a hand-marked spot
    /// (<see cref="MapSectionDef.Secrets"/>). Touching it unlocks that hero for everyone in the run. It only appears while
    /// someone in the run still lacks the hero.
    ///
    /// The interactable is a Shrine of Order with its shrine behaviour and visuals taken off (its PurchaseInteraction, free,
    /// is the networked "use"); the silhouette itself is the hero's own Deadlock model in its idle, solid black, built on
    /// every machine from that machine's converted model.
    /// </summary>
    public static class HeroSilhouette
    {
        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[Secret] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Secret] " + s);

        public const string NameToken = "CURSEDAPPLE_SILHOUETTE_NAME";
        public const string ContextToken = "CURSEDAPPLE_SILHOUETTE_CONTEXT";

        /// <summary>One networked prefab per secret hero (the hero is baked in, so clients know whose silhouette it is).</summary>
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static GameObject? _useEffect;
        internal static Material? Black;

        /// <summary>At load: a prefab for each hero some district hides (only heroes this build registers).</summary>
        internal static void Init(IEnumerable<MapSectionDef> districts)
        {
            LanguageAPI.Add(NameToken, "???");
            LanguageAPI.Add(ContextToken, "Inspect the silhouette");
            GameObject? shrine = Load<GameObject>("RoR2/Base/ShrineRestack/ShrineRestack.prefab");
            _useEffect = Load<GameObject>("RoR2/Base/Common/VFX/ShrineUseEffect.prefab");
            if (shrine == null) { Warn("Shrine of Order prefab not found: no silhouettes."); return; }
            foreach (MapSectionDef d in districts)
                foreach (SecretSpot s in d.Secrets)
                {
                    if (_prefabs.ContainsKey(s.Hero)) continue;
                    try { _prefabs[s.Hero] = Build(shrine, s.Hero); }
                    catch (Exception e) { Warn($"{s.Hero}: prefab failed: {e}"); }
                }
        }

        private static GameObject Build(GameObject shrine, string hero)
        {
            GameObject p = shrine.InstantiateClone("CursedAppleSilhouette" + hero.Replace(" ", ""), true);
            foreach (MonoBehaviour mb in p.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "ShrineRestackBehavior") UnityEngine.Object.DestroyImmediate(mb);
            // The shrine's colliders stay (they're what you look at to use it); everything it shows goes (particles included: theirs are renderers too).
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Light l in p.GetComponentsInChildren<Light>(true)) l.enabled = false;

            var pi = p.GetComponent<PurchaseInteraction>();
            pi.displayNameToken = NameToken;
            pi.contextToken = ContextToken;
            pi.costType = CostTypeIndex.None;
            pi.cost = 0;
            pi.available = true;
            pi.isShrine = false;
            pi.isGoldShrine = false;
            pi.setUnavailableOnTeleporterActivated = false;
            var names = p.GetComponent<GenericDisplayNameProvider>() ?? p.AddComponent<GenericDisplayNameProvider>();
            names.displayToken = NameToken;

            p.AddComponent<SilhouetteLook>().Hero = hero;
            return p;
        }

        private static T? Load<T>(string key) where T : UnityEngine.Object
        {
            try { return Addressables.LoadAssetAsync<T>(key).WaitForCompletion(); }
            catch { return null; }
        }

        /// <summary>Server, once the stage is ready: a silhouette at each of the district's secret spots someone still lacks.</summary>
        internal static void SpawnAll(MapSectionDef def, Transform world)
        {
            if (!NetworkServer.active) return;
            foreach (SecretSpot s in def.Secrets)
            {
                if (!_prefabs.TryGetValue(s.Hero, out GameObject prefab) || SurvivorUnlocks.Of(s.Hero) == null) continue;
                if (SurvivorUnlocks.EveryoneHas(s.Hero)) { Log($"{s.Hero}: everyone has them, no silhouette"); continue; }
                Vector3 at = world.TransformPoint(new Vector3(s.X, s.Y, s.Z));
                GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.Euler(0f, s.Yaw + world.eulerAngles.y, 0f));
                NetworkServer.Spawn(go);
                Log($"{s.Hero}'s silhouette stands at {at}");
            }
        }

        /// <summary>Server: someone touched a silhouette.</summary>
        internal static void Found(SilhouetteLook look, Interactor? who)
        {
            if (!NetworkServer.active || look.FoundServer) return;
            look.FoundServer = true;
            SurvivorUnlocks.GrantToEveryone(look.Hero);
            CharacterBody? body = who != null ? who.GetComponent<CharacterBody>() : null;
            string finder = body != null && body.master != null ? Util.GetBestMasterName(body.master) : "Someone";
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = $"<color=#c9a0ff>{finder} found {look.Hero}'s silhouette. {look.Hero} is unlocked for everyone!</color>",
            });
            if (_useEffect != null)
                EffectManager.SpawnEffect(_useEffect, new EffectData { origin = look.transform.position, rotation = Quaternion.identity, scale = 2f, color = new Color(0.6f, 0.3f, 1f) }, true);
            Log($"{finder} found {look.Hero}");
        }
    }

    /// <summary>
    /// On the silhouette (every machine): stands the hero's Deadlock model in its idle, all black, and once it's found
    /// (its PurchaseInteraction turns unavailable, synced) says the hero's select line and fades away; the server removes it.
    /// </summary>
    public class SilhouetteLook : MonoBehaviour
    {
        public string Hero = "";
        internal bool FoundServer;

        private PurchaseInteraction? _pi;
        private GameObject? _model;
        private DeadlockHeroDef? _def;
        private float _nextTry, _fadeFrom = -1f;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private void Awake()
        {
            _pi = GetComponent<PurchaseInteraction>();
            if (_pi != null) _pi.onPurchase.AddListener(OnPurchase);
        }

        private void OnPurchase(Interactor who)
        {
            if (!NetworkServer.active || _pi == null) return;
            _pi.SetAvailable(false);
            HeroSilhouette.Found(this, who);
        }

        private void Update()
        {
            if (_model == null && Time.unscaledTime >= _nextTry) { _nextTry = Time.unscaledTime + 0.5f; TryBuild(); }
            if (_pi != null && !_pi.available && _fadeFrom < 0f)
            {
                _fadeFrom = Time.time;
                if (_def != null) DeadlockSounds.Play(_def.Sound("vo_select"), transform.position);
            }
            if (_fadeFrom >= 0f)
            {
                // Sinks into the floor over 1.5 s, then the server takes the object away.
                float t = (Time.time - _fadeFrom) / 1.5f;
                if (_model != null) _model.transform.localPosition = Vector3.down * (t * 3f) / Mathf.Max(1e-4f, transform.lossyScale.y);
                if (t >= 1f)
                {
                    if (_model != null) _model.SetActive(false);
                    if (NetworkServer.active) NetworkServer.Destroy(gameObject);
                }
            }
        }

        private void TryBuild()
        {
            string key = Hero.ToLowerInvariant().Replace(" ", "");
            _def ??= DeadlockHeroes.Get(key);
            GlbModel? template = _def != null ? DeadlockInstall.Model(_def.Hero) : null;
            if (_def == null || template == null) return; // still converting, or no Deadlock: the spot stays invisible but usable

            _model = Instantiate(template.Root, transform, false);
            _model.name = "Silhouette_" + _def.DisplayName;
            // Face the spot's forward (the hips run left to right), as the hero models do.
            Quaternion yaw = Quaternion.identity;
            if (template.RestInRoot.TryGetValue("leg_upper_L", out Matrix4x4 ll) && template.RestInRoot.TryGetValue("leg_upper_R", out Matrix4x4 lr))
            {
                Vector3 right = (Vector3)lr.GetColumn(3) - (Vector3)ll.GetColumn(3);
                right.y = 0f;
                if (right.sqrMagnitude > 1e-6f) yaw = Quaternion.FromToRotation(right.normalized, Vector3.right);
            }
            _model.transform.localRotation = yaw;
            _model.transform.localPosition = Vector3.zero;
            // In-game size: the body's capsule height times the hero's own height scale (as on the body).
            float height = 1.9f * _def.ModelHeightScale;
            SurvivorDef? sd = SurvivorUnlocks.SurvivorOf(Hero);
            CapsuleCollider? capsule = sd != null && sd.bodyPrefab != null ? sd.bodyPrefab.GetComponent<CapsuleCollider>() : null;
            if (capsule != null) height = capsule.height * sd!.bodyPrefab.transform.localScale.y * _def.ModelHeightScale;
            _model.transform.localScale = Vector3.one * (height / Mathf.Max(0.1f, template.Height) / Mathf.Max(1e-4f, transform.lossyScale.x));
            _model.SetActive(true);

            HeroSilhouette.Black ??= MakeBlack();
            foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = HeroSilhouette.Black;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _renderers.Add(r);
            }
            // Its idle (a display animator: no body, it just breathes).
            var anim = _model.AddComponent<DeadlockAnimator>();
            anim.Init(_def, null, transform, true);

            // Looking at it outlines the hero, not the (hidden) shrine.
            var hl = GetComponent<Highlight>();
            if (hl != null && _renderers.Count > 0) hl.targetRenderer = _renderers[0];
        }

        private static Material MakeBlack()
        {
            Material m = DeadlockHeroes.MakeMaterial("Silhouette", "black", null);
            m.color = Color.black;
            if (m.HasProperty("_SpecularStrength")) m.SetFloat("_SpecularStrength", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            return m;
        }
    }
}
