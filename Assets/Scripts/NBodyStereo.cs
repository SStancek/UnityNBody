using UnityEngine;

/// <summary>
/// =========================================================================================
/// STEREOSCOPIC 3D CAMERA MANAGER (CROSS-EYED & PARALLEL STEREOGRAMS)
/// =========================================================================================
/// 
/// [CODING GUIDE: OPTICAL FOUNDATIONS & VIEWPORT MATHEMATICS]
/// 
/// 1. HOW STEREOSCOPIC VISION WORKS:
///    Human depth perception (stereopsis) relies on horizontal binocular disparity: the two
///    eyes observe the world from slightly different vantage points (~63mm apart in humans).
///    By rendering the virtual scene from two virtual cameras offset horizontally by an
///    interpupillary distance (IPD), each eye receives a unique perspective.
/// 
/// 2. CROSS-EYED VS. PARALLEL VIEWING:
///    - Parallel Viewing (Wall-Eyed / Magic Eye):
///      Left Eye looks at Left Image, Right Eye looks at Right Image.
///      Eyes diverge towards infinity. Challenging for large displays because human eyes cannot
///      diverge outward beyond parallel without severe strain.
///    - Cross-Eyed Viewing (Recommended for Computer Monitors):
///      The user crosses their lines of sight in front of the screen.
///      - Left Eye line-of-sight intersects the RIGHT screen half.
///      - Right Eye line-of-sight intersects the LEFT screen half.
///      Therefore, the LEFT screen half MUST display the RIGHT eye's camera, and the
///      RIGHT screen half MUST display the LEFT eye's camera!
/// 
/// 3. VIEWPORT SPLITTING WITH CENTRAL SEPARATOR STRIPE:
///    Unity camera viewports are defined in normalized screen coordinates [0, 1]:
///    `Camera.rect = new Rect(x, y, width, height)`
///    To eliminate peripheral visual cross-talk between the inner edges of both images,
///    we place a solid black stripe of width S = 1/20 (5% of screen width):
///      - Left Viewport:   x = 0.000, width = 0.5 - S/2 = 0.475 (47.5% width)
///      - Central Stripe:  x = 0.475, width = S         = 0.050 ( 5.0% width)
///      - Right Viewport:  x = 0.525, width = 0.5 - S/2 = 0.475 (47.5% width)
/// 
/// 4. CONVERGENCE & ZERO-PARALLAX PLANE:
///    Both eye cameras toe-in towards a focal convergence point at distance D:
///    - Objects located exactly at distance D have zero horizontal parallax (appear at screen depth).
///    - Objects closer than D pop out into negative parallax (floating in front of the screen).
///    - Objects further than D sink into positive parallax (receding into deep space).
/// 
/// 5. DUAL-FUSED 3D HUD:
///    By rendering an identical status overlay at the exact optical centers of both viewports,
///    the brain fuses the two text labels into a single 3D status HUD floating in space!
/// 
/// =========================================================================================
/// </summary>
public class NBodyStereo : MonoBehaviour
{
    // =====================================================================================
    // INSPECTOR CONFIGURATION
    // =====================================================================================

    [Header("Stereo State")]
    [Tooltip("Enable or disable stereoscopic split-screen mode")]
    public bool isStereoActive = false;

    [Tooltip("If true, Cross-Eyed mode (Right eye on left, Left eye on right). If false, Parallel mode.")]
    public bool crossEyedMode = true;

    [Header("Divider Stripe")]
    [Tooltip("Width of the central black separator stripe as a fraction of screen width (1/20 = 0.05)")]
    [Range(0.0f, 0.2f)]
    public float stripeWidthFraction = 0.05f; // Exactly 1/20th of total screen width

    [Header("Optical Parameters")]
    [Tooltip("Virtual Interpupillary Distance (IPD) - physical separation between virtual eyes")]
    [Range(0.2f, 40.0f)]
    public float eyeSeparation = 4.5f;

    [Tooltip("Convergence distance - distance to the zero-parallax focal plane where eyes intersect")]
    [Range(10.0f, 600.0f)]
    public float convergenceDistance = 140.0f;

    [Header("Hotkey")]
    [Tooltip("Hotkey used to toggle stereoscopy on and off")]
    public KeyCode toggleHotkey = KeyCode.X;

    // =====================================================================================
    // CAMERA REFERENCES & STATE
    // =====================================================================================

    private Camera mainCamera;
    private Camera leftEyeCam;
    private Camera rightEyeCam;

    /// <summary>
    /// Read-only accessor indicating whether stereoscopic mode is currently active.
    /// Used by NBodyUI to automatically hide the 2D menu panel during 3D immersion.
    /// </summary>
    public bool IsStereoActive => isStereoActive;

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();
        if (mainCamera == null)
            mainCamera = Camera.main;

