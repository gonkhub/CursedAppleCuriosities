using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Items
{
    public abstract class ItemBase
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public abstract string ItemName { get; }

        // SCREAMING_SNAKE suffix used to build language token names.
        // E.g. "RESTORATIVESHOT" → tokens CURSEDAPPLE_RESTORATIVESHOT_NAME, etc.
        public abstract string ItemLangTokenName { get; }

        public virtual string ItemPickupDesc => string.Empty;
        public abstract string ItemFullDescription { get; }
        public abstract string ItemLore { get; }

        // ── Behaviour ─────────────────────────────────────────────────────────
        public abstract ItemTier Tier { get; }
        public abstract ItemTag[] ItemTags { get; }
        public virtual bool CanRemove => true;

        // Override with the embedded PNG filename (e.g. "icon_berserker.png") to give
        // the item a pickup icon. Leave null to fall back to no icon. CreateItem()
        // loads this automatically — do NOT touch ItemDef.pickupIconSprite directly,
        // since ItemDef does not exist until CreateItem() runs.
        protected virtual string? IconFileName => null;

        // Embedded GLB (made with tools\fbx_to_glb.py) shown as the world pickup model (ground droplet /
        // multishop terminal), textured with the icon. Every item with an icon gets the shared icon card;
        // override to give an item its own model.
        protected virtual string? ModelFileName => PickupModels.IconCard;

        // ── Runtime ref ───────────────────────────────────────────────────────
        public ItemDef? ItemDef { get; private set; }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        public abstract void Init(ConfigFile config);

        protected void CreateLang()
        {
            LanguageAPI.Add($"CURSEDAPPLE_{ItemLangTokenName}_NAME", ItemName);
            LanguageAPI.Add($"CURSEDAPPLE_{ItemLangTokenName}_PICKUP", string.IsNullOrEmpty(ItemPickupDesc) ? ItemFullDescription : ItemPickupDesc); // the game shows this one in most places
            LanguageAPI.Add($"CURSEDAPPLE_{ItemLangTokenName}_DESC", ItemFullDescription);
            LanguageAPI.Add($"CURSEDAPPLE_{ItemLangTokenName}_LORE", ItemLore);
        }

        protected void CreateItem()
        {
            ItemDef = ScriptableObject.CreateInstance<ItemDef>();
            ItemDef.name = $"ITEM_CURSEDAPPLE_{ItemLangTokenName}";
            ItemDef.nameToken = $"CURSEDAPPLE_{ItemLangTokenName}_NAME";
            ItemDef.pickupToken = $"CURSEDAPPLE_{ItemLangTokenName}_PICKUP";
            ItemDef.descriptionToken = $"CURSEDAPPLE_{ItemLangTokenName}_DESC";
            ItemDef.loreToken = $"CURSEDAPPLE_{ItemLangTokenName}_LORE";
            // NOTE: Do NOT set `ItemDef.tier` here. In SOTV the tier setter is
            // `_itemTierDef = ItemTierCatalog.GetItemTierDef(value)` — it queries the tier
            // catalog, which is NOT initialized this early in plugin Awake, so it resolves to
            // null and the item silently falls back to Tier1 (white). The plugin instead calls
            // ResolveTier() from an On.ItemTierCatalog.Init hook, once the catalog exists.
            ItemDef.tags = ItemTags;
            ItemDef.canRemove = CanRemove;
            ItemDef.requiredExpansion = CuriositiesExpansion.Def; // off in the lobby = never drops or given

            // 2D pickup icon (inventory bar, logbook, multishop list, pickup ping).
            ItemDef.pickupIconSprite = IconFileName != null ? LoadEmbeddedIcon(IconFileName) : null;

            // 3D world pickup model (ground droplet / multishop terminal). Items without one stay null.
            // pickupModelPrefab is marked obsolete but CreatePickupDef still reads it into displayPrefab.
#pragma warning disable CS0618
            if (ModelFileName != null && ItemDef.pickupIconSprite != null)
                ItemDef.pickupModelPrefab = PickupModels.Load(ModelFileName, ItemDef.pickupIconSprite.texture);
#pragma warning restore CS0618

            ItemAPI.Add(new CustomItem(ItemDef, new ItemDisplayRuleDict(null)));
        }

        public abstract void Hooks();

        // Call AFTER ItemTierCatalog.Init() has run. Assigning .tier now actually populates
        // _itemTierDef (the field every color/drop/visual system reads), fixing the white-tier bug.
        public void ResolveTier()
        {
            if (ItemDef != null)
                ItemDef.tier = Tier;
        }

        // Buff icon: the item's embedded PNG if it has one, otherwise a solid-color placeholder.
        internal static Sprite BuffIcon(string? iconFileName, Color fallback)
        {
            if (iconFileName != null)
            {
                var sprite = LoadEmbeddedIcon(iconFileName);
                if (sprite != null) return sprite;
            }
            return CreatePlaceholderSprite(fallback);
        }

        /// <summary>
        /// Damage flagged DamageTypeExtended.BypassDamageCalculations is final (vanilla skips armour, crits and every attacker
        /// bonus for it): mod hooks must not rescale it either. Shiv's Bloodletting ticks use it (user 3.0).
        /// </summary>
        internal static bool FinalDamage(DamageInfo? info) =>
            info != null && (info.damageType.damageTypeExtended & DamageTypeExtended.BypassDamageCalculations) != 0;

        internal static Sprite CreatePlaceholderSprite(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }
        // Decoded once per file: an item's icon and its buff icon are the same PNG.
        private static readonly System.Collections.Generic.Dictionary<string, Sprite?> IconCache = new System.Collections.Generic.Dictionary<string, Sprite?>();

        internal static Sprite? LoadEmbeddedIcon(string fileName)
        {
            if (IconCache.TryGetValue(fileName, out Sprite? cached) && cached != null) return cached;
            return IconCache[fileName] = DecodeEmbeddedIcon(fileName);
        }

        private static Sprite? DecodeEmbeddedIcon(string fileName)
        {
            var asm    = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream($"CursedAppleCuriosities.Resources.{fileName}");
            if (stream == null)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] Embedded icon not found: {fileName}");
                return null;
            }

            var bytes  = new byte[stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) break;
                offset += read;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] Failed to decode icon PNG: {fileName}");
                return null;
            }

            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
