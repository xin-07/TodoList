<#
    publish-all.ps1 - Publish the app as portable self-contained single-file artifacts into dist/<rid>/.

    Usage (from repository root):
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Rids win-x64
      powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1 -Configuration Debug -SelfContained:$false

    Notes:
      - Default RIDs: win-x64 / osx-arm64，自包含单文件，终端用户无需安装 .NET 运行时。
      - Linux 不在此产出：Linux 由 scripts/package-linux.ps1 生成 deb / rpm / apk 原生安装包，
        发行版 -> 编译基线的映射只存在于那个脚本里（此处不再重复定义）。
        仍需要通用 Linux 便携产物时显式传入：-Rids linux-x64（glibc）或 linux-musl-x64（musl）。
      - Platform-specific values (RID list, naming) live here only; the main project does not
        hardcode any RID.
      - The runnable artifact is named TodoList-<rid>[.exe] (e.g. TodoList-win-x64.exe) so its
        OS and architecture are visible in the file name itself.
      - 跨平台：Windows 用 powershell / powershell.exe，macOS·Linux 用 pwsh。
        .husky/pre-push 会自动探测可用 PowerShell —— push 前执行的就是本脚本，因此本脚本不得依赖 Docker。
#>
[CmdletBinding()]
param(
    # Target platforms to publish; override with any portable RID
    # (e.g. -Rids linux-x64 for a generic glibc portable build)
    [string[]]$Rids = @(
        'win-x64',
        'osx-arm64'
    ),
    # Self-contained by default: users don't need the runtime installed
    [switch]$SelfContained = $true,
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'dist'
)

$ErrorActionPreference = 'Stop'

# 统一按 UTF-8 解码原生命令（dotnet）的输出：PowerShell 5.1 默认用控制台 ANSI 代码页
# （简体中文 Windows 为 GBK）解码，遇到 UTF-8 输出就会乱码。
# 输出被重定向时（如 git 钩子）没有控制台，改动代码页会失败，故先判断再设置。
if (-not [Console]::IsOutputRedirected) {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
}

# The script lives under <root>/scripts/, so one level up is the root.
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $ProjectRoot 'TodoList.csproj'

if (-not (Test-Path $Csproj)) {
    throw "TodoList.csproj not found. Run this script from the repository root. Checked: $Csproj"
}

foreach ($rid in $Rids) {
    $out = Join-Path $ProjectRoot (Join-Path $OutputRoot $rid)
    if (Test-Path $out) {
        Remove-Item $out -Recurse -Force
    }
    New-Item -ItemType Directory -Path $out | Out-Null

    Write-Host ""
    Write-Host "=== Publishing $rid -> $out ===" -ForegroundColor Cyan
    # Name the runnable after the RID (OS + architecture visible in the file name);
    # AssemblyName keeps exe/pdb/deps/runtimeconfig names consistent in every mode.
    $assemblyArg = "-p:AssemblyName=TodoList-$rid"

    # PowerShell 5.1 在 $ErrorActionPreference='Stop' 下会把原生命令的 stderr 当成终止性错误，
    # 因此这里临时降级为 Continue，由退出码判断成败，保证失败时给出明确信息而不是 NativeCommandError。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet publish $Csproj -c $Configuration -r $rid --self-contained $SelfContained $assemblyArg -o $out 2>&1 |
            ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($exitCode -ne 0) {
        Write-Host "Publishing $rid failed with exit code $exitCode" -ForegroundColor Red
        exit $exitCode
    }
}

Write-Host ""
Write-Host "All publications finished." -ForegroundColor Green
foreach ($rid in $Rids) {
    Write-Host "  $(Join-Path $ProjectRoot (Join-Path $OutputRoot $rid))"
}