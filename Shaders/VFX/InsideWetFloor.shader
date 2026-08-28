// A film of standing water laid over the floor as a piece of geometry. The whole of the mesh is water, and the whole of
// it mirrors: what the surface shows is the scene reflected in it, with the floor underneath bent up through it and a
// few glints of the lamps left on the ripples. There is no dry concrete in here any more. The floor already draws the
// dry part of itself, and the water fades into it wherever the mesh says there is less of it.
//
// The mirror comes from the PlanarReflection component, which renders the scene mirrored across its plane into
// _PlanarReflectionTexture, so it is sampled with the very same screen space coordinates as the fragment being shaded.
// The refraction is the floor itself, read back out of the URP opaque texture and bent the other way by the same
// ripples: two layers moving against each other is what keeps water reading as something with a bottom to it rather
// than as a sheet of chrome. That grab is why this pass draws in the transparent queue, and it is why the URP asset
// needs Opaque Texture ticked — with it off the water has nothing to look through and the refraction comes back black.
//
// The shape of the water is the shape of the mesh. A puddle is a piece of geometry an artist lays on the floor, the same
// way the occluders and the bounce lights in this package are shapes placed by hand, so the mask texture and the vertex
// alpha both default to one and the whole mesh is water unless one of them says otherwise. Thinning the vertex alpha
// out at the rim is what lets a puddle run out into the floor instead of ending at its own silhouette.
Shader "Inside/Wet Floor"
{
    Properties
    {
        [Header(Water)]
        _WaterMask("Water Mask", 2D) = "white" {}
        _VertexAlphaMask("Vertex Alpha Mask", Range(0.0, 1.0)) = 1.0
        _WaterTint("Water Tint", Color) = (0.5, 0.55, 0.6, 1.0)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.95

        [Header(Ripples)]
        _RippleTiling("Ripple Tiling", Float) = 0.35
        _RippleStrength("Ripple Strength", Range(0.0, 2.0)) = 0.5
        _RippleSpeed("Ripple Speed", Float) = 0.35
        _RippleFadeDistance("Ripple Fade Distance", Float) = 14.0

        [Header(Reflection)]
        _ReflectionStrength("Reflection Strength", Range(0.0, 1.0)) = 1.0
        _ReflectionTint("Reflection Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _ReflectionDistortion("Reflection Distortion", Range(0.0, 0.25)) = 0.03
        _ReflectionStretch("Reflection Vertical Stretch", Range(1.0, 6.0)) = 2.5
        _ReflectionBlur("Reflection Blur", Range(0.0, 1.0)) = 0.4

        [Header(Refraction)]
        _RefractionAmount("Refraction Amount", Range(0.0, 1.0)) = 0.25
        _RefractionDistortion("Refraction Distortion", Range(0.0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _WaterMask_ST;
            half4 _WaterTint;
            half4 _ReflectionTint;
            half _VertexAlphaMask;
            half _Smoothness;
            half _RippleTiling;
            half _RippleStrength;
            half _RippleSpeed;
            half _RippleFadeDistance;
            half _ReflectionStrength;
            half _ReflectionDistortion;
            half _ReflectionStretch;
            half _ReflectionBlur;
            half _RefractionAmount;
            half _RefractionDistortion;
        CBUFFER_END

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // The water is composited over the floor it lies on rather than replacing it, which is what lets the rim of
            // the mesh thin out into the dry floor with no seam. Depth is left to the floor as well: the two are the same
            // plane to within a few millimetres, and writing the water into it only buys z-fighting.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // The shape of the water, in the UV of the mesh. It defaults to white, so a mesh cut to the shape of the
            // puddle needs no texture at all: the mesh is the shape.
            TEXTURE2D(_WaterMask);
            SAMPLER(sampler_WaterMask);
            // Written every frame by the PlanarReflection component. It holds the scene as seen from the mirrored
            // camera, so it is sampled with the very same screen space coordinates as the fragment being shaded.
            TEXTURE2D(_PlanarReflectionTexture);
            SAMPLER(sampler_PlanarReflectionTexture);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half4 fogFactorAndVertexLight : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _WaterMask);
                output.color = input.color;
                output.shadowCoord = GetShadowCoord(positionInputs);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                half3 vertexLight = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
                half fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);

                return output;
            }

            // Gets the tangent space normal of the ripples. They are built out of a handful of waves crossing each
            // other at unrelated angles and speeds, which never lines up into a visible repeat and costs no texture.
            // The amount flattens them back towards the plane, which is what stops the far end of the room from
            // breaking up into a shimmer too fine for the frame to hold.
            half3 GetRippleNormalTS(float2 uv, half amount)
            {
                float2 p = uv * _RippleTiling;
                float t = _Time.y * _RippleSpeed;

                float2 slope = float2(0.0, 0.0);
                slope += float2(cos(p.x * 6.28 + t * 1.3), cos(p.y * 6.28 + t * 1.1)) * 0.5;
                slope += float2(cos(p.y * 11.0 - t * 0.9), cos(p.x * 9.0 + t * 1.7)) * 0.3;
                slope += float2(cos((p.x + p.y) * 17.0 + t * 2.1), cos((p.x - p.y) * 15.0 - t * 1.9)) * 0.15;

                return normalize(half3(slope * _RippleStrength * amount * 0.15, 1.0));
            }

            // Gets how much water covers the pixel, from nothing at all to a film deep enough to mirror. Both the mask
            // and the vertex alpha default to one, so an untouched mesh is water from edge to edge; painting the vertex
            // alpha down at the rim is how the water is made to run out before the geometry does.
            half GetWaterAmount(float2 uv, half vertexAlpha)
            {
                half mask = SAMPLE_TEXTURE2D(_WaterMask, sampler_WaterMask, uv).r;

                return saturate(mask * lerp(1.0, vertexAlpha, _VertexAlphaMask));
            }

            // Samples the reflection with a small cross of taps, because a film of water a few millimetres deep never
            // returns a sharp image: what it reflects is broken up by the water moving underneath it. The taps are spread
            // twice as far along the vertical axis, matching the direction the reflection is stretched in, so the blur
            // smears the image the way water does rather than fogging it evenly.
            half3 SampleReflection(float2 uv, half radius)
            {
                half3 reflection = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, saturate(uv)).rgb;

                if (radius <= 0.0)
                    return reflection;

                float2 offset = float2(radius, radius * 2.0);

                reflection += SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, saturate(uv + float2(offset.x, 0.0))).rgb;
                reflection += SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, saturate(uv - float2(offset.x, 0.0))).rgb;
                reflection += SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, saturate(uv + float2(0.0, offset.y))).rgb;
                reflection += SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, saturate(uv - float2(0.0, offset.y))).rgb;

                return reflection * 0.2;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewOffset = GetCameraPositionWS() - input.positionWS;
                half viewDistance = length(viewOffset);
                half3 viewDirWS = viewOffset / max(viewDistance, 0.0001);

                half water = GetWaterAmount(input.uv, input.color.a);

                // Ripples only travel across the water, and they stop being resolvable long before the floor runs out, so
                // they fade with distance rather than turning the far end of the room into a shimmer. Distant water goes
                // back to being the flat mirror it actually is.
                half rippleAmount = water * (_RippleFadeDistance / (_RippleFadeDistance + viewDistance));
                half3 normalTS = GetRippleNormalTS(input.positionWS.xz, rippleAmount);

                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                // The water has no colour of its own. Everything it shows comes from the mirror and from the floor read
                // back through it, so the lit surface is here for one thing only: the glints the lamps leave where a
                // ripple happens to be tilted towards them, which is what stops a mirror from reading as a hole.
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = half3(0.0, 0.0, 0.0);
                surfaceData.alpha = 1.0;
                surfaceData.metallic = 0.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = 1.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                half3 glints = UniversalFragmentPBR(inputData, surfaceData).rgb;

                float2 screenUV = inputData.normalizedScreenSpaceUV;

                // The ripples bend the reflection instead of the geometry, which is what sells the surface as a film of
                // water rather than as a mirror. The bend is stretched along the vertical axis of the screen, because
                // that is the direction a reflection travels in: a slope tilting towards the camera moves what it
                // reflects up or down the image several times as far as it moves it sideways.
                float2 reflectionOffset = normalTS.xy * _ReflectionDistortion * float2(1.0, _ReflectionStretch);
                half3 reflection = SampleReflection(screenUV + reflectionOffset, _ReflectionBlur * 0.006);

                // What is under the water is the floor, and the floor has already been drawn: the opaque texture holds it
                // lit, bounced and shadowed, one copy behind this pass. Bending it the opposite way to the reflection is
                // the whole of the refraction — the two layers slide against each other as the ripples go past, which is
                // what tells the eye there is a depth between them.
                float2 refractionOffset = -normalTS.xy * _RefractionDistortion;
                half3 underwater = SampleSceneColor(saturate(screenUV + refractionOffset)) * _WaterTint.rgb;

                // The hand placed shade reaches the water the same way it reaches every other surface, except that it has
                // to be applied by hand here: what the water shows is a mirror and a grab rather than an indirect term
                // for URP to shade. Without it a puddle stays lit in the middle of a shadow an occluder has laid down.
                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(screenUV, 1.0);

                half3 color = reflection * _ReflectionStrength * _ReflectionTint.rgb + underwater * _RefractionAmount;
                color = color * aoFactor.indirectAmbientOcclusion + glints;
                color = MixFog(color, inputData.fogCoord);

                return half4(color, water);
            }

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

                output.positionCS = positionCS;

                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
