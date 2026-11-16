# Compiles the assets of a game for the browser and packages them for its browser head.
# The game project is built already: -Manifest is its asset build manifest (obj/<configuration>/<framework>/<project>.sdbuild)
# and -GameAssembly its assembly. The tools of this checkout (asset compiler, content and shader packagers) are built
# already too: PrepareSampleContent.ps1 builds them.
param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$GameAssembly,
    # Where the packaged content goes: the folder the browser head serves as "content"
    [Parameter(Mandatory)][string]$Output,
    # A folder of the game for the compiled assets and their cache (its obj folder)
    [Parameter(Mandatory)][string]$Work,
    [Parameter(Mandatory)][string]$Naga,
    # The effects the game asked for in a browser run (validate-sample.cjs --record-effects), if recorded
    [string]$EffectRequests,
    # The native WebGPU library (build/wgpu/DownloadNative.ps1): the assets that are rendered when compiled, such
    # as a skybox, need it
    [string]$WebGpuNative,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Game content preparation currently requires Windows desktop asset tools.' }
$checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $checkout 'sources/assets/Stride.AssetCompiler/bin/Release/net11.0/Stride.AssetCompiler.dll'
$packager = Join-Path $PSScriptRoot 'ContentPackageTool/bin/Release/net11.0/ContentPackageTool.dll'
$shaderPackager = Join-Path $checkout 'build/wgpu/ShaderPackageTool/bin/Release/net11.0/ShaderPackageTool.dll'
foreach ($tool in $compiler, $packager, $shaderPackager) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Tool not built: $tool. Run PrepareSampleContent.ps1 for a sample once." }
}
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$assemblyPath = (Resolve-Path -LiteralPath $GameAssembly).Path
$nagaPath = (Resolve-Path -LiteralPath $Naga).Path
$workRoot = [IO.Path]::GetFullPath($Work)
$native = Join-Path $workRoot 'webgpu-native'
$cache = Join-Path $workRoot 'cache'
$staging = Join-Path $workRoot ('browser-' + [Guid]::NewGuid().ToString('N'))
$web = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
$oldNative = $env:STRIDE_WEBGPU_NATIVE_LIBRARY
try {
    if ($WebGpuNative) { $env:STRIDE_WEBGPU_NATIVE_LIBRARY = (Resolve-Path -LiteralPath $WebGpuNative).Path }
    # Loading API-specific assemblies and selecting compiled asset format are separate switches.
    & dotnet $compiler build $manifestPath --graphics-api WebGPU --property=StrideGraphicsApi=WebGPU --compile-property=StrideGraphicsApi=WebGPU --platform Windows --project-configuration $Configuration --disable-auto-compile --output-path $native --build-path $cache
    if ($LASTEXITCODE -ne 0) { throw 'Game asset build failed.' }
    # The effects the first frames of the game ask for. It needs the native WebGPU library; without it, or when the
    # game cannot run in the tool, the recorded requests are all there is.
    $warmUp = Join-Path $workRoot 'warm-up-requests.json'
    # The run is kept while the game, its content and the tool are the same: it is the slow part of the step
    $warmUpKey = Join-Path $workRoot 'warm-up-requests.key'
    $inputs = @($assemblyPath, $packager, $manifestPath) + @(Get-ChildItem -LiteralPath (Join-Path $native 'db/bundles') -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
    $key = ($inputs | ForEach-Object { "$_ $((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash)" }) -join "`n"
    $current = (Test-Path -LiteralPath $warmUp) -and (Test-Path -LiteralPath $warmUpKey) -and ((Get-Content -LiteralPath $warmUpKey -Raw) -eq $key)
    if (-not $current -and (Test-Path -LiteralPath $warmUp)) { Remove-Item -LiteralPath $warmUp }
    if ($current) {
        & dotnet $packager --replay-effects "$native/db" $warmUp $assemblyPath $checkout
        if ($LASTEXITCODE -ne 0) { throw 'The effects of the first frames could not be compiled.' }
    }
    elseif ($env:STRIDE_WEBGPU_NATIVE_LIBRARY) {
        # A game that hangs in the tool must not hang the build
        $run = Start-Process dotnet -ArgumentList @($packager, '--warm-up', $native, $warmUp, $assemblyPath, $checkout) -NoNewWindow -PassThru
        if (-not $run.WaitForExit(180000)) {
            $run.Kill($true)
            Write-Warning 'The game did not finish its first frames in the content tool within 3 minutes: only recorded effect requests are compiled.'
        }
        elseif ($run.ExitCode -ne 0) { Write-Warning 'The game did not run in the content tool: only recorded effect requests are compiled.' }
        elseif (Test-Path -LiteralPath $warmUp) {
            & dotnet $packager --replay-effects "$native/db" $warmUp $assemblyPath $checkout
            if ($LASTEXITCODE -ne 0) { throw 'The effects of the first frames could not be compiled.' }
            Set-Content -LiteralPath $warmUpKey -Value $key -NoNewline
        }
    }
    if ($EffectRequests -and (Test-Path -LiteralPath $EffectRequests)) {
        & dotnet $packager --replay-effects "$native/db" (Resolve-Path -LiteralPath $EffectRequests).Path $assemblyPath $checkout
        if ($LASTEXITCODE -ne 0) { throw 'Exact browser effect request replay failed.' }
    }
    & dotnet $packager --webgpu-cache "$native/db" $staging $nagaPath $shaderPackager
    if ($LASTEXITCODE -ne 0) { throw 'Game WGSL cache preparation failed.' }
    & dotnet $packager $staging $web
    if ($LASTEXITCODE -ne 0) { throw 'Game browser manifest packaging failed.' }
} finally {
    $env:STRIDE_WEBGPU_NATIVE_LIBRARY = $oldNative
    # Only delete this run's private temporary database, never desktop outputs/cache.
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $workRoot -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^browser-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the private content staging directory.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
