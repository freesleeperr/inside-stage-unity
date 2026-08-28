# Changelog

## [0.12.0] - 2026-08-28

- Planar reflections are no longer rendered in the editor Scene View by default, removing the extra full-scene render that made authoring sluggish. Game View reflections are unchanged, and the Scene View preview can still be enabled with `Render In Scene View`.
- The wet floor shader skips ripple math, reflection sampling and refraction sampling when their corresponding material controls are disabled, and relies on the reflection render texture's clamp sampler for edge handling.

## [0.11.0] - 2026-08-28

- The `Inside/Wet Floor` shader is now standing water rather than a damp floor. The whole of its mesh mirrors, at full strength, instead of the reflection being scaled by a fresnel term that left the floor under the camera nearly dry and only opened up towards the horizon. `Reflection Strength` defaults to one and is the only thing left between the mirror and the surface; `Reflection Fresnel Power` and `Reflection Facing Amount` are gone.
- Water refracts what is under it. The floor beneath is read back out of the URP opaque texture and bent by the same ripples that bend the reflection, in the opposite direction, so the two layers slide against each other as a ripple goes past. That is what tells the eye there is a depth between them, and it is the cheapest refraction there is: the floor has already been drawn, lit, bounced and shadowed, one copy behind this pass. `Refraction Amount` is how much of it shows through and `Refraction Distortion` is how far it bends.
- **The URP asset now needs `Opaque Texture` ticked.** The refraction is that grab. With it off the water has nothing to look through and the refracted layer comes back black. The sample has had it on since it shipped.
- The pass draws in the transparent queue and composites over the floor rather than replacing it, which is what the grab requires and what lets the water thin out into the dry floor at the rim of the mesh with no seam. The water no longer writes depth, as it and the floor are the same plane to within a few millimetres and writing into it only bought z-fighting.
- Because of that, the surface no longer shades a floor of its own. `Base Map`, `Base Color`, `Metallic`, `Water Darkening`, `Dry Smoothness`, `Puddle Mask` and the three `Puddle Edge` parameters are gone with the dry half they described, along with the value noise that used to eat into the rim of the puddle: what shows through the water is the real floor, with the real noise, the real texture and the real shade already on it. The lighting the surface still does is the glints the lamps leave on the ripples, and `Smoothness` is what sizes them.
- The shape of the water is still the shape of the mesh. `Water Mask` replaces `Puddle Mask` and `Vertex Alpha Mask` is new, defaulting to one to keep the vertex alpha driving the rim the way it always did; a mesh with no vertex colour channel wants it at zero.
- The hand placed occlusion is applied to the water by hand, as what the surface shows is a mirror and a grab rather than an indirect term for URP to shade. Without that a puddle stayed lit in the middle of a shadow an occluder had laid down.
- The `InsideWetFloor` material in the sample was retuned around all of this. It had shipped with `Reflection Strength` at zero, so the wet surface in the sample scene reflected nothing at all.

## [0.10.0] - 2026-08-28

- The fog now casts shadows onto itself for the point and spot lights, and not only for the main light, through the new `Enable Additional Lights Self Shadowing` parameter in the `Additional Lights` section of the volume. The light march that gives the clouds of fog bright edges and dark cores was wired to the main light alone, so a scene lit by lamps rather than by a sun got none of it: with no directional light there is no main light, the march was compiled out entirely, and the ticked `Enable Self Shadowing` above it did nothing. That is exactly the kind of scene this package is for, and it is why the smoke in it read as a flat glowing blob.
- Every light that reaches a raymarching step marches four samples of its own towards itself, half the six the main light marches, and the march stops at the light instead of running past it, since the fog behind a lamp does not shade what is in front of it. `Additional Lights Self Shadowing Intensity` and `Additional Lights Self Shadowing Distance` dial it in apart from the main light ones, as a lamp lights the fog around it rather than the whole world.
- The sample scene turns it on, and the `Smoke Puff` in it was retuned around it: denser lobes, tighter borders and a lower noise coverage. The puff was thin enough that the view ray averaged its own noise out and brought it back smooth, so the noise those lobes already authored never reached the image.
- Documented how to author smoke with the `Override Noise` parameters of a fog area, which had shipped since 0.6.2 without a word in the readme about what they are for.

