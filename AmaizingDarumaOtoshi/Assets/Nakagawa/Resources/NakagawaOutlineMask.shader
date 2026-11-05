// キャラクターの体が見えているところにステンシルの印(64)を書く(色・奥行きは書かない)
// PlayerOutlines がキャラクターのレンダラーに、輪郭(NakagawaOutline)の前に足して使う
// 体の不透明な描画(Geometry)のあと・輪郭(Geometry+10)の前に描くので、輪郭は体の外側だけに出る
Shader "Nakagawa/PlayerOutlineMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+5" }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0
            // 体と同じ形を同じ奥行きで描くので、わずかに手前に寄せて確実に奥行きの判定を通す
            Offset -1, -1

            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
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
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
