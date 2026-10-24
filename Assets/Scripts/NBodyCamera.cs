using UnityEngine;

/// <summary>
/// =========================================================================================
/// 6-DOF SCIENTIFIC & CINEMATIC NAVIGATION CAMERA CONTROLLER
/// =========================================================================================
/// 
/// [CODING GUIDE: CAMERA MATHEMATICS & NAVIGATION DESIGN]
/// 
/// 1. DUAL NAVIGATION PARADIGMS:
///    - Free-Flight Mode (Spectator / 6-DOF Flycam):
///      Permits full translation along local forward, right, and world up vectors. Ideal for
///      flying directly through the galactic core or following hyper-velocity slingshots.
///    - Orbital Tracking Mode:
///      Locks onto a target or cosmological origin (0, 0, 0) using spherical coordinates:
///      position = center + Rotation(pitch, yaw) * (0, 0, -distance).
/// 
/// 2. GIMBAL LOCK PREVENTION:
///    Euler angle rotations can suffer from gimbal lock when pitch reaches +/- 90 degrees.
///    We clamp pitch strictly to [-89.0, +89.0] degrees, guaranteeing the up-vector never flips.
/// 
/// 3. FRAME-RATE INDEPENDENT SMOOTHING (UNSCALED DELTA TIME):
///    We use `Time.unscaledDeltaTime` for all camera interpolation (`Vector3.Lerp`, `Quaternion.Slerp`).
///    Why? If the simulation is paused (`timeScale = 0.0`) or placed in slow-motion, the camera
///    and mouse look remain fully responsive and silky smooth at 165+ FPS.
/// 
/// =========================================================================================
/// </summary>
public class NBodyCamera : MonoBehaviour
{
    public enum CameraMode
    {
        FreeFly, // 6-DOF WASD spectator navigation with mouse look
        Orbit    // Spherical orbit around center of mass with scroll zoom
    }

    [Header("Navigation Mode")]
    [Tooltip("Active navigation paradigm")]
    public CameraMode mode = CameraMode.FreeFly;

    [Tooltip("Target transform to orbit around (defaults to orbitCenter if null)")]
    public Transform orbitTarget;

    [Tooltip("World-space coordinate used as the orbital pivot center")]
    public Vector3 orbitCenter = Vector3.zero;

    [Header("Free-Flight Parameters")]
    [Tooltip("Base translational speed (units/sec)")]
    public float moveSpeed = 40.0f;

    [Tooltip("Speed multiplier applied when holding LeftShift")]
    public float turboMultiplier = 4.0f;

    [Tooltip("Speed multiplier applied when holding LeftControl")]
    public float slowMultiplier = 0.25f;

    [Tooltip("Mouse look sensitivity for pitch and yaw")]
    public float lookSensitivity = 2.5f;

    [Tooltip("Positional and rotational smoothing factor")]
    public float smoothing = 10.0f;

    [Header("Orbital Parameters")]
    [Tooltip("Current distance from the orbital center")]
    public float orbitDistance = 140.0f;

    [Tooltip("Angular speed for mouse orbit drag")]
    public float orbitSpeed = 120.0f;

    [Tooltip("Scrollwheel zoom speed factor")]
    public float zoomSpeed = 25.0f;

    [Tooltip("Minimum allowed orbital distance")]
    public float minOrbitDistance = 5.0f;

    [Tooltip("Maximum allowed orbital distance")]
    public float maxOrbitDistance = 2000.0f;

    // Internal Euler tracking
    private float pitch = 25.0f;
    private float yaw = 0.0f;

    // Target transforms for exponential smoothing
    private Vector3 targetPosition;
    private Quaternion targetRotation;

    private void Start()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;

        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;
    }

    private void Update()
    {
        // Toggle camera mode with 'C'
        if (Input.GetKeyDown(KeyCode.C))
        {
            mode = (mode == CameraMode.FreeFly) ? CameraMode.Orbit : CameraMode.FreeFly;
        }

        // Reset view to default cosmological perspective with 'R'
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetView();
        }

        // Dispatch input handling based on active navigation mode
        if (mode == CameraMode.FreeFly)
        {
            HandleFreeFly();
        }
        else
        {
            HandleOrbit();
        }

        // Exponential smoothing (unscaled time ensures responsiveness when paused)
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.unscaledDeltaTime * smoothing);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.unscaledDeltaTime * smoothing);
    }

    /// <summary>
    /// Handles 6-DOF translation and mouse look.
    /// Right Mouse Button enables cursor lock and mouse look.
    /// </summary>
    private void HandleFreeFly()
    {
        // Mouse Look (Active when holding Right Mouse Button)
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;

            // Clamp pitch to prevent gimbal flip
            pitch = Mathf.Clamp(pitch, -89.0f, 89.0f);

            targetRotation = Quaternion.Euler(pitch, yaw, 0.0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Speed Modifiers
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= turboMultiplier;
        if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier;

        // Translational Vector Accumulation
        Vector3 moveInput = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) moveInput += transform.forward;
        if (Input.GetKey(KeyCode.S)) moveInput -= transform.forward;
        if (Input.GetKey(KeyCode.D)) moveInput += transform.right;
        if (Input.GetKey(KeyCode.A)) moveInput -= transform.right;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) moveInput += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) moveInput -= Vector3.up;

        // Dynamic speed scaling via scrollwheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            moveSpeed = Mathf.Clamp(moveSpeed * (1.0f + scroll * 1.5f), 2.0f, 1000.0f);
        }

        targetPosition += moveInput.normalized * speed * Time.unscaledDeltaTime;
    }

    /// <summary>
    /// Handles spherical orbital navigation around an anchor pivot.
    /// </summary>
    private void HandleOrbit()
    {
        Vector3 center = (orbitTarget != null) ? orbitTarget.position : orbitCenter;

        // Mouse drag rotates orbital angles
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * orbitSpeed * 0.05f;
            pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 0.05f;
            pitch = Mathf.Clamp(pitch, -85.0f, 85.0f);
        }

        // Scrollwheel exponential zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        orbitDistance = Mathf.Clamp(orbitDistance - scroll * zoomSpeed * (orbitDistance * 0.1f), minOrbitDistance, maxOrbitDistance);

        // Spherical coordinate transformation
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 offset = rot * new Vector3(0.0f, 0.0f, -orbitDistance);

        targetPosition = center + offset;
        targetRotation = Quaternion.LookRotation(center - targetPosition);
    }

    /// <summary>
    /// Resets the camera to the optimal cosmological vantage point.
    /// </summary>
    public void ResetView()
    {
        targetPosition = new Vector3(0.0f, 60.0f, -140.0f);
        targetRotation = Quaternion.Euler(22.0f, 0.0f, 0.0f);
        pitch = 22.0f;
        yaw = 0.0f;
        orbitDistance = 150.0f;
    }
}
