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

## 6. Game Studio in the browser

The Avalonia Game Studio runs in a browser tab from a head of its own, `Stride.GameStudio.Avalonia.Browser`, next to
the desktop head. It shows what works there, and turns off the rest with a "(desktop only)" note
(`DesktopOnlyCommands`, `EditorPluginHost.IsDesktop`).

Steps, each one a usable editor:

1. The shell: the head builds, starts in single-view mode and shows the main view with its menus.
2. A project: open a packaged project (`.sdpkg`, assets and sources in one archive, or a folder picked with the File
   System Access API) into the in-memory file system. Sessions load without MSBuild: the project graph comes from the
   package files, not from `.csproj` evaluation.
3. Assets: the asset view, the property grid, undo and redo, save back to the archive or the folder.
4. The scene editor: its game runs on a WebGPU canvas under the Avalonia view, as the game of a browser head does.
5. Asset compilation in the tab for what has no native tool: materials, scenes, prefabs, sprite sheets, UI pages,
   effects through the shaders compiled offline.
6. Preview: run the game of the project in another tab, from compiled content and a prebuilt game assembly.

Desktop only, by design:

- Building and running the project, debugging, script reload: they need MSBuild and processes.
- Importers that need native tools: FBX and other models (Assimp), video, audio transcoding, texture compression
  with the native compressors.
- Opening files in an IDE or in the file explorer, the launcher, NuGet package restore.
- Watching the disk for changes (`FileSystemWatcher`).

Done: step 1. The head builds and shows the menu bar of the main view in Chrome. It pins SkiaSharp 4.156.1 and
HarfBuzzSharp 14.4.0.101: the versions Avalonia 12 depends on have no WebAssembly library for the Emscripten of
.NET 11. The code editor has no syntax highlighting there: Onigwrap ships no library for that Emscripten either, and
needs a build of its own.

Known blockers: 340 call sites that the analyzer marks as unsupported in a browser, most of them `Process` (232) and
`FileSystemWatcher`; blocking waits on tasks, which a runtime with one thread cannot do; the native libraries the
asset compiler loads.
