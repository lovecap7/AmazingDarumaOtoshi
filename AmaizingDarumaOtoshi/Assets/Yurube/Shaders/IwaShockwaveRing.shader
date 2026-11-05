Shader "Yurube/IwaShockwaveRing"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.97, 0.78, 1)
        _RingRadius ("Ring Radius", Range(0,0.5)) = 0.1
        _Thickness ("Thickness", Range(0.001,0.5)) = 0.05
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "IwaShockwaveRing"
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _RingRadius;
            float _Thickness;
            float _Alpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5);
                float ring = abs(d - _RingRadius);
                float edge = saturate(1.0 - ring / max(_Thickness, 0.0001));
                edge = smoothstep(0.0, 1.0, edge);
                return fixed4(_Color.rgb, _Color.a * _Alpha * edge);
            }
            ENDCG
        }
    }
}
