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
- One host for the samples (`build/browser/Sample.Game`, `Sample.Browser`, `PrepareSampleContent.ps1`,
  `validate-sample.cjs`). In Chrome, each of these starts, loads its scene and draws 180 frames without an error:
  SpriteFonts, GameMenu, AnimatedModel (skinning, three views), ParticlesSample, MaterialShader (a material with its
  own shaders). The check looks at the page, not at the game play: nothing clicks through them yet.

## 1. Make the baseline dependable

1. Check text input with a real input method, on desktop and on a phone. Then: copy to the clipboard, and the
   candidate window at the caret instead of the last pointer position.
2. Release builds need AOT: the interpreter is about 30 times slower in a matrix loop. The Mono AOT compiler of
   .NET 11.0.0 asserts (`sgen-alloc.c:409`) on any assembly with a P/Invoke that has
   `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]`; one such method is enough. The WebGPU bindings use
   `DllImport` with `CallingConvention.Cdecl` instead. `VTuneProfiler` (Stride.Core) and `Spv2DXIL`
   (Stride.Shaders.Compilers) have the attribute too: trimming removes them from a game, an untrimmed build still
   fails. This is dotnet/runtime#136351: fixed for .NET 12, and its backport (dotnet/runtime#136627) is approved for
   a .NET 11 servicing release. When that release is out, the workaround in the bindings generator can go. To do:
   measure size and frame time of Space Escape, interpreter against AOT.
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

Samples still to run: JumpyJet (it uses Bullet physics for its collisions), SimpleAudio (audio), CustomEffect,
MorphTargets, SpriteStudioDemo, UIElementLink, UIParticles, BepuSample, TopDownRPG.

A sample asks for the effects of the engine at run time, and the browser cannot compile them: the first run records
the requests (`validate-sample.cjs --record-effects`), and the content is prepared again with them. An effect that
only shows later in the game (a menu that opens, an explosion) is recorded only if the run gets there.

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
- Threads: see "Threads" below.

## 5. Upstream

- .NET 12 moves WebAssembly to CoreCLR. Upstream Stride waits for it (stride3d/stride#70). Keep the browser code free
  of Mono-only assumptions, and try CoreCLR when its packs are published.
- Send upstream what is not specific to the browser: fixes found on the way, and limits WebGPU puts on new graphics
  code (no geometry shaders, no push constants, asynchronous readback).

## Threads

WebAssembly threads are workers that share one memory. .NET 11 has a multithreaded browser runtime
(`WasmEnableThreads`, still called experimental), and Stride's code already asks the runtime whether it has threads
(`RuntimeFeature.IsMultithreadingSupported`): the same engine runs with or without them. The build without threads
stays, for the pages that cannot be cross-origin isolated.

What a page with threads needs:

- The cross-origin isolation headers (`Cross-Origin-Opener-Policy: same-origin`,
  `Cross-Origin-Embedder-Policy: require-corp`), or the browser gives no shared memory.
- A pool of workers filled before the application starts (`pthreadPoolInitialSize` of the runtime configuration).
  A thread that finds the pool empty never starts, and its `Start` never returns: Stride's thread pool starts one
  thread per processor at once, and hung with the default pool of 7.
- No synchronous call from the page thread into managed code: managed code runs on a worker. Reading the stack of a
  managed exception from JavaScript is such a call.
- Every native library built with threads (the WebGPU bindings, FreeType, the audio library).

The plan: the engine runs on a worker, with the canvas given to it as an `OffscreenCanvas`; the page thread keeps
the DOM (input events, the text field of the input methods) and forwards the input. The engine may then block, as on
a desktop, and its thread pool has real threads. WebGPU objects belong to one thread, so one thread still submits
the commands.

1. Done: `build/browser/ThreadsProbe` (`validate-threads.cjs`). In Chrome, with 12 processors: threads start and
   join, a lock blocks, Stride's thread pool has 11 workers and `Dispatcher.For` runs on 11 threads, 3.2 times
   faster than one for a loop of 64 heavy items (interpreter).
2. Done: `build/browser/ThreadsGpuProbe`. A managed thread with its own JavaScript interop (`JSWebWorker`, in the
   runtime but not yet in its reference assembly: called by reflection) gets the canvas of the page as an
   `OffscreenCanvas`, draws 120 frames on it with WebGPU from its own animation frames, and each frame calls
   managed code synchronously, which runs a `Dispatcher.For` on Stride's thread pool. The page shows what the
   worker drew. The runtime warns when such a thread starts a thread or blocks ("dangerous"): the engine's pool
   should start before the worker of the engine does. The WebGPU calls of this probe are JavaScript.
3. The same through the C bindings the engine uses: the native link with threads (the WebGPU bindings, FreeType),
   and the surface of the device made from the `OffscreenCanvas`.
4. Space Escape with threads, its input forwarded from the page.
5. A heavy scene, with and without threads: whether threads become the default where the page is isolated.
