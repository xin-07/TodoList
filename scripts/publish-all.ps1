<#
    publish-all.ps1 - Publish the app for multiple RIDs into dist/<rid>/.

    Usage (from repository root):
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Rids win-x64
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Configuration Debug -SelfContained:$false

    Notes:
      - Default RIDs: win-x64 / linux-x64 / osx-arm64, all self-contained
        so end users don't need to install the .NET runtime.
      - Platform-specific values (RID list, naming) live here only; the
        main project does not hardcode any RID.
      - Cross-platform: run via `powershell`/`powershell.exe` (Windows)
        or `pwsh` (PowerShell Core, macOS/Linux). The git hooks in /.husky
        detect the available PowerShell automatically.
#>
[CmdletBinding()]
param(
    # Target platforms to publish; override with -Rids
    [string[]]$Rids = @('win-x64', 'linux-x64', 'osx-arm64'),
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

foreach ($rid in $Rids) {
    $out = Join-Path $ProjectRoot (Join-Path $OutputRoot $rid)
    Write-Host ""
    Write-Host "=== Publishing $rid -> $out ===" -ForegroundColor Cyan
    dotnet publish $Csproj -c $Configuration -r $rid --self-contained $SelfContained -o $out
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publishing $rid failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host ""
Write-Host "All publications finished." -ForegroundColor Green
foreach ($rid in $Rids) {
    Write-Host "  $(Join-Path $ProjectRoot (Join-Path $OutputRoot $rid))"
}