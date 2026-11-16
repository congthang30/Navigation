Shader "Vps/ARRouteLine"
{
    Properties
    {
        _Color ("Main Color", Color) = (0.0, 0.9, 1.0, 0.95)
        // Metric depth priority offset (meters) applied along the view axis.
        // Keeps the ribbon glued to the floor plane while still winning the depth test
        // against floor depth noise. Must stay small: large values make the line
        // punch through low obstacles (steps, thresholds, bench legs).
        _DepthBiasMeters ("Floor Depth Bias (m)", Float) = 0.03
        _ScrollSpeed ("Chevron Scroll Speed", Float) = 1.55
        _ChevronFreq ("Chevron Density", Float) = 0.62
        _CoreGlow ("Chevron Glow Intensity", Range(0.2, 2.0)) = 1.35
        _EdgeSoftness ("Edge Anti-Alias", Range(0.01, 0.25)) = 0.06
        _NearFadeStart ("Near Fade Start (m)", Float) = 0.20
        _NearFadeEnd ("Near Fade End (m)", Float) = 0.60
        _TotalUvLength ("Total Route UV Length", Float) = 0.0
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
            // The ribbon is a blended overlay: it must not write depth, otherwise it
            // clips transparent content that is rendered after it.
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eyeDist : TEXCOORD1;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float _DepthBiasMeters;
            float _ScrollSpeed;
            float _ChevronFreq;
            float _CoreGlow;
            float _EdgeSoftness;
            float _NearFadeStart;
            float _NearFadeEnd;
            float _TotalUvLength;

            v2f vert (appdata v)
            {
                v2f o;

                float3 viewPos = UnityObjectToViewPos(v.vertex);
                // In Unity view space, vertices in front of the camera have negative Z (see COMPUTE_EYEDEPTH in UnityCG.cginc).
                o.eyeDist = -viewPos.z;

                if (_DepthBiasMeters > 0.0f && viewPos.z > 0.05f)
                {
                    viewPos.z -= _DepthBiasMeters;
                }

                o.vertex = mul(UNITY_MATRIX_P, float4(viewPos, 1.0f));
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Lateral normalized distance from centerline [0..1]
                float lat = abs(i.uv.y - 0.5f) * 2.0f;
                float outerMask = 1.0f - smoothstep(1.0f - _EdgeSoftness, 1.0f, lat);

                // Minimal glass bed + delicate outer hologram rails so the real floor stays clean
                float baseGlow = 0.055f * (1.0f - lat * 0.50f);
                float railBand = 1.0f - smoothstep(0.025f, 0.070f, abs(lat - 0.90f));

                // Crisp well-proportioned forward chevrons (^ ^ ^) with clean spacing between arrows
                float innerWindow = 1.0f - smoothstep(0.58f, 0.72f, lat);
                float chevronPhase = frac(i.uv.x * _ChevronFreq + lat * 0.44f - _Time.y * _ScrollSpeed);
                float d = abs(chevronPhase - 0.5f);
                float chevronStroke = (1.0f - smoothstep(0.055f, 0.105f, d)) * innerWindow;
                float chevronAura = (1.0f - smoothstep(0.09f, 0.20f, d)) * innerWindow;

                // Energy wave traveling forward + smooth fade near camera and at far tip
                float energyPulse = 0.84f + 0.16f * sin(i.uv.x * 0.55f - _Time.y * 2.8f);
                float nearFade = smoothstep(_NearFadeStart, _NearFadeEnd, i.eyeDist);
                float endFade = _TotalUvLength > 1.0f
                    ? smoothstep(0.0f, 1.2f, _TotalUvLength - i.uv.x)
                    : 1.0f;

                fixed3 coreColor = lerp(
                    i.color.rgb,
                    fixed3(1.0, 1.0, 1.0),
                    saturate(chevronStroke * 0.68f + railBand * 0.25f));

                float patternAlpha = (baseGlow + railBand * 0.32f + chevronAura * 0.18f + chevronStroke * 0.92f * _CoreGlow)
                    * outerMask * energyPulse * nearFade * endFade;

                return fixed4(coreColor, saturate(patternAlpha) * i.color.a);
            }
            ENDCG
        }
    }
}
