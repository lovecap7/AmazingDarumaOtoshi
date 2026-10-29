// トロンのレーザーが通った地面の焼け焦げ。
// レーザーの軌跡に沿って作った「1本の帯メッシュ」に、
// ワールド座標のノイズで焦げのムラとフチの残り火を描く。
// 円を並べるのではなく帯状にすることで、レーザーが地面を焼いた見た目にする。
Shader "Yurube/TronScorch"
{
    Properties
    {
        _CharColor ("Char Color", Color) = (0.10, 0.08, 0.06, 1)
        _EmberColor ("Ember Color", Color) = (1, 0.45, 0.15, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.30
        _EmberStrength ("Ember Strength", Range(0, 1)) = 0.25
        _NoiseScale ("Noise Scale", Float) = 4.5
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.55
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.65
        [HideInInspector] _Now ("Now", Float) = 0
        [HideInInspector] _FadeStart ("Fade Start", Float) = 2.0
        [HideInInspector] _FadeTime ("Fade Time", Float) = 2.5
        [HideInInspector] _EmberTime ("Ember Time", Float) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Scorch"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CharColor;
                float4 _EmberColor;
                float _Opacity;
                float _EmberStrength;
                float _NoiseScale;
                float _EdgeSoftness;
                float _NoiseStrength;
                float _Now;
                float _FadeStart;
                float _FadeTime;
                float _EmberTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                // uv2.x = その地点を焼いた時刻
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                output.uv2 = input.uv2;
                return output;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    value += amplitude * valueNoise(p);
                    p *= 2.03;
                    amplitude *= 0.5;
                }
                return value;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // その地点が焼かれてからの時間
                float age = max(0.0, _Now - input.uv2.x);
                float fade = 1.0 - saturate((age - _FadeStart) / max(0.01, _FadeTime));
                if (fade <= 0.001) discard;

                // uv.x = 帯の幅方向(0..1)。中央を濃く、フチをぼかす
                float edge = abs(input.uv.x * 2.0 - 1.0);
                float side = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, edge);

                // ワールド座標のノイズでムラとギザギザしたフチを作る
                float2 world = input.positionWS.xz * _NoiseScale;
                float noise = fbm(world);
                float burn = saturate(side * (1.0 - _NoiseStrength + _NoiseStrength * noise) * 1.6);

                float alpha = burn * fade * _Opacity;
                if (alpha <= 0.001) discard;

                // 焦げのフチだけ残り火の色にする(控えめに)
                float ember = saturate(1.0 - age / max(0.01, _EmberTime));
                float rim = saturate(1.0 - abs(burn - 0.45) * 2.5);
                float3 color = lerp(_CharColor.rgb, _EmberColor.rgb, rim * ember * _EmberStrength);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
