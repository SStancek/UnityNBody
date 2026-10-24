using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// =========================================================================================
/// HIGH-PERFORMANCE GPU N-BODY SIMULATION CONTROLLER & PIPELINE COORDINATOR
/// =========================================================================================
/// 
/// [CODING GUIDE: ARCHITECTURE & GPU RESOURCE LIFECYCLE]
/// 
/// 1. CPU-GPU MEMORY DIVISION:
///    In massive particle simulations, transferring positions between CPU and GPU every frame
///    via `ComputeBuffer.GetData()` creates a fatal PCIe bandwidth bottleneck.
///    This architecture maintains 100% of particle data on GPU VRAM:
///    - CPU Role: Generates astrophysical initial conditions (coordinates, masses, velocities)
///      ONCE during startup, uploads them to VRAM, and then only dispatches compute kernels.
///    - GPU Role: Integrates Newtonian gravity in-place using compute shaders and feeds the
///      resulting buffer directly into the rasterizer via indirect procedural instancing.
/// 
/// 2. DOUBLE BUFFERING (PING-PONG BUFFERS):
///    Compute kernels cannot safely read from and write to the same buffer concurrently due to
///    SIMD execution hazards (race conditions). We allocate two buffers:
///    - Frame N:   Read from Buffer A, Write to Buffer B. Render Buffer B.
///    - Frame N+1: Read from Buffer B, Write to Buffer A. Render Buffer A.
///    Swapping references takes O(1) time without moving any memory.
/// 
/// 3. PROCEDURAL INDIRECT DRAW CALLS (Graphics.DrawMeshInstancedIndirect):
///    Instead of submitting mesh instances from the CPU, Unity passes a 5-element GPU buffer
///    describing how many instances to draw. The GPU draws all 65,536+ particles in 1 call.
/// 
/// =========================================================================================
/// </summary>
public class NBodyController : MonoBehaviour
{
    /// <summary>
    /// Memory structure representing a single celestial body.
    /// MUST match the 32-byte HLSL 'Body' struct exactly:
    /// - Vector3 position: 12 bytes
    /// - float mass:        4 bytes
    /// - Vector3 velocity: 12 bytes
    /// - float padding:     4 bytes (stores scalar speed for vertex shader color mapping)
    /// Total stride = 32 bytes (16-byte aligned for optimal GPU memory transaction coalescing).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Body
    {
        public Vector3 position;
        public float mass;
        public Vector3 velocity;
        public float padding;
    }

    /// <summary>
    /// Available initial astrophysical distribution presets.
    /// </summary>
    public enum PresetScenario
    {
        CollidingGalaxies,    // Two rotating disk galaxies on an eccentric collision course
        PlummerSphereCluster, // Self-gravitating globular star cluster in dynamic equilibrium
        AccretionDisk         // Central stellar mass with surrounding Keplerian proto-planetary disk
    }

    // =====================================================================================
    // INSPECTOR CONFIGURATION
    // =====================================================================================

    [Header("Simulation Setup")]
    [Tooltip("Total number of celestial bodies (Enforced to be a multiple of 256 for warp alignment)")]
    [Range(256, 262144)]
    public int bodyCount = 65536;

    [Tooltip("Active scenario preset loaded on startup")]
    public PresetScenario initialPreset = PresetScenario.CollidingGalaxies;

    [Header("Physical Constants")]
    [Tooltip("Universal Gravitational Constant (G) - scales overall gravitational strength")]
    public float gravitationalConstant = 1.0f;

    [Tooltip("Plummer softening factor (epsilon) - prevents infinite acceleration on close flybys")]
    [Range(0.01f, 5.0f)]
    public float softening = 0.85f;

    [Tooltip("Simulation time scale factor (dt multiplier)")]
    [Range(0.0f, 5.0f)]
    public float timeScale = 1.0f;

    [Tooltip("Numerical velocity damping factor (1.0 = frictionless vacuum, 0.999 = slight drag)")]
    [Range(0.95f, 1.0f)]
    public float damping = 1.0f;

