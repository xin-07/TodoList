#!/usr/bin/env sh
# 定位可用的 PowerShell（pwsh / powershell.exe）并执行发布脚本 publish-all.ps1。
#
# 用法: run-publish.sh [strict|weak]   （默认 weak）
#   strict: 找不到 PowerShell 时打印错误并以非零码退出，阻断当前 git 操作（pre-push 使用）。
#   weak:   找不到 PowerShell 时仅打印警告并返回 0，不阻塞 clone/pull 等其它操作（post-checkout/post-merge 使用）。
set -u

MODE="${1:-weak}"

# 本脚本位于 <root>/.husky/lib/，上跳两级即仓库根目录。
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

if command -v pwsh >/dev/null 2>&1; then
    RUNNER="pwsh"
elif command -v powershell.exe >/dev/null 2>&1; then
    RUNNER="powershell.exe"
else
    if [ "$MODE" = "strict" ]; then
        echo "publish: 未找到 PowerShell（需要 pwsh 或 powershell.exe），已中断操作。" >&2
        echo "请安装 PowerShell，或手动运行 $ROOT/scripts/publish-all.ps1。" >&2
        exit 1
    fi
    echo "publish: 未找到 PowerShell（pwsh / powershell.exe），跳过发布。" >&2
    echo "请安装 PowerShell，或手动运行 $ROOT/scripts/publish-all.ps1。" >&2
    exit 0
fi

"$RUNNER" -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/publish-all.ps1"