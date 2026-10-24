using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// High-Performance GPU N-Body Simulation Controller.
/// Coordinates GPU Compute Shader execution and indirect procedural instancing.
/// </summary>
public class NBodyController : MonoBehaviour
{
    // Struct layout exactly matches the 32-byte HLSL 'Body' struct
    [StructLayout(LayoutKind.Sequential)]
    public struct Body
    {
        public Vector3 position;
        public float mass;
        public Vector3 velocity;
        public float padding; // Stores speed for velocity-based shader color remap
    }

    public enum PresetScenario
    {
        CollidingGalaxies,
        PlummerSphereCluster,
        AccretionDisk
    }

    [Header("Simulation Setup")]
    [Tooltip("Total number of celestial bodies (Must be multiple of 256)")]
    [Range(256, 262144)]
    public int bodyCount = 65536;

    [Tooltip("Initial preset scenario to simulate")]
    public PresetScenario initialPreset = PresetScenario.CollidingGalaxies;

    [Header("Physical Constants")]
    [Tooltip("Universal Gravitational Constant")]
    public float gravitationalConstant = 1.0f;

    [Tooltip("Plummer softening factor to prevent infinite singularity acceleration on close flybys")]
    [Range(0.01f, 5.0f)]
    public float softening = 0.85f;

    [Tooltip("Simulation time scale factor")]
    [Range(0.0f, 5.0f)]
    public float timeScale = 1.0f;

    [Tooltip("Velocity damping factor (1.0 = ideal frictionless vacuum)")]
    [Range(0.95f, 1.0f)]
    public float damping = 1.0f;

    [Header("GPU Resources")]
    public ComputeShader nbodyCompute;
    public Material particleMaterial;

    [Header("Visual Tuning")]
    [Range(0.05f, 5.0f)]
    public float particleSize = 0.85f;

    [Range(1.0f, 50.0f)]
    public float maxSpeedForColor = 20.0f;

    [Range(0.5f, 10.0f)]
    public float glowIntensity = 2.5f;

    // GPU Compute Buffers
    private ComputeBuffer bufferA;
    private ComputeBuffer bufferB;
    private ComputeBuffer argsBuffer;

    private int kernelIntegrate;
    private Mesh quadMesh;
    private Bounds renderBounds;
    private bool isInitialized = false;

    // Track active preset
    public PresetScenario CurrentPreset { get; private set; }

    private void Start()
    {
        InitializeSimulation(initialPreset);
    }

    /// <summary>
    /// Initializes or reinitializes the simulation with a given scenario preset.
    /// </summary>
    public void InitializeSimulation(PresetScenario preset)
    {
        // Ensure bodyCount is a clean multiple of the compute threadgroup size (256)
        bodyCount = Mathf.Max(256, (bodyCount / 256) * 256);
        CurrentPreset = preset;

        ReleaseBuffers();

        // 1. Allocate Double-Buffered GPU VRAM arrays
        // Stride = 32 bytes (sizeof(float) * 8: pos(3) + mass(1) + vel(3) + pad(1))
        int stride = Marshal.SizeOf(typeof(Body));
        bufferA = new ComputeBuffer(bodyCount, stride, ComputeBufferType.Default);
        bufferB = new ComputeBuffer(bodyCount, stride, ComputeBufferType.Default);

        // 2. Generate Initial Conditions on CPU
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

        // Upload initial bodies to GPU VRAM once (after this, data remains 100% on the GPU)
        bufferA.SetData(initialBodies);
        bufferB.SetData(initialBodies);

        // 3. Prepare Procedural Quad Mesh for Billboard Particles
        quadMesh = CreateQuadMesh();

        // 4. Setup DrawMeshInstancedIndirect Argument Buffer
        // Args: [index count per instance, instance count, start index, base vertex, start instance]
        uint[] args = new uint[5]
        {
            quadMesh.GetIndexCount(0),
            (uint)bodyCount,
            quadMesh.GetIndexStart(0),
            quadMesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        // Locate Compute Kernel
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
            Debug.LogError("[NBodyController] nbodyCompute is null!");
        }

        // Huge bounding box so particles aren't prematurely frustum-culled
        renderBounds = new Bounds(Vector3.zero, Vector3.one * 50000.0f);

        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized || nbodyCompute == null || particleMaterial == null)
            return;

        // Effective simulation timestep
        float dt = Time.deltaTime * timeScale;
        if (dt > 0.05f) dt = 0.05f; // Clamp to avoid explosive numerical divergence during stutter

        // ---------------------------------------------------------------------------------
        // 1. DISPATCH GPU COMPUTE KERNEL
        // ---------------------------------------------------------------------------------
        nbodyCompute.SetInt("_BodyCount", bodyCount);
        nbodyCompute.SetFloat("_DeltaTime", dt);
        nbodyCompute.SetFloat("_G", gravitationalConstant);
        nbodyCompute.SetFloat("_SofteningSquared", softening * softening);
        nbodyCompute.SetFloat("_Damping", damping);

        // Bind read buffer and write buffer
        nbodyCompute.SetBuffer(kernelIntegrate, "_BodiesRead", bufferA);
        nbodyCompute.SetBuffer(kernelIntegrate, "_BodiesWrite", bufferB);

        int threadGroups = bodyCount / 256;
        nbodyCompute.Dispatch(kernelIntegrate, threadGroups, 1, 1);

        // Ping-pong buffer swap (B becomes current state for rendering & next frame's read)
        ComputeBuffer temp = bufferA;
        bufferA = bufferB;
        bufferB = temp;

