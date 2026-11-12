Shader "Vps/AROcclusionStabilizationMesh"
{
    Properties
    {
        _DebugColor ("Debug Wire Color", Color) = (0.05, 0.88, 1.0, 0.28)
        _ColorMask ("Color Mask (0 = Hidden, 15 = RGBA)", Float) = 0
    }
    SubShader
    {
        // Built-in Forward Render Pipeline requires RenderType=Opaque and a ShadowCaster pass
        // so that NsdkOcclusionExtension's MeshObserverCamera (depthTextureMode = Depth)
        // populates _CameraDepthTexture with the fused runtime environment mesh.
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "OcclusionMeshDepthAndDebug"
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask [_ColorMask]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            fixed4 _DebugColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Grid contour lines when debug visualization (_ColorMask = 15) is enabled
                float3 grid = abs(frac(i.worldPos * 4.0 - 0.5) - 0.5) / fwidth(i.worldPos * 4.0);
                float lineFactor = 1.0 - saturate(min(min(grid.x, grid.y), grid.z));
                float ndotl = 0.5 + 0.5 * saturate(dot(normalize(i.worldNormal), float3(0.2, 0.9, 0.3)));
                fixed4 col = _DebugColor;
                col.rgb = lerp(col.rgb * ndotl, fixed3(0.4, 0.98, 1.0), lineFactor * 0.65);
                col.a = saturate(col.a + lineFactor * 0.35);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
            };

            v2fShadow vertShadow(appdata_base v)
            {
                v2fShadow o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack "VertexLit"
}
