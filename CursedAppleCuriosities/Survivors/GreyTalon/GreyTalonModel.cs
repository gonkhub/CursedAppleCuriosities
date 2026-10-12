using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Grey Talon on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs). His bow hangs off his left hand
    /// (weapon_hand_L, muzzle_fx under it), so it follows the hand without Abrams' HandIK. His owl rides his right arm
    /// (owl_bodygroup): it is hidden while the Guided Owl flies (GreyTalonOwlLook).
    /// </summary>
    public static class GreyTalonModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = GreyTalonData.Hero,
            DisplayName = "Grey Talon",
            DlModel = GreyTalonData.DlModel,
            // Reload props (infinite ammo here): the arrow clip in his hand and the quiver's animated clip set, which sits
            // on top of the static one.
            HideMeshes = new[] { "archer_clip_inhand_model", "archer_quiverclips_anim_model" },
            ModelHeightScale = GreyTalonData.ModelHeightScale,
            BaseSpeed = GreyTalonData.BaseSpeed,
            MuzzleBone = GreyTalonData.MuzzleBone,
            MuzzleName = GreyTalonSurvivor.MuzzleName,
            Anims = GreyTalonData.Anims,
            RootLocked = GreyTalonData.Locked,
            // Charged Shot drawn: walks and stands with the bow up (ability_charged_shot_walk_*).
            LocoSet = body => GreyTalonSurvivor.ChargingBuff != null && body.HasBuff(GreyTalonSurvivor.ChargingBuff) ? "charged" : null,
            HoldSeconds = id => id == "jump" || id == "air_jump" || id == "wall_jump" || id == "in_air" || id == "falling" ? 30f : 3f,
            LoopRule = c =>
                // A jump pose holds while airborne (his set has no in-air loop); back to moving once grounded.
                (c.State == "jump" || c.State == "air_jump" || c.State == "wall_jump" || c.State == "in_air" || c.State == "falling") && c.Grounded && c.StateTime > 0.15f ? c.Loco :
                c.State == "charge_air" && c.Grounded ? c.Loco :
                // Rain of Arrows' glide pose ends with the glide.
                (c.State == "rain_air" || c.State == "rain_fire") && c.Grounded && c.StateTime > 0.3f ? c.Loco :
                // The owl channel poses end when the owl does; if that call never came, back to moving.
                (c.State == "owl_loop" || c.State == "owl_channel") && c.StateTime > GreyTalonData.OwlCastDelay + GreyTalonData.OwlFlight + 1f ? c.Loco : null,
        };

        /// <summary>Entity states ask for animation states by name (every client runs OnEnter, so all see it).</summary>
        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }

    /// <summary>Every client: hides the owl on his arm while his Guided Owl is out (the owl buff is synced).</summary>
    public class GreyTalonOwlLook : MonoBehaviour
    {
        private CharacterBody? _body;
        private readonly System.Collections.Generic.List<GameObject> _owl = new System.Collections.Generic.List<GameObject>();
        private float _nextSearch;
        private bool? _flying;

        private void Awake() => _body = GetComponent<CharacterBody>();

        private void Update()
        {
            bool flying = _body != null && GreyTalonSurvivor.OwlFlyingBuff != null && _body.HasBuff(GreyTalonSurvivor.OwlFlyingBuff);
            if (_owl.Count == 0 || _owl.Exists(g => g == null))
            {
                if (Time.time < _nextSearch) return;
                _nextSearch = Time.time + 0.25f; // the Deadlock model arrives a little after spawn
                _owl.Clear();
                Transform? model = _body != null && _body.modelLocator != null ? _body.modelLocator.modelTransform : null;
                if (model == null) return;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.name.StartsWith(GreyTalonData.PerchedOwlMesh + "_")) _owl.Add(r.gameObject);
                _flying = null;
            }
            if (_flying == flying) return;
            _flying = flying;
            foreach (GameObject g in _owl) if (g != null) g.SetActive(!flying);
        }
    }
}
