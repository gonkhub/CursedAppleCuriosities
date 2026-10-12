using System.Collections.Generic;
using CursedAppleCuriosities.Deadlock;
using CursedAppleCuriosities.Survivors.Lash;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// Shared visuals and sounds for equipment (and a few items):
    /// one-shot vanilla effects, Deadlock sounds sent to every client, and per-buff visuals + sound loops.
    /// Sounds come from the player's own Deadlock install (see DeadlockSounds); players without it hear the
    /// vanilla Wwise fallback when one is set and the sound follows a body.
    /// </summary>
    public static class EquipmentFx
    {
        private static readonly Dictionary<string, GameObject?> _prefabs = new Dictionary<string, GameObject?>();
        private static readonly HashSet<string> _failed = new HashSet<string>();

        /// <summary>A vanilla prefab by Addressables key, cached (null and logged once if it can't load).</summary>
        public static GameObject? Prefab(string key)
        {
            if (_prefabs.TryGetValue(key, out var cached) && cached != null) return cached;
            GameObject? prefab = null;
            string why = "";
            try
            {
                var handle = Addressables.LoadAssetAsync<GameObject>(key);
                prefab = handle.WaitForCompletion();
                if (prefab == null) why = $" (status {handle.Status}{(handle.OperationException != null ? ": " + handle.OperationException.Message : "")})";
            }
            catch (System.Exception e) { why = ": " + e.Message; }
            // A failed load isn't cached (playtest 2026-10-12: real keys came back null mid-run, and the cached null
            // hid those effects for the rest of the session); it is retried next time, logged once per key.
            if (prefab != null) _prefabs[key] = prefab;
            else if (_failed.Add(key)) CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] FX prefab not loaded: {key}{why}");
            return prefab;
        }

        /// <summary>Server: a vanilla effect for everyone.</summary>
        public static void Effect(string key, Vector3 origin, float scale)
        {
            var prefab = Prefab(key);
            if (prefab != null) EffectManager.SpawnEffect(prefab, new EffectData { origin = origin, scale = scale }, true);
        }

        /// <summary>Server: a Deadlock sound for everyone, following the body when given.</summary>
        public static void Sound(string id, Vector3 point, GameObject? follow = null)
        {
            if (NetworkServer.active) SoundMessage.Everyone(id, point, follow);
        }

        public static void RegisterSound(string id, string vpkPrefix, float volume = 0.9f, float range = 50f, float minGap = 0.1f, string? fallback = null)
            => DeadlockSounds.Register(new DeadlockSound { Id = id, VpkPrefix = vpkPrefix, Volume = volume, Range = range, MinGap = minGap, Fallback = fallback });

        // ── Buff visuals ─────────────────────────────────────────────────────────

        internal class BuffFx
        {
            public BuffDef Buff = null!;
            public string? PrefabKey;
            public string? LoopSound;
        }

        internal static readonly List<BuffFx> Registered = new List<BuffFx>();
        private static bool _hooked;

        /// <summary>
        /// While a body has the buff, show a vanilla TemporaryVisualEffect prefab on it (the kind the game uses for
        /// Tonic, Death Mark, etc.) and/or loop a Deadlock sound. Runs on every client: it follows the synced buff.
        /// </summary>
        public static void RegisterBuffFx(BuffDef? buff, string? prefabKey, string? loopSound = null)
        {
            if (buff == null) return;
            Registered.Add(new BuffFx { Buff = buff, PrefabKey = prefabKey, LoopSound = loopSound });
            if (_hooked) return;
            _hooked = true;
            // The game refreshes its own buff visuals here, on every client, whenever stats or buffs change.
            On.RoR2.CharacterBody.UpdateAllTemporaryVisualEffects += (orig, self) =>
            {
                orig(self);
                var holder = self.GetComponent<BuffFxHolder>();
                if (holder == null)
                {
                    bool any = false;
                    foreach (var fx in Registered)
                        if (fx.Buff.buffIndex != BuffIndex.None && self.HasBuff(fx.Buff.buffIndex)) { any = true; break; }
                    if (!any) return;
                    holder = self.gameObject.AddComponent<BuffFxHolder>();
                }
                holder.Sync();
            };
        }
    }

    /// <summary>Per body: the visual instances and sound loops for EquipmentFx's registered buffs.</summary>
    public class BuffFxHolder : MonoBehaviour
    {
        private CharacterBody _body = null!;
        private readonly Dictionary<EquipmentFx.BuffFx, TemporaryVisualEffect?> _visuals = new Dictionary<EquipmentFx.BuffFx, TemporaryVisualEffect?>();
        private readonly Dictionary<EquipmentFx.BuffFx, DeadlockVoice?> _loops = new Dictionary<EquipmentFx.BuffFx, DeadlockVoice?>();

        private void Awake() => _body = GetComponent<CharacterBody>();

        public void Sync()
        {
            if (_body == null) return;
            foreach (var fx in EquipmentFx.Registered)
            {
                bool active = fx.Buff.buffIndex != BuffIndex.None && _body.HasBuff(fx.Buff.buffIndex)
                    && _body.healthComponent != null && _body.healthComponent.alive;
                SyncVisual(fx, active && !_body.hideExtraEffects);
                SyncLoop(fx, active);
            }
        }

        private void SyncVisual(EquipmentFx.BuffFx fx, bool active)
        {
            if (fx.PrefabKey == null) return;
            _visuals.TryGetValue(fx, out var current);
            if (active && current == null)
            {
                var prefab = EquipmentFx.Prefab(fx.PrefabKey);
                if (prefab == null) return;
                var go  = Instantiate(prefab, _body.corePosition, Quaternion.identity);
                var tve = go.GetComponent<TemporaryVisualEffect>();
                if (tve == null)
                {
                    CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] {fx.PrefabKey} is not a TemporaryVisualEffect");
                    Destroy(go);
                    fx.PrefabKey = null; // don't retry every refresh
                    return;
                }
                tve.parentTransform = _body.coreTransform != null ? _body.coreTransform : _body.transform;
                tve.visualState     = TemporaryVisualEffect.VisualState.Enter;
                tve.healthComponent = _body.healthComponent;
                tve.radius          = _body.bestFitRadius;
                var cam = go.GetComponent<LocalCameraEffect>();
                if (cam != null) cam.targetCharacter = _body.gameObject;
                _visuals[fx] = tve;
            }
            else if (!active && current != null)
            {
                current.visualState = TemporaryVisualEffect.VisualState.Exit; // plays its outro and destroys itself
                _visuals[fx] = null;
            }
        }

        private void SyncLoop(EquipmentFx.BuffFx fx, bool active)
        {
            if (fx.LoopSound == null) return;
            _loops.TryGetValue(fx, out var voice);
            if (active && voice == null)
                _loops[fx] = DeadlockSounds.Loop(fx.LoopSound, _body.gameObject, 3600f);
            else if (!active && voice != null)
            {
                DeadlockSounds.Stop(voice);
                _loops[fx] = null;
            }
        }

        private void OnDestroy()
        {
            foreach (var tve in _visuals.Values)
                if (tve != null) tve.visualState = TemporaryVisualEffect.VisualState.Exit;
            foreach (var voice in _loops.Values) DeadlockSounds.Stop(voice);
        }
    }
}