    [Header("GPU Resources")]
    [Tooltip("Reference to the HLSL DirectCompute physics kernel")]
    public ComputeShader nbodyCompute;

    [Tooltip("Material using Custom/NBodyParticle procedural instanced shader")]
    public Material particleMaterial;

    [Header("Visual Tuning")]
    [Tooltip("World-space size of individual star billboard quads")]
    [Range(0.05f, 5.0f)]
    public float particleSize = 0.85f;

    [Tooltip("Velocity threshold mapped to superheated white/cyan core color")]
    [Range(1.0f, 50.0f)]
    public float maxSpeedForColor = 20.0f;

    [Tooltip("Additive optical glow intensity multiplier")]
    [Range(0.5f, 10.0f)]
    public float glowIntensity = 2.5f;

    // =====================================================================================
    // PRIVATE GPU BUFFERS & STATE
    // =====================================================================================

    // Double-buffered VRAM arrays for race-free ping-pong integration
    private ComputeBuffer bufferA;
    private ComputeBuffer bufferB;

    // Indirect arguments buffer consumed by the GPU hardware rasterizer:
    // [0] = Index Count per Instance (6 for a 2-triangle quad)
    // [1] = Instance Count (Body Count: e.g. 65,536)
    // [2] = Start Index Location (0)
    // [3] = Base Vertex Location (0)
    // [4] = Start Instance Location (0)
    private ComputeBuffer argsBuffer;

    private int kernelIntegrate;
    private Mesh quadMesh;
    private Bounds renderBounds;
    private bool isInitialized = false;

    /// <summary>
    /// Currently simulated preset scenario.
    /// </summary>
    public PresetScenario CurrentPreset { get; private set; }

    private void Start()
    {
        InitializeSimulation(initialPreset);
    }

