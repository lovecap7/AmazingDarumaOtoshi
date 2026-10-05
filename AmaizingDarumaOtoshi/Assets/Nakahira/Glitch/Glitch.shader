// 画面全体にかけるグリッチ
// GlitchEffect.cs から値を受け取って描画する
Shader "Nakahira/Glitch"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Glitch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全画面三角形の頂点シェーダー(Vert)と _BlitTexture はここに入っている
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _GlitchIntensity;   // 全体の強さ 0~1
            float _GlitchTime;        // 経過時間(秒)
            float _GlitchSpeed;       // ノイズが切り替わる速さ(回/秒)
            float2 _GlitchBlockCount; // ブロックの分割数
            float _GlitchBlockAmount; // ずれるブロックの割合
            float _GlitchBlockShift;  // ブロックがずれる幅
            float _GlitchJitter;      // 横線ごとの揺れ幅
            float _GlitchColorShift;  // 色ずれの幅
            float _GlitchScanline;    // 走査線の濃さ

            // 走査線・ライン揺れ1本あたりの太さ(ピクセル)
            #define LINE_HEIGHT 3.0

            // 0~1の疑似乱数
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 時間をコマ送りにして、カクカク切り替わるようにする
                float step_t = floor(_GlitchTime * _GlitchSpeed);

                // --- ブロックずらし ---
                // 画面をブロックに分けて、乱数が閾値を超えたブロックだけ横にずらす
                float2 block = floor(uv * _GlitchBlockCount);
                float blockOn = step(1.0 - _GlitchBlockAmount * _GlitchIntensity, Hash21(block + step_t * 17.0));
                float blockShift = (Hash21(block + step_t * 31.0 + 5.0) - 0.5) * 2.0 * _GlitchBlockShift * blockOn;

                // --- ライン揺れ ---
                // 数ピクセルの横線ごとに少しだけ横へずらす
                float row = floor(uv.y * _ScreenParams.y / LINE_HEIGHT);
                float rowShift = (Hash21(float2(row, step_t)) - 0.5) * 2.0 * _GlitchJitter * _GlitchIntensity;

                // はみ出した分は反対側から回り込ませる
                uv.x = frac(uv.x + blockShift + rowShift);

                // --- 色ずれ ---
                // RとBを左右逆方向にずらして読む。ずれたブロックの中は大きくずらす
                float colorDir = Hash21(float2(step_t, 3.0)) < 0.5 ? -1.0 : 1.0;
                float colorShift = _GlitchColorShift * _GlitchIntensity * (1.0 + blockOn * 2.0) * colorDir;

                half4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(frac(uv.x + colorShift), uv.y)).r;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(frac(uv.x - colorShift), uv.y)).b;
                half3 color = half3(r, center.g, b);

                // --- 走査線 ---
                // 1本おきに暗くする
                float scan = fmod(row, 2.0);
                color *= 1.0 - scan * _GlitchScanline * _GlitchIntensity;

                return half4(color, center.a);
            }
            ENDHLSL
        }
    }
}
