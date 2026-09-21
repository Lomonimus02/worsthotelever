// Original depth-tested TextMesh font shader for the built-in Cyrillic-capable font.
Shader "WorstHotel/HotelLettering"
{
    Properties { _MainTex ("Font atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            V Vertex(A a) { V o; o.p=TransformObjectToHClip(a.p.xyz); o.uv=a.uv; o.color=a.color; return o; }
            half4 Fragment(V i) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a * i.color.a;
                clip(alpha - .015h);
                return half4(i.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
