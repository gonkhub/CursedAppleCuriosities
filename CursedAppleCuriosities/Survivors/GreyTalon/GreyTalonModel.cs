using CursedAppleCuriosities.Deadlock;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Survivors.GreyTalon
{
    /// <summary>
    /// Grey Talon on the shared Deadlock model layer (Deadlock/DeadlockHeroModel.cs). His bow hangs off his left hand
    /// (weapon_hand_L, muzzle_fx under it), so it follows the hand without Abrams' HandIK. The owl that rides his right
    /// arm in Deadlock (owl_bodygroup) is hidden (user).
    /// </summary>
    public static class GreyTalonModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = GreyTalonData.Hero,
            DisplayName = "Grey Talon",
            DlModel = GreyTalonData.DlModel,
            // Reload props (infinite ammo here): the arrow clip in his hand and the quiver's animated clip set, which sits
            // on top of the static one. The owl on his arm is hidden too (user).
            HideMeshes = new[] { "archer_clip_inhand_model", "archer_quiverclips_anim_model", GreyTalonData.PerchedOwlMesh },
            ModelHeightScale = GreyTalonData.ModelHeightScale,
            BaseSpeed = GreyTalonData.BaseSpeed,
            MuzzleBone = GreyTalonData.MuzzleBone,
            MuzzleName = GreyTalonSurvivor.MuzzleName,
            Anims = GreyTalonData.Anims,
            RootLocked = GreyTalonData.Locked,
            ItemAnchors = GreyTalonItemDisplays.Anchors,
            // Night Owl (skin 1, his mastery, user 3.0): his greens (cloak, bow wraps) turn violet, and his oranges and browns
            // (scarf, leather, bow) turn blue. Body and bow textures only: his face and hair keep their colours.
            Skins = new[] { new DeadlockSkin { Id = "nightowl1", Only = new[] { "archer_color", "archer_bow_color" }, Recolor = px =>
            {
                bool greens = DeadlockSkin.HueShift(70f, 175f, 120f)(px);  // greens first (they leave the orange range)
                bool browns = DeadlockSkin.HueShift(-10f, 50f, -165f)(px);
                return greens || browns;
            } } },
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
}
