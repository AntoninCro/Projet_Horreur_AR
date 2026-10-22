// Grande sphère autour du joueur : assombrit le passthrough là où il n'y a pas de maillage scanné.
// Dessinée après les objets opaques, elle ne recouvre ni le maillage de la pièce ni le monstre.
Shader "HorrorMR/DarknessDome"
{
    Properties
    {
        _ProbeDistance ("Distance de sonde de la lampe (m)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DarknessDome"
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HorrorCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ProbeDistance;
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
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 cameraPos = GetCameraPositionWS();
                float3 viewDir = normalize(i.positionWS - cameraPos);
                // Approximation : on évalue la lampe sur un point situé à quelques mètres dans la direction du regard.
                float light = HorrorFlashlight(cameraPos + viewDir * _ProbeDistance);
                float alpha = saturate(_HorrorDarkness * (1.0 - light));
                return half4(0, 0, 0, alpha);
            }
            ENDHLSL
        }
    }
}
