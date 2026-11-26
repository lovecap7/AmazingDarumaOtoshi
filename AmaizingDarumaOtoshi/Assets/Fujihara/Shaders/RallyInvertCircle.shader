// ラリーの演出（RallyVfx）：画面の _Center（画面の割合）を中心に、半径 _Radius の円の中だけネガポジ反転する。円のふちは白く光る。
// カメラの前に置いた、画面いっぱいの板に使う。カメラの「写った画面（Opaque Texture）」を読むので、
// 演出の間は RallyVfx がカメラの requiresColorTexture を入れる（半透明の物・UI は反転しない）
Shader "Fujihara/RallyInvertCircle"
{
    Properties
    {
        _Center ("Center (画面の割合)", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius (画面の高さに対する割合)", Float) = 0
        _Edge ("Edge (円のふちのぼかし)", Float) = 0.01
        _Rim ("Rim (光るふちの太さ)", Float) = 0.025
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+500" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InvertCircle"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            float4 _Center;
            float _Radius;
            float _Edge;
            float _Rim;
            half4 _RimColor;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 d = uv - _Center.xy;
                d.x *= aspect;
                float dist = length(d);

                float inside = 1.0 - smoothstep(_Radius - _Edge, _Radius, dist);
                float rim = (1.0 - smoothstep(0.0, _Rim, abs(dist - _Radius))) * step(0.001, _Radius);

                half3 col = SampleSceneColor(uv);
                half3 outColor = lerp(col, 1.0 - col, inside);
                outColor = lerp(outColor, _RimColor.rgb, rim * _RimColor.a);
                return half4(outColor, saturate(max(inside, rim)));
            }
            ENDHLSL
        }
    }
}