## [0.9.1] - 2026-08-28

- The `Inside Stage Demo` sample is now `Scn_InsideMood`, the scene this package is actually developed and tuned against, together with every asset that scene references. The sample used to be `Scn_Sample`, a copy taken once and then maintained by hand: every release since 0.7.0 has an entry in this file about the sample pointing at something in the development project, or holding a material that had been fixed on one side and not the other. Shipping the development scene itself is what stops that happening again.
- The sample brings across the volume profile, the URP asset and the renderer asset that the scene was lit under, rather than copies of them with their own GUIDs. The copies had drifted: the fog, the dither and the indirect lighting in the sample were not configured the way the scene had been composed, so the imported sample did not look like the screenshots taken of it.
- The `InsideWetFloor` material ships with the sample for the first time. The wet surface in the new scene uses the `Inside/Wet Floor` shader, and the material driving it lived in the development project.
- The lamp shade mesh is unchanged but its object is now named `InsideLampShade` rather than `Lamp Shade`, matching the file it has always been shipped as.
- Dropped `InsideBackdrop`, `InsideConcrete`, `InsideFloor`, `InsideTube` and `InsideWaterPuddle` from the sample, along with `Scn_Sample` itself. Nothing in the new scene references any of them, and a sample that imports assets its scene does not use is asking to be pulled apart in the wrong order.

## [0.9.0] - 2026-08-28

- The analytic ambient occlusion and the bounce light are now the one `Inside Indirect Lighting` renderer feature. They were a fullscreen pass each, and each of them sampled the depth and the normals of every pixel of the frame and reconstructed its world position before it could do anything with them, so the frame paid for that twice over. There is also no longer an ordering to get wrong between them: the bounce is evaluated first and the shade is applied on top of it, inside the one pass, which is what the two features only did correctly when they happened to be listed in the right order on the renderer.
- **This replaces both of the old renderer features.** A renderer asset that holds `Analytic Ambient Occlusion` or `Inside Bounce Light` will come up with a missing script where each of them was. Remove those two entries and add `Inside Indirect Lighting` in their place. The `Analytic Occluder` and `Inside Bounce Light` components in the scene are unchanged and keep everything that was authored on them.
- The shade is no longer multiplied over the finished frame. It is handed to the pipeline as `_ScreenSpaceOcclusionTexture`, the channel URP's own screen space ambient occlusion writes, so it lands on the indirect term of each surface as that surface is shaded. Multiplying it over the finished image meant it darkened the sky, the emissive surfaces and the light that reached a surface directly, none of which occlusion has any business touching.
- Because of that, the shade now reads much more quietly in a scene lit almost entirely by direct light, which an INSIDE style scene usually is. The new `Occlusion Direct Lighting Strength` parameter puts a proportion of it back onto the direct light. It defaults to `0.25`, matching URP, and raising it towards one gets close to how the occlusion used to look.
- Two consequences worth knowing about: the shade only reaches shaders that declare the `_SCREEN_SPACE_OCCLUSION` keyword, which URP's lit shaders and the `Inside/Wet Floor` shader do and an unlit shader does not; and it cannot be used alongside URP's own `Screen Space Ambient Occlusion` renderer feature, as the two write the same channel.
- Added the `Resolution` parameter, which evaluates both halves at half the resolution of the camera. Both are broad and soft, so it is rarely visible and takes three quarters of the pixels out of the pass.
- Occluders and bounce lights are now culled against the frame before the slots are handed out. A shape standing behind the camera used to take one of the sixteen or eight slots away from a shape that was actually in shot, and then be looped over by every pixel of the frame for the privilege. Each shape that survives also carries the bounds of everything it reaches, so a pixel rejects it with a subtraction rather than with a matrix multiplication and a square root.
- The planar reflection writes its texture onto the reflective surfaces themselves rather than into a global. A second reflective plane used to overwrite the first, so only one of them could work in a scene.
- The planar reflection now renders for the scene view as well, so a wet floor can be composed without entering play mode. It can be turned off with `Render In Scene View`.
- The planar reflection keeps a texture per camera. A single one shared between the scene view and the game view would be resized back and forth every frame, and whichever rendered last left its point of view on the floor of the other.
- The planar reflection renders through a render request on Unity 6, where `UniversalRenderPipeline.RenderSingleCamera` no longer exists.
- Removed the duplicate `Samples` folder. This is the third changelog entry to say so, and this time the folder is gone: the reason it kept coming back is that its copy of the scene was the one open in the editor of the project the package is developed in, so the editor wrote it out again every time it saved.

