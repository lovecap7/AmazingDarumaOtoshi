Shader "Yurube/IwaCrater"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("Emission Color", Color) = (1,0.2,0.03,1)
        _EmissionStrength ("Emission Strength", Range(0,4)) = 0
        _Fade ("Fade", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "IwaCrater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : COLOR;
                float2 uv : TEXCOORD1;
            };

            fixed4 _Color;
            fixed4 _EmissionColor;
            float _EmissionStrength;
            float _Fade;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 頂点法線から簡易的な陰影を作り、窪みの内壁を暗く見せる
                float shade = lerp(0.62, 1.0, saturate(normalize(i.normal).y * 0.5 + 0.5));
                float3 color = i.color.rgb * _Color.rgb * shade;

                // UV.x=0が中心。中心だけ赤橙に発光させる
                float center = saturate(1.0 - i.uv.x * 3.0);
                color += _EmissionColor.rgb * _EmissionStrength * center;

                float alpha = i.color.a * _Color.a * _Fade;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
