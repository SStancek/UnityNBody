using UnityEngine;

/// <summary>
/// Cross-Eyed & Parallel Stereoscopic 3D Camera Manager.
/// Splits the display into dual side-by-side viewports with convergence for stereoscopic 3D.
/// Includes a central black divider stripe (1/20th of screen width) to eliminate visual crosstalk.
/// In Cross-Eyed view:
///   - Left Screen Half  = Right Eye Camera (viewing from +IPD/2)
///   - Center Stripe     = Pitch black divider (1/20 screen width)
///   - Right Screen Half = Left Eye Camera  (viewing from -IPD/2)
/// Crossing eyes causes the two images to fuse in the center into a stunning 3D hologram.
/// </summary>
public class NBodyStereo : MonoBehaviour
{
    [Header("Stereo Settings")]
    [Tooltip("Enable or disable stereoscopic split-screen")]
    public bool isStereoActive = false;

    [Tooltip("If true, Cross-Eyed mode (Right eye on left, Left eye on right). If false, Parallel mode.")]
    public bool crossEyedMode = true;

    [Header("Divider Stripe")]
    [Tooltip("Width of the central black separator stripe as a fraction of screen width (1/20 = 0.05)")]
    [Range(0.0f, 0.2f)]
    public float stripeWidthFraction = 0.05f; // Exactly 1/20th of screen width

    [Header("Optical Parameters")]
    [Tooltip("Interpupillary distance (physical separation between virtual eyes)")]
    [Range(0.2f, 40.0f)]
    public float eyeSeparation = 4.5f;

    [Tooltip("Distance at which the eyes converge (focal plane of zero parallax)")]
    [Range(10.0f, 600.0f)]
    public float convergenceDistance = 140.0f;

    [Header("Hotkey")]
    public KeyCode toggleHotkey = KeyCode.X;

    // References
    private Camera mainCamera;
    private Camera leftEyeCam;
    private Camera rightEyeCam;

    public bool IsStereoActive => isStereoActive;

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();
        if (mainCamera == null)
            mainCamera = Camera.main;

        CreateEyeCameras();
        UpdateCameraStates();
    }

    private void CreateEyeCameras()
    {
        // 1. Create Left Eye Camera
        GameObject leftGo = new GameObject("StereoCamera_LeftEye");
        leftGo.transform.SetParent(mainCamera.transform, false);
        leftEyeCam = leftGo.AddComponent<Camera>();
        leftEyeCam.CopyFrom(mainCamera);
        leftEyeCam.enabled = false;
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
        // Toggle stereoscopy with hotkey (Default: 'X')
        if (Input.GetKeyDown(toggleHotkey))
        {
            SetStereoMode(!isStereoActive);
        }

        if (!isStereoActive)
            return;

        // Toggle between Cross-Eyed and Parallel with 'Z'
        if (Input.GetKeyDown(KeyCode.Z))
        {
            crossEyedMode = !crossEyedMode;
            UpdateViewportRects();
        }

        // --- Responsive Depth / IPD Adjustments ---
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation + 0.8f, 0.2f, 40.0f);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation - 0.8f, 0.2f, 40.0f);
        }

        float ipdRate = (Input.GetKey(KeyCode.LeftShift) ? 14.0f : 5.0f) * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.UpArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation + ipdRate, 0.2f, 40.0f);
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            eyeSeparation = Mathf.Clamp(eyeSeparation - ipdRate, 0.2f, 40.0f);
        }

        // --- Focal / Convergence Distance Adjustments ---
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance + 10.0f, 10.0f, 600.0f);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance - 10.0f, 10.0f, 600.0f);
        }

        float convRate = (Input.GetKey(KeyCode.LeftShift) ? 70.0f : 25.0f) * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.RightArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance + convRate, 10.0f, 600.0f);
        }
        else if (Input.GetKey(KeyCode.LeftArrow))
        {
            convergenceDistance = Mathf.Clamp(convergenceDistance - convRate, 10.0f, 600.0f);
        }

        UpdateEyeTransforms();
    }

    public void SetStereoMode(bool active)
    {
        isStereoActive = active;
        UpdateCameraStates();
    }

    private void UpdateCameraStates()
    {
        if (mainCamera == null || leftEyeCam == null || rightEyeCam == null)
            return;

        if (isStereoActive)
        {
            mainCamera.enabled = false;
            leftEyeCam.enabled = true;
            rightEyeCam.enabled = true;

            UpdateViewportRects();
            UpdateEyeTransforms();
        }
        else
        {
            mainCamera.enabled = true;
            mainCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);

            leftEyeCam.enabled = false;
            rightEyeCam.enabled = false;
        }
    }

    private void UpdateViewportRects()
    {
        if (leftEyeCam == null || rightEyeCam == null)
            return;

        // Central divider stripe width (1/20 = 0.05)
        float halfStripe = stripeWidthFraction * 0.5f; // 0.025
        float eyeWidth = 0.5f - halfStripe;            // 0.475
        float rightStartX = 0.5f + halfStripe;         // 0.525

        if (crossEyedMode)
        {
            // CROSS-EYED: Right eye on Left half, Left eye on Right half
            rightEyeCam.rect = new Rect(0.0f, 0.0f, eyeWidth, 1.0f);
            leftEyeCam.rect  = new Rect(rightStartX, 0.0f, eyeWidth, 1.0f);
        }
        else
        {
            // PARALLEL: Left eye on Left half, Right eye on Right half
            leftEyeCam.rect  = new Rect(0.0f, 0.0f, eyeWidth, 1.0f);
            rightEyeCam.rect = new Rect(rightStartX, 0.0f, eyeWidth, 1.0f);
        }
    }

    private void UpdateEyeTransforms()
    {
        float halfIpd = eyeSeparation * 0.5f;

        Vector3 leftPosLocal  = new Vector3(-halfIpd, 0.0f, 0.0f);
        Vector3 rightPosLocal = new Vector3( halfIpd, 0.0f, 0.0f);

        leftEyeCam.transform.localPosition  = leftPosLocal;
        rightEyeCam.transform.localPosition = rightPosLocal;

        Vector3 convergencePointLocal = new Vector3(0.0f, 0.0f, convergenceDistance);

        leftEyeCam.transform.localRotation  = Quaternion.LookRotation(convergencePointLocal - leftPosLocal, Vector3.up);
        rightEyeCam.transform.localRotation = Quaternion.LookRotation(convergencePointLocal - rightPosLocal, Vector3.up);
    }

    private void OnGUI()
    {
        if (!isStereoActive)
            return;

        // 1. Draw solid central black divider stripe (exactly 1/20th of screen width)
        float stripeWidthPixels = Screen.width * stripeWidthFraction;
        float stripeX = (Screen.width - stripeWidthPixels) * 0.5f;

        Color prevColor = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(stripeX, 0, stripeWidthPixels, Screen.height), Texture2D.whiteTexture);
        GUI.color = prevColor;

        // 2. Render sleek, dual-fused HUD centered in each eye's viewport
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