    /// <summary>
    /// Initializes or resets the simulation with a specified initial condition preset.
    /// Releases old GPU buffers, allocates new VRAM structures, and seeds astrophysical coordinates.
    /// </summary>
    public void InitializeSimulation(PresetScenario preset)
    {
        // 1. Threadgroup Alignment:
        // Ensure bodyCount is a strict multiple of the compute shader block size (256).
        bodyCount = Mathf.Max(256, (bodyCount / 256) * 256);
        CurrentPreset = preset;

        // Release old GPU allocations to avoid memory leaks
        ReleaseBuffers();

        // 2. Allocate Double-Buffered Structured VRAM Arrays:
        // ComputeBufferType.Default maps to an HLSL StructuredBuffer.
        int stride = Marshal.SizeOf(typeof(Body)); // Exactly 32 bytes
        bufferA = new ComputeBuffer(bodyCount, stride, ComputeBufferType.Default);
        bufferB = new ComputeBuffer(bodyCount, stride, ComputeBufferType.Default);

        // 3. Generate Astrophysical Initial Conditions on CPU:
        Body[] initialBodies = new Body[bodyCount];
        switch (preset)
        {
            case PresetScenario.CollidingGalaxies:
                GenerateCollidingGalaxies(initialBodies);
                break;
            case PresetScenario.PlummerSphereCluster:
                GeneratePlummerSphere(initialBodies);
                break;
            case PresetScenario.AccretionDisk:
                GenerateAccretionDisk(initialBodies);
                break;
        }

        // Upload initial bodies to GPU VRAM once (after this, data remains 100% on GPU)
        bufferA.SetData(initialBodies);
        bufferB.SetData(initialBodies);

        // 4. Create Minimal Billboard Quad Mesh:
        quadMesh = CreateQuadMesh();

        // 5. Setup Indirect Arguments Buffer:
        uint[] args = new uint[5]
        {
            quadMesh.GetIndexCount(0), // 6 indices
            (uint)bodyCount,           // N instances
            quadMesh.GetIndexStart(0), // 0
            quadMesh.GetBaseVertex(0), // 0
            0                          // 0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        // 6. Locate Compute Shader Kernel:
        if (nbodyCompute != null)
        {
            kernelIntegrate = nbodyCompute.FindKernel("IntegrateNBody");
            if (kernelIntegrate < 0)
            {
                Debug.LogError("[NBodyController] Could not find kernel 'IntegrateNBody' in compute shader!");
            }
        }
        else
        {
            Debug.LogError("[NBodyController] nbodyCompute reference is null!");
        }

        // 7. Define Global Rendering Bounds:
        // A large bounding box prevents premature camera frustum culling when panning.
        renderBounds = new Bounds(Vector3.zero, Vector3.one * 50000.0f);

        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized || nbodyCompute == null || particleMaterial == null || kernelIntegrate < 0)
            return;

        // Effective simulation timestep (dt):
        // Clamp to 0.05s (20 FPS equivalent) to prevent explosive numerical instability during frame hiccups.
        float dt = Time.deltaTime * timeScale;
        if (dt > 0.05f) dt = 0.05f;

        // ---------------------------------------------------------------------------------
        // STAGE 1: DISPATCH GPU COMPUTE KERNEL
        // ---------------------------------------------------------------------------------
        // Pass simulation constants to the compute shader
        nbodyCompute.SetInt("_BodyCount", bodyCount);
        nbodyCompute.SetFloat("_DeltaTime", dt);
        nbodyCompute.SetFloat("_G", gravitationalConstant);
        nbodyCompute.SetFloat("_SofteningSquared", softening * softening);
        nbodyCompute.SetFloat("_Damping", damping);

        // Bind ping-pong buffers: Read from A, Write updated states into B
        nbodyCompute.SetBuffer(kernelIntegrate, "_BodiesRead", bufferA);
        nbodyCompute.SetBuffer(kernelIntegrate, "_BodiesWrite", bufferB);

        // Dispatch grid: (bodyCount / 256) threadgroups of 256 threads each
        int threadGroups = bodyCount / 256;
        nbodyCompute.Dispatch(kernelIntegrate, threadGroups, 1, 1);

        // Swap buffer pointers: B contains the newly integrated state (time t + dt)
        ComputeBuffer temp = bufferA;
        bufferA = bufferB;
        bufferB = temp;

        // ---------------------------------------------------------------------------------
        // STAGE 2: ZERO-COPY PROCEDURAL INSTANCED DRAW CALL
        // ---------------------------------------------------------------------------------
        // Bind the active VRAM buffer directly to the material.
        // The vertex shader reads directly from this buffer via SV_InstanceID.
        particleMaterial.SetBuffer("_Bodies", bufferA);
        particleMaterial.SetFloat("_ParticleSize", particleSize);
        particleMaterial.SetFloat("_MaxSpeed", maxSpeedForColor);
        particleMaterial.SetFloat("_GlowIntensity", glowIntensity);

        // Submits an indirect draw command to the GPU rasterizer for all active cameras
        Graphics.DrawMeshInstancedIndirect(quadMesh, 0, particleMaterial, renderBounds, argsBuffer);
    }

    #region Astrophysical Initial Condition Generators

    /// <summary>
    /// Generates two interacting spiral disk galaxies on an eccentric collision trajectory (Milky Way & Andromeda analog).
    /// Demonstrates the formation of tidal tails, bridges, and galactic mergers.
    /// </summary>
    private void GenerateCollidingGalaxies(Body[] bodies)
    {
        int half = bodyCount / 2;

        // Galaxy 1: Center at (-50, 12, 0), moving along (+0.6, -0.15, 0)
        Vector3 center1 = new Vector3(-50.0f, 12.0f, 0.0f);
        Vector3 velocity1 = new Vector3(0.6f, -0.15f, 0.0f);
        Vector3 normal1 = Vector3.up;
        float radius1 = 40.0f;
        float coreMass1 = 3000.0f;

        // Galaxy 2: Center at (+50, -12, 0), moving along (-0.6, 0.15, 0)
        Vector3 center2 = new Vector3(50.0f, -12.0f, 0.0f);
        Vector3 velocity2 = new Vector3(-0.6f, 0.15f, 0.0f);
        // Tilt Galaxy 2 disk by 45 degrees to produce asymmetric 3D tidal bridges and extended tails
        Vector3 normal2 = Quaternion.Euler(45.0f, 25.0f, 0.0f) * Vector3.up;
        float radius2 = 35.0f;
        float coreMass2 = 2500.0f;

        PopulateSpiralGalaxy(bodies, 0, half, center1, velocity1, normal1, radius1, coreMass1);
        PopulateSpiralGalaxy(bodies, half, bodyCount, center2, velocity2, normal2, radius2, coreMass2);
    }

