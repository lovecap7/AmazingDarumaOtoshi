// 全自動卓の中央の装飾(URP)。Stage22 こたつの全自動卓の、洗牌の穴の真ん中に残る島の上面に貼る
//  ・メッシュは平らな板(UV 0〜1 が島の上面全体。U = +X、V = +Z)。段差はなく、高さの模様(Layout)から法線と影を作って立体に見せる
//    銀の外枠 → 黒い盤 → 四辺が内側に反った銀の台座 → サイコロの入った丸いガラスのドーム
//  ・各辺(席の側)に点数の窓(7セグメント。プレイヤーの色)。文字はその辺の席から読む向き(本物の卓と同じ)
//    南 = 1P・東 = 2P・北 = 3P・西 = 4P。点数・色・いるかは MahjongTable が MaterialPropertyBlock で渡す
//  ・点数の窓の両わきに、洗牌のカウントダウンのランプ(全部で 8 個。1P の左から反時計回り)。_LampLit 個だけ点く
//    _LampChase = 1 のときは、点いたランプの明るさが反時計回りに回る(吸い込んでいる間)
//  ・四隅の丸いランプは飾り(いつも少し光る)
Shader "Nakagawa/MahjongConsole"
{
    Properties
    {
        _Silver("銀の枠の色", Color) = (0.74, 0.76, 0.79, 1)
        _Panel("黒い盤の色", Color) = (0.028, 0.03, 0.034, 1)
        _Relief("立体感の強さ", Range(0.0, 3.0)) = 1.0
        _LampLit("点いているランプの数(0〜8)", Float) = 0
        [HDR] _LampColor("点いたランプの色", Color) = (3.0, 2.4, 0.4, 1)
        _LampChase("ランプが回る(吸い込み中 = 1)", Float) = 0
        [HDR] _CornerColor("四隅のランプの色", Color) = (1.2, 1.6, 2.4, 1)
        _Scores("点数(1P〜4P)", Vector) = (0, 0, 0, 0)
        _SeatOn("席にプレイヤーがいる(1P〜4P。0 / 1)", Vector) = (1, 1, 1, 1)
        [HDR] _SeatColor0("1P の色", Color) = (2.4, 0.35, 0.3, 1)
        [HDR] _SeatColor1("2P の色", Color) = (0.35, 0.8, 2.6, 1)
        [HDR] _SeatColor2("3P の色", Color) = (0.4, 2.2, 0.5, 1)
        [HDR] _SeatColor3("4P の色", Color) = (2.4, 1.9, 0.3, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Silver;
                half4 _Panel;
                float _Relief;
                float _LampLit;
                half4 _LampColor;
                float _LampChase;
                half4 _CornerColor;
                float4 _Scores;
                float4 _SeatOn;
                half4 _SeatColor0;
                half4 _SeatColor1;
                half4 _SeatColor2;
                half4 _SeatColor3;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            //----------------------------------------
            // 形(座標 q は島の中心が 0、辺が ±1)
            //----------------------------------------

            float SdRoundBox(float2 p, float2 b, float r)
            {
                float2 d = abs(p) - b + r;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
            }

            // 四辺が内側に反った四角(h: 角までの半分の幅、k: 辺の中央の反り)
            float SdConcave(float2 p, float h, float k)
            {
                float2 a = abs(p);
                float mx = max(a.x, a.y), mn = min(a.x, a.y);
                float t = saturate(mn / h);
                return mx - (h - k * (1.0 - t * t));
            }

            float2 Rot(float2 p, float deg)
            {
                float s, c;
                sincos(radians(deg), s, c);
                return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
            }

            // 席 seat から見た向きの座標(右 = +x、奥 = +y)。1P は南の席(北を向く)、反時計回りに 2P(東)・3P(北)・4P(西)
            float2 SeatLocal(float2 q, int seat)
            {
                if (seat == 1) return float2(q.y, -q.x);
                if (seat == 2) return float2(-q.x, -q.y);
                if (seat == 3) return float2(-q.y, q.x);
                return q;
            }

            static const float kOuter = 0.985;   // 外枠の外側
            static const float kInner = 0.80;    // 外枠の内側(黒い盤の縁)
            static const float kPlate = 0.42;    // 台座(角までの半分の幅)
            static const float kDomeRing = 0.33; // ドームのまわりの溝
            static const float kDome = 0.28;     // ガラスのドーム
            static const float kWindowY = -0.61; // 点数の窓の中心(席の座標)
            static const float2 kWindowHalf = float2(0.215, 0.105);
            static const float kLampX = 0.43;    // カウントダウンのランプ(席の座標の ±x)
            static const float kLampR = 0.05;
            static const float kCorner = 0.61;   // 四隅のランプの中心
            static const float kCornerR = 0.075;

            struct Shape
            {
                float h;          // 高さ(q と同じ単位)
                float silver;     // 銀(外枠・台座・ランプの縁)
                float dome;       // ガラスのドーム
                float window;     // 点数の窓
                float lamp;       // カウントダウンのランプのレンズ
                float corner;     // 四隅のランプのレンズ
                float groove;     // 暗い溝
                int seat;         // この点がどの席の辺の側か
                float2 local;     // 席の座標
                int lampIndex;    // ランプの番号(0〜7)
            };

            // 小さな丸いレンズ(縁は銀、中はふくらんだレンズ)。返り値: x = 縁, y = レンズ
            float2 Lens(float d, float r, inout float h, float baseH)
            {
                float bezel = smoothstep(r + 0.022, r + 0.012, d);
                float lens = smoothstep(r + 0.002, r - 0.006, d);
                float hb = baseH + 0.012 * bezel;
                float hl = baseH + 0.004 + 0.02 * sqrt(saturate(1.0 - (d * d) / (r * r)));
                h = lerp(h, hb, bezel);
                h = lerp(h, hl, lens);
                return float2(bezel * (1.0 - lens), lens);
            }

            Shape Layout(float2 q)
            {
                Shape s = (Shape)0;
                // 外枠: 外から丸く盛り上がり、内側は斜めに盤へ下りる
                float dO = SdRoundBox(q, float2(kOuter, kOuter), 0.2);
                float dI = SdRoundBox(q, float2(kInner, kInner), 0.12);
                float rise = smoothstep(0.0, 0.07, -dO);
                float frameH = 0.035 * rise * (0.75 + 0.25 * smoothstep(0.0, 0.06, dI));
                float panel = smoothstep(0.0, 0.03, -dI);
                s.h = lerp(frameH, -0.014, panel);
                s.silver = 1.0 - panel;

                // 台座(四辺が反った銀の板)
                float dP = SdConcave(q, kPlate, 0.075);
                float plate = smoothstep(0.0, 0.025, -dP);
                s.h = lerp(s.h, 0.018 + 0.006 * smoothstep(0.0, 0.08, -dP), plate);
                s.silver = max(s.silver, plate);

                // ドームのまわりの溝と、ガラスのドーム
                float r = length(q);
                float groove = smoothstep(kDomeRing + 0.012, kDomeRing, r);
                s.h = lerp(s.h, -0.006, groove);
                s.silver *= 1.0 - groove;
                s.groove = groove;
                float dome = smoothstep(kDome + 0.006, kDome - 0.004, r);
                s.h = lerp(s.h, -0.004 + 0.075 * sqrt(saturate(1.0 - (r * r) / (kDome * kDome))), dome);
                s.dome = dome;
                s.groove *= 1.0 - dome;

                // どの辺の側か(一番近い辺)
                float2 a = abs(q);
                int seat = a.y >= a.x ? (q.y < 0.0 ? 0 : 2) : (q.x > 0.0 ? 1 : 3);
                float2 l = SeatLocal(q, seat);
                s.seat = seat;
                s.local = l;

                // 点数の窓(へこんだ黒いガラス)
                float dW = SdRoundBox(l - float2(0.0, kWindowY), kWindowHalf, 0.03);
                float wall = smoothstep(0.012, 0.0, dW);
                float win = smoothstep(0.0, 0.012, -dW);
                s.h = lerp(s.h, -0.004, wall);
                s.silver = max(s.silver * (1.0 - wall), wall * (1.0 - win) * 0.85);
                s.h = lerp(s.h, -0.026, win);
                s.window = win;

                // カウントダウンのランプ(窓の両わき)
                float2 lp = float2(l.x < 0.0 ? -kLampX : kLampX, kWindowY);
                float2 lens = Lens(length(l - lp), kLampR, s.h, -0.014);
                s.silver = max(s.silver, lens.x);
                s.lamp = lens.y;
                s.lampIndex = seat * 2 + (l.x < 0.0 ? 0 : 1);

                // 四隅の丸いランプ
                float2 cp = float2(q.x < 0.0 ? -kCorner : kCorner, q.y < 0.0 ? -kCorner : kCorner);
                float2 corner = Lens(length(q - cp), kCornerR, s.h, -0.014);
                s.silver = max(s.silver, corner.x);
                s.corner = corner.y;
                return s;
            }

            float Height(float2 q) { return Layout(q).h; }

            //----------------------------------------
            // 表示
            //----------------------------------------

            // 7セグメントの1文字(p: 文字の座標。幅 ±0.5・高さ ±1)。返り値: x = 点いている所, y = 全部の画
            float2 Segments(float2 p, int digit)
            {
                // a b c d e f g
                static const uint kBits[10] = { 0x3Fu, 0x06u, 0x5Bu, 0x4Fu, 0x66u, 0x6Du, 0x7Du, 0x07u, 0x7Fu, 0x6Fu };
                uint bits = digit >= 0 && digit <= 9 ? kBits[digit] : 0u;
                p.x -= p.y * 0.12;   // 少し斜めに
                float on = 0.0, all = 0.0;
                float2 hs = float2(0.36, 0.085), vs = float2(0.085, 0.38);
                float d[7];
                d[0] = SdRoundBox(p - float2(0.0, 0.92), hs, 0.08);
                d[1] = SdRoundBox(p - float2(0.46, 0.46), vs, 0.08);
                d[2] = SdRoundBox(p - float2(0.46, -0.46), vs, 0.08);
                d[3] = SdRoundBox(p - float2(0.0, -0.92), hs, 0.08);
                d[4] = SdRoundBox(p - float2(-0.46, -0.46), vs, 0.08);
                d[5] = SdRoundBox(p - float2(-0.46, 0.46), vs, 0.08);
                d[6] = SdRoundBox(p - float2(0.0, 0.0), hs, 0.08);
                [unroll] for (int i = 0; i < 7; i++)
                {
                    float m = smoothstep(0.05, -0.02, d[i]);
                    all = max(all, m);
                    if (((bits >> (uint)i) & 1u) != 0u) on = max(on, m);
                }
                return float2(on, all);
            }

            half3 SeatColor(int seat)
            {
                if (seat == 1) return _SeatColor1.rgb;
                if (seat == 2) return _SeatColor2.rgb;
                if (seat == 3) return _SeatColor3.rgb;
                return _SeatColor0.rgb;
            }

            float SeatValue(float4 v, int seat)
            {
                if (seat == 1) return v.y;
                if (seat == 2) return v.z;
                if (seat == 3) return v.w;
                return v.x;
            }

            // サイコロ(上から見た面。center・半分の大きさ・向き・目の数)。返り値: x = 面, y = 目
            float2 Die(float2 p, float2 center, float half, float deg, int pips)
            {
                float2 l = Rot(p - center, deg) / half;
                float face = smoothstep(0.06, -0.02, SdRoundBox(l, float2(1, 1), 0.3));
                float pip = 0.0;
                float pr = pips == 1 ? 0.34 : 0.21;
                if (pips == 1) pip = smoothstep(pr + 0.04, pr - 0.04, length(l));
                else if (pips == 3)
                {
                    pip = max(pip, smoothstep(pr + 0.04, pr - 0.04, length(l)));
                    pip = max(pip, smoothstep(pr + 0.04, pr - 0.04, length(l - float2(-0.52, 0.52))));
                    pip = max(pip, smoothstep(pr + 0.04, pr - 0.04, length(l - float2(0.52, -0.52))));
                }
                else
                {
                    float2 al = abs(l) - float2(0.5, 0.5);
                    pip = smoothstep(pr + 0.04, pr - 0.04, length(al));
                }
                return float2(face, pip * face);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 q = input.uv * 2.0 - 1.0;
                float3 positionWS = input.positionWS;
                float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
                Shape s = Layout(q);

                // 高さの傾きから法線を作る(島は回さないので、q の x = ワールドの X、y = ワールドの Z)
                const float e = 0.004;
                float hx = Height(q + float2(e, 0)) - Height(q - float2(e, 0));
                float hz = Height(q + float2(0, e)) - Height(q - float2(0, e));
                float3 normalWS = normalize(float3(-hx / (2.0 * e) * _Relief, 1.0, -hz / (2.0 * e) * _Relief));

                // 光の向きに沿って高さを調べ、くぼみに落ちる影を作る(段差の影)
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float2 toLight = normalize(mainLight.direction.xz + 1e-4);
                float slope = mainLight.direction.y / max(length(mainLight.direction.xz), 0.05);
                float self = 0.0;
                [unroll] for (int i = 1; i <= 6; i++)
                {
                    float dist = 0.012 * i;
                    float above = Height(q + toLight * dist) - (s.h + dist * slope * 0.35);
                    self = max(self, saturate(above * 60.0));
                }
                // くぼみの奥ほど少し暗く(まわりより低い所)
                float around = (Height(q + float2(0.03, 0)) + Height(q - float2(0.03, 0)) + Height(q + float2(0, 0.03)) + Height(q - float2(0, 0.03))) * 0.25;
                float cavity = saturate(1.0 - saturate(around - s.h) * 25.0);

                // 材質
                half3 albedo = _Panel.rgb;
                half metallic = 0.0;
                half smoothness = 0.82;
                half3 emission = 0;

                // 銀(細かい筋を入れる)
                float brush = frac(sin(dot(floor(q * float2(420.0, 9.0)), float2(12.9898, 78.233))) * 43758.5453);
                half3 silver = _Silver.rgb * (0.92 + 0.08 * brush);
                albedo = lerp(albedo, silver, s.silver);
                metallic = lerp(metallic, 0.9, s.silver);
                smoothness = lerp(smoothness, 0.62, s.silver);
                albedo = lerp(albedo, half3(0.01, 0.01, 0.012), s.groove);

                // ガラスのドームの中のサイコロ(少し奥に見えるよう、視線でずらす)
                if (s.dome > 0.0)
                {
                    float2 inner = q - viewWS.xz / max(viewWS.y, 0.35) * 0.05;
                    float2 red = Die(inner, float2(-0.075, 0.055), 0.075, 24.0, 3);
                    float2 white = Die(inner, float2(0.085, -0.07), 0.07, -18.0, 4);
                    half3 inside = half3(0.02, 0.02, 0.025);
                    inside = lerp(inside, half3(0.75, 0.06, 0.05), red.x);
                    inside = lerp(inside, half3(0.95, 0.95, 0.92), red.y);
                    inside = lerp(inside, half3(0.92, 0.92, 0.9), white.x);
                    inside = lerp(inside, half3(0.08, 0.08, 0.1), white.y);
                    // ガラスの縁ほど暗く(厚く見える)
                    float rim = saturate(length(q) / kDome);
                    inside *= 1.0 - 0.6 * rim * rim;
                    albedo = lerp(albedo, inside * 0.8, s.dome);
                    metallic = lerp(metallic, 0.0, s.dome);
                    smoothness = lerp(smoothness, 0.96, s.dome);
                    emission += inside * 0.25 * s.dome;
                }

                // 点数の窓
                if (s.window > 0.0)
                {
                    int seat = s.seat;
                    bool present = SeatValue(_SeatOn, seat) > 0.5;
                    half3 col = SeatColor(seat);
                    float2 w = s.local - float2(0.0, kWindowY);
                    float score = clamp(round(SeatValue(_Scores, seat)), 0.0, 999.0);
                    uint value = (uint)score;
                    float lit = 0.0, ghost = 0.0;
                    [unroll] for (int k = 0; k < 3; k++)
                    {
                        // 右から 1・10・100 の位
                        float2 p = (w - float2(0.115 - k * 0.115, 0.005)) / float2(0.075, 0.072);
                        uint place = k == 0 ? 1u : (k == 1 ? 10u : 100u);
                        int digit = (int)((value / place) % 10u);
                        bool show = present && (k == 0 || value >= place);
                        float2 seg = Segments(p, show ? digit : -1);
                        lit = max(lit, seg.x);
                        ghost = max(ghost, seg.y);
                    }
                    // 席の色の細い帯(窓の手前の縁)
                    float band = present ? smoothstep(0.012, 0.0, abs(w.y + 0.08) - 0.008) * step(abs(w.x), 0.17) : 0.0;
                    albedo = lerp(albedo, half3(0.012, 0.012, 0.014), s.window);
                    smoothness = lerp(smoothness, 0.95, s.window);
                    metallic = lerp(metallic, 0.0, s.window);
                    emission += s.window * (col * (lit * 1.6 + band * 0.7) + col * 0.035 * ghost);
                }

                // カウントダウンのランプ
                if (s.lamp > 0.0)
                {
                    float on = step((float)s.lampIndex + 0.5, _LampLit);
                    float chase = 1.0;
                    if (_LampChase > 0.5)
                    {
                        float phase = frac(s.lampIndex / 8.0 - _Time.y * 1.4);
                        chase = 0.35 + 0.65 * pow(1.0 - phase, 3.0);
                    }
                    float2 lc = s.local - float2(s.local.x < 0.0 ? -kLampX : kLampX, kWindowY);
                    float spot = 1.0 - saturate(length(lc - float2(-0.015, 0.015)) / kLampR);
                    albedo = lerp(albedo, half3(0.09, 0.075, 0.03), s.lamp);
                    smoothness = lerp(smoothness, 0.95, s.lamp);
                    metallic = lerp(metallic, 0.0, s.lamp);
                    emission += s.lamp * _LampColor.rgb * on * chase * (0.55 + 0.6 * spot);
                }

                // 四隅のランプ(飾り。ゆっくり明るさが揺れる)
                if (s.corner > 0.0)
                {
                    float2 cp = float2(q.x < 0.0 ? -kCorner : kCorner, q.y < 0.0 ? -kCorner : kCorner);
                    float spot = 1.0 - saturate(length(q - cp - float2(-0.02, 0.02)) / kCornerR);
                    float pulse = 0.8 + 0.2 * sin(_Time.y * 2.0 + cp.x * 3.0 + cp.y * 5.0);
                    albedo = lerp(albedo, half3(0.6, 0.7, 0.85), s.corner);
                    smoothness = lerp(smoothness, 0.95, s.corner);
                    metallic = lerp(metallic, 0.0, s.corner);
                    emission += s.corner * _CornerColor.rgb * pulse * (0.45 + 0.8 * spot * spot);
                }

                albedo *= lerp(1.0, 0.35, self) * lerp(0.55, 1.0, cavity);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.occlusion = lerp(0.5, 1.0, cavity) * lerp(1.0, 0.6, self);
                surface.normalTS = half3(0, 0, 1);
                surface.emission = emission;
                surface.alpha = 1.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = shadowCoord;
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // 深度だけ(深度テクスチャに入れる。影は島の本体が落とすので、この板は落とさない)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
