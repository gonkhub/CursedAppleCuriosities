using BepInEx.Configuration;
using CursedAppleCuriosities.Items;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>
    /// Every player starts the run holding a random Goose Egg, rolled by the eggs' drop weights.
    /// Players who join mid-run get one too (their first master of the run).
    /// </summary>
    public class GooseArtifact : ArtifactBase
    {
        public override string ArtifactName          => "Artifact of the Goose";
        public override string ArtifactLangTokenName => "GOOSE";
        public override string ArtifactDescription   => "Each player starts the run with a random Goose Egg.";

        protected override string? EnabledIconFileName => "icon_goldengooseegg.png";

        public override void Init(ConfigFile config)
        {
            CreateLang();
            CreateArtifact();
            if (ArtifactDef != null && ArtifactDef.smallIconSelectedSprite != null)
                ArtifactDef.smallIconDeselectedSprite = Greyscale(ArtifactDef.smallIconSelectedSprite);
            Hooks();
        }

        public override void Hooks()
        {
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;
        }

        private void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController player)
        {
            if (!NetworkServer.active || ArtifactDef == null) return;
            if (RunArtifactManager.instance == null || !RunArtifactManager.instance.IsArtifactEnabled(ArtifactDef)) return;
            Inventory? inventory = player?.master?.inventory;
            if (inventory == null) return;

            GooseEgg? egg = GooseEgg.PickWeighted(new Xoroshiro128Plus(run.runRNG.nextUlong));
            if (egg?.EquipmentDef == null) return;
            inventory.SetEquipmentIndex(egg.EquipmentDef.equipmentIndex);
        }

        private static Sprite Greyscale(Sprite src)
        {
            Texture2D from = src.texture;
            var tex = new Texture2D(from.width, from.height, TextureFormat.RGBA32, false);
            Color[] px = from.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float lum = (px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f) * 0.6f;
                px[i] = new Color(lum, lum, lum, px[i].a);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