    /// <summary>
    /// Populates a single inclined spiral disk galaxy with an exponential density profile and Keplerian orbital dynamics.
    /// </summary>
    private void PopulateSpiralGalaxy(Body[] bodies, int startIdx, int endIdx, Vector3 center, Vector3 bulkVelocity, Vector3 normal, float maxRadius, float coreMass)
    {
        // 1. Central Supermassive Black Hole (SMBH):
        // Anchors the galactic potential well.
        bodies[startIdx] = new Body
        {
            position = center,
            mass = coreMass,
            velocity = bulkVelocity,
            padding = 0.0f
        };

        // Construct orthonormal basis in the disk plane perpendicular to the normal vector
        Vector3 tangent1 = Vector3.Cross(normal, Vector3.forward);
        if (tangent1.sqrMagnitude < 0.001f)
            tangent1 = Vector3.Cross(normal, Vector3.right);
        tangent1.Normalize();
        Vector3 tangent2 = Vector3.Cross(normal, tangent1).normalized;

        float starMass = 1.0f / (endIdx - startIdx);

        for (int i = startIdx + 1; i < endIdx; ++i)
        {
            // Exponential disk radial distribution:
            // Sampling r = R_max * sqrt(u) produces a realistic quadratic density concentration towards the core.
            float u = UnityEngine.Random.value;
            float r = maxRadius * Mathf.Sqrt(u);
            if (r < 1.0f) r = 1.0f; // Soft core cutoff

            float theta = UnityEngine.Random.value * Mathf.PI * 2.0f;

            // Vertical disk scale height (thickness dispersion):
            // Thicker near core, thinning out toward edge: z ~ (1 - r / R_max)
            float z = (UnityEngine.Random.value - 0.5f) * 1.5f * (1.0f - r / maxRadius);

            Vector3 pos = center + (tangent1 * Mathf.Cos(theta) + tangent2 * Mathf.Sin(theta)) * r + normal * z;

            // Keplerian orbital speed: v_circ = sqrt(G * M_enclosed / r)
            float enclosedMass = coreMass + (coreMass * 0.5f * (r / maxRadius));
            float orbitalSpeed = Mathf.Sqrt((gravitationalConstant * enclosedMass) / (r + softening));

            // Direction: Tangent to the radius vector in the disk plane (perpendicular to normal)
            Vector3 radialDir = (pos - center - normal * z).normalized;
            Vector3 orbitVelocity = Vector3.Cross(normal, radialDir).normalized * orbitalSpeed;

            bodies[i] = new Body
            {
                position = pos,
                mass = starMass,
                velocity = bulkVelocity + orbitVelocity,
                padding = orbitalSpeed // Scalar speed for velocity-based shader color mapping
            };
        }
    }

