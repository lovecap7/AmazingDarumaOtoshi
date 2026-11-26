// 透明感のある氷(URP)。Stage21 氷割りゲームの割れる氷のタイルに使う
//  ・半透明: 下の海が透けて見える。白く曇った所(霜)ほど不透明にして、タイルが床として見えるようにする
//  ・厚み: 中の泡・ひびの模様を少し奥にずらして描き、上から見ても厚みがあるように見せる。横から見る面ほど濃く不透明にする
//  ・つや: 空の映り込みと太陽の照り返し(URP の PBR の計算。照り返しは透けても薄くならない)
//  ・水の中: 海面(_SeaLevel + 波の高さ。波は IceSeaWaves.hlsl で海と同じ)より下の部分は、水の色に沈めて描く。海面の高さには泡の線を出す
//  ・浮いている氷(_Bob = 1): 物の中心の位置の波に合わせて、見た目だけ上下させる(当たり判定は動かない)
//  ・色(_BaseColor)は IceFloorGimmick が耐久に合わせて変える(MaterialPropertyBlock)。透け具合は _Opacity で決める
//  ・海(Nakagawa/Water)のあとに描く。影は受けるが、落とさない(光を通す氷なので)
Shader "Nakagawa/IceTile"
{
    Properties
    {
        [MainColor] _BaseColor("色(耐久で変わる)", Color) = (0.8, 0.88, 0.92, 1)
        [MainTexture] _BaseMap("中の模様(R: 霜 / G: 泡 / B: ひび)", 2D) = "black" {}
        _PatternScale("模様の大きさ(m)", Float) = 2.4
        _Opacity("不透明さ", Range(0.0, 1.0)) = 0.5
        _FrostOpacity("霜の不透明さ", Range(0.0, 1.0)) = 0.4
        _InnerDepth("中の模様の深さ(m)", Range(0.0, 1.0)) = 0.3
        _DeepTint("厚い所の色", Color) = (0.42, 0.6, 0.72, 1)
        _Smoothness("つや", Range(0.0, 1.0)) = 0.9
        _SeaLevel("海面の高さ", Float) = -0.55
        _UnderwaterColor("水の中の色", Color) = (0.03, 0.08, 0.12, 1)
        _UnderwaterFade("水の中で見えなくなる深さ(m)", Float) = 0.6
        _WaterlineColor("海面の泡の色", Color) = (0.85, 0.9, 0.93, 1)
        _WaterlineWidth("海面の泡の幅(m)", Float) = 0.05
        _WaveHeight("うねりの高さの倍率(海と同じにする)", Float) = 0.2
        _WaveTimeScale("うねりの速さの倍率(海と同じにする)", Float) = 0.8
        _Bob("波で上下する割合(浮いている氷は 1)", Range(0.0, 1.0)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            // タイルどうしの前後が崩れないよう、半透明でも深度は書く
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _ALPHAPREMULTIPLY_ON 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "IceSeaWaves.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _PatternScale;
                half _Opacity;
                half _FrostOpacity;
                float _InnerDepth;
                half4 _DeepTint;
                half _Smoothness;
                float _SeaLevel;
                half4 _UnderwaterColor;
                float _UnderwaterFade;
                half4 _WaterlineColor;
                float _WaterlineWidth;
                float _WaveHeight;
                float _WaveTimeScale;
                half _Bob;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // 浮いている氷: 物の中心の位置の波の高さだけ上下させる
                if (_Bob > 0.0)
                {
                    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                    positionWS.y += SeaWaveHeight(origin.xz, _Time.y, _WaveHeight, _WaveTimeScale) * _Bob;
                }
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
                bool top = normalWS.y > 0.5;

                // 模様はワールド座標で貼る(上面は XZ、側面は面の向きに合わせる。タイルどうしで模様がつながる)
                float3 an = abs(normalWS);
                float2 uv = top ? positionWS.xz : (an.x > an.z ? positionWS.zy : positionWS.xy);
                uv /= max(_PatternScale, 0.01);
                half frost = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).r;
                // 中の泡・ひびは、視線に沿って少し奥の位置で読む
                float2 inside = top ? (-viewWS.xz / max(viewWS.y, 0.3)) * (_InnerDepth / max(_PatternScale, 0.01)) : float2(0, 0);
                half4 inner = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + inside + 0.37);
                half detail = saturate(inner.g + inner.b * 0.7);

                half NoV = saturate(dot(normalWS, viewWS));
                // 横から見る面・浅い角度ほど、氷の中を長く通るので濃く不透明に
                half thick = top ? pow(1.0 - NoV, 3.0) : 0.6 + 0.4 * (1.0 - NoV);
                half3 tint = lerp(half3(1, 1, 1), _DeepTint.rgb, saturate(thick * 0.8 + (1.0 - frost) * 0.35));
                half3 albedo = _BaseColor.rgb * tint;
                albedo = lerp(albedo, _BaseColor.rgb * 1.1, detail * 0.5);
                half alpha = saturate(_Opacity + frost * _FrostOpacity + detail * 0.2 + thick * 0.35);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0.0;
                surface.smoothness = _Smoothness * (1.0 - frost * 0.35);
                surface.occlusion = 1.0;
                surface.normalTS = half3(0, 0, 1);
                surface.alpha = alpha;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);

                // 海面より下: 水の色に沈め、深いほど薄く(色は透けの割合を掛けた値なので、同じ割合で混ぜる)
                // 海面の高さは、この位置の波の高さだけ上下する
                float seaLevel = _SeaLevel + SeaWaveHeight(positionWS.xz, _Time.y, _WaveHeight, _WaveTimeScale);
                half below = positionWS.y < seaLevel ? 1.0 : 0.0;
                half under = saturate((seaLevel - positionWS.y) / max(_UnderwaterFade, 0.01));
                color.rgb = lerp(color.rgb, _UnderwaterColor.rgb * color.a, below * (0.55 + 0.45 * under));
                color *= lerp(1.0, 0.7, below * under);

                // 海面の高さの泡の線(側面だけ)
                half waterline = top ? 0.0 : saturate(1.0 - abs(positionWS.y - seaLevel) / max(_WaterlineWidth, 0.001));
                waterline *= 0.6 + 0.4 * frost;
                color.rgb = lerp(color.rgb, _WaterlineColor.rgb, waterline * 0.85);
                color.a = lerp(color.a, 1.0, waterline * 0.85);

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
