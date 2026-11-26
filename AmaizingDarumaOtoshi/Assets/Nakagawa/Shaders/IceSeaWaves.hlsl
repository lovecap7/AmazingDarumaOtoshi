#ifndef NAKAGAWA_ICE_SEA_WAVES_INCLUDED
#define NAKAGAWA_ICE_SEA_WAVES_INCLUDED

// 海のうねり(ゲルストナー波を4つ重ねる)。Nakagawa/Water(水面)と Nakagawa/IceTile(氷の海面の線・浮いている氷の上下)で同じ波を使う
// 波の速さは、深い水の波の速さ(波長から決まる)に timeScale を掛けたもの
// heightScale: 高さの倍率(1 なら、とがりの値どおりの高さ)

// xy: 進む向き(長さは何でもよい) / z: 波長(m) / w: とがり(0〜1)
static const float4 kSeaWaves[4] =
{
    float4(1.0, 0.35, 9.0, 0.35),
    float4(-0.45, 1.0, 6.0, 0.3),
    float4(0.8, -0.7, 3.7, 0.25),
    float4(-0.9, -0.3, 2.3, 0.2),
};

// xz の位置の波。offset: 水面の点の動き(m) / normal: 水面の向き
void SeaWave(float2 xz, float time, float heightScale, float timeScale, out float3 offset, out float3 normal)
{
    offset = float3(0, 0, 0);
    float3 tangent = float3(1, 0, 0);
    float3 binormal = float3(0, 0, 1);
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 w = kSeaWaves[i];
        float2 d = normalize(w.xy);
        float k = 6.28318530718 / w.z;
        float c = sqrt(9.8 / k) * timeScale;
        float f = k * (dot(d, xz) - c * time);
        float steep = w.w * heightScale;
        float a = steep / k;
        float sf, cf;
        sincos(f, sf, cf);
        offset += float3(d.x * a * cf, a * sf, d.y * a * cf);
        tangent += float3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
        binormal += float3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
    }
    normal = normalize(cross(binormal, tangent));
}

// xz の位置の、波による水面の高さの変化(m)
float SeaWaveHeight(float2 xz, float time, float heightScale, float timeScale)
{
    float3 offset, normal;
    SeaWave(xz, time, heightScale, timeScale, offset, normal);
    return offset.y;
}

#endif
