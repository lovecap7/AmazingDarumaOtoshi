// 穴の中の暗がり(URP)。Stage22 こたつの全自動卓の、洗牌の穴(中央の島のまわり)の口に張る見えない板
//  ・板より奥に見えている物(穴の壁・底・落ちていくプレイヤーや積み木)を、板からの深さに合わせて暗くする
//    (深度テクスチャから奥の物の位置を求める。深いほど _Density に従って黒に近づく)
//  ・ふたが閉じている間は、ふた(板より上)に隠れて見えない
//  ・深度テクスチャがないとき(設定で切ったとき)は、奥の物はとても深いとみなされ、穴の中は真っ暗になる
Shader "Nakagawa/HoleDarkness"
{
    Properties
    {
        _Color("暗がりの色", Color) = (0, 0, 0, 1)
        _Density("暗くなる速さ(1m あたり)", Float) = 0.55
        _MaxDarkness("一番暗いときの濃さ", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-60" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Density;
                half _MaxDarkness;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(float4 positionOS : POSITION)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                float3 behind = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                // 板から下へどれだけ深いか(奥に何もなければ、とても深い)
                float below = max(0.0, input.positionWS.y - behind.y);
                half dark = (1.0 - exp(-below * _Density)) * _MaxDarkness;
                return half4(_Color.rgb, dark);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
