#!/usr/bin/env sh
# 定位可用的 PowerShell（pwsh / powershell.exe）并执行发布脚本 publish-all.ps1。
# 找不到 PowerShell 或发布失败时以非零码退出，阻断 pre-push。
set -u

# 本脚本位于 <root>/.husky/lib/，上跳两级即仓库根目录。
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

if command -v pwsh >/dev/null 2>&1; then
    RUNNER="pwsh"
elif command -v powershell.exe >/dev/null 2>&1; then
    RUNNER="powershell.exe"
else
    echo "publish: 未找到 PowerShell（需要 pwsh 或 powershell.exe），已中断操作。" >&2
    echo "请安装 PowerShell，或手动运行 $ROOT/scripts/publish-all.ps1。" >&2
    exit 1
fi

"$RUNNER" -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/publish-all.ps1"
