#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NBodySceneBuilder
{
    [MenuItem("N-Body/Build Simulation Scene")]
    public static void BuildScene()
    {
        Debug.Log("[NBodySceneBuilder] Starting scene automated configuration...");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // 1. Locate Compute Shader
        ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/NBodySimulation.compute");
        if (compute == null)
        {
            Debug.LogError("[NBodySceneBuilder] Could not find compute shader at Assets/Shaders/NBodySimulation.compute");
            return;
        }

        // 2. Locate or Create Particle Material
        Shader particleShader = Shader.Find("Custom/NBodyParticle");
        if (particleShader == null)
        {
            particleShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/NBodyParticle.shader");
        }

        if (particleShader == null)
        {
            Debug.LogError("[NBodySceneBuilder] Could not find shader Custom/NBodyParticle");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shaders/NBodyMaterial.mat");
        if (material == null)
        {
            material = new Material(particleShader);
            material.name = "NBodyParticleMaterial";
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, "Assets/Shaders/NBodyMaterial.mat");
        }
        else
        {
            material.shader = particleShader;
            material.enableInstancing = true;
        }

        // 3. Setup New Scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 4. Create Deep-Space Camera
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; // Deep cosmos black
        cam.farClipPlane = 50000.0f;
        cam.nearClipPlane = 0.5f;
        cam.allowHDR = true;

        camGo.transform.position = new Vector3(0.0f, 60.0f, -140.0f);
        camGo.transform.rotation = Quaternion.Euler(22.0f, 0.0f, 0.0f);
        camGo.AddComponent<AudioListener>();

        NBodyCamera nbodyCamera = camGo.AddComponent<NBodyCamera>();
        NBodyStereo stereo = camGo.AddComponent<NBodyStereo>();

        // 5. Create Simulation Manager GameObject
        GameObject simGo = new GameObject("NBody_Simulation");
        NBodyController controller = simGo.AddComponent<NBodyController>();
        controller.nbodyCompute = compute;
        controller.particleMaterial = material;
        controller.bodyCount = 65536;
        controller.initialPreset = NBodyController.PresetScenario.CollidingGalaxies;
        controller.gravitationalConstant = 1.0f;
        controller.softening = 0.85f;
        controller.timeScale = 1.0f;
        controller.particleSize = 0.85f;
        controller.glowIntensity = 2.5f;

        NBodyUI ui = simGo.AddComponent<NBodyUI>();
        ui.controller = controller;
        ui.simCamera = nbodyCamera;
        ui.stereo = stereo;

        // 6. Save Scene and Register in Build Settings
        string scenePath = "Assets/Scenes/NBodySimulationScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(scenePath, true)
        };

        AssetDatabase.SaveAssets();
        Debug.Log("[NBodySceneBuilder] SUCCESS: N-Body Simulation Scene successfully generated at " + scenePath);
    }

    [MenuItem("N-Body/Build Windows Standalone Player")]
    public static void BuildStandalone()
    {
        BuildScene();

        string buildDir = "c:/UnityNBody/Build";
        if (!System.IO.Directory.Exists(buildDir))
        {
            System.IO.Directory.CreateDirectory(buildDir);
        }

        string exePath = buildDir + "/NBodySimulation.exe";
        Debug.Log("[NBodySceneBuilder] Compiling Windows Standalone 64-bit Player to " + exePath);

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = new string[] { "Assets/Scenes/NBodySimulationScene.unity" },
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(buildOptions);
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("[NBodySceneBuilder] BUILD SUCCEEDED: Standalone executable created at " + exePath);
        }
        else
        {
            Debug.LogError("[NBodySceneBuilder] BUILD FAILED: " + report.summary.totalErrors + " errors.");
        }
    }
}
#endif
