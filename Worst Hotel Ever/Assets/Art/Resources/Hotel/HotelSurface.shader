// Original textured hotel surface. Kept in Resources to survive player stripping.
Shader "WorstHotel/HotelSurface"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Smoothness ("Polish", Range(0,1)) = 0.15
        _Emission ("Glow", Range(0,2)) = 0
        [MainTexture] _BaseMap ("Original surface albedo", 2D) = "white" {}
        [Enum(UV0,0,WorldTriplanar,1)] _MappingMode ("Projection", Float) = 0
        _WorldScale ("World repeats per metre", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;
            half _Emission;
            float4 _BaseMap_ST;
            float _MappingMode;
            float _WorldScale;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fog : TEXCOORD2; float2 uv : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        Varyings HotelVertex(Attributes i)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
            o.positionCS = p.positionCS; o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.fog = ComputeFogFactor(p.positionCS.z);
            o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "HotelForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HotelVertex
            #pragma fragment HotelFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half3 HotelAlbedo(Varyings i, half3 n)
            {
                half3 sampled;
                UNITY_BRANCH if (_MappingMode > .5)
                {
                    // Only immutable architecture uses this path; batching preserves world position.
                    float3 p = i.positionWS * _WorldScale;
                    half3 weights = abs(n);
                    weights *= weights; weights *= weights;
                    weights /= max(weights.x + weights.y + weights.z, .0001h);
                    // Do not frac these coordinates: the imported sampler owns Repeat/Mirror wrapping.
                    sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * weights.x;
                    sampled += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xz * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * weights.y;
                    sampled += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * weights.z;
                }
                else sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // Continuous remap preserves dark albedo detail after sRGB-to-linear sampling.
                // The offset keeps wear readable without flattening it against a lower clamp.
                half3 modulation = .32h + sqrt(max(sampled, 0.0h)) * 1.15h;
                return _BaseColor.rgb * modulation;
            }
            half3 HotelLight(Light light, half3 n, half3 view, half3 albedo)
            {
                half diffuse = saturate(dot(n, light.direction));
                half highlight = pow(saturate(dot(n, SafeNormalize(light.direction + view))), lerp(12.0h, 96.0h, _Smoothness));
                return (albedo * diffuse + highlight * _Smoothness * 0.26h) * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }
            half4 HotelFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 albedo = HotelAlbedo(i, n);
                // Cool, readable fill remains during utility outages; local lamps supply warm pools.
                half3 fill = lerp(half3(.075,.095,.12), half3(.19,.23,.25), saturate(n.y * .5h + .5h));
                half3 color = albedo * (fill + _Emission);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
                #endif
                color += HotelLight(GetMainLight(shadowCoord), n, view, albedo) * .62h;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            color += HotelLight(GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1)), n, view, albedo);
                        }
                    #endif
                    LIGHT_LOOP_BEGIN(count)
                        color += HotelLight(GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1)), n, view, albedo);
                    LIGHT_LOOP_END
                #endif
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    color *= lerp(.68h, 1.0h, ao.indirectAmbientOcclusion);
                #endif
                return half4(MixFog(color, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HotelShadowVertex
            #pragma fragment HotelShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            float4 HotelShadowVertex(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - p);
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p,n,direction)));
            }
            half4 HotelShadowFragment() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HotelVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HotelVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 NormalsFragment(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
