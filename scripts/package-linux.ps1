<#
    package-linux.ps1 - 用 Docker + fpm 为每个目标发行版生成原生安装包（deb / rpm / apk）。

    Usage (from repository root):
      powershell -ExecutionPolicy Bypass -File scripts/package-linux.ps1
      powershell -ExecutionPolicy Bypass -File scripts/package-linux.ps1 -Targets ubuntu.22.04-x64,debian.12-x64

    Notes:
      - 前置条件：Docker 已安装且**正在运行**（Docker Desktop / dockerd）。
        脚本不做静默降级：docker 不可用时直接报错退出。
      - 每个目标发行版独立出一份包，包内依赖集按该发行版声明；包名统一为 todolist，
        因此 dpkg -r todolist / dnf remove todolist / apk del todolist 通用，发行版只体现在文件名上。
      - 安装布局：/opt/<InstallDirName>/TodoList 为载体，/usr/bin/todolist 为启动器，
        桌面入口与图标进 /usr/share。数据不在安装目录内，落用户级目录（$XDG_DATA_HOME/todolist）。
      - 产物落 dist/linux/<目标>/；不打包 .pdb。
      - 本脚本不进 .husky/pre-push：push 不应强依赖 Docker，由发布者显式执行。
      - 目标矩阵、依赖集、包元数据、安装路径只在本文件定义一处（打包相关值的单一权威源）。
#>
[CmdletBinding()]
param(
    # 目标发行版；可只打其中几个
    [string[]]$Targets = @(
        'ubuntu.22.04-x64', 'debian.12-x64', 'rhel.9-x64', 'alpine.3.18-x64'
    ),
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'dist'
)

$ErrorActionPreference = 'Stop'

# 统一按 UTF-8 解码原生命令（dotnet / docker）的输出：PowerShell 5.1 默认用控制台 ANSI 代码页
# （简体中文 Windows 为 GBK）解码，遇到 UTF-8 输出就会乱码。
# 输出被重定向时（如 git 钩子）没有控制台，改动代码页会失败，故先判断再设置。
if (-not [Console]::IsOutputRedirected) {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
}

# 脚本位于 <root>/scripts/，上跳一级即仓库根。
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $ProjectRoot 'TodoList.csproj'
if (-not (Test-Path $Csproj)) {
    throw "TodoList.csproj not found. Run this script from the repository root. Checked: $Csproj"
}

# ---- 包元数据（单一权威源）----
# 版本号不在此定义：权威源是 TodoList.csproj 的 <Version>，运行时读取。
$PackageName = 'todolist'
$Maintainer = 'xin-07 <2074835619@qq.com>'
$Vendor = 'xin-07'
$License = 'Proprietary'
$Homepage = 'https://github.com/xin-07/TodoList'
# 描述经命令行传给 fpm，限 ASCII 以免宿主代码页造成乱码；面向用户的菜单文案在 packaging/todolist.desktop。
$Description = 'TodoList - lightweight to-do list desktop app (Avalonia + SQLite)'
$Category = 'utils'
$InstallDirName = 'todolist'   # 程序目录：/opt/<InstallDirName>
$LauncherName = 'todolist'     # 启动器：/usr/bin/<LauncherName>
$ImageTag = 'todolist-fpm:local'

# ---- 目标矩阵：发行版 -> 包格式 / 编译基线 / 架构 / 依赖集 ----
# 自包含发布不需要 .NET 运行时，但依赖系统 ICU、C++ 运行库与 X11 栈
# （Avalonia 的 X11 后端为运行时 dlopen，ldd 不可见，必须显式声明）。
$CommonDebDeps = @(
    'libstdc++6', 'zlib1g',
    'libx11-6', 'libxext6', 'libxrender1', 'libxrandr2', 'libxi6', 'libxcursor1',
    'libice6', 'libsm6', 'libfontconfig1', 'libfreetype6'
)
$CommonRpmDeps = @(
    'libstdc++', 'zlib',
    'libX11', 'libXext', 'libXrender', 'libXrandr', 'libXi', 'libXcursor',
    'libICE', 'libSM', 'fontconfig', 'freetype'
)
$CommonApkDeps = @(
    'libstdc++', 'zlib',
    'libx11', 'libxext', 'libxrender', 'libxrandr', 'libxi', 'libxcursor',
    'libice', 'libsm', 'fontconfig', 'freetype'
)

$TargetMatrix = @(
    @{ Name = 'ubuntu.22.04-x64'; Format = 'deb'; Baseline = 'linux-x64'; Arch = 'amd64'; Iteration = '1'; Deps = @('libicu70') + $CommonDebDeps },
    @{ Name = 'debian.12-x64'; Format = 'deb'; Baseline = 'linux-x64'; Arch = 'amd64'; Iteration = '1'; Deps = @('libicu72') + $CommonDebDeps },
    @{ Name = 'rhel.9-x64'; Format = 'rpm'; Baseline = 'linux-x64'; Arch = 'x86_64'; Iteration = '1'; Deps = @('libicu') + $CommonRpmDeps },
    @{ Name = 'alpine.3.18-x64'; Format = 'apk'; Baseline = 'linux-musl-x64'; Arch = 'x86_64'; Iteration = '0'; Deps = @('icu-libs') + $CommonApkDeps }
)

