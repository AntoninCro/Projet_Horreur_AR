// Appliqué sur le maillage scanné de la pièce (et les plans AR).
// - Assombrit le passthrough (on dessine du noir avec un alpha) sauf là où la lampe éclaire.
// - Écrit la profondeur : les meubles réels cachent le monstre (occlusion).
Shader "HorrorMR/RoomDarkness"
{
    Properties
    {
        _Push ("Recul vers l'arrière (m)", Float) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "RoomDarkness"
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HorrorCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Push;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                ZERO_INITIALIZE(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                // On recule légèrement le maillage pour que les pieds du monstre ne soient pas mangés par le sol scanné.
                float3 fromCamera = positionWS - GetCameraPositionWS();
                positionWS += normalize(fromCamera) * _Push;
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float light = HorrorFlashlight(i.positionWS);
                float alpha = saturate(_HorrorDarkness * (1.0 - light));
                return half4(0, 0, 0, alpha);
            }
            ENDHLSL
        }

        // Nécessaire quand URP fait un pré-passage de profondeur.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Push;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                ZERO_INITIALIZE(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += normalize(positionWS - GetCameraPositionWS()) * _Push;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return 0;
            }
            ENDHLSL
        }
    }
}
