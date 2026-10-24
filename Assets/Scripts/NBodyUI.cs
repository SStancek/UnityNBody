using UnityEngine;

/// <summary>
/// =========================================================================================
/// REAL-TIME SCIENTIFIC DIAGNOSTICS & INTERACTIVE RUNTIME CONTROL HUD
/// =========================================================================================
/// 
/// [CODING GUIDE: RUNTIME IMGUI & FLOATING-POINT BENCHMARKING]
/// 
/// 1. WHY IMMEDIATE-MODE GUI (IMGUI / OnGUI):
///    Rather than creating a heavy GameObject-based UI Canvas with EventSystems and RectTransforms,
///    IMGUI executes procedurally from code with zero scene dependencies. It compiles cleanly
///    in batch mode, runs with near-zero memory footprint, and is ideal for scientific testbenches.
/// 
/// 2. GRAVITATIONAL FLOATING-POINT OPERATIONS (GFLOPs) DERIVATION:
///    Each pairwise gravitational calculation evaluates:
///      r = pos_j - pos_i                          ( 3 FLOPs: vector subtraction)
///      distSq = dot(r, r) + epsilon^2             ( 6 FLOPs: 3 muls, 2 adds, 1 add)
///      invDist = rsqrt(distSq)                    ( 1 FLOP : hardware fast rsqrt)
///      invDistCube = invDist * invDist * invDist  ( 2 FLOPs: 2 multiplications)
///      scalar = G * m_j * invDistCube             ( 2 FLOPs: 2 multiplications)
///      acc += r * scalar                          ( 6 FLOPs: 3 muls, 3 adds)
///    Total per pair = ~20 FLOPs.
///    With N bodies at FPS frames per second:
///      GFLOPs = (N^2 * 20 * FPS) / 10^9.
///    For 65,536 bodies at 60 FPS: (4.29B * 20 * 60) / 10^9 = ~5.15 TFLOPs!
/// 
/// 3. DISTRACTION-FREE 3D SUPPRESSION:
///    When stereoscopy is activated, `stereo.IsStereoActive` returns true.
///    OnGUI returns immediately, keeping both eye viewports pristine and unobstructed.
/// 
/// =========================================================================================
/// </summary>
public class NBodyUI : MonoBehaviour
{
    [Header("Connected Controllers")]
    public NBodyController controller;
    public NBodyCamera simCamera;
    public NBodyStereo stereo;

    private bool showUI = true;
    private float fpsUpdateTimer = 0.0f;
    private float currentFps = 60.0f;
    private int frameCount = 0;

    private void Start()
    {
        // Auto-locate components if not linked via inspector
        if (controller == null)
            controller = FindFirstObjectByType<NBodyController>();
        if (simCamera == null)
            simCamera = FindFirstObjectByType<NBodyCamera>();
        if (stereo == null)
            stereo = FindFirstObjectByType<NBodyStereo>();
    }

    private void Update()
    {
        // Hotkey 'H': Toggle UI overlay visibility
        if (Input.GetKeyDown(KeyCode.H))
        {
            showUI = !showUI;
        }

        // Quick Preset Switching Hotkeys (1, 2, 3)
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

        // Particle Count Scaling with '[' and ']' (Multiplies or divides by 2)
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

        // Hotkey 'P': Pause or resume gravitational integration
        if (Input.GetKeyDown(KeyCode.P))
        {
            controller.timeScale = (controller.timeScale > 0.01f) ? 0.0f : 1.0f;
        }

        // Sample frame rate every 500ms using unscaled time
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

        // When in stereoscopic 3D mode, suppress the 2D menu panel so it doesn't distract the viewer
        if (stereo != null && stereo.IsStereoActive)
            return;

        // ---------------------------------------------------------------------------------
        // Diagnostic Control Panel
        // ---------------------------------------------------------------------------------
        GUI.Box(new Rect(15, 15, 340, 520), "");
        GUILayout.BeginArea(new Rect(25, 25, 320, 500));

        // Header Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.35f, 0.85f, 1.0f) }
        };
        GUILayout.Label("GPU N-Body Astro-Simulation", titleStyle);
        GUILayout.Space(5);

        // Performance & FLOP Metrics
        int n = controller.bodyCount;
        double interactionsPerFrame = (double)n * (double)n;
        double gflops = (interactionsPerFrame * 20.0 * (double)currentFps) / 1e9;

        GUILayout.Label($"<b>FPS:</b> {currentFps:F1}  ({(1000f / Mathf.Max(currentFps, 1f)):F1} ms)");
        GUILayout.Label($"<b>Active Bodies:</b> {n:N0}");
        GUILayout.Label($"<b>Pairwise Calculations/Frame:</b> {(interactionsPerFrame / 1e6):F1} Million");
        GUILayout.Label($"<b>Gravitational Compute:</b> ~{gflops:F1} GFLOPs");
        GUILayout.Label($"<b>Scenario:</b> <color=#99eeff>{controller.CurrentPreset}</color>");
        GUILayout.Space(6);

        // Preset Scenario Buttons
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

        // Body Count Selector Buttons
        GUILayout.Label("<b>Body Count:</b>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("16K")) { controller.bodyCount = 16384; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("65K")) { controller.bodyCount = 65536; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("131K")) { controller.bodyCount = 131072; controller.InitializeSimulation(controller.CurrentPreset); }
        if (GUILayout.Button("262K")) { controller.bodyCount = 262144; controller.InitializeSimulation(controller.CurrentPreset); }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        // Stereoscopic 3D Button
        if (GUILayout.Button("<b>[X] Enter Cross-Eyed 3D Mode</b>", GUILayout.Height(26)))
        {
            if (stereo != null)
                stereo.SetStereoMode(true);
        }

        GUILayout.Space(6);

        // Interactive Parameter Sliders
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

        // Hotkey Reference Footer
        GUIStyle helpStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
        GUILayout.Label("Controls:\n• [X]: Toggle Cross-Eyed 3D (Hides Menu)\n• Up/Down: 3D Depth | Z: Swap Eyes\n• Hold RMB + WASD: Fly | Shift: Turbo\n• C: Orbit/Fly | P: Pause | H: Hide UI", helpStyle);

        GUILayout.EndArea();
    }
}
