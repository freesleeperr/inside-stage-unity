// Breaks up the banding that shows up in the long, dark gradients of a moody image, and lays film grain on top of it.
// Both are concentrated in the shadows, which is where an 8 bit target runs out of values to describe a gradient and
// where grain reads as part of the image rather than as dirt on the lens.
Shader "Hidden/Inside/Dither"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "InsideDither"

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

            float _DitherIntensity;
            float _GrainIntensity;
            float _GrainShadowBias;
            float _GrainResponse;
            float _FrameIndex;

            // The classic ordered 8x8 threshold matrix, normalized to the [0, 1] range. Compared to plain noise it is
            // what keeps the dithering from reading as grain of its own, since its pattern averages out over any
            // 8x8 block of the image.
            static const float BayerMatrix8[64] =
            {
                 0.0 / 64.0, 32.0 / 64.0,  8.0 / 64.0, 40.0 / 64.0,  2.0 / 64.0, 34.0 / 64.0, 10.0 / 64.0, 42.0 / 64.0,
                48.0 / 64.0, 16.0 / 64.0, 56.0 / 64.0, 24.0 / 64.0, 50.0 / 64.0, 18.0 / 64.0, 58.0 / 64.0, 26.0 / 64.0,
                12.0 / 64.0, 44.0 / 64.0,  4.0 / 64.0, 36.0 / 64.0, 14.0 / 64.0, 46.0 / 64.0,  6.0 / 64.0, 38.0 / 64.0,
                60.0 / 64.0, 28.0 / 64.0, 52.0 / 64.0, 20.0 / 64.0, 62.0 / 64.0, 30.0 / 64.0, 54.0 / 64.0, 22.0 / 64.0,
                 3.0 / 64.0, 35.0 / 64.0, 11.0 / 64.0, 43.0 / 64.0,  1.0 / 64.0, 33.0 / 64.0,  9.0 / 64.0, 41.0 / 64.0,
                51.0 / 64.0, 19.0 / 64.0, 59.0 / 64.0, 27.0 / 64.0, 49.0 / 64.0, 17.0 / 64.0, 57.0 / 64.0, 25.0 / 64.0,
                15.0 / 64.0, 47.0 / 64.0,  7.0 / 64.0, 39.0 / 64.0, 13.0 / 64.0, 45.0 / 64.0,  5.0 / 64.0, 37.0 / 64.0,
                63.0 / 64.0, 31.0 / 64.0, 55.0 / 64.0, 23.0 / 64.0, 61.0 / 64.0, 29.0 / 64.0, 53.0 / 64.0, 21.0 / 64.0
            };

            // Gets the ordered dithering threshold for the given pixel, centered around zero.
            float GetBayerOffset(uint2 positionSS)
            {
                uint index = (positionSS.y & 7u) * 8u + (positionSS.x & 7u);

                return BayerMatrix8[index] - 0.5;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                float2 positionSS = uv * _ScreenParams.xy;
                uint2 pixel = (uint2)positionSS;

                // A value of one spreads the dithering across a bit more than one step of an 8 bit target, which is
                // exactly what a gradient needs in order to stop showing its steps.
                color += GetBayerOffset(pixel) * (_DitherIntensity * (1.5 / 255.0));

                // The grain leans towards the shadows: bright parts of the image already carry enough detail to hide
                // it, while the dark ones are where film actually shows its grain.
                float luminance = saturate(Luminance(max(color, 0.0)));
                float shadowWeight = lerp(1.0, PositivePow(1.0 - luminance, _GrainResponse), _GrainShadowBias);

                float noise = InterleavedGradientNoise(positionSS, _FrameIndex) - 0.5;
                color += noise * _GrainIntensity * shadowWeight;

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
