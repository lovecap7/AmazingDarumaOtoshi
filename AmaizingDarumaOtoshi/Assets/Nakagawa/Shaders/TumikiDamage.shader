// 積み木の損傷の見た目(URP)。積み木と同じメッシュに2つ目のマテリアルとして重ねて描く(TumikiDurability が付け外しする)
// 下の色に掛け合わせる(2倍の乗算: 0.5 で変化なし、0 に近いほど暗く、0.5 より大きいと明るく)ので、積み木の色・光の当たり方はそのまま残る
//  ・ヒビ: 立体のボロノイの境界線。衝撃を受けた点(_Seed で決まる)から、損傷が進むほど遠くまで広がる。境目の両側は少し明るく欠けたように見せる
//  ・細かいヒビ: 損傷が半分を超えると、細かい網目のヒビが増える
//  ・汚れ: 損傷に応じて、まだらにくすむ
//  ・欠け: 上下の縁の角が、損傷に応じてところどころ欠けて、明るい木地がのぞく
// 模様はオブジェクトの空間で作るので、積み木と一緒に動き・回る(拡大縮小は打ち消して、実寸で同じ大きさになる)
// _Damage(0〜1)と _Seed は MaterialPropertyBlock で積み木ごとに渡す
Shader "Nakagawa/TumikiDamage"
{
    Properties
    {
        _Damage("Damage", Range(0.0, 1.0)) = 0.0
        _Seed("Seed", Float) = 0.0

        [Header(Cracks)]
        _CrackScale("Crack Cell Density (1/m)", Float) = 4.0
        _CrackWidth("Crack Width (m)", Float) = 0.016
        _CrackDarkness("Crack Darkness", Range(0.0, 1.0)) = 0.9
        _CrackSpread("Crack Spread (m at full damage)", Float) = 1.3
        _CrackRim("Crack Rim Brightness", Range(0.0, 1.0)) = 0.1

        [Header(Fine Cracks)]
        _FineScale("Fine Crack Density (1/m)", Float) = 14.0
        _FineWidth("Fine Crack Width (m)", Float) = 0.006
        _FineStart("Fine Crack Start Damage", Range(0.0, 1.0)) = 0.45

        [Header(Grime)]
        _Grime("Grime Strength", Range(0.0, 1.0)) = 0.35
        _GrimeScale("Grime Scale (1/m)", Float) = 7.0

        [Header(Chips)]
        _Chip("Chip Strength", Range(0.0, 1.0)) = 0.8
        _ChipBand("Chip Band Width (m)", Float) = 0.07
        _ChipBrightness("Chip Brightness", Range(0.5, 1.0)) = 0.68
    }

    SubShader
    {
        // 積み木(不透明)を描いた後に重ねる
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TumikiDamage"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor SrcColor
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Damage;
                float _Seed;
                float _CrackScale;
                float _CrackWidth;
                float _CrackDarkness;
                float _CrackSpread;
                float _CrackRim;
                float _FineScale;
                float _FineWidth;
                float _FineStart;
                float _Grime;
                float _GrimeScale;
                float _Chip;
                float _ChipBand;
                float _ChipBrightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;  // オブジェクトの空間(メッシュの座標)
                float3 positionMS : TEXCOORD1;  // 実寸に直したオブジェクトの空間(m)
                float3 objectScale : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }

            float Fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * ValueNoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            // ボロノイの境界線までの距離(セルの大きさを1とした単位)と、その境界線ごとの乱数(0〜1。境界線の両側のセルで決まる)
            // 乱数で境界線を間引くと、網目ではなく枝分かれしたヒビになる
            float VoronoiEdge(float3 x, out float edgeId)
            {
                float3 n = floor(x);
                float3 f = frac(x);

                float3 mr = 0;
                float3 mg = 0;
                float md = 8.0;
                for (int k = -1; k <= 1; k++)
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float3 g = float3(i, j, k);
                    float3 r = g + Hash33(n + g) - f;
                    float d = dot(r, r);
                    if (d < md) { md = d; mr = r; mg = g; }
                }

                md = 8.0;
                float3 other = mg;
                for (int k2 = -1; k2 <= 1; k2++)
                for (int j2 = -1; j2 <= 1; j2++)
                for (int i2 = -1; i2 <= 1; i2++)
                {
                    float3 g = mg + float3(i2, j2, k2);
                    float3 r = g + Hash33(n + g) - f;
                    float3 diff = r - mr;
                    if (dot(diff, diff) > 0.00001)
                    {
                        float d = dot(0.5 * (mr + r), normalize(diff));
                        if (d < md) { md = d; other = g; }
                    }
                }
                // 両側のセルの番号の和(どちら側から見ても同じ)から境界線の乱数を作る
                edgeId = Hash13((n + mg) + (n + other) + 0.37);
                return md;
            }

            // ヒビの線をぎざぎざにするための位置のゆがみ
            float3 Warp(float3 p, float3 seed)
            {
                return float3(ValueNoise(p * 11.0 + seed), ValueNoise(p * 11.0 + seed.yzx + 5.1), ValueNoise(p * 11.0 + seed.zxy + 9.7)) - 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                      length(unity_ObjectToWorld._m01_m11_m21),
                                      length(unity_ObjectToWorld._m02_m12_m22));
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionMS = input.positionOS.xyz * scale;
                output.objectScale = scale;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float damage = saturate(_Damage);
                if (damage <= 0.001) return half4(0.5, 0.5, 0.5, 1.0);

                float3 p = input.positionMS;
                float3 seedOffset = Hash33(float3(_Seed, _Seed * 1.37, _Seed * 2.11)) * 100.0;

                // 衝撃を受けた点。上から見下ろすカメラから見えやすいよう、上面の縁寄りに置く
                // ここから遠いほどヒビが届くのが遅い(損傷が小さくても、衝撃点のまわりには必ずヒビが入る)
                float3 hitDir = Hash33(float3(_Seed * 3.1, 7.7, _Seed)) - 0.5;
                hitDir.y = abs(hitDir.y) + 0.35;
                hitDir = normalize(hitDir + 0.0001);
                float3 hitPoint = hitDir * input.objectScale * float3(0.5, 1.0, 0.5);
                float wobble = Fbm(p * 6.0 + seedOffset);
                float reach = 0.15 + damage * _CrackSpread;
                float spread = saturate((reach - distance(p, hitPoint) + (wobble - 0.5) * 0.35) * 10.0);

                // 太いヒビ: ゆがめた位置のボロノイの境界線のうち、損傷に応じた割合だけを残す(枝分かれしたヒビになる)
                // 衝撃点に近い境界線ほど残りやすい
                float edgeId;
                float3 cp = p + Warp(p, seedOffset) * 0.09;
                float edge = VoronoiEdge(cp * _CrackScale + seedOffset, edgeId) / _CrackScale;
                float keep = saturate(0.3 + damage * 0.55 + spread * 0.1);
                float edgeMask = step(edgeId, keep);
                float width = _CrackWidth * lerp(0.5, 1.15, damage) * lerp(0.35, 1.25, ValueNoise(p * 13.0 + seedOffset.zxy));
                float crack = (1.0 - smoothstep(width * 0.3, width, edge)) * spread * edgeMask;
                float rim = saturate((1.0 - smoothstep(width, width * 2.0, edge)) * spread * edgeMask - crack);

                // 細かいヒビ: 太いヒビから枝分かれするように、太いヒビの近くにだけ出る
                float fineDamage = saturate((damage - _FineStart) / max(0.001, 1.0 - _FineStart));
                float fineId;
                float fineEdge = VoronoiEdge((p + Warp(p * 1.7, seedOffset.yzx) * 0.04) * _FineScale + seedOffset.yzx, fineId) / _FineScale;
                float nearCrack = 1.0 - smoothstep(width, 0.09, edge * (edgeMask > 0.5 ? 1.0 : 3.0));
                float fineMask = fineDamage * step(fineId, 0.25 + fineDamage * 0.35) * spread * nearCrack;
                float fine = (1.0 - smoothstep(_FineWidth * 0.4, _FineWidth, fineEdge)) * fineMask;

                // 汚れ(まだらなくすみ)
                float grime = Fbm(p * _GrimeScale + seedOffset.yxz);
                float grimeShade = 1.0 - _Grime * damage * (0.55 + 0.45 * grime);

                // 縁の欠け: 上下の面と側面の境目(円柱の角)の近く
                float3 os = input.positionOS;
                float capDistance = (1.0 - abs(os.y)) * input.objectScale.y;
                float sideDistance = (0.5 - length(os.xz)) * min(input.objectScale.x, input.objectScale.z);
                float cornerDistance = max(capDistance, sideDistance);
                float band = 1.0 - smoothstep(_ChipBand * 0.3, _ChipBand, cornerDistance);
                float chipNoise = Fbm(p * 22.0 + seedOffset);
                float chip = smoothstep(1.0 - damage * _Chip * 0.55, 1.03 - damage * _Chip * 0.55, chipNoise) * band;
                float chipOutline = saturate(band * smoothstep(0.92 - damage * _Chip * 0.55, 1.0 - damage * _Chip * 0.55, chipNoise) - chip);

                float shade = 0.5 * grimeShade;
                shade = lerp(shade, shade * (1.0 + _CrackRim), rim);
                shade = lerp(shade, 0.5 * (1.0 - _CrackDarkness), saturate(crack));
                shade = lerp(shade, 0.5 * (1.0 - _CrackDarkness * 0.8), saturate(fine) * 0.85);
                shade = lerp(shade, 0.5 * 0.55, chipOutline * 0.8);
                shade = lerp(shade, _ChipBrightness, chip);
                return half4(shade, shade, shade, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