## [0.8.1] - 2026-08-28

- A reflective surface no longer renders itself into its own reflection. `PlanarReflection` drew the very surface it was about to write the reflection for, so the surface sampled the texture it had written the frame before and fed the reflection back into itself frame after frame, until a fully reflective floor had settled into a flat block of colour with nothing of the scene left in it.
- The wet floor material shipped with the sample was authored with every parameter at the end of its range: a fully reflective, rippleless, white and fully metallic mirror. `Reflection Facing Amount` at one in particular cancels the fresnel outright, so the floor showed the reflection alone rather than the lit surface with the reflection laid over it, and read as a hole cut into the deck of a dark room rather than as a puddle. It now uses the values the shader documents as its own defaults.
- The puddle in the sample scene no longer renders the reflection with post processing. The reflection was being tonemapped once on its way into the texture and a second time along with the frame it was blended into, which crushed it towards black.

## [0.8.0] - 2026-08-28

- Removed the `Tools/Inside/Build Mood Scene` editor tool. It authored a whole scene in code into `Assets/Inside/`, which only ever made sense in the project the package was developed in: installed anywhere else it wrote a folder into the user's own project and overwrote it on every run. The `Inside Stage Demo` sample is now the one scene the package ships, and `Tools/Inside/Light Stage` remains for relighting a scene that already exists.
- The sample scene no longer reaches outside of the package. Its global volume pointed at a volume profile that lived in the development project rather than at the profile shipped next to it, so an imported sample came up with an empty volume and no fog at all.
- The lamp shade mesh is now shipped with the sample, as `Meshes/InsideLampShade.asset`. It used to be referenced from the development project, so the lamp imported with a missing mesh.
- The sample's URP asset now points at the renderer asset shipped beside it instead of the one in the development project, so an imported sample no longer comes up with a renderer holding none of the effects.
- The wall and the three crates in the sample scene were left on URP's built in `Lit` material, bright grey in a scene that is meant to be dark. They now use the `InsideConcrete` material that the sample already shipped.
- Removed the duplicate `Samples` folder for real. The 0.7.0 entry below announced it, but the folder and its copy of the scene were still in the package.

## [0.7.0] - 2026-08-28

- The shaders behind the analytic ambient occlusion, the bounce light, the dither and the wet floor are now part of the package. They used to live in the project that the package was developed in, so installing the package anywhere else brought the C# across without them and those four effects silently did nothing.
- Those three renderer features now hold a serialized reference to their shader instead of calling `Shader.Find` at runtime. `Shader.Find` only sees the shaders that made it into the build, and nothing references a hidden shader, so the effects worked in the editor and were missing in a player. They also log a warning now when the shader cannot be resolved, instead of disabling themselves without a word.
- Everything in the package now lives in the `FreeSleeperr.InsideStage` and `FreeSleeperr.InsideStage.Editor` namespaces. Names as common as `PlanarReflection` and `Settings` used to sit in the global namespace of every project that installed the package.
- Removed the duplicate `Samples` folder. It was imported into every project that installed the package, alongside the `Samples~` copy that the package manager imports on demand. Its scene, volume profile and puddle material, which were newer, were moved into `Samples~/InsideStageDemo`.
- The package description, keywords and version now describe this package rather than the one it was forked from.

