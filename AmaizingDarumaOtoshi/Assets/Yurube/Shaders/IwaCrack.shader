Shader "Yurube/IwaCrack"
{
    Properties
    {
        _Color ("Color", Color) = (0.014, 0.011, 0.009, 0.95)
        _Grow ("Grow", Range(0,1)) = 1
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "IwaCrack"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _Grow;
            float _Alpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 伸長中の先端を柔らかく切る
                float show = 1.0 - smoothstep(max(0.0, _Grow - 0.025), _Grow, i.uv.y);
                if (show <= 0.001) discard;

                // 先端ほど薄く、根元ほど濃い
                float tipFade = lerp(1.0, 0.10, i.uv.y);
                float alpha = i.color.a * _Color.a * _Alpha * show * tipFade;
                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
