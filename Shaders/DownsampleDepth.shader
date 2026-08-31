Shader "Hidden/DownsampleDepth"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "DownsampleDepth"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off
            ColorMask R

            HLSLPROGRAM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            #pragma target 4.5
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag

            float _DownsampleFactor;

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 depths;

                // One gather covers a 2x2 block of full resolution texels, which is the whole of the block that one
                // half resolution pixel stands for. A bigger factor stands for a bigger block than a gather reaches,
                // so its corners are loaded instead: the minimum and the maximum they carry are what the upsample
                // needs later on to find the edges of the geometry.
                UNITY_BRANCH
                if (_DownsampleFactor <= 2.0)
                {
                    depths = GATHER_RED_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, input.texcoord);
                }
                else
                {
                    uint factor = max(uint(_DownsampleFactor), 1u);
                    uint2 fullResTopLeftCorner = uint2(input.positionCS.xy) * factor;
                    uint offset = factor - 1u;

                    depths.x = LoadSceneDepth(fullResTopLeftCorner + uint2(0, offset));
                    depths.y = LoadSceneDepth(fullResTopLeftCorner + uint2(offset, offset));
                    depths.z = LoadSceneDepth(fullResTopLeftCorner + uint2(offset, 0));
                    depths.w = LoadSceneDepth(fullResTopLeftCorner);
                }

                float minDepth = Min3(depths.x, depths.y, min(depths.z, depths.w));
                float maxDepth = Max3(depths.x, depths.y, max(depths.z, depths.w));

                return (uint(input.positionCS.x + input.positionCS.y) & 1) > 0 ? minDepth : maxDepth;
            }

            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "DownsampleDepth"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off
            ColorMask R

            HLSLPROGRAM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            #pragma editor_sync_compilation

            #pragma vertex Vert
            #pragma fragment Frag

            float _DownsampleFactor;

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 depths;

                uint factor = max(uint(_DownsampleFactor), 1u);
                uint2 fullResTopLeftCorner = uint2(input.positionCS.xy) * factor;
                uint offset = factor - 1u;

                depths.x = LoadSceneDepth(fullResTopLeftCorner + uint2(0, offset));
                depths.y = LoadSceneDepth(fullResTopLeftCorner + uint2(offset, offset));
                depths.z = LoadSceneDepth(fullResTopLeftCorner + uint2(offset, 0));
                depths.w = LoadSceneDepth(fullResTopLeftCorner);

                float minDepth = Min3(depths.x, depths.y, min(depths.z, depths.w));
                float maxDepth = Max3(depths.x, depths.y, max(depths.z, depths.w));

                return (uint(input.positionCS.x + input.positionCS.y) & 1) > 0 ? minDepth : maxDepth;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
