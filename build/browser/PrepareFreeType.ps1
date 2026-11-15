param([ValidateSet('ManagedWebGpuProbe', 'SpaceEscape.Browser')][string]$Project = 'ManagedWebGpuProbe')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This pinned FreeType port preparation currently targets the Windows WASM toolchain.' }
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$source = Join-Path $dotnetRoot 'packs/Microsoft.NET.Runtime.Emscripten.6.0.2.Sdk.win-x64/11.0.0/tools/emscripten/tools/ports/freetype.py'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne '58BA1CD4DCEB38142130CCD13F3B09C42BD6C9C8B5B42C58B1F1C95320C86557') {
    throw 'Pinned SDK FreeType port changed; audit its exception-handling configuration before upgrading.'
}
# The SDK port forwards SUPPORT_LONGJMP but omits WASM_LEGACY_EXCEPTIONS when
# compiling its sources. Its default legacy EH then conflicts with .NET's modern
# Wasm EH at final link. Generate a project-private port with a distinct library
# cache identity and explicit modern EH; leave the installed SDK untouched.
$script = Get-Content -Raw -LiteralPath $source
$script = $script.Substring(0, $script.IndexOf('# SIG # Begin Windows Authenticode signature block', [StringComparison]::Ordinal))
$script = [regex]::Replace($script, '(?m)^variants = .*$', 'variants = {}')
$script = $script.Replace("'libfreetype-legacysjlj.a'", "'libstride-freetype-modern-sjlj.a'").Replace("'libfreetype.a'", "'libstride-freetype-modern.a'")
$script = $script.Replace("'-DFT2_BUILD_LIBRARY',", "'-DFT2_BUILD_LIBRARY',`n      '-sWASM_LEGACY_EXCEPTIONS=0',")
$script = $script.Replace("final, 'freetype', flags=flags", "final, 'stride-freetype-modern', flags=flags")
if (-not $script.Contains("'-sWASM_LEGACY_EXCEPTIONS=0'") -or -not $script.Contains("'libstride-freetype-modern-sjlj.a'")) {
    throw 'FreeType port preparation failed to apply its audited exception-mode/cache changes.'
}
$output = Join-Path $PSScriptRoot "$Project/obj/freetype"
New-Item -ItemType Directory -Path $output -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'stride-freetype.port.py'), $script)
Write-Output 'Prepared project-private FreeType 2.13.3 port with modern Wasm EH and separate cache names.'
