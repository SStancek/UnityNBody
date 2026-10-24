Shader "Custom/NBodyParticle"
{
    // =========================================================================================
    // PROCEDURAL INSTANCED BILLBOARD SHADER FOR GPU N-BODY VISUALIZATION
    // =========================================================================================
    //
    // [CODING GUIDE: HOW ZERO-COPY RENDERING WORKS IN UNITY]
    //
    // 1. WHY STANDARD GAME OBJECT RENDERING FAILS AT SCALE:
    //    In Unity, creating 65,536 GameObjects with MeshRenderers creates massive CPU overhead
    //    (transform hierarchy updates, culling, matrix preparation, draw call submission).
    //    Frame rates plummet to < 2 FPS.
    //
    // 2. THE ZERO-COPY PIPELINE (Graphics.DrawMeshInstancedIndirect):
    //    - The GPU Compute Shader writes positions & velocities to a VRAM buffer (ComputeBuffer).
    //    - We bind this exact buffer directly to the material: `material.SetBuffer("_Bodies", buffer)`.
    //    - In the Vertex Shader, each instance reads its own position using `SV_InstanceID`.
    //    - Data never leaves GPU VRAM: 0 bytes of CPU readback (`GetData`), 1 single draw call!
    //
    // 3. PROCEDURAL BILLBOARDING IN EYE-SPACE:
    //    To render stars as 2D camera-facing disks in 3D space:
    //    - Transform the body's world center into Camera View Space via `UnityWorldToViewPos()`.
    //    - In view space, the camera looks down the -Z axis, and the camera's image plane is XY.
    //    - By expanding the mesh vertices along view-space XY, the quad is mathematically
    //      guaranteed to face the camera lens perfectly at any orientation with zero matrix trigonometry!
    //
    // 4. ADDITIVE OPTICAL GLOW (Blend One One):
    //    - Transparent additive blending adds emitted photons directly to the framebuffer.
    //    - ZWrite Off eliminates depth-buffer fighting between overlapping particles.
    //    - Where thousands of stars concentrate in galactic cores, additive values naturally
    //      saturate to brilliant celestial white (HDR blooming effect).
    //
    // =========================================================================================

    Properties
    {
        _BaseColor ("Base Star Color", Color) = (0.3, 0.6, 1.0, 1.0)
        _CoreColor ("Core High-Velocity Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _ParticleSize ("Particle Size", Float) = 0.85
        _MaxSpeed ("Color Remap Max Speed", Float) = 20.0
        _GlowIntensity ("Glow Intensity", Float) = 2.5
    }

    SubShader
    {
        // Transparent queue ensures particles render after opaque geometry
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        
        // Additive blending: FinalColor = SourceColor * 1 + DestColor * 1
        Blend One One
        ZWrite Off  // Disable depth writes to prevent self-occlusion artifacts
        Cull Off    // Render both front and back faces

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            // Informs Unity to compile GPU instancing variants
            #pragma multi_compile_instancing

            // CRITICAL DIRECTIVE FOR PROCEDURAL INSTANCING:
            // Without `procedural:setup`, Unity strips out the procedural instancing variant,
            // causing UNITY_PROCEDURAL_INSTANCING_ENABLED to evaluate to false!
            #pragma instancing_options procedural:setup
            #include "UnityCG.cginc"

            // Data structure matching the 32-byte HLSL compute kernel layout
            struct Body
            {
                float3 position;
                float mass;
                float3 velocity;
                float speed; // Extracted from padding field by compute shader
            };

            // StructuredBuffer bound directly from GPU VRAM
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<Body> _Bodies;
            #endif

            float4 _BaseColor;
            float4 _CoreColor;
            float _ParticleSize;
            float _MaxSpeed;
            float _GlowIntensity;

            // Required lifecycle hook for Unity's procedural instancing system
            void setup()
            {
                // Can be used for custom per-instance matrix setup if needed
            }

            // Vertex input structure
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID; // Unique hardware instance index (0..N-1)
            };

            // Vertex to Fragment interpolator structure
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // -------------------------------------------------------------------------
                // 1. Fetch Instance State from VRAM Buffer
                // -------------------------------------------------------------------------
                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    Body b = _Bodies[v.instanceID];
                    float3 worldBodyPos = b.position;
                    float speed = b.speed;
                #else
                    float3 worldBodyPos = float3(0.0f, 0.0f, 0.0f);
                    float speed = 0.0f;
                #endif

                // -------------------------------------------------------------------------
                // 2. Camera-Facing Spherical Billboarding in View-Space
                // -------------------------------------------------------------------------
                // Transform particle world center to camera eye space (view space)
                float3 viewPos = UnityWorldToViewPos(worldBodyPos);

                // Offset the quad's local vertices along the camera's view-plane XY axes
                viewPos.xy += v.vertex.xy * _ParticleSize;

                // Project from view space to homogeneous clip space
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = v.uv;

                // -------------------------------------------------------------------------
                // 3. Spectral Energy Remapping (Blackbody Radiation Gradient)
                // -------------------------------------------------------------------------
                // Normalize scalar speed against _MaxSpeed
                float speedNorm = saturate(speed / max(_MaxSpeed, 0.001f));
                
                // Color Gradient Palette:
                // - Low velocity (outer galaxy disk): Warm stellar amber/red (~3,000 K)
                // - Medium velocity (mid-disk stars): Azure cyan (~6,000 K)
                // - Extreme relativistic velocity (core stars): Hot blazing white (~15,000+ K)
                float4 slowColor = float4(0.95, 0.40, 0.10, 1.0);
                float4 midColor  = float4(0.25, 0.65, 1.00, 1.0);
                float4 fastColor = float4(1.00, 1.00, 1.00, 1.0);

                float4 starColor;
                if (speedNorm < 0.5)
                {
                    starColor = lerp(slowColor, midColor, speedNorm * 2.0);
                }
                else
                {
                    starColor = lerp(midColor, fastColor, (speedNorm - 0.5) * 2.0);
                }

                o.color = starColor * _GlowIntensity;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // -------------------------------------------------------------------------
                // 4. Procedural Radial Gaussian Star Disk
                // -------------------------------------------------------------------------
                // Map UV coordinates from [0, 1] to [-1, 1] relative to quad center
                float2 coord = i.uv - float2(0.5, 0.5);
                float distSq = dot(coord, coord) * 4.0; // 0.0 at center, 1.0 at outer edge

                // Discard pixels outside the circular radius to save raster fillrate
                clip(1.0 - distSq);

                // Gaussian intensity falloff: I(r) = exp(-k * r^2)
                // Replicates the optical point spread function (PSF) of a luminous celestial point
                float alpha = exp(-distSq * 3.5);

                return i.color * alpha;
            }
            ENDCG
        }
    }
    FallBack Off
}
