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
- Physics in the browser is BepuPhysics (`Stride.BepuPhysics`), not Bullet (decided 2026-11-16). Bullet would need a
  rebuilt BulletSharp: its callbacks to managed code are instance delegates, which the .NET WebAssembly runtime cannot
  call. Bepu is managed and builds for the browser. A game that uses `Stride.Physics` (every template, the
  tutorials, JumpyJet) needs a Bepu version to run there; the build of a browser head warns about it
  (`STRIDEWEB001`). The templates and samples on Bullet are converted to Bepu in place, not kept twice (decided
  2026-11-16). PhysicsSample is ported too, though BepuSample also shows Bepu's constraints; the C# Intermediate
  tutorial is ported, and the changes of its pages in stride-docs are drafted on the fork of stride-docs for review
  (decided 2026-11-16).

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
- A game in a folder of its own, with the engine as packages and a browser head, runs with `dotnet run`
  ([game-head.md](game-head.md)). Run this way: the samples above, UIParticles, UIElementLink, TouchInputs,
  MorphTargets (compute deformation), CustomEffect and SimpleAudio.
- Audio: the native audio library of the engine over the OpenAL of Emscripten (Web Audio). SimpleAudio plays its
  music (CELT) and its effect.

## 1. Make the baseline dependable

1. Check text input with a real input method, on desktop and on a phone. Then: copy to the clipboard, and the
   candidate window at the caret instead of the last pointer position.
