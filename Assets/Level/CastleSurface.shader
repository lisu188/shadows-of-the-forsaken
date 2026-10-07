Shader "Shadows/CastleSurface"
{
    Properties
    {
        _Color ("Stone color", Color) = (0.2,0.23,0.25,1)
        _EmissionColor ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EmissionColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS; output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normal = normalize(input.normalWS);
                float noise = frac(sin(dot(floor(input.positionWS * 11), float3(12.9898,78.233,38.719))) * 43758.5453);
                half3 albedo = _Color.rgb * (.88 + noise * .22);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = SampleSH(normal) + mainLight.color * saturate(dot(normal, mainLight.direction)) * mainLight.shadowAttenuation;
                #ifdef _ADDITIONAL_LIGHTS
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalWS = normal;
                    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                    {
                        Light extra = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        light += extra.color * saturate(dot(normal, extra.direction)) * extra.distanceAttenuation;
                    }
                    LIGHT_LOOP_END
                #endif
                return half4(MixFog(albedo * light + _EmissionColor.rgb, input.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
