using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;

namespace CursedAppleCuriosities.Artifacts
{
    public abstract class ArtifactBase
    {
        public abstract string ArtifactName { get; }
        public abstract string ArtifactLangTokenName { get; }
        public abstract string ArtifactDescription { get; }

        // Optional embedded PNG filenames for enabled/disabled states.
        // If null, falls back to placeholder colored squares.
        protected virtual string? EnabledIconFileName  => null;
        protected virtual string? DisabledIconFileName => null;
        protected virtual Color   EnabledIconColor     => new Color(1f, 0.75f, 0.15f);
        protected virtual Color   DisabledIconColor    => new Color(0.35f, 0.35f, 0.35f);

        public ArtifactDef? ArtifactDef { get; private set; }

        public abstract void Init(ConfigFile config);
        public abstract void Hooks();

        protected void CreateLang()
        {
            LanguageAPI.Add($"CURSEDAPPLE_{ArtifactLangTokenName}_NAME", ArtifactName);
            LanguageAPI.Add($"CURSEDAPPLE_{ArtifactLangTokenName}_DESC", ArtifactDescription);
        }

        protected void CreateArtifact()
        {
            ArtifactDef = ScriptableObject.CreateInstance<ArtifactDef>();
            ArtifactDef.cachedName         = $"ARTIFACT_CURSEDAPPLE_{ArtifactLangTokenName}";
            ArtifactDef.nameToken          = $"CURSEDAPPLE_{ArtifactLangTokenName}_NAME";
            ArtifactDef.descriptionToken   = $"CURSEDAPPLE_{ArtifactLangTokenName}_DESC";
            ArtifactDef.smallIconSelectedSprite   = LoadIcon(EnabledIconFileName,  EnabledIconColor);
            ArtifactDef.smallIconDeselectedSprite = LoadIcon(DisabledIconFileName, DisabledIconColor);
            ArtifactDef.requiredExpansion = CuriositiesExpansion.Def;
            ContentAddition.AddArtifactDef(ArtifactDef);
        }

        private static Sprite LoadIcon(string? fileName, Color fallback)
        {
            if (fileName != null)
            {
                var s = Items.ItemBase.LoadEmbeddedIcon(fileName);
                if (s != null) return s;
            }
            return Items.ItemBase.CreatePlaceholderSprite(fallback);
        }
    }
}
