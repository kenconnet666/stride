# Compiles the assets of a sample (-Sample Graphics/SpriteFonts) and packages them for Sample.Browser.
param([Parameter(Mandatory)][string]$Sample, [Parameter(Mandatory)][string]$Naga, [string]$EffectRequests)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Sample content preparation currently requires Windows desktop asset tools.' }
$name = Split-Path $Sample -Leaf
$checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$nagaPath = (Resolve-Path -LiteralPath $Naga).Path
$project = Join-Path $PSScriptRoot 'Sample.Game'
$sampleProperty = "-p:Sample=$Sample"
$compiler = Join-Path $checkout 'sources/assets/Stride.AssetCompiler'
$packager = Join-Path $PSScriptRoot 'ContentPackageTool'
$shaderPackager = Join-Path $checkout 'build/wgpu/ShaderPackageTool'
$native = Join-Path $project "obj/$name/content/webgpu-native"
$cache = Join-Path $project "obj/$name/content/cache"
$stagingParent = [IO.Path]::GetFullPath((Join-Path $project "obj/$name/content"))
$staging = Join-Path $stagingParent ('browser-' + [Guid]::NewGuid().ToString('N'))
$web = Join-Path $PSScriptRoot "Sample.Browser/obj/$name/content/web"
# The effects the sample asked for in a browser run, if one was recorded (see validate-sample.cjs)
if (-not $EffectRequests) {
    $recorded = Join-Path $PSScriptRoot "Sample.Browser/effect-requests/$name.json"
    if (Test-Path -LiteralPath $recorded) { $EffectRequests = $recorded }
}
Push-Location $checkout
try {
    $properties = @('-p:StridePlatforms=Windows', '-p:StridePlatform=Windows', '-p:StrideGraphicsApi=WebGPU', '-p:StrideGraphicsApis=WebGPU', '-p:StrideGraphicsApiDependentBuildAll=false', '-p:StrideSkipAutoPack=true', '-p:StrideAssemblyProcessorFramework=net10.0')
    & dotnet build "$compiler/Stride.AssetCompiler.csproj" -m:1 -c Release --nologo -v:minimal @properties -p:TargetFramework=net11.0
    if ($LASTEXITCODE -ne 0) { throw 'Desktop asset compiler build failed.' }
    & dotnet build "$project/Sample.Game.csproj" -m:1 -c Release --nologo -v:minimal @properties $sampleProperty
    if ($LASTEXITCODE -ne 0) { throw 'Desktop sample assembly/asset companion build failed.' }
    # Loading API-specific assemblies and selecting compiled asset format are separate switches.
    & dotnet "$compiler/bin/Release/net11.0/Stride.AssetCompiler.dll" build "$project/obj/$name/Release/net11.0/Sample.Game.sdbuild" --graphics-api WebGPU --property=StrideGraphicsApi=WebGPU --compile-property=StrideGraphicsApi=WebGPU --platform Windows --project-configuration Release --disable-auto-compile --output-path $native --build-path $cache
    if ($LASTEXITCODE -ne 0) { throw 'Sample asset build failed.' }
    & dotnet build "$packager/ContentPackageTool.csproj" -m:1 -c Release --nologo -v:minimal @properties -p:TargetFramework=net11.0
    if ($LASTEXITCODE -ne 0) { throw 'Browser content packager build failed.' }
    & dotnet build "$shaderPackager/ShaderPackageTool.csproj" -m:1 -c Release --nologo -v:minimal @properties
    if ($LASTEXITCODE -ne 0) { throw 'Offline WGSL packager build failed.' }
    if ($EffectRequests) {
        $requestsPath = (Resolve-Path -LiteralPath $EffectRequests).Path
        & dotnet "$packager/bin/Release/net11.0/ContentPackageTool.dll" --replay-effects "$native/db" $requestsPath "$project/bin/$name/Release/net11.0/$name.Game.dll"
        if ($LASTEXITCODE -ne 0) { throw 'Exact browser effect request replay failed.' }
    }
    & dotnet "$packager/bin/Release/net11.0/ContentPackageTool.dll" --webgpu-cache "$native/db" $staging $nagaPath "$shaderPackager/bin/Release/net11.0/ShaderPackageTool.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Sample WGSL cache preparation failed.' }
    & dotnet "$packager/bin/Release/net11.0/ContentPackageTool.dll" $staging $web
    if ($LASTEXITCODE -ne 0) { throw 'Sample browser manifest packaging failed.' }
} finally {
    Pop-Location
    # Only delete this run's private temporary database, never desktop outputs/cache.
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $stagingParent -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^browser-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the private content staging directory.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
