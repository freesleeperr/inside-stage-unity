// The hand placed indirect lighting, evaluated once and delivered in two places.
//
// The first pass reconstructs the world position and the normal of every pixel of the frame a single time, and writes
// the shade the occluder shapes lay down into its red channel and the light the bounce shapes throw into the other
// three. It runs before the opaque geometry, so the red channel can be handed to the pipeline as
// _ScreenSpaceOcclusionTexture and land on the indirect term of every surface that is about to be shaded, rather than
// being multiplied over the finished image where it would darken the emissive surfaces, the sky and the direct light
// along with everything else.
//
// The second pass adds the bounce onto the frame once the opaque geometry is drawn, attenuated by that same shade.
// There is no equivalent hook in the pipeline for adding light to the indirect term of a surface, so this half is
// still a composite, but it is the half that only ever adds light where a surface was found and never touches the sky.
Shader "Hidden/Inside/IndirectLighting"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "InsideIndirectLightingEvaluate"

            Blend Off

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment

            // Blit.hlsl builds on the core shader library, so it has to come after the include that brings it in.
            #include "./InsideIndirectLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);

                // Nothing was drawn here, so there is no surface to lay shade onto or to throw light at. The sky is left
                // unoccluded and unlit, which is the whole reason this is evaluated before the frame is composited.
                UNITY_BRANCH
                if (rawDepth <= 0.0)
                    return half4(1.0, 0.0, 0.0, 0.0);

                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 normalWS = normalize(SampleSceneNormals(uv));

                float occlusion = EvaluateAnalyticOcclusion(posWS, normalWS);
                float3 bounce = EvaluateInsideBounceLight(posWS, normalWS);

                return half4(occlusion, bounce);
            }

            ENDHLSL
        }

        Pass
        {
            Name "InsideIndirectLightingComposite"

            // Added straight onto the color target, so the pass needs no copy of it to read from.
            Blend One One

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // The shade in r and the bounce in gba, as the evaluation pass left them.
            TEXTURE2D_X(_InsideIndirectLightingTexture);
            SAMPLER(sampler_InsideIndirectLightingTexture);

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 indirect = SAMPLE_TEXTURE2D_X(_InsideIndirectLightingTexture, sampler_InsideIndirectLightingTexture, input.texcoord);

                // The shade is applied to the bounce here rather than being left to the pipeline, so that the occluders
                // still darken the light the bounce shapes throw. That ordering is what the two separate passes used to
                // get out of the order they were listed in on the renderer.
                return half4(indirect.gba * indirect.r, 0.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