## [0.6.2] - 2026-08-25

- Volumetric fog areas can now hold fog several at a time, instead of only the one closest to the camera being rendered. Up to eight of them contribute at once, which removes the popping that happened when the camera moved from one area to another.
- Areas that overlap keep the densest of them instead of adding up, so that sliding one area over another does not thicken the fog where they cross.
- Areas are now sorted by the distance to the box that they define, and no longer by the distance to its center, so that a big area surrounding the camera is never dropped in favour of the small ones gathered further away.
- Added the "Area Global Density" parameter to the volume component. It sets how much fog is left outside of the areas: zero keeps the fog entirely inside of them, as it was before, while higher values leave a thinner layer of fog everywhere and turn the areas into denser pockets on top of it.
- The raymarching is only restricted to the areas when there is no fog left outside of them, as the global fog fills the gaps between areas otherwise.

## [0.6.1] - 2026-08-25

- Added self shadowing for the main light. A short ray is marched towards it at every raymarching step, so that the fog casts shadows onto itself and the clouds of fog get bright edges and dark cores instead of being lit uniformly.
- Added the "Enable Self Shadowing", "Self Shadowing Intensity" and "Self Shadowing Distance" parameters to the main light section of the volume component.
- The raymarching now stops as soon as the fog is thick enough to hide whatever is behind it, which saves a good amount of steps in dense fog.

## [0.6.0] - 2026-08-24

- Added noise based, cloud alike volumetric fog. The fog density can now be modulated with animated, tileable 3D noise.
- Added the "Noise (Clouds)" section to the volume component, with coverage, intensity, tile size, wind and distance fade parameters.
- Added an optional and higher frequency detail noise that erodes the edges of the clouds of fog.
- Added the powder effect parameter, which darkens the light scattered by the thinnest parts of the fog so that it looks fluffier.
- The 3D noise textures are procedurally generated at runtime with the job system, so no additional assets are needed.

## [0.5.9] - 2026-03-21

- Removed warning displayed in versions starting from 6000.3. Add support to 6000.4.

## [0.5.8] - 2025-09-11

- Safety check for array OOB with very high number of lights being visible in the scene.

## [0.5.7] - 2025-08-29

- Fix for Forward+ (and likely Deferred+ too) issue on Metal.

## [0.5.6] - 2025-05-09

- Minor change to prevent PS5 error reported by user.

## [0.5.5] - 2025-04-30

- Updated some of the documentation to make it clear that the package supports deferred+ on Unity 6000.1.0f1.
- Updated additional information from the readme and updated the license year.
- No functional changes made.

## [0.5.4] - 2025-04-07

- Added initial APV support for Unity 2023.1+. It can be toggled from the volume.

## [0.5.3] - 2025-03-19

- Fix for situational null reference in Create() from the volumetric fog renderer feature.
- Replaced custom render pass event enum values to read the ones from URP's enum.

## [0.5.2] - 2025-02-23

- Removed empty version dependency as it was throwing errors. User should make sure that they have URP package installed before installing the volumetric light package to avoid console errors.

## [0.5.1] - 2025-02-16

