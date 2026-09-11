#!/usr/bin/env node
/**
 * update-readme —— README 审查与自动更新（配合 .husky/pre-commit 触发）。
 *
 * 职责：
 *  1. 重写 `<!-- AUTO-README:START/END -->`：最近提交、时间、DB 路径；
 *  2. 重写 `<!-- AUTO-TREE:START/END -->`：按实际文件系统生成项目结构目录树
 *     （条目注释只提取自源文件类级 `<summary>` 首行或首行 `//` 注释，不硬编码）；
 *  3. 重写 `<!-- AUTO-FLOW:START/END -->`：从 App.axaml.cs 装配代码推导数据流
 *     调用链（SQLite ←─ DatabaseService ←─ TodoRepository ←─ ViewModel ←─ View）；
 *  4. 审查项：若标记块丢失或生成失败，给出告警（不强制阻断提交，避免钩子误伤）。
 *
 * 为什么在 pre-commit 做：让 README 的版本/结构/调用信息随每次 commit 实时同步，
 * 无需人工维护。数据来源（git、文件系统、App.axaml.cs、config.json）即事实，
 * 脚本只做推导与展示，不重复定义。
 */
import { execSync } from "node:child_process";
import { readFileSync, writeFileSync, existsSync, readdirSync } from "node:fs";
import path from "node:path";

const root = execSync("git rev-parse --show-toplevel").toString().trim();

function git(args) {
  return execSync(`git ${args}`, { cwd: root }).toString().trim();
}

