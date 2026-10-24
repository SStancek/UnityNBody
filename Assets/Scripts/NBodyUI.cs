using UnityEngine;

/// <summary>
/// On-screen Diagnostics & Interactive Control HUD for N-Body Simulation.
/// Uses immediate-mode GUI (IMGUI) for zero-dependency runtime interaction.
/// Automatically hides itself in stereoscopic cross-eyed mode to eliminate distraction.
/// </summary>
public class NBodyUI : MonoBehaviour
{
    public NBodyController controller;
    public NBodyCamera simCamera;
    public NBodyStereo stereo;

    private bool showUI = true;
    private float fpsUpdateTimer = 0.0f;
    private float currentFps = 60.0f;
    private int frameCount = 0;

    private void Start()
    {
        if (controller == null)
            controller = FindFirstObjectByType<NBodyController>();
        if (simCamera == null)
            simCamera = FindFirstObjectByType<NBodyCamera>();
        if (stereo == null)
            stereo = FindFirstObjectByType<NBodyStereo>();
    }

    private void Update()
    {
        // Toggle HUD visibility with 'H'
        if (Input.GetKeyDown(KeyCode.H))
        {
            showUI = !showUI;
        }

        // Quick Preset Switching
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            controller.InitializeSimulation(NBodyController.PresetScenario.CollidingGalaxies);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            controller.InitializeSimulation(NBodyController.PresetScenario.PlummerSphereCluster);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            controller.InitializeSimulation(NBodyController.PresetScenario.AccretionDisk);
        }

        // Particle count scaling with '[' and ']'
        if (Input.GetKeyDown(KeyCode.LeftBracket))
        {
            controller.bodyCount = Mathf.Max(4096, controller.bodyCount / 2);
            controller.InitializeSimulation(controller.CurrentPreset);
        }
        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            controller.bodyCount = Mathf.Min(262144, controller.bodyCount * 2);
            controller.InitializeSimulation(controller.CurrentPreset);
        }

        // Pause / Resume with 'P'
        if (Input.GetKeyDown(KeyCode.P))
        {
            controller.timeScale = (controller.timeScale > 0.01f) ? 0.0f : 1.0f;
        }

        // Measure FPS
        fpsUpdateTimer += Time.unscaledDeltaTime;
        frameCount++;
        if (fpsUpdateTimer >= 0.5f)
        {
            currentFps = frameCount / fpsUpdateTimer;
            frameCount = 0;
            fpsUpdateTimer = 0.0f;
        }
    }

    private void OnGUI()
    {
        if (!showUI || controller == null)
            return;

        // When in stereoscopic view, completely hide the menu panel to avoid distracting the viewer!
        if (stereo != null && stereo.IsStereoActive)
            return;

        // Diagnostic Panel
        GUI.Box(new Rect(15, 15, 340, 520), "");
        GUILayout.BeginArea(new Rect(25, 25, 320, 500));

        // Header
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.35f, 0.85f, 1.0f) }
        };
        GUILayout.Label("GPU N-Body Astro-Simulation", titleStyle);
        GUILayout.Space(5);

        // Performance Metrics
        int n = controller.bodyCount;
        double interactionsPerFrame = (double)n * (double)n;
        double gflops = (interactionsPerFrame * 20.0 * (double)currentFps) / 1e9; // ~20 FLOPs per body-pair interaction

        GUILayout.Label($"<b>FPS:</b> {currentFps:F1}  ({(1000f / Mathf.Max(currentFps, 1f)):F1} ms)");
        GUILayout.Label($"<b>Active Bodies:</b> {n:N0}");
        GUILayout.Label($"<b>Pairwise Calculations/Frame:</b> {(interactionsPerFrame / 1e6):F1} Million");
        GUILayout.Label($"<b>Gravitational Compute:</b> ~{gflops:F1} GFLOPs");
        GUILayout.Label($"<b>Scenario:</b> <color=#99eeff>{controller.CurrentPreset}</color>");
        GUILayout.Space(6);

        // Preset buttons
        GUILayout.Label("<b>Switch Preset Scenarios:</b>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("[1] Galaxies"))
            controller.InitializeSimulation(NBodyController.PresetScenario.CollidingGalaxies);
        if (GUILayout.Button("[2] Cluster"))
            controller.InitializeSimulation(NBodyController.PresetScenario.PlummerSphereCluster);
        if (GUILayout.Button("[3] Disk"))
            controller.InitializeSimulation(NBodyController.PresetScenario.AccretionDisk);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("<b>Body Count:</b>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("16K")) { controller.bodyCount = 16384; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("65K")) { controller.bodyCount = 65536; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("131K")) { controller.bodyCount = 131072; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("262K")) { controller.bodyCount = 262144; controller.InitializeSimulation(controller.CurrentPreset); }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        // Stereoscopic toggle button
        if (GUILayout.Button("<b>[X] Enter Cross-Eyed 3D Mode</b>", GUILayout.Height(26)))
        {
            if (stereo != null)
                stereo.SetStereoMode(true);
        }

        GUILayout.Space(6);
        // Sliders
        GUILayout.Label($"Time Scale: {controller.timeScale:F2}x");
        controller.timeScale = GUILayout.HorizontalSlider(controller.timeScale, 0.0f, 3.0f);

        GUILayout.Label($"Gravitational Constant (G): {controller.gravitationalConstant:F2}");
        controller.gravitationalConstant = GUILayout.HorizontalSlider(controller.gravitationalConstant, 0.1f, 5.0f);

        GUILayout.Label($"Softening (\u03B5): {controller.softening:F2}");
        controller.softening = GUILayout.HorizontalSlider(controller.softening, 0.1f, 3.0f);

        GUILayout.Label($"Particle Size: {controller.particleSize:F2}");
        controller.particleSize = GUILayout.HorizontalSlider(controller.particleSize, 0.1f, 2.0f);

        GUILayout.Label($"Glow Intensity: {controller.glowIntensity:F1}");
        controller.glowIntensity = GUILayout.HorizontalSlider(controller.glowIntensity, 0.5f, 5.0f);

        GUILayout.Space(6);
        GUIStyle helpStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
        GUILayout.Label("Controls:\n• [X]: Toggle Cross-Eyed 3D (Hides Menu)\n• Up/Down: 3D Depth | Z: Swap Eyes\n• Hold RMB + WASD: Fly | Shift: Turbo\n• C: Orbit/Fly | P: Pause | H: Hide UI", helpStyle);

        GUILayout.EndArea();
    }
}
