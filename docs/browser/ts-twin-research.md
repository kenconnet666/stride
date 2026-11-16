# A TypeScript twin of Stride: research

Status: research, 2026-11-16. Question: a browser engine shaped like Stride, with its engine code in TypeScript (fast
builds, hot reload), the heavy parts in prebuilt WebAssembly libraries that are seldom rebuilt and cached for long,
and mature JavaScript libraries or browser APIs where they exist.

## Why the split works in a browser

The browser compiles JavaScript with a JIT (V8, SpiderMonkey). .NET in WebAssembly has none: its interpreter is 10 to
30 times slower than AOT code, which is why the C# port needs an AOT compile of every change (see
[ROADMAP.md](ROADMAP.md)). A TypeScript engine runs at JIT speed without a compile step: esbuild or Vite rebuild a
change in well under a second and swap modules in place. What JavaScript does poorly (tight numeric loops over many
objects) goes to WebAssembly libraries built once: physics, navigation, texture transcoding.

Engines built this way already: Babylon.js (TypeScript, 9.0 in March 2026, WebGPU and WebGL 2), PlayCanvas (JavaScript
with types, WebGPU since 2.x), three.js (WebGPURenderer and the TSL shading language, recommended since r182). Cocos
Creator 3 has a TypeScript engine layer over native code. Physics in these engines is WebAssembly (Havok, Ammo/Bullet,
Rapier, Jolt).

## What Stride uses at run time, and what a browser would use

From `sources/Directory.Packages.props`, the package references of the runtime projects (`sources/core`,
`sources/engine`, `sources/plugins`, `sources/shaders`), the `[DllImport]` names and `deps/`.

