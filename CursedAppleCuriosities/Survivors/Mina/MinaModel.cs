using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using Kit = CursedAppleCuriosities.Deadlock.DeadlockSurvivorKit;

namespace CursedAppleCuriosities.Survivors.Mina
{
    /// <summary>Mina on the shared Deadlock model layer: her model, animation table and skins.</summary>
    public static class MinaModel
    {
        public static readonly DeadlockHeroDef Def = new DeadlockHeroDef
        {
            Hero = MinaData.Hero,
            DisplayName = "Mina",
            DlModel = MinaData.DlModel,
            ModelHeightScale = MinaData.ModelHeightScale,
            BaseSpeed = MinaData.BaseSpeed,
            MuzzleBone = MinaData.MuzzleBone,
            MuzzleName = MinaSurvivor.MuzzleName,
            Anims = MinaData.Anims,
            RootLocked = MinaData.Locked,
            ItemAnchors = MinaItemDisplays.Anchors,
            // Hovering on her umbrella (the synced buff): her in-air loops become the glide.
            LocoSet = b => MinaSurvivor.HoverBuff != null && b.HasBuff(MinaSurvivor.HoverBuff) ? "hover" : null,
            HideMeshes = new[] { "head_ui" }, // the menu-portrait head, not part of her in game
            // Nightshade (skin 1): her red umbrella, bows and dress trim turned violet; her pale skin is left alone (minSat).
            Skins = new[] { new DeadlockSkin { Id = "nightshade1", Recolor = DeadlockSkin.HueShift(-25f, 20f, -75f, 0.35f), Only = new[] { "mina_umbrella", "mina_upper", "mina_lower", "mina_hair", "mina_bag", "mina_charm" } } },
        };

        public static Sprite DefaultIcon => _default ??= Kit.SkinIcon(new Color(0.8f, 0.08f, 0.1f), new Color(0.1f, 0.1f, 0.14f), new Color(0.18f, 0.2f, 0.3f), new Color(0.93f, 0.85f, 0.85f));
        public static Sprite NightshadeIcon => _night ??= Kit.SkinIcon(new Color(0.5f, 0.1f, 0.8f), new Color(0.1f, 0.1f, 0.14f), new Color(0.18f, 0.2f, 0.3f), new Color(0.93f, 0.85f, 0.85f));
        private static Sprite? _default, _night;

        public static void Play(GameObject bodyObject, string state) => DeadlockModelSwap.Play(bodyObject, state);
    }
}
