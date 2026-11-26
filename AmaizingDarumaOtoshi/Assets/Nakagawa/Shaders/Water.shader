// 海・水面(URP)。Stage21 氷割りゲームの冷たい海に使う
//  ・うねり: 水面の格子の頂点を、ゲルストナー波(IceSeaWaves.hlsl)で上下・前後に動かす。カメラから遠い所は波を弱める
//  ・さざ波: 法線のテクスチャを大きさ・向き・速さを変えて3枚重ね、流して動かす
//  ・反射: 空(反射プローブ)を、見る角度が浅いほど強く映す(URP の PBR の計算)。太陽の照り返しも出る
//  ・色と透け: 深い所ほど暗く不透明に、浅い所(物のまわり)ほど明るく透ける。波頭は明るく、波の谷は暗く
//  ・泡: 水面から出ている物(岩・流氷など)のまわりに、泡の模様を出す
//  ・深さは URP の深度テクスチャ(_CameraDepthTexture)で測る。深度テクスチャがない設定では、浅い所の色と泡は出ない
//  ・半透明なので、深度は書かない。氷(Nakagawa/IceTile)はこのあとに描く
//  ・エディタで再生していないときは、Scene ビューの「Always Refresh」をオンにすると動いて見える
// 色は彩度を落とした青にする(積み木・プレイヤーの色、減速の青緑と重ねない)
Shader "Nakagawa/Water"
{
    Properties
    {
        [MainColor] _BaseColor("深い所の色", Color) = (0.06, 0.13, 0.2, 1)
        _ShallowColor("浅い所の色", Color) = (0.2, 0.33, 0.43, 1)
        _ScatterColor("波頭の明るさの色", Color) = (0.22, 0.36, 0.46, 1)
        _WaveHeight("うねりの高さの倍率", Float) = 0.2
        _WaveTimeScale("うねりの速さの倍率", Float) = 0.8
        _WaveFadeStart("うねりを弱め始める距離(m)", Float) = 60
        _WaveFadeLength("うねりが消えるまでの距離(m)", Float) = 90
        [Normal][NoScaleOffset] _NormalMap("さざ波の法線", 2D) = "bump" {}
        _NormalStrength("さざ波の強さ", Range(0.0, 2.0)) = 0.35
        _WaveScale("さざ波の大きさ(m)", Float) = 14
        _WaveSpeed("さざ波の速さ", Float) = 0.04
        _Smoothness("つや", Range(0.0, 1.0)) = 0.94
        _Opacity("不透明さ(深い所)", Range(0.0, 1.0)) = 0.82
        _ShallowOpacity("不透明さ(浅い所)", Range(0.0, 1.0)) = 0.45
        _DepthFade("暗くなるまでの深さ(m)", Float) = 2.5
        [NoScaleOffset] _FoamMap("泡の模様", 2D) = "black" {}
        _FoamColor("泡の色", Color) = (0.85, 0.9, 0.93, 1)
        _FoamDistance("物のまわりの泡の幅(m)", Float) = 0.45
        _FoamScale("泡の模様の大きさ(m)", Float) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // 色は透けの割合を掛けてある(反射・照り返しは透けても薄くならない)
            Blend One OneMinusSrcAlpha
            ZWrite Off
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

            // 半透明として計算する(影は影の地図から読む・拡散光に透けの割合を掛ける)
            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _ALPHAPREMULTIPLY_ON 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "IceSeaWaves.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShallowColor;
                half4 _ScatterColor;
                float _WaveHeight;
                float _WaveTimeScale;
                float _WaveFadeStart;
                float _WaveFadeLength;
                half _NormalStrength;
                float _WaveScale;
                float _WaveSpeed;
                half _Smoothness;
                half _Opacity;
                half _ShallowOpacity;
                float _DepthFade;
                half4 _FoamColor;
                float _FoamDistance;
                float _FoamScale;
            CBUFFER_END

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // 波で動かす前の水平の位置(x, z)と、うねりの強さ(遠い所ほど小さい)
                float3 restXZFade : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            // カメラから遠い所は格子が粗いので、うねりを弱める
            float WaveFade(float3 positionWS)
            {
                float dist = distance(positionWS.xz, _WorldSpaceCameraPos.xz);
                return 1.0 - saturate((dist - _WaveFadeStart) / max(_WaveFadeLength, 0.01));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float fade = WaveFade(positionWS);
                float3 offset, normal;
                SeaWave(positionWS.xz, _Time.y, _WaveHeight * fade, _WaveTimeScale, offset, normal);
                output.restXZFade = float3(positionWS.xz, fade);
                positionWS += offset;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // 法線のテクスチャを流して読む
            half3 RippleNormal(float2 uv, float2 flow, half strength)
            {
                return UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + flow), strength);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float t = _Time.y * _WaveSpeed;

                // うねりの向きと高さ(画素ごとに計算し直して、なめらかにする)
                float fade = input.restXZFade.z;
                float3 swellOffset, swellNormal;
                SeaWave(input.restXZFade.xy, _Time.y, _WaveHeight * fade, _WaveTimeScale, swellOffset, swellNormal);

                // さざ波: 大・中・小の3枚を、違う向きに流して重ね、うねりの向きに足す
                float2 uv = positionWS.xz / max(_WaveScale, 0.01);
                half3 n1 = RippleNormal(uv, float2(t, t * 0.6), _NormalStrength);
                half3 n2 = RippleNormal(uv * 2.3 + 0.37, float2(-t * 1.3, t * 0.9), _NormalStrength * 0.35);
                half3 n3 = RippleNormal(uv * 0.21 + 0.71, float2(t * 0.25, -t * 0.18), _NormalStrength * 0.9);
                half2 ripple = n1.xy + n2.xy + n3.xy;
                float3 normalWS = normalize(swellNormal + float3(ripple.x, 0.0, ripple.y));
                float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);

                // 水の厚さ(水面からその奥の物までの距離。深度テクスチャがなければ深い扱い)
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float thickness = sceneEye - surfaceEye;
                if (!(thickness > 0.0)) thickness = 1000.0;
                half deep = saturate(thickness / max(_DepthFade, 0.01));

                // 水の色: 深い所ほど暗く。波頭(高い所・傾いた所)は光が透けて明るく、谷は暗く
                half3 water = lerp(_ShallowColor.rgb, _BaseColor.rgb, deep);
                half crestHeight = saturate(swellOffset.y / max(_WaveHeight * 0.6, 0.001) * 0.5 + 0.5);
                half crestSlope = saturate((1.0 - normalWS.y) * 6.0);
                water *= lerp(0.8, 1.15, crestHeight);
                water += _ScatterColor.rgb * (crestHeight * crestHeight * 0.35 + crestSlope * 0.3);
                half alpha = lerp(_ShallowOpacity, _Opacity, deep);

                // 泡: 物のまわりの細い線と、少し離れた所のまだらな泡
                float2 foamUV = positionWS.xz / max(_FoamScale, 0.01);
                half foamTex = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, foamUV + float2(t * 0.6, -t * 0.4)).r;
                foamTex = max(foamTex, SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, foamUV * 1.7 + float2(-t * 0.5, t * 0.7) + 0.5).r * 0.85);
                half edge = saturate(1.0 - thickness / max(_FoamDistance, 0.01));
                half foam = saturate(max(smoothstep(0.82, 1.0, edge), edge * smoothstep(0.5, 0.8, foamTex)));

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(water, _FoamColor.rgb, foam);
                surface.metallic = 0.0;
                surface.smoothness = lerp(_Smoothness, 0.35, foam);
                surface.occlusion = 1.0;
                surface.normalTS = half3(0, 0, 1);
                // 見る角度が浅いほど、水面が奥を隠す
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                surface.alpha = saturate(lerp(alpha, 1.0, max(foam, fresnel * 0.7)));

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = screenUV;
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
