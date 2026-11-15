# Browser target: roadmap

Stride in the browser: .NET WebAssembly for the code, WebGPU for the graphics. This file says where the work is and
what comes next. Update it when a step is done or the order changes.

Branches: `web` is the integration branch of this fork. `web-experiment-base` is `web` plus the community experiment
(`icauro/experiment/wgpu-browser`), where the work below happens until it is reviewed and merged into `web`.

## Decisions

- Every project targets `net11.0`. The browser has its own target framework, `net11.0-browser`, like Android and iOS.
  The .NET 11 wasm workload ships Emscripten 6.0.2, which the Dawn WebGPU port (`emdawnwebgpu`) needs.
- Graphics go through the `webgpu.h` C API with P/Invoke: wgpu-native on desktop, `emdawnwebgpu` in the browser. The
  desktop WebGPU backend is where rendering work is developed and debugged; the browser is where it is confirmed.
- One thread. Nothing blocks the main thread: device creation, readback and content loading are asynchronous.
- Shaders are converted offline, SDSL to SPIR-V to WGSL (Naga 30.0.2). The browser never compiles a shader.
- A browser build also builds the host framework: the shader source generator and the asset compiler run on the host.

## Done

- The runtime builds on .NET 11 for desktop, and for `net11.0-browser`.
- Smoke page: Stride.Core, Stride.Foundation and Stride.Games run; file system, image decoding and the canvas window work.
- WebGPU probe: graphics device, WGSL effects, compute, content database, game loop and game startup pass in Chrome.
- Space Escape runs in Chrome: menu, run, collision and game over render with the offline WGSL effects.
- Text input: the browser keyboard is an `ITextInputDevice` fed by a hidden DOM text field (input methods, paste).

## 1. Make the baseline dependable

1. Check text input with a real input method, on desktop and on a phone. Then: copy to the clipboard, and the
   candidate window at the caret instead of the last pointer position.
2. Release builds need AOT: the interpreter is about 30 times slower in a matrix loop. The Mono AOT compiler asserts
   (`sgen-alloc.c:409`) on `Stride.Core.dll`. Find the code that triggers it, work around it, report it to
   dotnet/runtime. Then measure size and frame time of Space Escape, interpreter against AOT.
3. Keyboard lane changes in Space Escape were not confirmed, and a page without focus pauses the game. Decide what a
   page without focus should do, and test the keyboard by hand.
4. A CI lane on the fork: build the browser pages and run `build/browser/validate-*.cjs` in headless Chrome.
5. Review the WebGPU backend (about 4,700 lines, 80 "not supported" paths) against the Vulkan backend, file by file,
   to the standards of [AGENTS.md](../../AGENTS.md). Run `Stride.Graphics.Tests` on desktop WebGPU.

## 2. Make it part of the engine

1. Effects: today each page records the effects it asked for in the browser, and a tool replays that list offline.
   The asset compiler should compile every effect of a game for WebGPU, as it does for the other graphics APIs.
2. Embedded effects: WebGPU bytecodes from `EmbeddedEffects.yaml`, built or committed like the other APIs, instead of a
   script that fills `obj/`.
3. One browser host library instead of a `Program.cs` with the same exports in every page.
4. A Browser platform in the build: `StridePlatforms=Browser` for a game project, a project template, `dotnet publish`.
5. Content: load on demand over HTTP with a cache, instead of downloading everything before the game starts.

## 3. Cover the engine

Samples, from simple to complete: JumpyJet, SpriteFonts, AnimatedModel, MaterialShader, ParticlesSample, GameMenu,
BepuSample, TopDownRPG.

- Clustered lighting reads typed buffers (`Buffer<>`), which WebGPU does not have: use storage buffers or textures.
  This is the largest known gap: 148 of 621 corpus permutations fail on it.
- No geometry or tessellation shaders: voxel GI, the wireframe and debug line shaders and the cube map camera need
  another path, or stay off.
- Shadows, post effects, skinning, particles.
- Audio with Web Audio. Physics with BepuPhysics on one thread. Gamepads with the Gamepad API.
- Not planned: Bullet physics, video, VR.

## 4. Ship

- Download size and start time: trimming, compression, caching of the runtime and the content.
- Firefox and Safari, phones, device loss.
- Threads, if and when the .NET wasm runtime supports them well enough.

## 5. Upstream

- .NET 12 moves WebAssembly to CoreCLR. Upstream Stride waits for it (stride3d/stride#70). Keep the browser code free
  of Mono-only assumptions, and try CoreCLR when its packs are published.
- Send upstream what is not specific to the browser: fixes found on the way, and limits WebGPU puts on new graphics
  code (no geometry shaders, no push constants, asynchronous readback).