- Solved error in the scattering falloff that made additional lights scattering to be stronger than it should. You may have to retweak your additional lights scattering. [Sorry :(]
- Reduced noise at certain, extreme combinations of near plane, camera field of view, fog density and light scattering.
- Exposed render pass event field in the volume settings. Overriding can still happen per volume.
- Replaced obsolete method by its newest version in non RG path in Unity 6.
- Other minor improvements/cleanups.

## [0.5.0] - 2025-01-19

- Added orthographic camera support.
- Decrease CPU work when setting parameters for lights during the volumetric fog render pass.
- Upsample uses texture gather when shader model 4.5+ is available.
- Cleanups in VolumetricFogRenderPass.cs and VolumetricFog.hlsl.
- Verified supported graphics APIs: DirectX11, DirectX12, OpenGLES3, OpenGLCore and Vulkan.

## [0.4.1] - 2025-01-13

- Address issue with lights potentially picking wrong parameters to affect the volumetric fog depending on whether there is a main light enabled in the scene.
- Correctly clamp the anisotropy value for additional lights when setting the property from code.
- Renamed main light parameters from the volume to not have the prefix "Main Light..." since additional lights are no longer displayed there since 0.4.0.
- Minor cleanups.

## [0.4.0] - 2025-01-02

- A new component has been added: VolumetricAdditionalLight. This component now must be added to each point and spot light that you want to influence the volumetric fog. Because of that, now it is possible to have different settings per light.
  The main light settings are still configured in the volume, as it should still be used with Unity's volume workflow (mainly for blending purposes).
- Shadow sampling during raymarching now ignores whether soft shadows are enabled and its quality level. This greatly enhances performance when using soft shadows with a barely noticeable loss in quality.
- Changed blur to use point sampling instead of bilinear since source and target are the same size.
- Added back shader model 4.5 instruction to increase the downsample depth pass performance, while also having an alternate path to support cases where it is not possible to use it.

## [0.3.7] - 2024-12-29

- Removed shader model 4.5 requirement for broader support.
- Solved issues related to OpenGL.

## [0.3.6] - 2024-12-02

- Set proper texture usage flags for the camera depth texture and rendergraph path.

## [0.3.5] - 2024-11-29

- Additional checks to skip rendering when not necessary.

## [0.3.4] - 2024-11-16

- Tweaked the minimum distance from the volume that can be set for the fog to be rendered.

## [0.3.3] - 2024-11-12

- Added more optimal flags for some textures usage in rendergraph.

## [0.3.2] - 2024-11-12

- Added obsolete attributes to the overrided methods for Unity 6 and the non rendergraph path to avoid warnings in certain situations.

## [0.3.1] - 2024-11-08

- Added missing tooltips from the volume. Minor tweak to the fog shader when there is more than 1 directional light in the scene.

## [0.3.0] - 2024-10-06

- Added custom editor for the volumetric fog volume, which should slightly improve the usage. Added ground height parameter that allows fog to not be rendered below a certain height, for slightly better compatibility with transparencies like an ocean.
  The color tint now only affects the main light color which makes more sense. Many volume parameters have now a descriptive tooltip.

## [0.2.1] - 2024-10-02

- Added Single pass VR rendering support.

## [0.2.0] - 2024-09-27

- Mostly code cleanup and minor fixes regarding compatibility through different versions from 2022 to Unity 6. Tested with no warnings or errors in 2022.3.47f1, 2023.1.20f1, 2023.2.20f1 and 6000.0.18f1. Removed code referencing to a 2023.3 beta version which ultimately became Unity 6, so this package should not work any longer with 2023.3 beta versions : just upgrade to Unity 6.

## [0.1.7] - 2024-09-22

- Fix bug introduced in 0.1.6 that disabled the volumetric fog when it should not.

## [0.1.6] - 2024-09-22

- Now the main light computations will be skipped when its volume scattering parameter is set to 0. The same will happen with additional lights and their scattering parameter. Slightly changed the falloff of the volume parameter "Additional Light Radius".

## [0.1.5] - 2024-08-10

- Increased the number of maximum steps that can be tweaked through the volume from 128 to 256.

## [0.1.4] - 2024-07-26

- Removed some text and comments.

## [0.1.3] - 2024-07-07

- Updated some texts referencing old Unity 2023.3 beta to replace them by Unity 6 to make it clear that this package should work on Unity 6. Tested in Unity 6000.0.9f1.
- No other functional changes were made.

## [0.1.2] - 2024-02-12

- Fixed potential issue with Unity version check in shader.

## [0.1.1] - 2024-02-01

- Fixed non matching blur iterations in render graph and non render graph paths. Cleanup typos and unused attribute.

## [0.1.0] - 2024-01-28

- Initial Release. Verified in Unity 2022.3.18f1, 2023.2.7f1 and 2023.3.0b4.
