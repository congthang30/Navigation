// Companion (Doraemon) shader used for AR depth occlusion.
//
// Why a dedicated shader instead of the built-in Standard shader:
//  * The Standard shader exposes no depth offset / polygon offset properties
//    (verified against Shader.GetPropertyName on Unity 6000.0.74f1), so there is
//    no way to give the model a metric depth priority without rewriting it.
//  * The model needs a *contact only* bias: the feet sit exactly on the real
//    floor, and AR depth on the floor is noisy by a few centimetres, so the
//    bottom of the model gets clipped ("Doraemon bị cắt chân").
//    A constant bias over the whole model would let it see through walls.
//
// The bias is applied along the view axis (meters) and is faded out over the
// bottom band of the model, so:
//  * feet win the depth test against floor depth noise (no clipping), and
//  * the body keeps exact occlusion against walls, tables and people.
//
// Lighting is a simplified forward-lit model (main light + SH ambient + simple
// specular + emission) that matches the look of the previous Standard material
// closely enough for a stylised AR companion, while keeping the same material
// property names (_MainTex / _Color / _Cutoff / _EmissionColor) so the existing
// runtime brightness code keeps working.

Shader "Vps/ARCompanionOcclusion"
{
    Properties
    {
        _MainTex ("Base Map", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _EmissionColor ("Emission", Color) = (0,0,0,0)

        // Contact depth bias (meters) applied to the bottom band of the model.
        _ContactBiasMeters ("Contact Depth Bias (m)", Float) = 0.02
        // Height of the band (meters) over which the bias fades to zero.
        _ContactBandMeters ("Contact Band Height (m)", Float) = 0.06
        // World-space Y of the model base, written by the companion guide.
        _ContactBaseWorldY ("Contact Base World Y", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="ForwardBase" }

            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile _EMISSION
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float2 uv : TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed _Cutoff;
            fixed _Glossiness;
            fixed4 _EmissionColor;

            float _ContactBiasMeters;
            float _ContactBandMeters;
            float _ContactBaseWorldY;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);

                // Contact band weight: 1 at the model base, 0 above the band.
                float band = max(_ContactBandMeters, 1e-4);
                float weight = saturate((_ContactBaseWorldY + band - worldPos.y) / band);
                float bias = _ContactBiasMeters * weight * weight;

                // Apply the bias along the view axis only (Unity view space: +Z points away
                // from the camera, so subtracting pulls the vertex towards the camera).
                float3 viewPos = UnityObjectToViewPos(v.vertex);
                if (bias > 0.0f && viewPos.z > 0.05f)
                {
                    viewPos.z -= bias;
                    o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0f));
                    // Keep the world position consistent with the biased clip position.
                    worldPos = mul(UNITY_MATRIX_V, viewPos);
                }
                else
                {
                    o.pos = UnityObjectToClipPos(v.vertex);
                }

                o.worldNormal = worldNormal;
                o.worldPos = worldPos;
                o.uv = v.texcoord;

                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                #ifdef ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                fixed3 normal = normalize(i.worldNormal);
                fixed3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                fixed ndl = saturate(dot(normal, lightDir));
                fixed shadow = SHADOW_ATTENUATION(i);

                // Ambient from the spherical harmonics (same source the Standard shader uses).
                fixed3 ambient = ShadeSH9(float4(normal, 1.0));

                fixed3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                fixed3 halfVec = normalize(lightDir + viewDir);
                fixed specPower = exp2(_Glossiness * 10.0) + 1.0;
                fixed3 specular = pow(saturate(dot(normal, halfVec)), specPower) * _Glossiness;

                fixed3 color = albedo.rgb * (ambient + ndl * shadow * _LightColor0.rgb);
                color += specular * shadow * _LightColor0.rgb;

                #ifdef _EMISSION
                color += _EmissionColor.rgb * albedo.rgb;
                #endif

                fixed4 result = fixed4(color, albedo.a);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct appdataShadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2fShadow vertShadow (appdataShadow v)
            {
                v2fShadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow (v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