        CreateEyeCameras();
        UpdateCameraStates();
    }

    /// <summary>
    /// Instantiates two dedicated sub-cameras as children of the main camera rig.
    /// Childing to mainCamera ensures they automatically inherit 6-DOF position and rotation.
    /// </summary>
    private void CreateEyeCameras()
    {
        // 1. Create Left Eye Camera
        GameObject leftGo = new GameObject("StereoCamera_LeftEye");
        leftGo.transform.SetParent(mainCamera.transform, false);
        leftEyeCam = leftGo.AddComponent<Camera>();
        leftEyeCam.CopyFrom(mainCamera);
        leftEyeCam.enabled = false;

        // Strip duplicate AudioListener to avoid Unity console warnings
        AudioListener leftListener = leftGo.GetComponent<AudioListener>();
        if (leftListener != null) Destroy(leftListener);

        // 2. Create Right Eye Camera
        GameObject rightGo = new GameObject("StereoCamera_RightEye");
        rightGo.transform.SetParent(mainCamera.transform, false);
        rightEyeCam = rightGo.AddComponent<Camera>();
        rightEyeCam.CopyFrom(mainCamera);
        rightEyeCam.enabled = false;

        AudioListener rightListener = rightGo.GetComponent<AudioListener>();
        if (rightListener != null) Destroy(rightListener);
    }

    private void Update()
    {
        // Hotkey: Toggle Stereoscopic 3D mode
        if (Input.GetKeyDown(toggleHotkey))
        {
            SetStereoMode(!isStereoActive);
        }

        if (!isStereoActive)
            return;

        // Hotkey 'Z': Toggle between Cross-Eyed and Parallel stereogram modes
        if (Input.GetKeyDown(KeyCode.Z))
        {
            crossEyedMode = !crossEyedMode;
            UpdateViewportRects();
        }

        // ---------------------------------------------------------------------------------
        // Dynamic Depth (IPD) Adjustments
        // ---------------------------------------------------------------------------------
        // Discrete key taps for immediate, distinct visual feedback
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation + 0.8f, 0.2f, 40.0f);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation - 0.8f, 0.2f, 40.0f);
        }

        // Smooth continuous hold (hold LeftShift for accelerated speed)
        float ipdRate = (Input.GetKey(KeyCode.LeftShift) ? 14.0f : 5.0f) * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.UpArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation + ipdRate, 0.2f, 40.0f);
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation - ipdRate, 0.2f, 40.0f);
        }

        // ---------------------------------------------------------------------------------
        // Dynamic Focal Convergence Distance Adjustments
        // ---------------------------------------------------------------------------------
        // Discrete key taps
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance + 10.0f, 10.0f, 600.0f);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance - 10.0f, 10.0f, 600.0f);
        }

        // Smooth continuous hold
        float convRate = (Input.GetKey(KeyCode.LeftShift) ? 70.0f : 25.0f) * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.RightArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance + convRate, 10.0f, 600.0f);
        }
        else if (Input.GetKey(KeyCode.LeftArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance - convRate, 10.0f, 600.0f);
        }

        // Synchronize eye transforms based on updated IPD and convergence distance
        UpdateEyeTransforms();
    }

    /// <summary>
    /// Enables or disables stereoscopic rendering.
    /// </summary>
    public void SetStereoMode(bool active)
    {
        isStereoActive = active;
        UpdateCameraStates();
    }

    /// <summary>
    /// Activates either the single main camera (mono) or the dual eye cameras (stereo).
    /// </summary>
    private void UpdateCameraStates()
    {
        if (mainCamera == null || leftEyeCam == null || rightEyeCam == null)
            return;

        if (isStereoActive)
        {
            // Disable main camera, enable dual stereoscopic sub-cameras
            mainCamera.enabled = false;
            leftEyeCam.enabled = true;
            rightEyeCam.enabled = true;

            UpdateViewportRects();
            UpdateEyeTransforms();
        }
        else
        {
            // Restore standard monoscopic fullscreen viewport
            mainCamera.enabled = true;
            mainCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);

            leftEyeCam.enabled = false;
            rightEyeCam.enabled = false;
        }
    }

    /// <summary>
    /// Calculates the normalized viewport rectangles for both eye cameras,
    /// inserting the 1/20th screen-width central black divider stripe.
    /// </summary>
    private void UpdateViewportRects()
    {
        if (leftEyeCam == null || rightEyeCam == null)
            return;

        // Central divider stripe width (1/20 = 0.05)
        float halfStripe = stripeWidthFraction * 0.5f; // 0.025
        float eyeWidth = 0.5f - halfStripe;            // 0.475 (47.5% width per eye)
        float rightStartX = 0.5f + halfStripe;         // 0.525 (starts after black stripe)

        if (crossEyedMode)
        {
            // CROSS-EYED CONFIGURATION:
            // Left screen half  = RIGHT eye camera (viewing from +IPD/2)
            // Right screen half = LEFT eye camera  (viewing from -IPD/2)
            rightEyeCam.rect = new Rect(0.0f, 0.0f, eyeWidth, 1.0f);
            leftEyeCam.rect  = new Rect(rightStartX, 0.0f, eyeWidth, 1.0f);
        }
        else
        {
            // PARALLEL CONFIGURATION:
            // Left screen half  = LEFT eye camera  (viewing from -IPD/2)
            // Right screen half = RIGHT eye camera (viewing from +IPD/2)
            leftEyeCam.rect  = new Rect(0.0f, 0.0f, eyeWidth, 1.0f);
            rightEyeCam.rect = new Rect(rightStartX, 0.0f, eyeWidth, 1.0f);
        }
    }

    /// <summary>
    /// Updates eye positions and toe-in convergence rotations relative to the camera rig.
    /// </summary>
    private void UpdateEyeTransforms()
    {
        float halfIpd = eyeSeparation * 0.5f;

        // Position virtual eyes along the local X-axis (interpupillary baseline)
        Vector3 leftPosLocal  = new Vector3(-halfIpd, 0.0f, 0.0f);
        Vector3 rightPosLocal = new Vector3( halfIpd, 0.0f, 0.0f);

        leftEyeCam.transform.localPosition  = leftPosLocal;
        rightEyeCam.transform.localPosition = rightPosLocal;

        // Convergence point lying directly along the central line of sight (local Z)
        Vector3 convergencePointLocal = new Vector3(0.0f, 0.0f, convergenceDistance);

        // Toe-in rotation: Aim both cameras directly toward the focal convergence point.
        // This ensures objects at 'convergenceDistance' align at identical horizontal pixel positions.
        leftEyeCam.transform.localRotation  = Quaternion.LookRotation(convergencePointLocal - leftPosLocal, Vector3.up);
        rightEyeCam.transform.localRotation = Quaternion.LookRotation(convergencePointLocal - rightPosLocal, Vector3.up);
    }

    /// <summary>
    /// Renders the central black divider stripe and the dual-fused 3D stereoscopic status overlay.
    /// </summary>
    private void OnGUI()
    {
        if (!isStereoActive)
            return;

        // 1. Draw Solid Central Black Divider Stripe (1/20th of screen width)
        float stripeWidthPixels = Screen.width * stripeWidthFraction;
        float stripeX = (Screen.width - stripeWidthPixels) * 0.5f;

        Color prevColor = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(stripeX, 0, stripeWidthPixels, Screen.height), Texture2D.whiteTexture);
        GUI.color = prevColor;

        // 2. Render Sleek, Dual-Fused HUD Centered in Each Viewport
        // Because the status box is rendered at identical relative positions in both eyes,
        // it naturally fuses into a sharp 3D text panel when crossing eyes!
        float halfStripe = stripeWidthFraction * 0.5f;
        float eyeWidth = 0.5f - halfStripe;

        float leftCenterX = (eyeWidth * 0.5f) * Screen.width;
        float rightCenterX = (0.5f + halfStripe + eyeWidth * 0.5f) * Screen.width;

        float panelWidth = 370.0f;
        float panelHeight = 50.0f;
        float y = Screen.height - panelHeight - 15.0f;

        DrawFusedStereoHUD(new Rect(leftCenterX - panelWidth * 0.5f, y, panelWidth, panelHeight));
        DrawFusedStereoHUD(new Rect(rightCenterX - panelWidth * 0.5f, y, panelWidth, panelHeight));
    }

    /// <summary>
    /// Helper method to draw a single half of the stereoscopic status bar.
    /// </summary>
    private void DrawFusedStereoHUD(Rect rect)
    {
        GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.65f);
        GUI.Box(rect, "");
        GUI.color = Color.white;

        GUILayout.BeginArea(rect);
        GUIStyle styleTitle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.4f, 0.85f, 1.0f) }
        };
        string modeStr = crossEyedMode ? "CROSS-EYED 3D" : "PARALLEL 3D";
        GUILayout.Label($"<b>{modeStr}</b> | 3D Depth (IPD): <color=#ffff55>{eyeSeparation:F1}</color> | Focal: <color=#ffff55>{convergenceDistance:F0}</color>", styleTitle);

        GUIStyle styleHelp = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
        };
        GUILayout.Label("[▲/▼] Depth | [◄/►] Focal Dist | [Z] Swap | [X] Exit", styleHelp);
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (leftEyeCam != null)  Destroy(leftEyeCam.gameObject);
        if (rightEyeCam != null) Destroy(rightEyeCam.gameObject);
    }
}
