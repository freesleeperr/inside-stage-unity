# Requirements

Any Unity version posterior to 2022.3 LTS (included), up to Unity 6000.5.

# Features

* Render graph and compatibility mode support for Unity 6 and above.
* Support for the main light, spot lights and point lights.
* Shadows and light cookies support for the lights described above.
* Realtime and mixed lights support.
* APV support for Unity 2023.1+.
* Perspective and orthographic projection support.
* Single pass VR rendering support. This is verified by users, since I do not work on VR.
* Seamlessly integrated into URP volume system.
* Cloud alike fog through animated and tileable 3D noise, procedurally generated at runtime.
* Works in forward, deferred, forward+ and deferred+ rendering paths. See limitations section. 
* Verified in DirectX11, DirectX12, OpenGLES3, OpenGLCore and Vulkan.

# Installation

The recommended way to install the package is through the package manager in Unity (UPM).
Before installing, make sure that URP is installed and correctly set up in your project.
Then you can proceed to install this package:

1. Inside Unity, go to "Window"-> "Package Manager".
2. Once the window is opened, go to the "+" symbol at the top left corner, and select "Install package from git URL". See the image below.

![Install](https://github.com/CristianQiu/Unity-Packages-Gifs/blob/main/URP-Volumetric-Light/UPM1.jpg)

3. Write the following URL: https://github.com/CristianQiu/Unity-URP-Volumetric-Light.git and click install.

# How to add volumetric fog

1. Add volumetric fog renderer feature to you URP Renderer.
2. Enable post-processing both in your camera and URP Renderer.
3. Add a volume to your scene and add override: "Custom->Volumetric Fog".
4. Tick the "Enabled" checkbox, as fog is disabled by default.
5. Make sure that "Enable Main Light Contribution" and "Enable Additional Lights Contribution" are also enabled depending on your needs, as they are also disabled by default.
6. For spot and point lights, add the component "VolumetricAdditionalLight" to those lights that you want to contribute to the volumetric fog.
7. If you still can not see the fog, play with the different volume parameters or try to increase the intensity of your lights.

# How to add cloud alike fog

The fog is uniform by default, with only its height driving the density. To turn it into cloudy, volumetric fog:

1. In the volume override, tick "Enable Noise" inside the "Noise (Clouds)" section.
2. "Noise Tile Size" is the world size, in meters, of one tile of the noise that shapes the clouds of fog. Big values give big and soft clouds, while small values give small and busy ones.
3. "Noise Coverage" controls how much of the volume is filled. Lower it to open up gaps of clear air between the clouds of fog.
4. "Noise Intensity" blends between the uniform fog and the noise driven one.
5. "Enable Noise Detail" adds a second and higher frequency noise that erodes the edges of the clouds of fog. It costs one extra 3D texture fetch per raymarching step.
6. "Wind Direction" and "Wind Speed" define how the clouds of fog are blown. The wind direction does not need to be normalized.
7. "Noise Fade Distance" is the distance from the camera at which the noise fades back into uniform fog. Raymarching steps get longer with distance, so the noise aliases when it is far away. Set it to a value below the fog "Distance" if you see noise flickering in the distance. Zero disables the fade.
8. "Powder Intensity" darkens the light scattered by the thinnest parts of the fog, which adds contrast and makes the clouds of fog look fluffier.

The noise textures are procedurally generated the first time they are needed, which takes a few hundred milliseconds. Enabling the noise while in edit mode gets that cost out of the way before entering play mode.

Some notes about performance and quality:

* The noise is sampled at every raymarching step, so both "Max Steps" and the noise settings drive the cost.
* Dense noise with few raymarching steps leads to temporal noise. Increase "Max Steps", the blur iterations, or lower the noise frequency by increasing the tile sizes.

# Known limitations

* Multipass VR rendering is not supported.
* WebGL is not supported. See https://github.com/CristianQiu/Unity-URP-Volumetric-Light/issues/7.
* Not tested on Switch 1/2 and PS4 consoles. Verified by users in Steam Deck, Xbox One/XSX and PS5 as of v0.5.6.
* Transparent objects are not blended correctly with fog.
* Fully baked lights are only supported through APV.
* Fog is only rendered up to a certain distance from the camera.
* It may be possible to notice more noise in some light regions at certain view angles.
* When using forward or deferred rendering path, performance can be heavily affected when adding multiple additional lights. Forward+ or Deferred+ is highly recommended for best performance when support for additional lights is needed.

# Known Bugs

* There is an issue with volumetric shadows from lights when "Transparent Receive Shadows" is off in the URP Renderer. If you are having issues with volumetric shadows from lights you may need to turn the setting on. See https://github.com/CristianQiu/Unity-URP-Volumetric-Light/issues/10.

# TODO

There are a few things that I would like to add at some point, as I like coming back to this project from time to time in my spare time.
Some of which are (in no particular order):

* WebGL support.
* Further improvements on performance and quality: quarter resolution rendering, reprojection, better transparency support...
* New features: density volumes, noise, self shadowing...
* And of course... bug fixing! Do not hesitate to open issues for anything that you find unexpected or buggy.

# Preview

Using it in a [Tower Defense](https://www.youtube.com/watch?v=ND5eoEOw47E) I am working on as an indie dev <br><br>
![TD](https://github.com/CristianQiu/Unity-Packages-Gifs/blob/main/URP-Volumetric-Light/Towers.gif)

I am also working alongside Siesta Games studio to implement it in [Empire in Decay](https://store.steampowered.com/app/3345260/Empire_in_Decay/?utm_source=github&utm_campaign=cristianqiu&utm_medium=urp-volumetric-light), a roguelike deckbuilder chess-like game that will be released this year.
I also plan to release a new version in the future with some of the stuff that we used in the game.
You can play the [demo and wishlist](https://store.steampowered.com/app/3345260/Empire_in_Decay/?utm_source=github&utm_campaign=cristianqiu&utm_medium=urp-volumetric-light) it now or see the volumetric fog in action in our [release trailer](https://youtu.be/7ghsgWStH90)!<br><br>

![Empire In Decay](https://github.com/CristianQiu/Unity-Packages-Gifs/blob/main/URP-Volumetric-Light/EiDFog2.gif)
![Empire In Decay](https://github.com/CristianQiu/Unity-Packages-Gifs/blob/main/URP-Volumetric-Light/EiDFog.gif)
