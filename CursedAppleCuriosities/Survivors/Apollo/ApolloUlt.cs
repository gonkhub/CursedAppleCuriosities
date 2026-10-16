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
                    // A strike up (or off a ledge) leaves him in the air when the lock ends: no fall damage on that landing.
                    CursedAppleCuriosities.Deadlock.DeadlockFallGuard.ProtectNextLanding(b.gameObject, 10f);
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
        private float _nextScan;
        private GameObject? _mask;
        private Light? _light;
        private TemporaryOverlayInstance? _instance;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void LateUpdate()
        {
            bool want = ApolloSurvivor.UltMaskBuff != null && _body.HasBuff(ApolloSurvivor.UltMaskBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
            if (_light != null) _light.intensity = 2.5f + 0.8f * Mathf.Sin(Time.time * 7f);
            // Red for the whole special on him, and on everything his strike marked (user 2.5).
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.1f;
                ApolloRedGlow.Ensure(_body, ApolloSurvivor.UltMaskBuff);
                if (ApolloSurvivor.UltMarkedBuff != null)
                    foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
                        if (b != null && b.HasBuff(ApolloSurvivor.UltMarkedBuff)) ApolloRedGlow.Ensure(b, ApolloSurvivor.UltMarkedBuff);
            }
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
            // (The red overlay and light are ApolloRedGlow's, for the whole special.)
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
