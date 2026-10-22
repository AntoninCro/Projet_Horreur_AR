// Shader unique pour les effets (sang, rembourrage, étincelles, flash, faisceau, laser).
// Mode alpha ou additif via les propriétés de blend ; éclairage optionnel par la lampe torche.
Shader "HorrorMR/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Couleur", Color) = (1, 1, 1, 1)
        _Emission ("Émission", Float) = 0
        _Lit ("Éclairé par la lampe (0/1)", Float) = 1
        _VerticalFade ("Fondu vertical (0/1)", Float) = 0
        _PulseSpeed ("Vitesse de pulsation", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendA ("Src Blend Alpha", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendA ("Dst Blend Alpha", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HorrorParticle"
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HorrorCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Emission;
                float _Lit;
                float _VerticalFade;
                float _PulseSpeed;
                float _SrcBlend;
                float _DstBlend;
                float _SrcBlendA;
                float _DstBlendA;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                ZERO_INITIALIZE(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color * i.color;

                if (_Lit > 0.5)
                {
                    float light = HorrorFlashlight(i.positionWS);
                    c.rgb *= _HorrorAmbient.rgb + light * float3(1.0, 0.93, 0.8) * 1.4;
                }

                c.rgb += c.rgb * _Emission;

                if (_VerticalFade > 0.5)
                {
                    float f = 1.0 - i.uv.y;
                    c.a *= f * f;
                }

                if (_PulseSpeed > 0.0)
                    c.a *= 0.65 + 0.35 * sin(_Time.y * _PulseSpeed);

                // En mode additif (Src = One), on prémultiplie pour rester correct dans les deux modes.
                if (_SrcBlend < 1.5)
                {
                    c.rgb *= c.a;
                    c.a *= 0.5;
                }
                return half4(c.rgb, c.a);
            }
            ENDHLSL
        }
    }
}