        // ---------------------------------------------------------------------------------
        // 2. ZERO-COPY GPU PROCEDURAL INSTANCED RENDERING
        // ---------------------------------------------------------------------------------
        particleMaterial.SetBuffer("_Bodies", bufferA);
        particleMaterial.SetFloat("_ParticleSize", particleSize);
        particleMaterial.SetFloat("_MaxSpeed", maxSpeedForColor);
        particleMaterial.SetFloat("_GlowIntensity", glowIntensity);

        // Single indirect draw call renders all particles directly from VRAM!
        Graphics.DrawMeshInstancedIndirect(quadMesh, 0, particleMaterial, renderBounds, argsBuffer);
    }

    #region Initial Condition Generators

    /// <summary>
    /// Generates two interacting spiral galaxies on an eccentric collision trajectory (Milky Way & Andromeda analog).
    /// </summary>
    private void GenerateCollidingGalaxies(Body[] bodies)
    {
        int half = bodyCount / 2;

        // Galaxy 1: Center at (-45, 10, 0), moving along (+0.7, -0.2, 0)
        Vector3 center1 = new Vector3(-50.0f, 12.0f, 0.0f);
        Vector3 velocity1 = new Vector3(0.6f, -0.15f, 0.0f);
        Vector3 normal1 = Vector3.up;
        float radius1 = 40.0f;
        float coreMass1 = 3000.0f;

        // Galaxy 2: Center at (+50, -12, 0), moving along (-0.6, 0.15, 0)
        Vector3 center2 = new Vector3(50.0f, -12.0f, 0.0f);
        Vector3 velocity2 = new Vector3(-0.6f, 0.15f, 0.0f);
        // Tilt galaxy 2 disk by 45 degrees for asymmetric 3D tidal bridge & tail formation
        Vector3 normal2 = Quaternion.Euler(45.0f, 25.0f, 0.0f) * Vector3.up;
        float radius2 = 35.0f;
        float coreMass2 = 2500.0f;

        PopulateSpiralGalaxy(bodies, 0, half, center1, velocity1, normal1, radius1, coreMass1);
        PopulateSpiralGalaxy(bodies, half, bodyCount, center2, velocity2, normal2, radius2, coreMass2);
    }

    private void PopulateSpiralGalaxy(Body[] bodies, int startIdx, int endIdx, Vector3 center, Vector3 bulkVelocity, Vector3 normal, float maxRadius, float coreMass)
    {
        // Central Supermassive Black Hole
        bodies[startIdx] = new Body
        {
            position = center,
            mass = coreMass,
            velocity = bulkVelocity,
            padding = 0.0f
        };

        Vector3 tangent1 = Vector3.Cross(normal, Vector3.forward);
        if (tangent1.sqrMagnitude < 0.001f)
            tangent1 = Vector3.Cross(normal, Vector3.right);
        tangent1.Normalize();
        Vector3 tangent2 = Vector3.Cross(normal, tangent1).normalized;

        float starMass = 1.0f / (endIdx - startIdx);

        for (int i = startIdx + 1; i < endIdx; ++i)
        {
            // Exponential disk radial distribution
            float u = UnityEngine.Random.value;
            float r = maxRadius * Mathf.Sqrt(u);
            if (r < 1.0f) r = 1.0f;

            float theta = UnityEngine.Random.value * Mathf.PI * 2.0f;

            // Disk vertical thickness dispersion
            float z = (UnityEngine.Random.value - 0.5f) * 1.5f * (1.0f - r / maxRadius);

            Vector3 pos = center + (tangent1 * Mathf.Cos(theta) + tangent2 * Mathf.Sin(theta)) * r + normal * z;

            // Keplerian orbital speed: v = sqrt(G * M_enclosed / r)
            float enclosedMass = coreMass + (coreMass * 0.5f * (r / maxRadius));
            float orbitalSpeed = Mathf.Sqrt((gravitationalConstant * enclosedMass) / (r + softening));

            // Orbital velocity direction is perpendicular to radius vector in the disk plane
            Vector3 radialDir = (pos - center - normal * z).normalized;
            Vector3 orbitVelocity = Vector3.Cross(normal, radialDir).normalized * orbitalSpeed;

            bodies[i] = new Body
            {
                position = pos,
                mass = starMass,
                velocity = bulkVelocity + orbitVelocity,
                padding = orbitalSpeed
            };
        }
    }

    /// <summary>
    /// Generates a spherical star cluster in dynamic virial equilibrium (Plummer Model).
    /// </summary>
    private void GeneratePlummerSphere(Body[] bodies)
    {
        float scaleRadius = 20.0f;
        float totalMass = 5000.0f;
        float starMass = totalMass / bodyCount;

        for (int i = 0; i < bodyCount; ++i)
        {
            // Sample radius using inverse Plummer cumulative distribution
            float x1 = UnityEngine.Random.value;
            float r = scaleRadius / Mathf.Sqrt(Mathf.Pow(x1, -2.0f / 3.0f) - 1.0f);
            if (float.IsInfinity(r) || r > scaleRadius * 8.0f)
                r = scaleRadius * 8.0f;

            // Random spherical direction
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            Vector3 pos = dir * r;

            // Plummer escape velocity at radius r
            float vEscape = Mathf.Sqrt(2.0f * gravitationalConstant * totalMass / Mathf.Sqrt(r * r + scaleRadius * scaleRadius));

            // Von Neumann rejection sampling for Eddington distribution function
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
    /// Generates a proto-planetary dust disk surrounding a massive central star.
    /// </summary>
    private void GenerateAccretionDisk(Body[] bodies)
    {
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

            // Circular orbital velocity
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
    /// Generates a simple 2D billboard quad mesh centered at origin.
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

        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

        m.vertices = vertices;
        m.uv = uvs;
        m.triangles = triangles;
        m.RecalculateBounds();
        return m;
    }

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