| Area | Stride now | Browser replacement | Kind |
|---|---|---|---|
| Graphics API | Direct3D 11/12, Vulkan, WebGPU (`wgpu_native`, emdawnwebgpu in the C# port) | WebGPU (`navigator.gpu`), WebGL 2 fallback if wanted | Browser API |
| Window, input | SDL (Silk.NET.Sdl), DirectInput, XInput | DOM pointer and keyboard events, Pointer Lock, Gamepad API (with vibration), `devicemotion` | Browser API |
| Audio | `strideaudio` (XAudio2, OpenAL, OpenSL ES, AVFoundation) | Web Audio API (panner nodes for 3D, AudioWorklet for streaming) | Browser API |
| Compressed sounds | Celt (`deps/Celt`) | Re-encode to Opus or Ogg Vorbis when exporting: `decodeAudioData` and WebCodecs decode them; libopus in WebAssembly only if Celt must stay | Export step |
| Video | FFmpeg (FFmpeg.AutoGen) | `<video>` plus `copyExternalImageToTexture`, or WebCodecs `VideoDecoder` | Browser API |
| Runtime fonts | FreeType (`freetype`) | Canvas 2D `fillText` into a glyph atlas (system and web fonts); opentype.js for outlines; FreeType in WebAssembly only for exact parity | Browser API |
| Precompiled fonts | msdfgen at asset compile time | msdf-atlas-gen at export; the runtime only samples the atlas | Export step |
| Images | SixLabors.ImageSharp (Stride.Foundation) | `createImageBitmap`, `ImageDecoder` | Browser API |
| GPU textures | BC/ASTC from DirectXTex and astcenc at compile time | KTX2 with Basis Universal (official transcoder in WebAssembly), or BC/ASTC where the `texture-compression-*` features exist | Wasm, prebuilt |
| Content compression | LZ4 (K4os.Compression.LZ4.Legacy) | HTTP brotli/gzip, `DecompressionStream`; meshopt compression for meshes (meshoptimizer decoder) | Browser API, small wasm |
| Physics | BepuPhysics 2.5 (C#) | Jolt (JoltPhysics.js 1.x, WebAssembly; close to Bepu's features: characters, constraints, soft bodies), Rapier (`@dimforge/rapier3d` 0.19.x, Rust to WebAssembly, SIMD and deterministic builds), Havok (Babylon's plugin) | Wasm, prebuilt |
| Navigation | DotRecast (C# Recast and Detour) | recast-navigation-js 0.43.x (Recast and Detour in WebAssembly, same algorithms), navcat (pure JavaScript port) | Wasm or JS |
| VR | OpenXR (Silk.NET.OpenXR), Oculus OVR, OpenVR | WebXR | Browser API |
| Shaders | SDSL, compiled through SPIR-V (SPIRV-Tools, SPIRV-Cross, DXC), naga to WGSL in the C# port | WGSL written or generated at export; the effect permutations a scene uses, compiled ahead (the C# port's content step already does it) | Export step |
| Scene format | Stride binary serialization of compiled assets | glTF 2.0 for meshes, materials, skins and animations, plus a JSON scene of entities and components | Export step |
| Profiling | VTune, WinPix | Browser performance panel, WebGPU timestamp queries | Browser API |
| Crash reports | Native crash handler | `window.onerror`, a JavaScript error reporter | Browser API |

Stays on the desktop, never in a browser: the editor (Game Studio, WPF/Avalonia), the asset compiler and its tools
(Assimp, DirectXTex, astcenc, VHACD, msdfgen, FFmpeg import, SPIRV-Cross, SPIRV-Tools, DXC, LLVM), Roslyn, MSBuild,
NuGet.

So only three areas need WebAssembly libraries at run time, all prebuilt and maintained by others: physics (Jolt or
Rapier), navigation (Recast), texture transcoding (Basis). Everything else is a browser API or happens once, when the
content is exported.

## Long-lived caching

- Prebuilt libraries (`jolt.wasm`, `recast.wasm`, `basis_transcoder.wasm`) and the engine bundle are files named by
  their content hash, served with `Cache-Control: immutable`: a game update downloads neither.
- The game's own code is a separate small bundle, the only code that changes with a game update.
- Content is split per scene or asset group, each named by hash; a change re-downloads only its group.

This is what the C# port cannot do: AOT links the engine, .NET and the game into one `dotnet.native.wasm`, which
changes with every change of the game.

## What a "twin" would keep from Stride

1. **Authoring**: Game Studio and the asset compiler stay as they are, with an export step that writes glTF, KTX2,
   Opus and the JSON scene, and the WGSL of the materials. The C# port's content step already compiles effects to WGSL.
2. **The model of a game**: entities with components (Transform, Model, Light, Camera, Script, Physics), scenes and
   child scenes, prefabs, a script API in TypeScript with the names of Stride's (`SyncScript.update`,
   `AsyncScript.execute` as an async function, `Entity.get(Component)`), so that a C# script ports by translation.
3. **Rendering features** come from the engine underneath: PBR materials (glTF PBR plus extensions for Stride's
   material layers), clustered lights, shadows, post effects.

What it gives up: C# game scripts (TypeScript instead), custom SDSL shaders as such (they would need a WGSL port or a
generator), Stride's UI system (port it to TypeScript, or HTML on top of the canvas), and features of Bepu that Jolt or
Rapier do not have, which a scene would behave differently with.

## Options for the engine underneath

| | PlayCanvas | Babylon.js | three.js | Own renderer |
|---|---|---|---|---|
| Model | Entities with components and scripts: closest to Stride | Scene graph of nodes and meshes, behaviors | Scene graph, renderer only | Whatever we design |
| Language | JavaScript with TypeScript types | TypeScript | JavaScript with types | TypeScript |
| WebGPU | Yes (2.x, compute) | Yes (9.x, frame graph, clustered lights) | WebGPURenderer, TSL | To write |
| Physics | Ammo built in, others by plugin | Havok plugin, others | None built in | Jolt or Rapier |
| Cost to reach Stride's features | Low | Low | Medium | Years |

A twin on PlayCanvas or Babylon.js gets rendering, loaders and tools on day one; Stride's shape (its component names,
script API, scene format) is the layer we write. An own renderer is years of work before a game runs.

## A prototype to decide with

In a new repository, small and measurable:

1. Export Third Person Platformer from Game Studio: the scene as JSON, the meshes and animations as glTF, the textures
   as KTX2.
2. Load it on PlayCanvas (or Babylon.js), with the character on Jolt and its controller script ported from C# to
   TypeScript.
3. Measure against the C# port on the same machine: frame time, first load, size, the time from a script change to
   the page running it, and how a hot reload of a script feels.

Questions for the user: the engine underneath (PlayCanvas for its components, or Babylon.js for TypeScript and its
features); whether game scripts may be TypeScript only; whether the C# port goes on beside the twin.

## Sources

- [Rapier](https://rapier.rs/), [rapier.js](https://github.com/dimforge/rapier.js) (0.19.x)
- [JoltPhysics](https://github.com/jrouwe/JoltPhysics), [JoltPhysics.js](https://github.com/jrouwe/JoltPhysics.js)
- [recast-navigation-js](https://github.com/isaac-mason/recast-navigation-js) (0.43.1), navcat
- [Babylon.js 9.0](https://blogs.windows.com/windowsdeveloper/2026/03/26/announcing-babylon-js-9-0)
- [PlayCanvas engine releases](https://radiancefields.com/platforms/playcanvas)
- [three.js WebGPURenderer](https://threejs.org/manual/pages/webgpurenderer)
