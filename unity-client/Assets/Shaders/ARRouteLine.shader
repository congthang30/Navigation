Shader "Vps/ARRouteLine"
{
    Properties
    {
        _Color ("Main Color", Color) = (0.0, 0.9, 1.0, 0.95)
        _DepthBiasMeters ("Floor Depth Bias (m)", Float) = 0.18
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Geometry+100"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float _DepthBiasMeters;

            v2f vert (appdata v)
            {
                v2f o;
                float3 viewPos = UnityObjectToViewPos(v.vertex);
                float dist = length(viewPos);
                if (dist > 0.25f && _DepthBiasMeters > 0.0f)
                {
                    float pull = min(_DepthBiasMeters, dist - 0.20f);
                    viewPos *= (dist - pull) / dist;
                }
                o.vertex = mul(UNITY_MATRIX_P, float4(viewPos, 1.0f));
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
