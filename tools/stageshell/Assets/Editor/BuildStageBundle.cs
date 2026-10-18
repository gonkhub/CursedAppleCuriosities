// Builds the stage shell bundles for Cursed Apple Curiosities (Unity 2019.4.26f1, RoR2's version):
//   cac_stagescenes: empty scenes, one per Deadlock map stage (the mod fills them at runtime from the player's own
//                    Deadlock install: no Valve content is ever in here);
//   cac_stageassets: the shaders the stages use.
// Run: Unity.exe -batchmode -nographics -quit -projectPath tools/stageshell -executeMethod BuildStageBundle.Build
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class BuildStageBundle
{
    // Must match MapSectionDef.Scenes: one per district, plus spares so a new district needs no rebuild here.
    static readonly string[] Stages = { "cac_sunkenplaza", "cac_district2", "cac_district3", "cac_district4" };

    public static void Build()
    {
        Directory.CreateDirectory("Assets/Scenes");
        foreach (string name in Stages)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 900f;
            RenderSettings.fogColor = new Color(0.42f, 0.47f, 0.55f);
            RenderSettings.skybox = null;
            Lightmapping.bakedGI = false;
            Lightmapping.realtimeGI = false;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + name + ".unity");
        }
        AssetDatabase.Refresh();

        // Materials pin the shader variants the mod uses (shader_feature keywords are stripped otherwise).
        Directory.CreateDirectory("Assets/Materials");
        MakeMaterial("Assets/Materials/matBarrier.mat", "CursedApple/Barrier", null);
        MakeMaterial("Assets/Materials/matWorld.mat", "CursedApple/DeadlockWorld", null);
        MakeMaterial("Assets/Materials/matWorldCutout.mat", "CursedApple/DeadlockWorld", "_ALPHATEST_ON");
        MakeMaterial("Assets/Materials/matGlass.mat", "CursedApple/DeadlockGlass", null);

        var scenes = new AssetBundleBuild { assetBundleName = "cac_stagescenes", assetNames = new string[Stages.Length] };
        for (int i = 0; i < Stages.Length; i++) scenes.assetNames[i] = "Assets/Scenes/" + Stages[i] + ".unity";
        var assets = new AssetBundleBuild
        {
            assetBundleName = "cac_stageassets",
            assetNames = new[] { "Assets/Shaders/Barrier.shader", "Assets/Shaders/DeadlockWorld.shader", "Assets/Shaders/DeadlockGlass.shader",
                "Assets/Materials/matBarrier.mat", "Assets/Materials/matWorld.mat", "Assets/Materials/matWorldCutout.mat", "Assets/Materials/matGlass.mat" },
        };
        Directory.CreateDirectory("Build");
        BuildPipeline.BuildAssetBundles("Build", new[] { scenes, assets }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("[stageshell] bundles built");
    }

    static void MakeMaterial(string path, string shader, string keyword)
    {
        AssetDatabase.DeleteAsset(path); // rebuilt each time (a new shader may have been added)
        var m = new Material(Shader.Find(shader));
        if (keyword != null) m.EnableKeyword(keyword);
        AssetDatabase.CreateAsset(m, path);
    }
}
