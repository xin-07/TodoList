#!/usr/bin/env node
/**
 * commit-msg 钩子检查器。
 * 遵守《AI Collaboration Constitution》Article 1：
 * 代码变更的 commit 必须含 Why/What，且根因三选一（design/code/test wrong）。
 *
 * 用法：node scripts/check-commit-msg.js <commit-message-file>
 */
const fs = require("fs");

const file = process.argv[2];
if (!file) {
  console.error("[check-commit-msg] 缺少 commit message 文件参数");
  process.exit(1);
}

const msg = fs.readFileSync(file, "utf8");
// 跳过首行 subject，仅检查正文。
const body = msg.split(/\r?\n/).slice(1).join("\n");

const problems = [];

if (!/Why:/i.test(body)) {
  problems.push("缺少 Why（变更原因）段落，格式：Why: xxx");
}
if (!/What:/i.test(body)) {
  problems.push("缺少 What（变更内容）段落，格式：What: xxx");
}
if (!/\b(design|code|test)\s+(wrong|error|bug)\b/i.test(msg)) {
  problems.push("未声明根因，根因三选一（design/code/test wrong）");
}

if (problems.length > 0) {
  console.error("[check-commit-msg] 提交信息不符合约定：");
  problems.forEach((p) => console.error("  - " + p));
  process.exit(1);
}

process.exit(0);