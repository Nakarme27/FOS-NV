[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$presetPath = Join-Path $repository "runtime\export_presets.cfg"
$projectPath = Join-Path $repository "runtime\project.godot"
$csprojPath = Join-Path $repository "runtime\OpenNV.csproj"

if (-not (Test-Path -LiteralPath $presetPath -PathType Leaf)) {
    throw "Android export preset is missing: $presetPath"
}
if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
    throw "Godot C# project is missing: $csprojPath"
}

$preset = Get-Content -LiteralPath $presetPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$csproj = Get-Content -LiteralPath $csprojPath -Raw

$requiredPresetFragments = @(
    'name="Android"',
    'platform="Android"',
    'gradle_build/use_gradle_build=false',
    'architectures/arm64-v8a=true',
    'package/unique_name="org.opennv.fosnv"',
    'command_line/extra_args="-- --launcher"',
    'permissions/read_external_storage=false'
)
foreach ($fragment in $requiredPresetFragments) {
    if (-not $preset.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Android product contract is missing: $fragment"
    }
}

$forbiddenPresetFragments = @(
    '/sdcard/Android/data/',
    '--data-root',
    '--save-path',
    'gradle_build/use_gradle_build=true',
    'permissions/read_external_storage=true'
)
foreach ($fragment in $forbiddenPresetFragments) {
    if ($preset.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Android product contract must not hard-code startup storage: $fragment"
    }
}

$requiredProjectFragments = @(
    'renderer/rendering_method.mobile="gl_compatibility"',
    'textures/vram_compression/import_etc2_astc=true'
)
foreach ($fragment in $requiredProjectFragments) {
    if (-not $project.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Android runtime project setting is missing: $fragment"
    }
}

if (-not $csproj.Contains('<TargetFramework>net9.0</TargetFramework>', [StringComparison]::Ordinal)) {
    throw "Godot 4.7.2 Android export requires the OpenNV C# project to target net9.0."
}

Write-Output "OPENNV_ANDROID_PRODUCT_CONTRACT_PASS launcher=true hardcodedStorage=false gradleTemplate=false arm64=true dotnet=net9.0"
