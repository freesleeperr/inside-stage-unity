# Inside Stage VFX

A URP toolkit for building scenes that look like INSIDE: a silhouette in a shaft of light, fog holding the beam, shade pooling under whatever stands in the frame, and a wet floor throwing it all back.

It is built on top of [Cristian Qiu's Unity-URP-Volumetric-Light](https://github.com/CristianQiu/Unity-URP-Volumetric-Light), which provides the volumetric fog and light. The Inside Stage additions are the analytic ambient occlusion, the bounce light, the planar reflection and the dither and grain pass, along with the editor tool that relights a scene with them.

The idea running through the additions is that none of them are simulations. A lightmap bakes where light fails to reach and where it bounces, and then it is frozen. These place the same two things by hand, as shapes an artist moves around, and they stay alive at runtime: they can be parented to a character, animated, or swapped out with a lighting preset.

## Requirements

Any Unity version from 2022.3 LTS onwards, up to Unity 6000.5, with URP installed and set up.

Note on Unity 6: the volumetric fog supports both render graph and compatibility mode, but the Inside Stage effects are currently compatibility mode only. See [Known limitations](#known-limitations).

## Features

### Inside Stage

- **Hand placed indirect lighting.** Spheres and capsules that darken whatever surface is near them, and boxes and capsules that throw colored light back onto it, standing in for the indirect bounce a lightmap would have baked. Up to 16 occluders and 8 bounce lights contribute at once, and both are evaluated in a single pass. The shade is applied to the indirect term of each surface as it is shaded rather than to the finished image.
- **Planar reflection.** Renders the scene mirrored across a plane into a texture, which the included `Inside/Wet Floor` shader blends on top of a lit surface. The texture goes onto the reflective surfaces themselves, so a scene can hold several reflective planes.
- **Dither and grain.** Ordered dithering that keeps long dark gradients from banding on an 8 bit target, plus shadow biased film grain.
- **Editor tool.** `Tools/Inside/Light Stage` relights an existing scene, deriving every light position from the bounds of the object named `Floor`.

### Volumetric fog and light

- Render graph and compatibility mode support for Unity 6 and above.
- Support for the main light, spot lights and point lights, with shadows and light cookies.
- Realtime and mixed lights, and APV support for Unity 2023.1+.
- Perspective and orthographic projection, and single pass VR rendering.
- Seamlessly integrated into the URP volume system.
- Cloud alike fog through animated and tileable 3D noise, procedurally generated at runtime.
- Local fog volumes, so the fog can be held inside boxes of the world instead of filling it entirely.
- Optional self shadowing of the fog for the main light.
- Works in forward, deferred, forward+ and deferred+.
- Verified in DirectX11, DirectX12, OpenGLES3, OpenGLCore and Vulkan.

## Installation

Make sure URP is installed and set up first. Then, inside Unity:

1. Go to `Window` -> `Package Manager`.
2. Click the `+` at the top left corner and select `Install package from git URL`.
3. Enter `https://github.com/freesleeperr/inside-stage-unity.git` and click install.

## Quick start

Import the `Inside Stage Demo` sample from the package manager page. It brings in `Scn_InsideMood`, the scene this package is developed against, with the URP settings, volume profile, materials and mesh it was composed under: eight lights, seven fog areas, nine occluders, five bounce lights and a wet floor that reflects them. It is the fastest way to see what every effect contributes and to pull the pieces apart. The sample ships its own `URP-Inside.asset`; assign it under `Project Settings` -> `Graphics` before opening the scene, or the renderer features it carries are not the ones rendering.

To light a scene of your own instead, place a floor object named `Floor` and run `Tools/Inside/Light Stage`.

## The Inside Stage effects

The indirect lighting and the dither are renderer features. Add them to your URP Renderer asset through `Add Renderer Feature`. The planar reflection is a component you put on the reflective surface itself.

### Hand placed indirect lighting

Add the `Inside Indirect Lighting` renderer feature. It carries both halves of the hand placed lighting, so there is no ordering to get right between them: the bounce is evaluated first and the shade is applied on top of it, inside the one pass.

The shade is handed to the pipeline as `_ScreenSpaceOcclusionTexture`, the same channel URP's own screen space ambient occlusion writes, so it lands on the indirect term of a surface as that surface is shaded instead of being multiplied over the finished frame. That is why it no longer darkens the sky, an emissive surface or the light that reaches a surface directly. It also means the two cannot both be used: **do not enable URP's `Screen Space Ambient Occlusion` feature alongside this one**, as whichever runs last wins.

Because the shade now only touches the indirect light, it reads far more quietly in a scene lit almost entirely by direct light, which an INSIDE style scene usually is. **Occlusion Direct Lighting Strength** is the knob for that: it puts a proportion of the shade back onto the direct light. It defaults to `0.25`, matching URP, and going up towards one gets close to how the occlusion used to look.

On the feature:

- **Occlusion Intensity** is a master multiplier over every occluder in the scene, so the whole layer of shade can be dialed back without touching what has been placed.
- **Occlusion Direct Lighting Strength** is how much of the shade also falls on the direct light, as described above.
- **Bounce Intensity** is the same master multiplier for every bounce light.
- **Resolution** is what the two are evaluated at. Both are broad and soft, so `Half` is rarely visible and takes three quarters of the pixels out of the pass.
- **Bounce Render Pass Event** is when the bounce is added onto the frame. The shade is not affected by it, as it has to be ready before anything is shaded at all.

#### Occluders

Add `Rendering/Analytic Occluder` components to the scene wherever the image needs to get darker. Parent one to a character and the shade it lays down follows the character around.

Per occluder:

- **Shape** is either a sphere, which pools the shade under a single object, or a capsule, which spreads it along a length. A capsule is what a body, a pipe or a beam wants.
- **Radius** and **Height** define the shape, before the transform scale is applied. Height is measured between the centers of the two caps, along the local Y axis, and is ignored by spheres.
- **Intensity** is how dark the shade gets right at the surface of the shape. One takes the surface all the way to black.
- **Falloff** is how far past the surface, in world units, the shade reaches before fading out completely.
- **Directionality** is how much the shade is limited to surfaces actually facing the shape. At one it behaves like occlusion and leaves surfaces turned away from it alone; at zero it darkens everything nearby regardless of which way it faces, which is blunter but sometimes exactly what a composition needs.

Occluders that fall entirely outside the frame are dropped before the slots are handed out, so of the ones left, the 16 closest to the camera are the ones that render.

#### Bounce lights

Add `Rendering/Inside Bounce Light` components. The shape is a unit primitive that the transform stretches into place, so one of these covers a corridor as a drawn out pill or a floor as a flattened slab, rather than needing a row of point lights to do the same job.

Per light:

- **Shape** is a box or a capsule. A box flattened onto a floor is the light that floor throws back up; a capsule drawn out along an axis is what a corridor, a strip light or a window slot wants.
- **Color** is HDR. A real bounce carries the color of whatever it came off, so a warm floor should throw warm light even when the lamp above it is white.
- **Intensity** is how bright the light is right at the surface of the shape.
- **Reach** is how far past the surface, in world units, the light carries before fading out. It is measured in world space, so stretching the shape changes where the light comes from without changing how far it reaches.
- **Wrap** is how far the lambert term is wrapped past the terminator. At zero this behaves like an ordinary lamp; as it opens up the falloff softens and spreads until what is left has almost no direction to it, which is what makes it read as indirect light rather than as another light source.

Bounce lights that fall entirely outside the frame are dropped before the slots are handed out, so of the ones left, the 8 closest to the camera are the ones that render.

### Planar reflection and the wet floor

Add a `Rendering/Planar Reflection` component to the flat surface that should reflect, and give that surface a material using the `Inside/Wet Floor` shader. The component renders the scene mirrored across its own transform plane, writes the result into `_PlanarReflectionTexture` on the reflective renderers themselves, and the shader blends it on top of a lit surface with a fresnel term and a small animated normal distortion.

Because the texture goes onto the renderers rather than into a global, a scene can hold as many reflective planes as it can afford. It is kept per camera as well, so the scene view and the game view do not overwrite each other's point of view. Note that writing to the renderers uses a material property block, which takes those renderers out of the SRP batcher.

On the component:

- **Target Renderers** are the surfaces the reflection is written onto. Leaving it empty uses every renderer on this object and its children, which is what a floor made of a single mesh wants.
- **Reflection Layers** are what gets rendered into the reflection. Leaving small props out keeps it cheap without anybody noticing.
- **Resolution Scale** is the size of the reflection texture as a fraction of the camera. Reflections on a rough floor are blurry anyway, so this can go low.
- **Clip Plane Offset** is how far above the plane the mirrored camera starts clipping. Raising it hides the seam where geometry meets the floor, at the cost of cutting the reflection slightly short.
- **Render Shadows** and **Render Post Processing** are both off by default. Each nearly halves the cost of the reflection and neither is usually noticed on a floor.
- **Render In Scene View** is on by default. It costs a second pass over the scene while the editor window is open, and it is the only way to see what the floor will actually look like without entering play mode.

The reflection renders a second view of the scene, so it is the most expensive thing in this package by a wide margin. One is fine; a scene full of them is not.

### Dither and grain

Add the `Inside Dither` renderer feature. It runs after post processing, which is where the banding it has to break up actually appears.

- **Dither Intensity** is how much the ordered dithering spreads the banding out. One covers a bit more than a single step of an 8 bit target.
- **Grain Intensity** is how much grain is laid on top. Small values already read clearly once the image is in motion.
- **Grain Shadow Bias** pushes the grain towards the shadows. At zero it is spread evenly; at one it nearly disappears from the brightest parts.
- **Grain Response** is how sharply the grain falls off as the image gets brighter. Higher values confine it to the deepest shadows.
- **Animate Grain** moves the grain from frame to frame. A still grain reads as texture on the screen instead of as grain in the image.

## How to add volumetric fog

1. Add the volumetric fog renderer feature to your URP Renderer.
2. Enable post-processing both in your camera and URP Renderer.
3. Add a volume to your scene and add the override `Custom -> Volumetric Fog`.
4. Tick the `Enabled` checkbox, as fog is disabled by default.
5. Make sure that `Enable Main Light Contribution` and `Enable Additional Lights Contribution` are also enabled depending on your needs, as they are also disabled by default.
6. For spot and point lights, add the component `VolumetricAdditionalLight` to those lights that you want to contribute to the volumetric fog.
7. If you still can not see the fog, play with the different volume parameters or try to increase the intensity of your lights.

## How to add cloud alike fog

The fog is uniform by default, with only its height driving the density. To turn it into cloudy, volumetric fog:

1. In the volume override, tick `Enable Noise` inside the `Noise (Clouds)` section.
2. `Noise Tile Size` is the world size, in meters, of one tile of the noise that shapes the clouds of fog. Big values give big and soft clouds, while small values give small and busy ones.
3. `Noise Coverage` controls how much of the volume is filled. Lower it to open up gaps of clear air between the clouds of fog.
4. `Noise Intensity` blends between the uniform fog and the noise driven one.
5. `Enable Noise Detail` adds a second and higher frequency noise that erodes the edges of the clouds of fog. It costs one extra 3D texture fetch per raymarching step.
6. `Wind Direction` and `Wind Speed` define how the clouds of fog are blown. The wind direction does not need to be normalized.
7. `Noise Fade Distance` is the distance from the camera at which the noise fades back into uniform fog. Raymarching steps get longer with distance, so the noise aliases when it is far away. Set it to a value below the fog `Distance` if you see noise flickering in the distance. Zero disables the fade.
8. `Powder Intensity` darkens the light scattered by the thinnest parts of the fog, which adds contrast and makes the clouds of fog look fluffier.
9. `Enable Self Shadowing`, inside the `Main Light` section, makes the fog cast shadows onto itself. It is what gives the clouds of fog bright edges and dark cores, and it is the single parameter that makes them read as volumes rather than as flat patches. `Self Shadowing Intensity` controls how dark they get, while `Self Shadowing Distance` is how far the light march looks for the fog that occludes each step.

The noise textures are procedurally generated the first time they are needed, which takes a few hundred milliseconds. Enabling the noise while in edit mode gets that cost out of the way before entering play mode.

Some notes about performance and quality:

- The noise is sampled at every raymarching step, so both `Max Steps` and the noise settings drive the cost.
- Dense noise with few raymarching steps leads to temporal noise. Increase `Max Steps`, the blur iterations, or lower the noise frequency by increasing the tile sizes.
- Self shadowing marches six extra samples towards the main light at every raymarching step that finds fog, so it is by far the most expensive of the noise related settings. The detail noise is skipped in that march, since those steps are too long to resolve it anyway.

## How to hold the fog inside local volumes

By default the fog fills the whole world up to the `Distance` set in the volume. To gather it into local pockets instead, which is what lets a corridor be filled with thick fog while the room next to it stays clear:

1. Add an empty game object to your scene and add the component `Rendering/Volumetric Fog Area` to it.
2. `Size` is the size of the box that holds the fog, in local units. The box is placed, rotated and scaled with the transform, so it can be stretched and turned to follow whatever it is filling.
3. `Horizontal Border` and `Vertical Border` are the fraction of the box, measured from its sides towards its center, across which the fog fades in. Leave them at zero only when the box is fully hidden behind geometry, as its edges are hard otherwise.
4. `Density Multiplier` scales the density that the volume sets, for this area alone. Expect to need values well above one: an area is only a few meters deep, while the global fog accumulates across the whole fog distance, so the same density reads much thinner inside of a box.
5. Up to eight areas are rendered at the same time. When there are more in the scene, the eight closest to the camera are the ones that are kept. Areas that overlap keep the densest of them rather than adding up.
6. `Area Global Density`, in the `Fog Areas` section of the volume override, is how much fog is left outside of the areas, as a fraction of the density set in the volume. Keep it at zero to have fog only inside of the areas, or raise it to lay a thin haze everywhere and let the areas read as denser pockets on top of it.

While `Area Global Density` is zero, the raymarching is restricted to the part of the view ray that crosses the areas, so all of the steps are spent on the fog that is actually there and it resolves with a lot more detail than the same step count spread over the whole fog distance. Raising it above zero gives that up, since the ray then has to march the world as well.

The areas only shape where the fog is. What carves the shadows out of it is the lighting: turn on `Enable Main Light Contribution` and make sure that the occluding geometry casts shadows, and the fog inside the areas is shadowed by the scene like any other surface.

## Known limitations

### Inside Stage

- The indirect lighting and the dither do not implement a render graph path yet. On Unity 6 they only render in compatibility mode.
- The shade is delivered through `_ScreenSpaceOcclusionTexture`, so it only reaches shaders that declare the `_SCREEN_SPACE_OCCLUSION` keyword. URP's own lit shaders and the `Inside/Wet Floor` shader do; an unlit or a hand written shader that does not will show no occlusion at all. It also cannot be used together with URP's `Screen Space Ambient Occlusion` renderer feature.
- The bounce is still added onto the camera color rather than to the indirect term of a surface, as the pipeline offers no hook for adding light there. It is not modulated by the albedo of the surface it lands on.
- The indirect lighting is evaluated as a prepass and reads the depth normals prepass, so it follows the forward path. It has not been tested on the deferred path.
- Shapes are culled against the frame, but every pixel still evaluates every shape that survived that culling, rejecting each against its bounds. There is no tiled or per region binning.
- The planar reflection renders a full second view of the scene per reflective plane and per camera. It is the most expensive thing in the package.

### Volumetric fog

- Multipass VR rendering is not supported.
- WebGL is not supported.
- Not tested on Switch 1/2 and PS4 consoles. Verified by users on Steam Deck, Xbox One/XSX and PS5.
- Transparent objects are not blended correctly with fog.
- Fully baked lights are only supported through APV.
- Fog is only rendered up to a certain distance from the camera.
- It may be possible to notice more noise in some light regions at certain view angles.
- When using the forward or deferred rendering path, performance can be heavily affected by multiple additional lights. Forward+ or Deferred+ is highly recommended when additional light support is needed.

## Known bugs

- There is an issue with volumetric shadows from lights when `Transparent Receive Shadows` is off in the URP Renderer. If you are having issues with volumetric shadows from lights you may need to turn the setting on.

## Roadmap

- A render graph path for the Inside Stage effects.
- Screen space planar reflections, as a cheaper alternative to the mirrored camera.
- Smoke: local density volumes with an emitter, a lifetime and their own noise animation.
- Volumetric fog: WebGL support, quarter resolution rendering, reprojection, better transparency support, and self shadowing for additional lights.

## Credits

The volumetric fog and light is [Unity-URP-Volumetric-Light](https://github.com/CristianQiu/Unity-URP-Volumetric-Light) by Cristian Qiu, used and extended under the MIT license. If you only need the fog, use the original package rather than this one.

## License

MIT. See [LICENSE.md](LICENSE.md).