    /// <summary>
    /// Generates a spherical star cluster in dynamic virial equilibrium using the Plummer Model.
    /// Density Profile: rho(r) = (3M / 4pi*a^3) * (1 + r^2 / a^2)^(-5/2)
    /// Velocity distribution is sampled via Von Neumann rejection on Eddington's isotropic distribution.
    /// </summary>
    private void GeneratePlummerSphere(Body[] bodies)
    {
        float scaleRadius = 20.0f;
        float totalMass = 5000.0f;
        float starMass = totalMass / bodyCount;

        for (int i = 0; i < bodyCount; ++i)
        {
            // Inverse cumulative distribution function (CDF) for Plummer radius:
            // r = a / sqrt(X^(-2/3) - 1), where X ~ Uniform(0, 1)
            float x1 = UnityEngine.Random.value;
            float r = scaleRadius / Mathf.Sqrt(Mathf.Pow(x1, -2.0f / 3.0f) - 1.0f);
            if (float.IsInfinity(r) || r > scaleRadius * 8.0f)
                r = scaleRadius * 8.0f;

            // Isotropic random direction on unit sphere
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            Vector3 pos = dir * r;

            // Local escape velocity at distance r: v_esc = sqrt(2 * G * M / sqrt(r^2 + a^2))
            float vEscape = Mathf.Sqrt(2.0f * gravitationalConstant * totalMass / Mathf.Sqrt(r * r + scaleRadius * scaleRadius));

            // Von Neumann Rejection Sampling for velocity magnitude q = v / v_esc:
            // Eddington distribution function for Plummer: f(q) ~ q^2 * (1 - q^2)^(7/2)
            float q = 0.0f;
            float g = 0.0f;
            do
            {
                q = UnityEngine.Random.value;
                g = 0.1f * UnityEngine.Random.value;
            } while (g > q * q * Mathf.Pow(1.0f - q * q, 3.5f));

            float speed = q * vEscape;
            Vector3 vel = UnityEngine.Random.onUnitSphere * speed;

            bodies[i] = new Body
            {
                position = pos,
                mass = starMass,
                velocity = vel,
                padding = speed
            };
        }
    }

    /// <summary>
    /// Generates a protoplanetary dust disk surrounding a massive central star.
    /// Demonstrates Keplerian ring dynamics and planetary formation disc mechanics.
    /// </summary>
    private void GenerateAccretionDisk(Body[] bodies)
    {
        // Central stellar attractor
        float centralMass = 8000.0f;
        bodies[0] = new Body
        {
            position = Vector3.zero,
            mass = centralMass,
            velocity = Vector3.zero,
            padding = 0.0f
        };

        float diskInner = 5.0f;
        float diskOuter = 65.0f;
        float dustMass = 0.1f;

        for (int i = 1; i < bodyCount; ++i)
        {
            float r = UnityEngine.Random.Range(diskInner, diskOuter);
            float angle = UnityEngine.Random.value * Mathf.PI * 2.0f;
            float z = (UnityEngine.Random.value - 0.5f) * 0.4f * (r / diskOuter);

            Vector3 pos = new Vector3(Mathf.Cos(angle) * r, z, Mathf.Sin(angle) * r);

            // Circular Keplerian orbital speed: v = sqrt(G * M / r)
            float speed = Mathf.Sqrt((gravitationalConstant * centralMass) / (r + softening));
            Vector3 vel = new Vector3(-Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * speed;

            bodies[i] = new Body
            {
                position = pos,
                mass = dustMass,
                velocity = vel,
                padding = speed
            };
        }
    }

    #endregion

    /// <summary>
    /// Creates a minimal 4-vertex, 2-triangle billboard quad centered at (0, 0, 0).
    /// Used as the instanced geometry submitted to Graphics.DrawMeshInstancedIndirect.
    /// </summary>
    private Mesh CreateQuadMesh()
    {
        Mesh m = new Mesh();
        m.name = "ParticleQuad";

        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-0.5f, -0.5f, 0.0f),
            new Vector3( 0.5f, -0.5f, 0.0f),
            new Vector3(-0.5f,  0.5f, 0.0f),
            new Vector3( 0.5f,  0.5f, 0.0f)
        };

        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0.0f, 0.0f),
            new Vector2(1.0f, 0.0f),
            new Vector2(0.0f, 1.0f),
            new Vector2(1.0f, 1.0f)
        };

        // Two clockwise triangles: (0, 2, 1) and (2, 3, 1)
        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

        m.vertices = vertices;
        m.uv = uvs;
        m.triangles = triangles;
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Safely releases native GPU ComputeBuffer resources from VRAM.
    /// CRITICAL: In Unity, failing to release ComputeBuffers results in GPU memory leaks
    /// that persist until the entire Unity process terminates!
    /// </summary>
    private void ReleaseBuffers()
    {
        if (bufferA != null) { bufferA.Release(); bufferA = null; }
        if (bufferB != null) { bufferB.Release(); bufferB = null; }
        if (argsBuffer != null) { argsBuffer.Release(); argsBuffer = null; }
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }
}
