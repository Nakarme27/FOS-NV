[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$presetPath = Join-Path $repository "runtime\export_presets.cfg"
$projectPath = Join-Path $repository "runtime\project.godot"
$csprojPath = Join-Path $repository "runtime\OpenNV.csproj"
$logPath = Join-Path $repository "runtime\src\Diagnostics\FosNvLog.cs"
$launcherPath = Join-Path $repository "runtime\src\Presentation\Ui\NativeGodotLauncher.cs"

if (-not (Test-Path -LiteralPath $presetPath -PathType Leaf)) {
    throw "Android export preset is missing: $presetPath"
}
if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
    throw "Godot C# project is missing: $csprojPath"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "FOS NV persistent diagnostic logger is missing: $logPath"
}
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "Godot launcher is missing: $launcherPath"
}

$preset = Get-Content -LiteralPath $presetPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$csproj = Get-Content -LiteralPath $csprojPath -Raw
$log = Get-Content -LiteralPath $logPath -Raw
$launcher = Get-Content -LiteralPath $launcherPath -Raw

$requiredPresetFragments = @(
    'name="Android"',
    'platform="Android"',
    'gradle_build/use_gradle_build=false',
    'architectures/arm64-v8a=true',
    'package/unique_name="org.opennv.fosnv"',
    'command_line/extra_args="-- --launcher"',
    'permissions/manage_external_storage=true',
    'permissions/read_external_storage=true'
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
    'gradle_build/use_gradle_build=true'
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

$requiredLogFragments = @(
    'AndroidDownloadRoot = "/storage/emulated/0/Download"',
    'LogDirectoryName = "FOS NV"',
    'LogFileName = "fosnv.log"',
    'ProbeSelectedGameFolder'
)
foreach ($fragment in $requiredLogFragments) {
    if (-not $log.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Android diagnostics contract is missing: $fragment"
    }
}

$requiredLauncherFragments = @(
    'FosNvLog.ProbeSelectedGameFolder(path)',
    'folder_selection_fail',
    'Diagnostic: Download/FOS NV/fosnv.log'
)
foreach ($fragment in $requiredLauncherFragments) {
    if (-not $launcher.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Launcher folder diagnostics contract is missing: $fragment"
    }
}

if (-not $csproj.Contains('<TargetFramework>net9.0</TargetFramework>', [StringComparison]::Ordinal)) {
    throw "Godot 4.7.2 Android export requires the OpenNV C# project to target net9.0."
}

$incompatibleProjectReferences = @(
    Get-ChildItem -LiteralPath $repository -Recurse -File -Filter "*.csproj" |
        ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw
            if ($content -match '(?i)<ProjectReference\s+Include="[^"]*OpenNV\.csproj"' -and
                $content -notmatch '<TargetFramework>net9\.0</TargetFramework>') {
                [IO.Path]::GetRelativePath($repository, $_.FullName)
            }
        }
)
if ($incompatibleProjectReferences.Count -ne 0) {
    $formatted = ($incompatibleProjectReferences | Sort-Object | ForEach-Object { " - $_" }) -join "`n"
    throw "Projects that reference the net9.0 OpenNV runtime must also target net9.0:`n$formatted"
}

Write-Output "OPENNV_ANDROID_PRODUCT_CONTRACT_PASS launcher=true hardcodedStorage=false gradleTemplate=false arm64=true dotnet=net9.0 broadStorage=true downloadLogs=true folderProbe=true"