function Convert-ToDockerPath {
    param([Parameter(Mandatory)][string]$Path)
    return ($Path -replace '\\', '/')
}

# 执行原生命令，返回退出码与捕获的合并输出。
# 捕获时必须临时放开 $ErrorActionPreference：PowerShell 5.1 在 Stop 下会把原生命令写到 stderr 的内容
# 当成终止性错误（NativeCommandError），使"判断退出码 + 给出友好报错"永远走不到。
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$EchoOutput
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $FileName @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($EchoOutput) {
        $output | ForEach-Object { Write-Host $_ }
    }

    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Invoke-DockerStep {
    param(
        [Parameter(Mandatory)][string[]]$DockerArgs,
        [Parameter(Mandatory)][string]$Step
    )

    $result = Invoke-Native -FileName 'docker' -Arguments $DockerArgs -EchoOutput
    if ($result.ExitCode -ne 0) {
        throw "步骤失败（$Step）：docker $($DockerArgs -join ' ') 退出码 $($result.ExitCode)"
    }
}

# 以 LF + 无 BOM 写文本文件：CRLF 会破坏 /bin/sh 的 shebang，BOM 会让内核无法识别 shebang。
function Copy-AsLfFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )
    $text = [System.IO.File]::ReadAllText($Source) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Destination, $text, [System.Text.UTF8Encoding]::new($false))
}

# ---- 0. 前置检查：Docker 必须可用 ----
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker 不可用：未找到 docker 命令，请先安装 Docker Desktop。'
}

$dockerInfo = Invoke-Native -FileName 'docker' -Arguments @('info')
if ($dockerInfo.ExitCode -ne 0) {
    $dockerInfo.Output | ForEach-Object { Write-Host $_ } -ForegroundColor DarkGray
    throw 'Docker 不可用：请先启动 Docker Desktop（或 dockerd），再重跑本脚本。'
}

