#!/usr/bin/env node
/**
 * update-readme —— README 审查与自动更新（配合 .husky/pre-commit 触发）。
 *
 * 职责：
 *  1. 读取 git 最近一次提交（短 hash、主题、提交日期）；
 *  2. 重写 README.md 中 `<!-- AUTO-README:START -->` 与 `<!-- AUTO-README:END -->`
 *     之间的内容为最新信息；
 *  3. 审查项：若标记块丢失或缺少关键数据段，给出告警（不强制阻断提交，避免钩子误伤）。
 *
 * 为什么在 pre-commit 做：让 README 的版本/数据信息随每次 commit 实时同步，
 * 无需人工维护。
 */
import { execSync } from "node:child_process";
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import path from "node:path";

const root = execSync("git rev-parse --show-toplevel").toString().trim();

function git(args) {
  return execSync(`git ${args}`, { cwd: root }).toString().trim();
}

const shortHash = git("log -1 --format=%h");
const subject = git("log -1 --format=%s");
const commitDate = git("log -1 --format=%cs");

// 与 App.axaml.cs 中 AppContext.BaseDirectory/data/todo.db 对应的、dotnet run 时的相对路径。
const DB_PATH = "bin/Debug/net8.0/data/todo.db";

const readmePath = path.join(root, "README.md");
const markerStart = "<!-- AUTO-README:START -->";
const markerEnd = "<!-- AUTO-README:END -->";

const block = [
  "| 项目 | 值 |",
  "| --- | --- |",
  `| 最近提交 | \`${shortHash}\` — ${subject} |`,
  `| 提交时间 | ${commitDate} |`,
  `| 数据库文件 | \`${DB_PATH}\` |`,
].join("\n");

if (!existsSync(readmePath)) {
  console.warn("[update-readme] ✖ 未找到 README.md，跳过更新");
  process.exit(0);
}

let readme = readFileSync(readmePath, "utf8");
const pattern = new RegExp(`(${escape(markerStart)})[\\s\\S]*?(${escape(markerEnd)})`);

if (pattern.test(readme)) {
  readme = readme.replace(pattern, `${markerStart}\n${block}\n${markerEnd}`);
  writeFileSync(readmePath, readme, "utf8");
  console.log(`[update-readme] ✔ README 已更新 (commit ${shortHash})`);
} else {
  console.warn(
    "[update-readme] ⚠ 未找到 AUTO-README 标记块，跳过更新。\n" +
      "  请在 README.md 中保留等标记注释，否则版本信息将不会自动刷新（审查未通过）。",
  );
}

// 审查：数据库文件是否已生成（开发模式下预期路径）。
if (!existsSync(path.join(root, DB_PATH))) {
  console.warn(
    `[update-readme] ⚠ 审查提示：数据库文件 ${DB_PATH} 暂不存在（尚未执行过 dotnet run 或已清理）。`,
  );
}

function escape(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}