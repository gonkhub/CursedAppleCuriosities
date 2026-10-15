using CursedAppleCuriosities.Deadlock;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Survivors.Apollo
{
    public enum ApolloUltKind : byte { Channel, Strike, End, SigilHaste }

    /// <summary>
    /// Owner to server: Itani Lo Sahn's channel began (the mask: 70% less damage taken, Deadlock IncomingDamageReductionPercent),
    /// he struck (the red streak from where he stood to where he lands), or it ended.
    /// </summary>
    public class ApolloUltMessage : INetMessage
    {
        public GameObject? Apollo;
        public ApolloUltKind Kind;
        public Vector3 From, To;

        public void Serialize(NetworkWriter w) { w.Write(Apollo); w.Write((byte)Kind); w.Write(From); w.Write(To); }
        public void Deserialize(NetworkReader r) { Apollo = r.ReadGameObject(); Kind = (ApolloUltKind)r.ReadByte(); From = r.ReadVector3(); To = r.ReadVector3(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || Apollo == null || !Apollo.TryGetComponent(out CharacterBody b) || ApolloSurvivor.UltMaskBuff == null) return;
            switch (Kind)
            {
                case ApolloUltKind.Channel:
                    b.AddTimedBuff(ApolloSurvivor.UltMaskBuff, ApolloData.UltCharge + ApolloData.UltMaxHold + ApolloData.UltLock + 1f);
                    break;
                case ApolloUltKind.Strike:
                    if (ApolloSurvivor.UltStreak != null)
                        EffectManager.SpawnEffect(ApolloSurvivor.UltStreak, new EffectData { origin = To, start = From }, true);
                    ApolloNet.Effect(ApolloSurvivor.UltEffect, To, 2f);
                    break;
                case ApolloUltKind.End:
                    b.ClearTimedBuffs(ApolloSurvivor.UltMaskBuff);
                    break;
                case ApolloUltKind.SigilHaste: // Disengaging Sigil: +25% fire rate for a while (user 2.5)
                    if (ApolloSurvivor.SigilHasteBuff != null) b.AddTimedBuff(ApolloSurvivor.SigilHasteBuff, ApolloData.SigilHasteSeconds * Items.DurationExtender.Multiplier(b));
                    break;
            }
        }
    }

    /// <summary>
    /// Every machine, on Apollo: while the mask buff is on he wears Deadlock's ult mask (models/abilities/fencer_mask_model_fx) on his
    /// head and glows red (a red light and the red fresnel overlay).
    /// </summary>
    public class ApolloUltLook : MonoBehaviour
    {
        private static Material? _overlay;
        private CharacterBody _body = null!;
        private bool _on;
        private GameObject? _mask;
        private Light? _light;
        private TemporaryOverlayInstance? _instance;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            bool want = ApolloSurvivor.UltMaskBuff != null && _body.HasBuff(ApolloSurvivor.UltMaskBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
            if (_light != null) _light.intensity = 2.5f + 0.8f * Mathf.Sin(Time.time * 7f);
        }

        private void Begin()
        {
            _on = true;
            Transform? mt = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
            var swap = mt != null ? mt.GetComponent<DeadlockModelSwap>() : null;
            GameObject? hero = swap != null ? swap.HeroObject : null;
            GlbModel? mask = DeadlockInstall.Model(ApolloData.MaskKey);
            GlbModel? heroModel = DeadlockInstall.Model(ApolloData.Hero);
            Transform? head = hero != null ? FindBone(hero.transform, "head") : null;
            if (mask != null && head != null)
            {
                _mask = Instantiate(mask.Root);
                _mask.name = "CursedAppleApolloMask";
                _mask.SetActive(true);
                // The mask is modelled where his face is in the bind pose (model space), or at its own origin for an attachment:
                // either way it rides his head bone.
                Bounds b = new Bounds();
                bool any = false;
                foreach (Renderer r in _mask.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                _mask.transform.SetParent(head, false);
                if (any && b.center.magnitude > 0.5f && heroModel != null && heroModel.RestInRoot.TryGetValue("head", out Matrix4x4 rest))
                {
                    Matrix4x4 local = rest.inverse;
                    _mask.transform.localPosition = local.MultiplyPoint3x4(Vector3.zero);
                    _mask.transform.localRotation = local.rotation;
                }
                else
                {
                    _mask.transform.localPosition = Vector3.zero;
                    _mask.transform.localRotation = Quaternion.identity;
                }
                _mask.transform.localScale = Vector3.one;
                foreach (Renderer r in _mask.GetComponentsInChildren<Renderer>())
                    foreach (Material m in r.materials)
                        if (m.HasProperty("_EmColor")) { m.SetColor("_EmColor", new Color(1f, 0.15f, 0.1f)); m.SetFloat("_EmPower", 2f); }
            }
            var go = new GameObject("ApolloUltLight");
            go.transform.SetParent(_body.coreTransform != null ? _body.coreTransform : transform, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.1f, 0.08f);
            _light.range = 6f;
            CharacterModel? model = mt != null ? mt.GetComponent<CharacterModel>() : null;
            _overlay ??= UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>("RoR2/Base/Items/AttackSpeedOnCrit/matWolfhatOverlay.mat").WaitForCompletion();
            if (model != null && _overlay != null)
            {
                _instance = TemporaryOverlayManager.AddOverlay(gameObject);
                _instance.duration = float.PositiveInfinity;
                _instance.animateShaderAlpha = false;
                _instance.destroyComponentOnEnd = false;
                _instance.originalMaterial = _overlay;
                _instance.AddToCharacterModel(model);
            }
        }

        private static Transform? FindBone(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private void End()
        {
            _on = false;
            if (_mask != null) Destroy(_mask);
            if (_light != null) Destroy(_light.gameObject);
            _instance?.Destroy();
            _mask = null; _light = null; _instance = null;
        }

        private void OnDestroy() => End();
    }
}