# ---- 1. 版本号：唯一权威源是 csproj 的 <Version> ----
$versionProbe = Invoke-Native -FileName 'dotnet' -Arguments @('msbuild', $Csproj, '-getProperty:Version', '-nologo')
if ($versionProbe.ExitCode -ne 0) {
    $versionProbe.Output | ForEach-Object { Write-Host $_ }
    throw "读取版本号失败：dotnet msbuild -getProperty:Version 退出码 $($versionProbe.ExitCode)"
}
$Version = ($versionProbe.Output |
    Where-Object { $_ -is [string] -and -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw '无法从 TodoList.csproj 读取 <Version>（版本号的唯一权威源）。'
}
Write-Host "版本：$Version" -ForegroundColor Cyan

# ---- 2. 校验目标 ----
$unknown = @($Targets | Where-Object { $TargetMatrix.Name -notcontains $_ })
if ($unknown.Count -gt 0) {
    throw "未知目标：$($unknown -join ', ')。可选：$($TargetMatrix.Name -join ', ')"
}
$selected = @($TargetMatrix | Where-Object { $Targets -contains $_.Name })

# ---- 3. 打包镜像（工具链唯一权威源：packaging/Dockerfile）----
# 每次都走 docker build（命中缓存时近乎瞬时）：若只按"镜像是否存在"跳过构建，
# Dockerfile 改了却拿到旧镜像，会得到难以定位的打包结果差异。
Write-Host "准备打包镜像 $ImageTag ..." -ForegroundColor Cyan
Invoke-DockerStep -Step 'build image' -DockerArgs @(
    'build', '-t', $ImageTag, (Convert-ToDockerPath (Join-Path $ProjectRoot 'packaging'))
)

# ---- 4. 编译基线与组装包内文件树 ----
$StageRoot = Join-Path $ProjectRoot (Join-Path $OutputRoot '.stage')
if (Test-Path $StageRoot) {
    Remove-Item $StageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $StageRoot | Out-Null

foreach ($baseline in @($selected | ForEach-Object { $_.Baseline } | Select-Object -Unique)) {
    $stage = Join-Path $StageRoot "baseline-$baseline"
    Write-Host ""
    Write-Host "=== 编译基线 $baseline -> $stage ===" -ForegroundColor Cyan
    # 运行文件按 RID 命名，使 OS 与架构体现在文件名上；AssemblyName 保证 exe/pdb/deps/runtimeconfig 名称一致。
    $assemblyArg = "-p:AssemblyName=TodoList-$baseline"
    $publish = Invoke-Native -FileName 'dotnet' -Arguments @(
        'publish', $Csproj, '-c', $Configuration, '-r', $baseline,
        '--self-contained', 'true', $assemblyArg, '-o', $stage
    ) -EchoOutput
    if ($publish.ExitCode -ne 0) {
        throw "编译基线 $baseline 失败，退出码 $($publish.ExitCode)"
    }
}

foreach ($target in $selected) {
    $tree = Join-Path $StageRoot "pkg-$($target.Name)"
    $appDir = Join-Path $tree "opt/$InstallDirName"
    $binDir = Join-Path $tree 'usr/bin'
    $desktopDir = Join-Path $tree 'usr/share/applications'
    $iconDir = Join-Path $tree 'usr/share/icons/hicolor/256x256/apps'
    foreach ($dir in @($appDir, $binDir, $desktopDir, $iconDir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    # 主程序在包内身份固定为 TodoList（发行版/架构只体现在包名与文件名上，不再进入内部命名）。
    Copy-Item (Join-Path $StageRoot "baseline-$($target.Baseline)/TodoList-$($target.Baseline)") (Join-Path $appDir 'TodoList')
    # config.json 随包安装：运行时由程序根读取，是数据目录定义的权威文件。
    Copy-Item (Join-Path $ProjectRoot 'config.json') (Join-Path $appDir 'config.json')
    Copy-AsLfFile (Join-Path $ProjectRoot 'packaging/todolist') (Join-Path $binDir $LauncherName)
    Copy-Item (Join-Path $ProjectRoot 'packaging/todolist.desktop') (Join-Path $desktopDir 'todolist.desktop')
    Copy-Item (Join-Path $ProjectRoot 'Assets/app.png') (Join-Path $iconDir 'todolist.png')
}

# rpm 的卸载脚本供 fpm 读取，放在 fpm 输入树（pkg-<目标>）之外，因此不会进入包内容。
if ($selected.Format -contains 'rpm') {
    Copy-AsLfFile (Join-Path $ProjectRoot 'packaging/postremove.sh') (Join-Path $StageRoot 'postremove.sh')
}

# ---- 5. 逐目标打包 ----
foreach ($target in $selected) {
    $outDir = Join-Path $ProjectRoot (Join-Path $OutputRoot "linux/$($target.Name)")
    if (Test-Path $outDir) {
        Remove-Item $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outDir | Out-Null

    $fileName = switch ($target.Format) {
        'deb' { "TodoList-$($target.Name)_${Version}_$($target.Arch).deb" }
        'rpm' { "TodoList-$($target.Name)-${Version}-$($target.Iteration).$($target.Arch).rpm" }
        'apk' { "TodoList-$($target.Name)-${Version}-r$($target.Iteration).apk" }
    }

    $depends = ($target.Deps | ForEach-Object { "--depends '$_'" }) -join ' '
    # rpm 的文件清单只含文件、不含目录项，卸载后会残留空目录 /opt/<InstallDirName>，
    # 用 %postun 补一次 rmdir；deb/apk 自身会移除目录，故只对 rpm 挂这个脚本。
    $afterRemove = if ($target.Format -eq 'rpm') { "--after-remove '/stage/postremove.sh'" } else { '' }
    $treeInContainer = "/stage/pkg-$($target.Name)"
    $fpmCommand = @(
        "fpm -s dir -t $($target.Format)",
        "-C $treeInContainer",
        "-n $PackageName",
        "-v $Version",
        "--iteration $($target.Iteration)",
        "--architecture $($target.Arch)",
        "--category $Category",
        "--maintainer '$Maintainer'",
        "--vendor '$Vendor'",
        "--license '$License'",
        "--url '$Homepage'",
        "--description '$Description'",
        $depends,
        $afterRemove,
        "--package '/out/$fileName'",
        './opt ./usr'
    ) -join ' '

    # Windows 挂载卷不保留 POSIX 权限（一律 0777），而 fpm 会照抄源文件模式，
    # 因此先归一化整棵树（目录 0755 / 文件 0644），再给两个可执行文件单独加执行位。
    $normalizeModes = @(
        "find $treeInContainer -type d -exec chmod 0755 {} +",
        "find $treeInContainer -type f -exec chmod 0644 {} +",
        "chmod 0755 $treeInContainer/opt/$InstallDirName/TodoList $treeInContainer/usr/bin/$LauncherName"
    ) -join ' && '
    $shellCommand = "$normalizeModes && $fpmCommand"

    Write-Host ""
    Write-Host "=== 打包 $($target.Name)（$($target.Format)）-> $outDir ===" -ForegroundColor Cyan
    Invoke-DockerStep -Step "package $($target.Name)" -DockerArgs @(
        'run', '--rm',
        '-v', "$(Convert-ToDockerPath $StageRoot):/stage",
        '-v', "$(Convert-ToDockerPath $outDir):/out",
        $ImageTag, 'sh', '-c', $shellCommand
    )

    $artifact = Join-Path $outDir $fileName
    if (-not (Test-Path $artifact)) {
        throw "fpm 报告成功但未找到产物：$artifact"
    }
}

# ---- 6. 清理暂存并汇报产物 ----
Remove-Item $StageRoot -Recurse -Force

Write-Host ""
Write-Host "All Linux packages finished." -ForegroundColor Green
foreach ($target in $selected) {
    $outDir = Join-Path $ProjectRoot (Join-Path $OutputRoot "linux/$($target.Name)")
    Get-ChildItem $outDir | ForEach-Object {
        Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB))
    }
}