// Calque plein écran (par œil) : vignette noire, écran rouge des dégâts, flash rouge, fondu au noir.
// Le maillage est un quad en coordonnées de clip, il n'est jamais déformé par la caméra.
Shader "HorrorMR/ScreenOverlay"
{
    Properties
    {
        _Damage ("Dégâts (0-1)", Range(0, 1)) = 0
        _Flash ("Flash rouge (0-1)", Range(0, 1)) = 0
        _Vignette ("Vignette noire (0-1)", Range(0, 1)) = 0
        _Fade ("Fondu au noir (0-1)", Range(0, 1)) = 0
        _BloodColor ("Couleur du sang", Color) = (0.55, 0.0, 0.0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ScreenOverlay"
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Damage;
                float _Flash;
                float _Vignette;
                float _Fade;
                float4 _BloodColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                ZERO_INITIALIZE(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Les sommets du quad sont déjà dans [-1, 1] : on les envoie directement en clip space.
                o.positionCS = float4(v.positionOS.xy, 0.5, 1.0);
                o.uv = v.positionOS.xy * 0.5 + 0.5;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 centered = (i.uv - 0.5) * 2.0;
                float r = length(centered);

                // Bords rouges irréguliers façon sang, qui pulsent.
                float n = Noise(i.uv * 7.0) * 0.6 + Noise(i.uv * 19.0) * 0.4;
                float pulse = 0.85 + 0.15 * sin(_Time.y * 6.0);
                float edge = smoothstep(1.05 - _Damage * 0.8, 1.35, r + n * 0.35);
                float redA = saturate(edge * _Damage * pulse * 1.2 + _Flash * (0.35 + 0.4 * r));

                // Vignette noire (le champ de vision se rétrécit quand le monstre est proche).
                float vig = smoothstep(0.95 - _Vignette * 0.75, 1.3, r + n * 0.15) * _Vignette;
                float blackA = saturate(vig * 1.3);

                float3 rgb = _BloodColor.rgb * redA * (1.0 - blackA);
                float a = blackA + redA * (1.0 - blackA);

                rgb *= (1.0 - _Fade);
                a = _Fade + a * (1.0 - _Fade);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