2. Release builds need AOT: the interpreter is about 30 times slower in a matrix loop. The Mono AOT compiler of
   .NET 11.0.0 asserts (`sgen-alloc.c:409`) on any assembly with a P/Invoke that has
   `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]`; one such method is enough. The WebGPU bindings use
   `DllImport` with `CallingConvention.Cdecl` instead. `VTuneProfiler` (Stride.Core) and `Spv2DXIL`
   (Stride.Shaders.Compilers) have the attribute too: trimming removes them from a game, an untrimmed build still
   fails. This is dotnet/runtime#136351: fixed for .NET 12, and its backport (dotnet/runtime#136627) is approved for
   a .NET 11 servicing release. When that release is out, the workaround in the bindings generator can go.
   A trimmed Release publish with `RunAOTCompilation=true` passes this assertion, and stops on another one in
   BepuPhysics: Mono's LLVM gives a comparison of `Vector<float>` the type of its operands, not of its `Vector<int>`
   mask (dotnet/runtime#136319, wrong results, then `LLVMTypeOf (lhs) == LLVMTypeOf (rhs)`). A fix for
   release/11.0 is dotnet/runtime#137615 (Mono left main's build in November 2026). Until it ships, AOT
   builds use `WasmEnableSIMD=false`. With AOT and without SIMD, against interpreted: First Person Shooter 13.4 ms a
   frame against about 170 ms, JumpyJet 3.5 ms against 9.3 ms. `dotnet.native.wasm` is about 35 MB (7.4 MB with
   brotli). A Release publish of a game head uses AOT ([game-head.md](game-head.md#publish)). A publish of First
   Person Shooter after an engine change took 633 s: content 129 s (now skipped when it cannot change), bitcode
   149 s (`aot-instances` alone about 2 minutes, compiled again after any change), compression 100 s and link 99 s
   (both cut by `StrideBrowserLocalPublish`), Mono AOT 91 s (only the assemblies that changed). The interpreter
   cannot replace it: with the jiterpreter on, First Person Shooter takes 64 to 170 ms a frame.
3. Keyboard lane changes in Space Escape were not confirmed, and a page without focus pauses the game. Decide what a
   page without focus should do, and test the keyboard by hand.
4. A CI lane on the fork: build the browser pages and run `build/browser/validate-*.cjs` in headless Chrome.
5. Review the WebGPU backend (about 4,700 lines, 80 "not supported" paths) against the Vulkan backend, file by file,
   to the standards of [AGENTS.md](../../AGENTS.md). `Stride.Graphics.Tests` on desktop WebGPU (`-p:StrideGraphicsApisTest=WebGPU`,
   on WARP): 157 pass, 34 skip, 13 fail. Ten are fixed since (shared texture options, and a debug group popped with a
   render pass open, which also left the frame's command list closed). Left:
   - Three image comparisons (geometric primitives, sprite batch to texture, static sprite font) have no WebGPU gold
     image yet: the gold images come from CI.

## 2. Make it part of the engine

1. Effects: today each page records the effects it asked for in the browser, and a tool replays that list offline.
   The asset compiler should compile every effect of a game for WebGPU, as it does for the other graphics APIs.
2. Embedded effects: WebGPU bytecodes from `EmbeddedEffects.yaml`, built or committed like the other APIs, instead of a
   script that fills `obj/`.
3. One browser host instead of a `Program.cs` with the same exports in every page: `Stride.Engine.BrowserGame`,
   with its page script in the assembly. The sample host is on it; Space Escape and the probes still have their own.
4. A Browser platform in the build: `StridePlatforms=Browser` for a game project, a project template, `dotnet publish`.
5. Content: load on demand over HTTP with a cache, instead of downloading everything before the game starts.

## 3. Cover the engine

Samples still to run: the C# Beginner and C# Intermediate tutorials. Interpreted, C# Beginner creates its graphics
device after 46 s and draws its scene after about 150 s (First Person Shooter with AOT: 3.4 s and 10 s), and one
effect it asks for is missing: they are checked with AOT.

Bullet to Bepu, in place (see Decisions), with `build/browser/BulletToBepu` and the scripts ported by hand:

- Converted: First Person Shooter and the prototyping blocks, Third Person Platformer, Platformer 2D, VR Sandbox, the
  C# Beginner tutorial, JumpyJet, GravitySensor, Top Down RPG, SpriteStudioDemo, PhysicsSample, C# Intermediate. All
  compile. Run in Chrome (interpreted): First Person Shooter (about 170 ms a frame), Third Person Platformer (230 ms),
  Top Down RPG (115 ms), JumpyJet (9 ms), GravitySensor (9 ms), PhysicsSample (9 ms), BepuSample (26 to 41 ms),
  Platformer 2D (20 to 31 ms), SpriteStudioDemo (13 to 18 ms). First Person Shooter with AOT: about 10 ms, while
  walking and turning without a wrong frame.
- Navigation: the converter moves Bullet's navigation to `Stride.BepuPhysics.Navigation` (Recast). Bepu builds the
  mesh at run time from the static colliders (`RecastMeshSystem.RebuildNavMesh`, called by the game), so the mesh
  assets go away; their collision groups become `BuildSettings.IncludedLayers`. Top Down RPG builds it at start and
  again when a chunk streams in or out.
- A kinematic body touches nothing static in Bepu (contacts need a dynamic body): Top Down RPG's punch became a
  dynamic trigger without gravity, JumpyJet's collisions overlap queries (two kinematic bodies).
- GravitySensor: the page gives Stride's accelerometer, user acceleration and gravity sensors from `devicemotion`.
  Not yet tried on a phone. iOS asks the user first (`DeviceMotionEvent.requestPermission`, from a tap): no page
  asks yet.
- PhysicsSample: ported. Its constraints scene shows Bepu's ball socket, hinge, angular axis gear motor, slider
  (point on line servo and linear axis limit), cone twist (ball socket, swing and twist limits) and weld. A Bullet
  body with the convex hull of a model gets a Bepu hull asset from the converter.
- C# Intermediate tutorial: ported. Its pages in stride-docs include the code of the samples and explain it; the
  draft of their changes is the branch `csharp-intermediate-bepu` of the fork kenconnet666/stride-docs, for review.
  The videos still show Bullet: the pages say so.
- SpriteStudioDemo: ported (bullets are dynamic triggers, enemies lock their rotation with an infinite inertia).

A sample asks for the effects of the engine at run time, and the browser cannot compile them. The content step runs
the game first without a window on desktop WebGPU (the warm-up of `PrepareGameContent.ps1`) and compiles the effects
its first frames ask for; a run in the browser records the others (`validate-sample.cjs --record-effects`), and the
content is prepared again with them. An effect that only shows later in the game (a menu that opens, an explosion)
is recorded only if a run gets there.

- Typed buffers (`Buffer<>`, clustered lighting): WebGPU has none, so a WebGPU build compiles them as storage buffers
  (`STRIDE_WEBGPU_TYPED_BUFFERS`); a typed buffer view must then have the size of the element the shader reads. Not yet
  tried in a sample with point or spot lights.
- No geometry or tessellation shaders: voxel GI, the wireframe and debug line shaders and the cube map camera need
  another path, or stay off.
- Shadows, post effects, skinning, particles.
- Physics with BepuPhysics on one thread: it builds for the browser and has dev packages.
- A game on Bullet physics: `build/browser/BulletToBepu` converts its scenes, prefabs, game settings and package
  references to Bepu, and lists what it cannot (scripts, triggers, filters, restitution, hulls and meshes). Bullet
  scales a shape with its entity and Bepu does not: the converter puts the scale into the sizes. The scripts are
  ported by hand; the two APIs differ (`Raycast` and `HitResult` against `RayCast` and `HitInfo`, `SetVelocity`
  against `MoveVector`).
- Gamepads: a gamepad of the Gamepad API in the "standard" layout is a Stride gamepad. No vibration.
- A pass that reads back from the GPU cannot block: the image readback and the mesh deformation have a path without
  it. Others (picking, screenshots, `GetData`) still throw.
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
- Report to Dawn: in `emdawnwebgpu`, when `wgpuBufferUnmap` aborts a pending map, the rejection handler of the
  JavaScript `mapAsync` deletes `bufferOnUnmaps[buffer]` a microtask later. A new map of the same buffer in between
  loses its state, and `wgpuBufferGetConstMappedRange` then throws. The engine waits for the next frame before it maps
  such a buffer again (`readsAbortedThisFrame`).

## 6. Game Studio and the browser

Games for the browser are made in the desktop Game Studio. A Game Studio that runs in a browser tab cannot compile
scripts, import models or build a game (no MSBuild, no processes, no native tools), so it is not a goal to make it
equal to the desktop one.

The desktop Game Studio:

1. Browser is a platform of a project, next to Windows, Linux and the others: the project gets a browser head, and
   Build, Run and Package work for it. Run opens the game in the default browser from a local server.
   Done: the new-game templates can add a browser head, and Start project serves it with `dotnet run` and opens it
   (`BrowserGameDeployer`). Left: Package (a publish, with `StrideBrowserLocalPublish` to play it on this machine).
2. The editor says what the browser does not support, where the user chooses it: Bullet physics, video, VR, the
   effects that need geometry or tessellation shaders, and the graphics profiles above what WebGPU gives.
   Done for components: an assembly with `[UnsupportedOSPlatform("browser", reason)]` (Stride.Physics, Stride.Video)
   gives its components the reason, as a tooltip in the property grid and next to the name in Add component (WPF),
   when the session has a browser head. Left: tessellation and geometry shaders in materials, graphics profiles, VR
   settings, and the Add component control of the Avalonia editor, which is not written yet.
3. Assets compile for WebGPU from the asset compiler (see "Make it part of the engine"), not from a replay of the
   effects a browser run asked for.

The browser head, `Stride.GameStudio.Avalonia.Browser`, stays: it shows that the Avalonia editor builds and starts in
a browser, and keeps the editor's code free of desktop-only assumptions (the same code paths serve a phone or a
tablet). It is kept building and starting; its features are not a goal.

- It pins SkiaSharp 4.156.1 and HarfBuzzSharp 14.4.0.101: the versions Avalonia 12 depends on have no WebAssembly
  library for the Emscripten of .NET 11.
- Dialogs: `ViewDialog` shows a dialog inside the view where there are no windows. The message boxes, the pickers,
  About and the progress dialog use it; the other dialogs are windows only, and their commands are off.
- A command that needs the desktop is off there, and its title says so (`DesktopOnlyCommands`).
- `?project=<address of a .zip>` downloads a project and loads it without MSBuild, as a library without its
  dependencies. The package and its assets load (Space Escape does); the session then does not finish opening: the
  progress dialog stays on "Package processed". Not looked into.
- The head ships the MSBuild assemblies: the asset libraries name MSBuild types in fields and closures, which must
  load even though nothing runs them.
- The code editor has no syntax highlighting: Onigwrap ships no library for that Emscripten.

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
3. Done in the probe. It links the C bindings of the engine with threads (the WebGPU port takes its shared memory
   setting from the link) and, on the worker: creates a device through `WebGpuDeviceContext`, writes a buffer and
   reads it back asynchronously, makes the surface of the canvas it was given, and clears it each frame; the page
   shows the color. Emscripten looks a canvas up by name in `specialHTMLTargets` on a thread without a document:
   the application exports it (`EmccExportedRuntimeMethod`) and the worker registers its canvas there as
   `#canvas`. Not done: FreeType with threads, and the engine's own device and presenter on the worker.
4. Space Escape with threads, its input forwarded from the page.
5. A heavy scene, with and without threads: whether threads become the default where the page is isolated.
