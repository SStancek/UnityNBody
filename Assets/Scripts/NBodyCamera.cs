using UnityEngine;

/// <summary>
/// Smooth 3D Cinematic & Scientific Navigation Camera for N-Body Simulations.
/// Supports seamless switching between Free-Flight (WASD) and Orbit Mode.
/// </summary>
public class NBodyCamera : MonoBehaviour
{
    public enum CameraMode
    {
        FreeFly,
        Orbit
    }

    [Header("Mode")]
    public CameraMode mode = CameraMode.FreeFly;
    public Transform orbitTarget;
    public Vector3 orbitCenter = Vector3.zero;

    [Header("Free Flight Movement")]
    public float moveSpeed = 40.0f;
    public float turboMultiplier = 4.0f;
    public float slowMultiplier = 0.25f;
    public float lookSensitivity = 2.5f;
    public float smoothing = 10.0f;

    [Header("Orbit Controls")]
    public float orbitDistance = 140.0f;
    public float orbitSpeed = 120.0f;
    public float zoomSpeed = 25.0f;
    public float minOrbitDistance = 5.0f;
    public float maxOrbitDistance = 2000.0f;

    private float pitch = 25.0f;
    private float yaw = 0.0f;
    private Vector3 currentVelocity;
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

        // Reset view with 'R'
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetView();
        }

        if (mode == CameraMode.FreeFly)
        {
            HandleFreeFly();
        }
        else
        {
            HandleOrbit();
        }

        // Smoothly interpolate towards target transforms
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.unscaledDeltaTime * smoothing);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.unscaledDeltaTime * smoothing);
    }

    private void HandleFreeFly()
    {
        // Mouse Look (Active when holding Right Mouse Button or Left Alt)
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89.0f, 89.0f);

            targetRotation = Quaternion.Euler(pitch, yaw, 0.0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Movement Speed Modifiers
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= turboMultiplier;
        if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier;

        // Translation Vector
        Vector3 moveInput = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) moveInput += transform.forward;
        if (Input.GetKey(KeyCode.S)) moveInput -= transform.forward;
        if (Input.GetKey(KeyCode.D)) moveInput += transform.right;
        if (Input.GetKey(KeyCode.A)) moveInput -= transform.right;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) moveInput += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) moveInput -= Vector3.up;

        // Mouse scrollwheel speed scaling
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            moveSpeed = Mathf.Clamp(moveSpeed * (1.0f + scroll * 1.5f), 2.0f, 1000.0f);
        }

        targetPosition += moveInput.normalized * speed * Time.unscaledDeltaTime;
    }

    private void HandleOrbit()
    {
        Vector3 center = (orbitTarget != null) ? orbitTarget.position : orbitCenter;

        // Mouse drag rotates orbit
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * orbitSpeed * 0.05f;
            pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 0.05f;
            pitch = Mathf.Clamp(pitch, -85.0f, 85.0f);
        }

        // Scroll to zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        orbitDistance = Mathf.Clamp(orbitDistance - scroll * zoomSpeed * (orbitDistance * 0.1f), minOrbitDistance, maxOrbitDistance);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 offset = rot * new Vector3(0.0f, 0.0f, -orbitDistance);

        targetPosition = center + offset;
        targetRotation = Quaternion.LookRotation(center - targetPosition);
    }

    public void ResetView()
    {
        targetPosition = new Vector3(0.0f, 60.0f, -140.0f);
        targetRotation = Quaternion.Euler(22.0f, 0.0f, 0.0f);
        pitch = 22.0f;
        yaw = 0.0f;
        orbitDistance = 150.0f;
    }
}

