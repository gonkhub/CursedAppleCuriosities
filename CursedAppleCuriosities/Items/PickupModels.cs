using System;
using CursedAppleCuriosities.Deadlock;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CursedAppleCuriosities.Items
{
    /// <summary>
    /// World pickup models (ground droplet + multishop terminal) from embedded GLBs, textured with the item's icon.
    /// Each item gets its own copy (own material), so one GLB serves every item.
    /// Made with tools\fbx_to_glb.py. Two traps the earlier script-built attempt hit, and how this avoids them:
    /// - A from-scratch hgstandard material renders see-through: Commando's opaque material is cloned instead
    ///   (same trick as Lash).
    /// - PickupDisplay normalizes scale by the cube root of the first renderer's bounds VOLUME, so a thin card
    ///   would be blown up (or, at zero thickness, never shown). Each mesh's bounds are padded to a cube.
    /// </summary>
    internal static class PickupModels
    {
        /// <summary>The icon card (originally battlevest.fbx): a thin square that shows the item's icon on both faces.</summary>
        public const string IconCard = "model_card.glb";

        private static Material? _baseMaterial;
        private static GameObject? _holder;

        // One parsed model per file; later items get a copy (shared mesh) with their own material.
        private static readonly System.Collections.Generic.Dictionary<string, GameObject> Templates = new System.Collections.Generic.Dictionary<string, GameObject>();

        public static GameObject? Load(string fileName, Texture2D? texture)
        {
            if (Templates.TryGetValue(fileName, out GameObject template) && template != null && _holder != null)
            {
                GameObject copy = UnityEngine.Object.Instantiate(template, _holder.transform, false);
                copy.name = template.name;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(mats[i] != null ? mats[i].name.Replace("matCursedApple_", "") : r.name, texture);
                    r.sharedMaterials = mats;
                }
                return copy;
            }
            try
            {
                byte[]? bytes = ReadResource(fileName);
                if (bytes == null)
                {
                    CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] Embedded model not found: {fileName}");
                    return null;
                }

                _baseMaterial ??= Addressables.LoadAssetAsync<Material>("RoR2/Base/Commando/matCommandoDualies.mat").WaitForCompletion();
                GlbModel model = GlbLoader.Load(bytes, System.IO.Path.GetFileNameWithoutExtension(fileName), (name, _) => MakeMaterial(name, texture));

                foreach (MeshFilter mf in model.Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Bounds b = mf.sharedMesh.bounds;
                    float side = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    mf.sharedMesh.bounds = new Bounds(b.center, Vector3.one * side);
                }

                // PickupDisplay instantiates the prefab as-is, so the root must be active: park it under an
                // inactive holder instead so the template itself never shows up in the scene.
                if (_holder == null)
                {
                    _holder = new GameObject("CursedApplePickupModels");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                }
                model.Root.transform.SetParent(_holder.transform, false);
                model.Root.SetActive(true);
                Templates[fileName] = model.Root;
                return model.Root;
            }
            catch (Exception e)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[CursedApple] Pickup model {fileName} failed: {e.Message}");
                return null;
            }
        }

        private static Material MakeMaterial(string name, Texture2D? tex)
        {
            Material m = new Material(_baseMaterial!);
            m.name = "matCursedApple_" + name;
            m.mainTexture = tex;
            m.color = Color.white;
            foreach (string p in new[] { "_NormalTex", "_EmTex", "_FresnelRamp", "_PrintRamp", "_SplatmapTex", "_FlowHeightmap", "_FlowHeightRamp" })
                if (m.HasProperty(p)) m.SetTexture(p, null);
            if (m.HasProperty("_EmPower")) m.SetFloat("_EmPower", 0f);
            if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0f);
            if (m.HasProperty("_SpecularStrength")) m.SetFloat("_SpecularStrength", 0.15f);
            m.DisableKeyword("FORCE_SPEC");
            return m;
        }

        private static byte[]? ReadResource(string fileName)
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream($"CursedAppleCuriosities.Resources.{fileName}");
            if (stream == null) return null;
            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
