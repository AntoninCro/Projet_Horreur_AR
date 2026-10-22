#ifndef HORROR_COMMON_INCLUDED
#define HORROR_COMMON_INCLUDED

// Variables globales écrites par DarknessController (Shader.SetGlobal*).
float  _HorrorDarkness;      // 0 = pièce normale, 1 = noir total
float4 _HorrorFlashPos;      // xyz = position de la lampe, w = intensité (0 si éteinte)
float4 _HorrorFlashDir;      // xyz = direction de la lampe
float4 _HorrorFlashParams;   // x = cos(angle externe), y = cos(angle interne), z = portée
float4 _HorrorAmbient;       // rgb = lumière ambiante pour les effets non éclairés par URP

// Éclairement (0..1) d'un point du monde par la lampe torche.
float HorrorFlashlight(float3 positionWS)
{
    float3 toPoint = positionWS - _HorrorFlashPos.xyz;
    float dist = length(toPoint);
    float3 dir = toPoint / max(dist, 1e-4);
    float cosAngle = dot(dir, _HorrorFlashDir.xyz);
    float cone = smoothstep(_HorrorFlashParams.x, _HorrorFlashParams.y, cosAngle);
    // Petit anneau plus lumineux au bord du faisceau, comme une vraie lampe.
    float ring = 1.0 + 0.25 * smoothstep(_HorrorFlashParams.x, lerp(_HorrorFlashParams.x, _HorrorFlashParams.y, 0.35), cosAngle)
                     * (1.0 - smoothstep(lerp(_HorrorFlashParams.x, _HorrorFlashParams.y, 0.35), _HorrorFlashParams.y, cosAngle));
    float atten = 1.0 / (1.0 + dist * dist / 12.0);
    float rangeFade = saturate(1.0 - dist / max(_HorrorFlashParams.z, 0.01));
    return saturate(cone * ring * atten * rangeFade * _HorrorFlashPos.w * 1.7);
}

#endif
