// 墨の筆跡(Stage25「書き初めの半紙」)の見た目(URP)。床の上に重ねた板に描く
// _InkTex の1画素が床の 1/_PixelsPerMeter m 四方。InkTrailGimmick が CPU で書き込む
//  R: 引いた人(0 = なし、1〜8 = プレイヤー番号+1) / G: 引いた時刻(1/8 秒単位を 256 で折り返したもの) / B: 本人の足元から離れたか(効く線か)
// 乾き: 引いてから _HoldTicks は濃いまま、そのあと _FadeTicks で消える
// 見分け: 色(プレイヤー番号の色を薄めたもの)に加えて、筆跡の質感をプレイヤーごとに変える(1P 濃い一本 / 2P 二本線 / 3P 点の連なり / 4P かすれ)
// 効く線(離れた線)は少しかすれて明るく、まだ効かない線はつやのある濃い墨に見せる
Shader "Nakagawa/InkTrail"
{
    Properties
    {
        _InkTex("Ink (R owner, G tick, B left)", 2D) = "black" {}
        _NowTick("Now Tick (0-256)", Float) = 0
        _HoldTicks("Hold Ticks", Float) = 56
        _FadeTicks("Fade Ticks", Float) = 24
        _Opacity("Opacity", Range(0, 1)) = 0.62
        _Color1("Color 1P", Color) = (0.74, 0.30, 0.28, 1)
        _Color2("Color 2P", Color) = (0.28, 0.44, 0.78, 1)
        _Color3("Color 3P", Color) = (0.76, 0.60, 0.22, 1)
        _Color4("Color 4P", Color) = (0.30, 0.58, 0.34, 1)
        _ColorOther("Color Other", Color) = (0.5, 0.5, 0.5, 1)
        // 速くなっている人のまわりの墨を光らせる(x, z: 足元 / z: 引いた人 1〜8 / w: 強さ 0〜1)。4人分
        _Glow0("Glow 0", Vector) = (0, 0, 0, 0)
        _Glow1("Glow 1", Vector) = (0, 0, 0, 0)
        _Glow2("Glow 2", Vector) = (0, 0, 0, 0)
        _Glow3("Glow 3", Vector) = (0, 0, 0, 0)
        _GlowRadius("Glow Radius (m)", Float) = 3.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "InkTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_point_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _InkTex_TexelSize;
                float _NowTick;
                float _HoldTicks;
                float _FadeTicks;
                float _Opacity;
                float4 _Color1;
                float4 _Color2;
                float4 _Color3;
                float4 _Color4;
                float4 _ColorOther;
                float4 _Glow0;
                float4 _Glow1;
                float4 _Glow2;
                float4 _Glow3;
                float _GlowRadius;
            CBUFFER_END

            // 速くなっている人(glow)のまわりの、その人の墨を光らせる強さ(0〜1)。足元から外へ広がる波で脈打つ
            float GlowAmount(float4 glow, float owner, float2 w)
            {
                if (glow.w <= 0.001 || abs(glow.z - owner) > 0.5) return 0.0;
                float d = distance(w, glow.xy);
                float fall = saturate(1.0 - d / max(0.01, _GlowRadius));
                float wave = 0.5 + 0.5 * sin(d * 5.0 - _Time.y * 12.0);
                return glow.w * fall * fall * lerp(0.55, 1.0, wave);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), u.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * Noise(p);
                    p = p * 2.07 + 13.7;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            // 画素の中身: owner(0〜8), 残りの濃さ(0〜1), 離れたか(0/1)
            float3 Texel(float2 uv)
            {
                float4 c = SAMPLE_TEXTURE2D_LOD(_InkTex, sampler_point_clamp, uv, 0);
                float owner = round(c.r * 255.0);
                float age = fmod(_NowTick - round(c.g * 255.0) + 512.0, 256.0);
                float life = owner > 0.5 ? saturate(1.0 - (age - _HoldTicks) / max(1.0, _FadeTicks)) : 0.0;
                return float3(owner, life, c.b);
            }

            float4 OwnerColor(float owner)
            {
                if (owner < 1.5) return _Color1;
                if (owner < 2.5) return _Color2;
                if (owner < 3.5) return _Color3;
                if (owner < 4.5) return _Color4;
                return _ColorOther;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 texel = _InkTex_TexelSize.xy;
                float3 center = Texel(input.uv);
                float owner = center.x;

                // 縁をなめらかに: 周りの4画素のうち、同じ人の墨がある割合を双線形でまぜる
                float2 st = input.uv / texel - 0.5;
                float2 base = (floor(st) + 0.5) * texel;
                float2 f = frac(st);
                float3 t00 = Texel(base);
                float3 t10 = Texel(base + float2(texel.x, 0));
                float3 t01 = Texel(base + float2(0, texel.y));
                float3 t11 = Texel(base + texel);
                // 周りに墨があれば、その人を中心の色にする(線の外側の縁でも色が決まるように)
                if (owner < 0.5)
                {
                    owner = max(max(t00.x, t10.x), max(t01.x, t11.x));
                    if (owner < 0.5) discard;
                }
                float c00 = (abs(t00.x - owner) < 0.5) ? t00.y : 0.0;
                float c10 = (abs(t10.x - owner) < 0.5) ? t10.y : 0.0;
                float c01 = (abs(t01.x - owner) < 0.5) ? t01.y : 0.0;
                float c11 = (abs(t11.x - owner) < 0.5) ? t11.y : 0.0;
                float cover = lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
                float left = lerp(lerp(t00.z, t10.z, f.x), lerp(t01.z, t11.z, f.x), f.y);

                // にじんだ縁(しきい値を雑音でゆらして、紙にしみたような輪郭にする)
                float2 w = input.positionWS.xz;
                float edgeNoise = Fbm(w * 3.1);
                float alpha = smoothstep(0.25 + edgeNoise * 0.35, 0.75 + edgeNoise * 0.2, cover);
                if (alpha <= 0.002) discard;

                // プレイヤーごとの筆跡の質感(色だけで区別しない)
                float pattern = 1.0;
                if (owner > 1.5 && owner < 2.5)
                {
                    // 2P: 二本線(斜めの縞)
                    float s = frac((w.x + w.y) * 0.9);
                    pattern = lerp(0.45, 1.0, step(0.32, abs(s - 0.5) * 2.0));
                }
                else if (owner > 2.5 && owner < 3.5)
                {
                    // 3P: 点の連なり
                    float2 g = frac(w * 1.25) - 0.5;
                    pattern = lerp(0.5, 1.0, 1.0 - smoothstep(0.22, 0.3, length(g)));
                }
                else if (owner > 3.5 && owner < 4.5)
                {
                    // 4P: かすれ(筆の毛の流れのような細い筋)
                    float streak = Noise(float2(w.x * 0.6, w.y * 7.0)) * 0.7 + Noise(w * 9.0) * 0.3;
                    pattern = lerp(0.4, 1.0, smoothstep(0.35, 0.6, streak));
                }

                // 効く線(離れた線)は少しかすれて明るく、まだ効かない線はつやのある濃い墨
                float dryStreak = smoothstep(0.3, 0.75, Fbm(float2(w.x * 1.7, w.y * 5.3) + 7.0));
                float4 col = OwnerColor(owner);
                float3 fresh = col.rgb * 0.72;
                float3 dry = lerp(col.rgb, float3(1, 1, 1), 0.12);
                float3 rgb = lerp(fresh, dry, left);
                float a = _Opacity * alpha * pattern * lerp(1.15, lerp(0.75, 1.0, dryStreak), left);
                // ふちを少し濃く(墨だまり)
                float rim = saturate(1.0 - abs(cover - 0.55) * 4.0);
                rgb *= lerp(1.0, 0.82, rim * 0.6);

                // 速くなっている人のまわりの墨が光る(色を明るくして、濃さも上げる)
                float glow = max(max(GlowAmount(_Glow0, owner, w), GlowAmount(_Glow1, owner, w)),
                                 max(GlowAmount(_Glow2, owner, w), GlowAmount(_Glow3, owner, w)));
                rgb = lerp(rgb, col.rgb * 1.4 + 0.55, glow);
                a = lerp(a, 0.95, glow);
                return half4(rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