function escape(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// ---------- 版本信息（AUTO-README） ----------
const shortHash = git("log -1 --format=%h");
const subject = git("log -1 --format=%s");
const commitDate = git("log -1 --format=%cs");

// 数据目录/文件名读取 config.json（单一权威源），禁止在此重复定义。
const config = JSON.parse(readFileSync(path.join(root, "config.json"), "utf8"));
const dataDir = config.data.directory;
const dbFileName = config.data.dbFileName;
const DB_PATH = `${dataDir}/${dbFileName}`; // README 展示用相对路径
const dbAbsPath = path.join(root, dataDir, dbFileName); // 审查用绝对路径

// ---------- 目录树（AUTO-TREE） ----------
/** 生成目录树时排除的目录名（生成物/无关内容）。 */
const EXCLUDED_DIR_NAMES = new Set([".git", ".vs", "bin", "obj", "data", "publish", ".trae"]);
/** 排除的相对路径片段（如 .husky/_ 内部机制）。 */
const EXCLUDED_REL_SEGMENTS = [path.join(".husky", "_")];

/** 是否应排除某目录。 */
function isExcludedDir(dirPath) {
  if (EXCLUDED_DIR_NAMES.has(path.basename(dirPath))) return true;
  const rel = path.relative(root, dirPath);
  return EXCLUDED_REL_SEGMENTS.some((seg) => rel === seg || rel.startsWith(seg + path.sep));
}

/** 提取 .cs 文件职责注释：类级 <summary> 首行，否则首行 // 注释；均无返回 null。 */
function extractComment(filePath) {
  const src = readFileSync(filePath, "utf8");
  const summary = src.match(/<summary>([\s\S]*?)<\/summary>/);
  if (summary) {
    const line = summary[1]
      .split("\n")
      .map((l) => l.replace(/^\s*\/\/\/?\s*/, "").trim())
      .find((l) => l.length > 0 && !l.startsWith("<"));
    if (line) return line;
  }
  const firstComment = src
    .split("\n")
    .map((l) => l.trim())
    .find((l) => l.startsWith("//") && !l.startsWith("//!"));
  if (firstComment) return firstComment.replace(/^\/\/\s*/, "").trim();
  return null;
}

/** 递归收集目录树节点：{ name, comment, children? }。 */
function collectNodes(absDir) {
  const entries = readdirSync(absDir, { withFileTypes: true })
    .filter((e) => !isExcludedDir(path.join(absDir, e.name)))
    .sort((a, b) => {
      const ad = a.isDirectory() ? 0 : 1;
      const bd = b.isDirectory() ? 0 : 1;
      return ad - bd || a.name.localeCompare(b.name);
    });

  return entries
    .map((e) => {
      const full = path.join(absDir, e.name);
      if (e.isDirectory()) {
        const children = collectNodes(full);
        // 空目录（且非 git 仓库根）不展示
        if (children.length === 0) return null;
        // 目录注释取目录内首个能提取到注释的 .cs 文件
        const comment = findFirstCsComment(children);
        return { name: e.name, comment, children };
      }
      return { name: e.name, comment: /\.cs$/i.test(e.name) ? extractComment(full) : null };
    })
    .filter(Boolean);
}

/** 在节点树中找第一个带注释的 .cs 文件注释。 */
function findFirstCsComment(nodes) {
  for (const n of nodes) {
    if (n.children) {
      const hit = findFirstCsComment(n.children);
      if (hit) return hit;
    }
    if (/\.cs$/i.test(n.name) && n.comment) return n.comment;
  }
  return null;
}

/** 渲染目录树文本（注释列右对齐）。 */
function renderTree(nodes) {
  const rows = [];
  const walk = (list, prefix) => {
    list.forEach((node, i) => {
      const last = i === list.length - 1;
      const nameLine = `${prefix}${last ? "└── " : "├── "}${node.name}`;
      rows.push({ nameLine, comment: node.comment || null });
      if (node.children) walk(node.children, prefix + (last ? "    " : "│   "));
    });
  };
  walk(nodes, "");
  const width = Math.max(...rows.map((r) => r.nameLine.length));
  return rows.map((r) =>
    r.comment ? `${r.nameLine.padEnd(width)}   # ${r.comment}` : r.nameLine,
  );
}

function buildTreeBlock() {
  const nodes = collectNodes(root);
  const rootName = path.basename(root) || root;
  return ["```text", `${rootName}/`, ...renderTree(nodes), "```"].join("\n");
}

// ---------- 调用链（AUTO-FLOW） ----------
/** 从 App.axaml.cs 装配代码推导调用链；解析失败返回 null（保留原文并告警）。 */
function deriveCallChain() {
  const appCs = readFileSync(path.join(root, "App.axaml.cs"), "utf8");

  // 1. 变量绑定：var name = new Type(
  const varToType = new Map();
  for (const m of appCs.matchAll(/\bvar\s+(\w+)\s*=\s*new\s+([A-Za-z_]\w*)\s*\(/g)) {
    varToType.set(m[1], m[2]);
  }

  // 2. 装配调用序列：所有 new Type(args)（按出现顺序）
  const calls = [];
  for (const m of appCs.matchAll(/new\s+([A-Za-z_]\w*)\s*\(([^)]*)\)/g)) {
    calls.push({ type: m[1], args: m[2] });
  }

  // 3. 依赖链接：某调用参数引用了此前创建的变量 → 形成父子边
  const chain = [];
  const push = (t) => {
    if (!chain.includes(t)) chain.push(t);
  };
  for (const call of calls) {
    let parent = null;
    for (const [v, t] of varToType) {
      if (new RegExp(`\\b${v}\\b`).test(call.args)) {
        parent = t;
        break;
      }
    }
    if (parent) {
      push(parent);
      push(call.type);
    }
  }

  // 4. View 装配：new MainWindow { ... DataContext = new Xxx(...) }
  if (!chain.includes("MainWindow") && /new\s+MainWindow\s*\{/.test(appCs)) {
    const lastVm = calls.map((c) => c.type).reverse().find((t) => t.endsWith("ViewModel"));
    if (lastVm) push(lastVm);
    push("MainWindow");
  }

  if (chain.length === 0) return null;

  // 5. 展示映射
  const label = {
    DatabaseService: "DatabaseService",
    TodoRepository: "TodoRepository（只读投影）",
    MainWindowViewModel: "MainWindowViewModel",
    MainWindow: "View",
  };
  const parts = chain.map((t) => label[t] ?? t);
  const head = chain[0] === "DatabaseService" ? "SQLite（权威源） ←─ " : "";
  return head + parts.join(" ←─ ");
}

// ---------- 主流程 ----------
const readmePath = path.join(root, "README.md");

if (!existsSync(readmePath)) {
  console.warn("[update-readme] ✖ 未找到 README.md，跳过更新");
  process.exit(0);
}

let readme = readFileSync(readmePath, "utf8");

/** 通用块重写：找不到标记块或生成失败时保留原文并告警，不阻断提交。 */
function rewriteBlock(readme, key, build) {
  const start = `<!-- ${key}:START -->`;
  const end = `<!-- ${key}:END -->`;
  const pattern = new RegExp(`(${escape(start)})[\\s\\S]*?(${escape(end)})`);
  if (!pattern.test(readme)) {
    console.warn(`[update-readme] ⚠ 未找到 ${key} 标记块，跳过更新。`);
    return readme;
  }
  let block;
  try {
    block = build();
  } catch (err) {
    console.warn(`[update-readme] ⚠ ${key} 生成失败：${err.message}（保留原内容，不阻断提交）`);
    return readme;
  }
  if (block == null) {
    console.warn(`[update-readme] ⚠ ${key} 生成结果为空（保留原内容，不阻断提交）`);
    return readme;
  }
  readme = readme.replace(pattern, `${start}\n${block}\n${end}`);
  return readme;
}

// 版本块（AUTO-README）
const versionBlock = [
  "| 项目 | 值 |",
  "| --- | --- |",
  `| 最近提交 | \`${shortHash}\` — ${subject} |`,
  `| 提交时间 | ${commitDate} |`,
  `| 数据库文件 | \`${DB_PATH}\` |`,
].join("\n");
readme = rewriteBlock(readme, "AUTO-README", () => versionBlock);

// 目录树（AUTO-TREE）
readme = rewriteBlock(readme, "AUTO-TREE", buildTreeBlock);

// 调用链（AUTO-FLOW）
readme = rewriteBlock(readme, "AUTO-FLOW", deriveCallChain);

writeFileSync(readmePath, readme, "utf8");
console.log(`[update-readme] ✔ README 已更新 (commit ${shortHash})`);

// 审查：数据库文件是否已生成（开发模式下预期路径）。
if (!existsSync(dbAbsPath)) {
  console.warn(
    `[update-readme] ⚠ 审查提示：数据库文件 ${DB_PATH} 暂不存在（尚未执行过 dotnet run 或已清理）。`,
  );
}
