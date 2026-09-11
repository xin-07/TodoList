#!/usr/bin/env node

// commit-msg 门禁 —— AI_CONSTITUTION.md 第一条的执行层。
// 目的：防止 AI 提交只写 "fix bug"，三个月后无法回溯修改动机（审计盲区）。
// 硬拦截：feat / fix / refactor / perf / test —— 必须同时含 "Why:" 与 "What:"。
// 豁免：docs / chore / style。
// 软警告：无法识别的 type（如手滑打成 featt）只提示不拦截——风格问题永不阻塞提交。

import fs from 'node:fs';
import process from 'node:process';

const HARD_TYPES = new Set(['feat', 'fix', 'refactor', 'perf', 'test']);
const EXEMPT_TYPES = new Set(['docs', 'chore', 'style']);

function main() {
  const msgFile = process.argv[2];
  if (!msgFile) {
    console.error('[check-commit-msg] 缺少 commit message 文件路径参数');
    process.exit(1);
  }

  const raw = fs.readFileSync(msgFile, 'utf8');
  // git 会把 "#" 开头的注释写入 message 文件，剥离后再解析
  const message = raw
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('#'))
    .join('\n')
    .trim();

  if (!message) {
    console.error('[check-commit-msg] commit message 为空，拒绝提交');
    process.exit(1);
  }

  // 解析 conventional type：type(scope)!: subject
  const match = message.match(/^([a-zA-Z]+)(?:\([^)]*\))?!?:/);
  if (!match) {
    console.warn('[check-commit-msg] ⚠ 无法解析 commit type（风格问题，仅提醒不拦截）');
    process.exit(0);
  }

  const type = match[1].toLowerCase();

  if (EXEMPT_TYPES.has(type)) {
    process.exit(0);
  }

  if (!HARD_TYPES.has(type)) {
    console.warn(`[check-commit-msg] ⚠ 未知 type "${type}"（仅提醒不拦截）`);
    process.exit(0);
  }

  // 五类硬门禁：必须含 Why: / What:（兼容全角冒号）
  const hasWhy = /^Why\s*[:：]/im.test(message);
  const hasWhat = /^What\s*[:：]/im.test(message);

  if (!hasWhy || !hasWhat) {
    console.error(
      `[check-commit-msg] ✖ type "${type}" 必须同时包含 "Why:" 与 "What:" 两行。\n` +
        `\n` +
        `  示例：\n` +
        `    ${type}: <一句话概括>\n` +
        `    Why: <为什么改，根因是什么>\n` +
        `    What: <具体做了什么>\n` +
        `\n` +
        `  （AI_CONSTITUTION.md 第一条：每次 commit 必须含 Why/What，否则无法回溯修改动机）\n`
    );
    process.exit(1);
  }

  process.exit(0);
}

main();
