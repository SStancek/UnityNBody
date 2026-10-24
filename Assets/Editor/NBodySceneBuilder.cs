#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// =========================================================================================
/// AUTOMATED SCENE GENERATION & STANDALONE CI/CD BUILD PIPELINE
/// =========================================================================================
/// 
/// [CODING GUIDE: EDITOR SCRIPTING & HEADLESS BATCHMODE BUILDS]
/// 
/// 1. HEADLESS EXECUTION (CI/CD AUTOMATION):
///    Instead of requiring manual GUI clicking in the Unity Editor to link references,
///    drag materials, and configure build settings, this script exposes static methods
///    invokable directly via CLI batch mode:
///      `Unity.exe -batchmode -projectPath . -executeMethod NBodySceneBuilder.BuildStandalone -quit`
/// 
/// 2. PROGRAMMATIC ASSET LINKING & SCENE SERIALIZATION:
///    - `AssetDatabase.LoadAssetAtPath<T>()` locates shaders and compute assets via GUID/path.
///    - `EditorSceneManager.NewScene()` creates a fresh, uncontaminated scene graph.
///    - Programmatically assigns references (compute shader, material, cameras).
///    - `EditorSceneManager.SaveScene()` serializes the scene to a YAML `.unity` asset file.
///    - `EditorBuildSettings.scenes` registers the scene into the active build index.
/// 
/// 3. AUTOMATED COMPILATION VIA BuildPipeline:
///    `BuildPipeline.BuildPlayer()` triggers Unity's IL2CPP / Roslyn compiler and asset
///    bundler, outputting a standalone 64-bit Windows executable (`.exe`).
/// 
/// =========================================================================================
/// </summary>
public static class NBodySceneBuilder
{
    /// <summary>
    /// Builds and wires the complete N-Body simulation scene from scratch.
    /// Accessible from the Unity Editor menu: "N-Body > Build Simulation Scene".
    /// </summary>
    [MenuItem("N-Body/Build Simulation Scene")]
    public static void BuildScene()
    {
        Debug.Log("[NBodySceneBuilder] Starting automated scene graph construction...");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // ---------------------------------------------------------------------------------
        // 1. Locate GPU Compute Shader Asset
        // ---------------------------------------------------------------------------------
        ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/NBodySimulation.compute");
        if (compute == null)
        {
            Debug.LogError("[NBodySceneBuilder] Missing compute shader at Assets/Shaders/NBodySimulation.compute");
            return;
        }

        // ---------------------------------------------------------------------------------
        // 2. Locate or Instantiate Procedural Particle Material
        // ---------------------------------------------------------------------------------
        Shader particleShader = Shader.Find("Custom/NBodyParticle");
        if (particleShader == null)
        {
            particleShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/NBodyParticle.shader");
        }

        if (particleShader == null)
        {
            Debug.LogError("[NBodySceneBuilder] Missing shader Custom/NBodyParticle");
            return;
        }

        // Check if material asset already exists on disk
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

        // ---------------------------------------------------------------------------------
        // 3. Create Fresh Empty Scene
        // ---------------------------------------------------------------------------------
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------------------------------------------------------------------------------
        // 4. Construct Deep-Space Camera Rig
        // ---------------------------------------------------------------------------------
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; // Deep cosmos background
        cam.farClipPlane = 50000.0f;       // Cosmological viewing frustum
        cam.nearClipPlane = 0.5f;
        cam.allowHDR = true;

        // Position camera for an elevated oblique overview of the galactic disk plane
        camGo.transform.position = new Vector3(0.0f, 60.0f, -140.0f);
        camGo.transform.rotation = Quaternion.Euler(22.0f, 0.0f, 0.0f);
        camGo.AddComponent<AudioListener>();

        // Attach 6-DOF navigation camera and stereoscopic manager
        NBodyCamera nbodyCamera = camGo.AddComponent<NBodyCamera>();
        NBodyStereo stereo = camGo.AddComponent<NBodyStereo>();

        // ---------------------------------------------------------------------------------
        // 5. Construct Simulation Coordinator GameObject
        // ---------------------------------------------------------------------------------
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

        // Attach runtime diagnostics HUD and link references
        NBodyUI ui = simGo.AddComponent<NBodyUI>();
        ui.controller = controller;
        ui.simCamera = nbodyCamera;
        ui.stereo = stereo;

        // ---------------------------------------------------------------------------------
        // 6. Serialize Scene & Register in Build Settings
        // ---------------------------------------------------------------------------------
        string scenePath = "Assets/Scenes/NBodySimulationScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        // Register scene as index 0 in the application build list
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(scenePath, true)
        };

        AssetDatabase.SaveAssets();
        Debug.Log("[NBodySceneBuilder] SUCCESS: Scene successfully created and saved to " + scenePath);
    }

    /// <summary>
    /// Builds the scene and compiles a standalone 64-bit Windows release player.
    /// Accessible from the Unity Editor menu: "N-Body > Build Windows Standalone Player".
    /// </summary>
    [MenuItem("N-Body/Build Windows Standalone Player")]
    public static void BuildStandalone()
    {
        // Rebuild scene graph to ensure all latest script references and assets are synchronized
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
