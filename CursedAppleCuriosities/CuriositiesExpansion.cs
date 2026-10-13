using R2API;
using RoR2;
using RoR2.ExpansionManagement;
using UnityEngine;

namespace CursedAppleCuriosities
{
    /// <summary>
    /// The mod's content as a lobby expansion (next to Survivors of the Void etc.). Every ItemDef, EquipmentDef and
    /// ArtifactDef the mod registers requires it, so with it off the game keeps them out of drop tables, shops,
    /// printers and monster inventories and refuses to give them. Interactables we spawn ourselves (the Cursed
    /// Broker) check <see cref="Enabled"/>. Survivors don't require it: they stay pickable either way.
    /// </summary>
    internal static class CuriositiesExpansion
    {
        public static ExpansionDef? Def { get; private set; }

        /// <summary>True outside a run, or when the run has the expansion on.</summary>
        public static bool Enabled
        {
            get
            {
                Run? run = Run.instance;
                return Def == null || run == null || run.IsExpansionEnabled(Def);
            }
        }

        /// <summary>Must run before any item, equipment or artifact is created (they read <see cref="Def"/>).</summary>
        public static void Init()
        {
            LanguageAPI.Add("CURSEDAPPLE_EXPANSION_NAME", "Cursed Apple Curiosities");
            LanguageAPI.Add("CURSEDAPPLE_EXPANSION_DESC",
                "Adds the items, equipment, artifacts and Cursed Broker adapted from Deadlock to the game. " +
                "Its survivors can be played with it on or off.");

            var def = ScriptableObject.CreateInstance<ExpansionDef>();
            def.name = "CursedAppleCuriosities"; // the lobby rule is "Expansions." + name
            def.nameToken = "CURSEDAPPLE_EXPANSION_NAME";
            def.descriptionToken = "CURSEDAPPLE_EXPANSION_DESC";
            Sprite? icon = Items.ItemBase.LoadEmbeddedIcon("icon_curiositycatalog.png");
#pragma warning disable CS0618 // RuleDef.FromExpansion still reads the sprite fields when set
            def.iconSprite = icon;
            def.disabledIconSprite = icon != null ? Greyed(icon) : null;
#pragma warning restore CS0618
            ContentAddition.AddExpansionDef(def);
            Def = def;
        }

        // The "Off" choice: the icon desaturated and darkened, like the base game's disabled expansion icons.
        private static Sprite Greyed(Sprite icon)
        {
            try
            {
                Texture2D src = icon.texture;
                Color32[] px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    byte g = (byte)((px[i].r * 30 + px[i].g * 59 + px[i].b * 11) / 100 * 45 / 100);
                    px[i] = new Color32(g, g, g, px[i].a);
                }
                var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (System.Exception e)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Expansion] Greyed icon failed, using the plain one: {e.Message}");
                return icon;
            }
        }
    }
}
