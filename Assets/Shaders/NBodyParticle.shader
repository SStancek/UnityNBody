Shader "Custom/NBodyParticle"
{
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        
        // Additive blending for luminous cosmic glow
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #include "UnityCG.cginc"

            struct Body
            {
                float3 position;
                float mass;
                float3 velocity;
                float speed; // Stored in padding field by compute shader
            };

            // Bound directly to GPU VRAM StructuredBuffer
            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                StructuredBuffer<Body> _Bodies;
            #endif

            float4 _BaseColor;
            float4 _CoreColor;
            float _ParticleSize;
            float _MaxSpeed;
            float _GlowIntensity;

            void setup()
            {
                // Required procedural hook for Unity's instancing pipeline
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    Body b = _Bodies[v.instanceID];
                    float3 worldBodyPos = b.position;
                    float speed = b.speed;
                #else
                    float3 worldBodyPos = float3(0, 0, 0);
                    float speed = 0.0;
                #endif

                // Spherical billboard quad oriented directly towards camera in eye-space
                float3 viewPos = UnityWorldToViewPos(worldBodyPos);
                viewPos.xy += v.vertex.xy * _ParticleSize;

                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = v.uv;

                // Velocity-mapped blackbody spectrum:
                // Slow/outer stars = warm amber; medium = azure cyan; relativistic core = brilliant white
                float speedNorm = saturate(speed / max(_MaxSpeed, 0.001f));
                
                float4 slowColor = float4(0.95, 0.40, 0.10, 1.0);  // Stellar amber
                float4 midColor  = float4(0.25, 0.65, 1.00, 1.0);  // Azure cyan
                float4 fastColor = float4(1.00, 1.00, 1.00, 1.0);  // Superheated core white

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
                // Procedural soft-radial Gaussian glow
                float2 coord = i.uv - float2(0.5, 0.5);
                float distSq = dot(coord, coord) * 4.0;
                clip(1.0 - distSq);
                float alpha = exp(-distSq * 3.5);

                return i.color * alpha;
            }
            ENDCG
        }
    }
    FallBack Off
}
