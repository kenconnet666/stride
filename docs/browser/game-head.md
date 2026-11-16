# A game with a browser head

A game runs in a browser from a project of its own, next to its Windows head, with the engine referenced as packages.
`dotnet run` in that project compiles the game, compiles its assets for WebGPU, and serves the page.

This works today against the dev-redirect packages of an engine checkout (see
[Running a Sample Against Your Local Build](../build/README.md#running-a-sample-against-your-local-build)). It was
tried with these samples, each copied to a folder outside the repository: SpriteFonts, GameMenu, UIParticles,
UIElementLink, TouchInputs, AnimatedModel, MorphTargets, ParticlesSample, MaterialShader, CustomEffect and
SimpleAudio.

## The projects

```
MyGame/
  nuget.config             the feed of the redirect packages
  Assets/ Resources/
  MyGame.Game/             the game: a library, with the Stride packages
  MyGame.Windows/
  MyGame.Browser/          the browser head
```

`nuget.config` adds the feed the engine builds deploy to (`%LocalAppData%\stride\nugetdev`) and maps `Stride` and
`Stride.*` to it.

`MyGame.Browser/MyGame.Browser.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <TargetFramework>net11.0-browser</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <WasmEnableHotReload>false</WasmEnableHotReload>
    <StridePlatform>Browser</StridePlatform>
    <WasmBuildNative>true</WasmBuildNative>
    <WasmAllowUndefinedSymbols Condition="'$(_IsPublishing)' != 'true'">true</WasmAllowUndefinedSymbols>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyGame.Game\MyGame.Game.csproj" />
    <TrimmerRootAssembly Include="MyGame.Game" />
    <AssemblyMetadata Include="Sample" Value="MyGame" />
  </ItemGroup>
  <Import Project="$(StrideDevRoot)\build\browser\Stride.Browser.Game.targets" Condition="'$(StrideDevRoot)' != ''" />
</Project>
```

[Stride.Browser.Game.targets](../../build/browser/Stride.Browser.Game.targets) gives the head what the packages do not
carry yet: the host of the page, the native libraries, and the content. It writes the page and the compiled content
into the `wwwroot` folder of the head, which `dotnet run` serves: keep that folder out of the game's repository.

## Run

```bash
dotnet run -c Release
```

in `MyGame.Browser`, then open the address it prints. A build with no change takes about 30 seconds; the first one
links the native libraries and takes longer.

A game asks the engine for effects at run time, and the browser cannot compile them. Record them with
`validate-sample.cjs --record-effects`, put the file next to the head as `effect-requests.json`, and build again.
A run stops at the first effect it does not find, so a game with post effects needs several rounds of this.

The build compiles the recorded effects with the game assembly loaded. It takes the other assemblies of the game
from the engine checkout, each from the output folder of the project of its name.

## What the packages lack for the browser

Found while making this work, each with its workaround in the targets file:

- The packages are packed for the desktop only. Their dependencies bring the SDL bindings, which the P/Invoke scan of
  the WebAssembly build rejects: the scan leaves that assembly out.
- `Stride.Graphics` depends on `Stride.Graphics.WebGPU.Bindings` for the browser, and the build deploys no package
  of it. Pack it into the feed once:
  `dotnet pack sources/engine/Stride.Graphics.WebGPU.Bindings -c Release -p:IsPackable=true -o %LocalAppData%\stride
ugetdev`.
- A game that references `Stride.Audio` links the native audio library of the engine from a port in the checkout
  (`build/browser/Shared/stride-audio.port.py`), over the OpenAL of Emscripten. The packages of `Stride.Audio` come
  from building `Stride.Audio` and `Stride.Audio.Assets` in the checkout.
- The symbols of an assembly that a redirect package replaces keep the destination name of the assembly, and take its
  place in the WebAssembly bundle: the head renames them.
- The native ports (WebGPU, FreeType), the host of the page and the content tools are in the checkout, not in a
  package.
- `Stride.AssetCompiler` does not pack in this checkout (its package asks for a file its build does not produce), so
  the game project here does not reference it: its Windows head cannot compile assets.
- A native link is not redone when only the table of native calls changed: after a failed build, delete
  `obj/<configuration>/net11.0-browser/wasm` of the head.
