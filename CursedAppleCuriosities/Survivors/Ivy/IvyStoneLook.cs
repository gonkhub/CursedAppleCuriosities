using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.Ivy
{
    /// <summary>
    /// Every machine, on Ivy: how Stone Form looks (user 2.5), driven by the synced Stone buff. Deadlock shows her statue
    /// (tengu_stone_form_model.vpcf: models/particle/tengu_stone_form_model_fx) in her place: once that model is converted she is
    /// hidden and the statue stands where she is, turned and sized as her model is. Until then her own model turns to stone (a grey
    /// stone overlay, in the statue pose). Breaking out leaves a puff of rubble.
    /// </summary>
    public class IvyStoneLook : MonoBehaviour
    {
        private static Material? _stone;
        private CharacterBody _body = null!;
        private bool _on;
        private GameObject? _statue;
        private TemporaryOverlayInstance? _overlay;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private static Material Stone()
        {
            if (_stone != null) return _stone;
            _stone = DeadlockHeroes.MakeMaterial("Ivy", "stone", null);
            _stone.color = new Color(0.55f, 0.55f, 0.52f);
            return _stone;
        }

        private void LateUpdate()
        {
            bool want = IvySurvivor.StoneBuff != null && _body.HasBuff(IvySurvivor.StoneBuff) && _body.healthComponent != null && _body.healthComponent.alive;
            if (want != _on) { if (want) Begin(); else End(); }
            if (_on && _statue != null && Hero() is GameObject hero)
                _statue.transform.SetPositionAndRotation(hero.transform.position, hero.transform.rotation);
        }

        private GameObject? Hero()
        {
            Transform? mt = _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
            var swap = mt != null ? mt.GetComponent<DeadlockModelSwap>() : null;
            return swap != null ? swap.HeroObject : null;
        }

        private void Begin()
        {
            _on = true;
            GameObject? hero = Hero();
            GlbModel? statue = DeadlockInstall.Model(IvyData.StatueKey);
            if (hero != null && statue != null)
            {
                _statue = Instantiate(statue.Root);
                _statue.name = "CursedAppleIvyStatue";
                _statue.transform.SetPositionAndRotation(hero.transform.position, hero.transform.rotation);
                _statue.transform.localScale = hero.transform.lossyScale;
                _statue.SetActive(true);
                SetHidden(hero, true);
                return;
            }
            // No statue yet: her own model, turned to stone.
            CharacterModel? model = _body.modelLocator != null && _body.modelLocator.modelTransform != null ? _body.modelLocator.modelTransform.GetComponent<CharacterModel>() : null;
            if (model == null) return;
            _overlay = TemporaryOverlayManager.AddOverlay(gameObject);
            _overlay.duration = float.PositiveInfinity;
            _overlay.animateShaderAlpha = false;
            _overlay.destroyComponentOnEnd = false;
            _overlay.originalMaterial = Stone();
            _overlay.AddToCharacterModel(model);
        }

        private void End()
        {
            _on = false;
            if (_statue != null) Destroy(_statue);
            _statue = null;
            if (Hero() is GameObject hero) SetHidden(hero, false);
            _overlay?.Destroy();
            _overlay = null;
            // Breaking out of the stone.
            DeadlockSounds.Play("ivy_stone_hit", _body.corePosition, gameObject);
            if (IvySurvivor.SlamEffect != null) EffectManager.SimpleEffect(IvySurvivor.SlamEffect, _body.corePosition, Quaternion.identity, false);
        }

        private static void SetHidden(GameObject hero, bool hidden)
        {
            foreach (Renderer r in hero.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hidden;
        }

        private void OnDestroy()
        {
            if (_statue != null) Destroy(_statue);
            _overlay?.Destroy();
        }
    }
}
