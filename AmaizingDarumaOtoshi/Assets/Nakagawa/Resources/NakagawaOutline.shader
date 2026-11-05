// キャラクター・攻撃判定のある積み木の輪郭(裏面を外側へ広げて描く)
// PlayerOutlines が描画中のレンダラーのマテリアルの最後に足して使う(元のマテリアルはそのまま)
//  ・太さは画面の高さ1080に対するピクセル数。基準の距離(_RefDistance)で _OutlineWidth になり、
//    近いほど太く、遠いほど細くなる(_MinScale〜_MaxScale 倍の範囲)。_RefDistance が 0 以下なら距離で変えない
//  ・_RadialBlend: 0 = 法線の向きに広げる / 1 = 形の中心(_Center)から外向きに広げる
//    積み木のような角のある形は法線だと角に隙間が出るので、中心からの向きを混ぜる
//  ・_StencilComp: 8 = いつも描く / 6 = 体の印(NakagawaOutlineMask が書くステンシル)が無いところだけ描く
//    キャラクターは 6 にして、体の外側だけに輪郭を出す(積んだ積み木どうしの境目には出さない)
Shader "Nakagawa/PlayerOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (px @1080p)", Range(0, 20)) = 5
        _RadialBlend ("Radial Blend", Range(0, 1)) = 0.6
        _Center ("Center (Object Space)", Vector) = (0, 0, 0, 0)
        _RefDistance ("Reference Distance", Float) = 0
        _MinScale ("Min Distance Scale", Float) = 0.4
        _MaxScale ("Max Distance Scale", Float) = 1.8
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 0
                Comp [_StencilComp]
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _RadialBlend;
                float4 _Center;
                float _RefDistance;
                float _MinScale;
                float _MaxScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 radialOS = input.positionOS.xyz - _Center.xyz;
                float3 radialWS = TransformObjectToWorldDir(radialOS, false);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS, true);
                float radialLen = length(radialWS);
                radialWS = radialLen > 1e-5 ? radialWS / radialLen : normalWS;
                float3 dirWS = normalize(lerp(normalWS, radialWS, _RadialBlend) + 1e-5);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 c0 = TransformWorldToHClip(positionWS);
                float4 c1 = TransformWorldToHClip(positionWS + dirWS * 0.01);
                float2 d = c1.xy / max(c1.w, 1e-5) - c0.xy / max(c0.w, 1e-5);
                float dl = length(d);
                if (dl > 1e-7)
                {
                    d /= dl;
                    // 距離で太さを変える(透視投影では w がカメラからの奥行き)
                    float scale = _RefDistance > 0.0 ? clamp(_RefDistance / max(c0.w, 1e-3), _MinScale, _MaxScale) : 1.0;
                    // 1080p での px → NDC(縦 2.0 = 1080px)。横は画面の縦横比で詰める
                    float2 ndc = d * (_OutlineWidth * scale / 540.0);
                    ndc.x *= _ScreenParams.y / _ScreenParams.x;
                    c0.xy += ndc * c0.w;
                }
                output.positionCS = c0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
