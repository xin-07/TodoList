<#
    publish-all.ps1 - Publish the app for multiple RIDs into dist/<rid>/.

    Usage (from repository root):
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Rids win-x64
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Configuration Debug -SelfContained:$false

    Notes:
      - Default RIDs: win-x64 / osx-arm64 plus Linux per-distro targets
        (ubuntu.22.04-x64 / debian.12-x64 / rhel.9-x64 / alpine.3.18-x64),
        all self-contained so end users don't need to install the .NET
        runtime. Pass -Rids linux-x64 for a generic glibc build covering
        any other glibc distro.
      - The .NET 8+ RID graph ships portable RIDs only (distro RIDs were
        removed), so per-distro artifacts are produced by building the
        matching portable baseline once (linux-x64 / linux-musl-x64) and
        copying it under each distro name. Binaries are byte-identical to
        what a legacy distro-RID build produced and forward-compatible:
        ubuntu.22.04-x64 runs on Ubuntu 24.04+, alpine.3.18-x64 on newer
        Alpine.
      - Platform-specific values (RID list, naming) live here only; the
        main project does not hardcode any RID.
      - The runnable artifact is named TodoList-<rid>[.exe] (e.g.
        TodoList-win-x64.exe) so its OS and architecture are visible in
        the file name itself.
      - Cross-platform: run via `powershell`/`powershell.exe` (Windows)
        or `pwsh` (PowerShell Core, macOS/Linux). The pre-push hook in
        .husky detects the available PowerShell automatically.
#>
[CmdletBinding()]
param(
    # Target platforms to publish; override with -Rids (e.g. -Rids linux-x64
    # for a generic glibc build usable on any other glibc distro)
    [string[]]$Rids = @(
        'win-x64',
        'ubuntu.22.04-x64', 'debian.12-x64', 'rhel.9-x64', 'alpine.3.18-x64',
        'osx-arm64'
    ),
    # Self-contained by default: users don't need the runtime installed
    [switch]$SelfContained = $true,
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'dist'
)

$ErrorActionPreference = 'Stop'

# The script lives under <root>/scripts/, so one level up is the root.
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $ProjectRoot 'TodoList.csproj'

if (-not (Test-Path $Csproj)) {
    throw "TodoList.csproj not found. Run this script from the repository root. Checked: $Csproj"
}

# .NET 8+ removed distro RIDs from the RID graph (NETSDK1083), so distro
# artifacts come from the matching portable baseline build. Runtime packs
# were always keyed on portable RIDs, making a legacy distro-RID build and
# its baseline byte-identical - the per-distro copy only carries the name.
$DistroRidToBaseline = @{
    'ubuntu.22.04-x64' = 'linux-x64'
    'debian.12-x64'    = 'linux-x64'
    'rhel.9-x64'       = 'linux-x64'
    'alpine.3.18-x64'  = 'linux-musl-x64'
}

$StageRoot = Join-Path $ProjectRoot (Join-Path $OutputRoot '.stage')
if (Test-Path $StageRoot) {
    Remove-Item $StageRoot -Recurse -Force
}

# Build every required baseline once, into a staging directory.
$baselines = $Rids |
    ForEach-Object { if ($DistroRidToBaseline.ContainsKey($_)) { $DistroRidToBaseline[$_] } else { $_ } } |
    Select-Object -Unique
foreach ($rid in $baselines) {
    $stage = Join-Path $StageRoot $rid
    Write-Host ""
    Write-Host "=== Publishing baseline $rid -> $stage ===" -ForegroundColor Cyan
    # Name the runnable after the RID (OS + architecture visible in the file name);
    # AssemblyName keeps exe/pdb/deps/runtimeconfig names consistent in every mode.
    dotnet publish $Csproj -c $Configuration -r $rid --self-contained $SelfContained -p:AssemblyName="TodoList-$rid" -o $stage
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publishing baseline $rid failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# Materialize one dist/<rid>/ per requested RID. Files sharing the
# TodoList-<baseline> basename are renamed to TodoList-<rid> as a set, so the
# apphost exe, dll, deps.json and runtimeconfig.json stay consistent.
foreach ($rid in $Rids) {
    $baseline = if ($DistroRidToBaseline.ContainsKey($rid)) { $DistroRidToBaseline[$rid] } else { $rid }
    $out = Join-Path $ProjectRoot (Join-Path $OutputRoot $rid)
    if (Test-Path $out) {
        Remove-Item $out -Recurse -Force
    }
    New-Item -ItemType Directory -Path $out | Out-Null
    Get-ChildItem (Join-Path $StageRoot $baseline) | ForEach-Object {
        $name = $_.Name -replace [regex]::Escape("TodoList-$baseline"), "TodoList-$rid"
        Copy-Item $_.FullName (Join-Path $out $name)
    }
}

Remove-Item $StageRoot -Recurse -Force

Write-Host ""
Write-Host "All publications finished." -ForegroundColor Green
foreach ($rid in $Rids) {
    Write-Host "  $(Join-Path $ProjectRoot (Join-Path $OutputRoot $rid))"
}