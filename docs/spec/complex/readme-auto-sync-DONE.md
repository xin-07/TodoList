# README 自动同步（目录结构 + 调用顺序）— complex · DONE

## Why

当前 README 的「项目结构」目录树与「架构：单一权威源」调用链均为**手写**，随代码演进已过时：

- 目录树缺失 `Models/Category.cs`、`Models/Tag.cs`、`Models/TaskPriority.cs`、`Models/TaskTitle.cs`、`Models/NameRules.cs`、`Tests/`、`config.json`、`Program.cs` 等新条目。
- 调用链只写 `ViewModel`，实际已拆分为 `MainWindowViewModel` / `TodoItemViewModel`，且装配链（`DatabaseService → TodoRepository → MainWindowViewModel → MainWindow`）未反映。
- `scripts/update-readme.js` 目前只更新 AUTO-README 版本块（最近提交/时间/DB 路径），不覆盖结构与调用链。

用户要求：**README 的目录结构与调用顺序也应根据项目实际自动更新**，不能只依赖 commit 信息。

## 架构约束（所有实现必须遵守）

- **单一权威源（SSOT）**：目录结构的事实 = 文件系统；调用顺序的事实 = `App.axaml.cs` 装配代码。脚本只做「推导 + 展示」，**不得在脚本内硬编码任何职责文本或目录清单**。
- **注释提取**：条目注释只从源文件内的类级 `<summary>`（或首行 `//` 注释）提取；无注释则只列名称，禁止人工在脚本中补写职责说明。
- **排除目录**（生成物/无关内容，不进入目录树）：`.git`、`.vs`、`bin`、`obj`、`data`、`publish`、`.trae`、`.husky/_`（`.husky` 下的 `pre-commit`/`commit-msg` 保留展示）。
- **失败安全**：脚本任一步骤解析失败时，保留 README 对应段落现状并输出告警，**不得破坏 README、不得导致 commit 失败**（退出码 0）。
- **宪法**：commit 含 Why/What（docs/chore 豁免）；技术债标注 `// DEFERRED:`；提交前向用户展示并获确认。

## What Changes

在 `scripts/update-readme.js` 中新增两个自动块的重写逻辑，仍由 `.husky/pre-commit` 在每次提交前触发：

1. **目录树（`<!-- AUTO-TREE:START/END -->`）**：递归遍历项目根，应用排除规则；每个条目（文件或目录）右侧附注释（提取自条目内第一个含 `<summary>` 的类的首行文字，目录取目录内首个此类文件）；以 `├──` / `└──` / `│` 树形格式输出，整体置于 ` ```text ` 代码围栏内。
2. **调用链（`<!-- AUTO-FLOW:START/END -->`）**：解析 `App.axaml.cs` 装配代码中的构造依赖（`var x = new A(...)` 及 `new B(x)` 的参数引用），按依赖关系推导调用链，输出 `SQLite（权威源） ←─ DatabaseService ←─ TodoRepository（只读投影） ←─ MainWindowViewModel ←─ View`；解析不到任何装配时保留原文并告警。
3. **版本块（已有 AUTO-README）**：行为不变。

同时调整 `README.md`：把「项目结构」与「架构：单一权威源」中的手写代码块分别替换为 `AUTO-TREE` / `AUTO-FLOW` 标记块，并加注「由 update-readme.js 自动生成，请勿手改」。非标记块段落（功能清单、技术栈、快速开始、数据存储说明、项目规约）原样保留，仍人工维护。

## Impact

- **Affected code**：
  - `scripts/update-readme.js`（主改：新增目录树 + 调用链生成）
  - `README.md`（结构调整：插入两个标记块）
- **Not touched**：应用源码（`Models/`、`Database/`、`Repositories/`、`ViewModels/`、`MainWindow*`）、`config.json`、`.husky/*`。
- **Tooling**：不新增依赖（纯 Node 内置 `fs`/`path`）。

## ADDED Requirements

### Requirement: 目录树自动同步
系统 SHALL 在每次提交时按项目实际文件系统重新生成 README 的「项目结构」目录树，替换 `AUTO-TREE` 标记块内容。
- **Scenario: 新增源文件**
  - **WHEN** 新增 `Models/Category.cs` 等文件并提交
  - **THEN** 目录树自动包含该文件，且注释取自其类 `<summary>` 首行。

### Requirement: 调用链自动同步
系统 SHALL 从 `App.axaml.cs` 装配代码推导数据流调用链，替换 `AUTO-FLOW` 标记块内容。
- **Scenario: 装配顺序变化**
  - **WHEN** 装配代码新增中间层
  - **THEN** 调用链按 `SQLite ←─ … ←─ View` 顺序反映实际依赖。

### Requirement: 注释仅来自源文件
系统 SHALL 不硬编码任何职责文本；目录树条目注释仅提取自源文件类 `<summary>`（首行）或首行 `//` 注释，无则省略。
- **Scenario: 无注释文件**
  - **WHEN** 某文件无 `<summary>`/注释
  - **THEN** 树中该条目只显示文件名，不显示伪造注释。

### Requirement: 失败安全
系统 SHALL 在解析失败时保留 README 现状并告警，不破坏文档、不阻断提交。
- **Scenario: 装配代码暂无法解析**
  - **WHEN** 调用链推导返回空
  - **THEN** AUTO-FLOW 块保持原内容，控制台输出告警，脚本以 0 退出。

## Tasks

- [x] T1 目录树生成器：递归遍历项目根；排除 `.git/.vs/bin/obj/data/publish/.trae/.husky/_`；注释提取（`<summary>` 首行或首行 `//`，目录取首个含注释的 .cs）；树形格式化并包进 ` ```text `
- [x] T2 调用链推导器：解析 `App.axaml.cs` 的 `new X(...)` 与构造参数变量引用建立依赖边；输出 4 层装配链；解析为空保留原文并告警
- [x] T3 update-readme.js 集成：新增 `AUTO-TREE`/`AUTO-FLOW` 标记块重写逻辑（复用版本块流程）；失败安全
- [x] T4 README.md 结构调整：项目结构 → `AUTO-TREE` 包裹；架构调用链 → `AUTO-FLOW` 包裹；两处加注「自动生成勿手改」
- [x] 验证：手动运行脚本目录树含全部源条目且排除正确、调用链正确；经 `.husky/pre-commit` 真实提交自动刷新成功

## Checklist

- [x] 目录树包含当前全部源条目，排除正确（无 bin/obj/data/publish/.trae/.husky/_/.vs）
- [x] 条目注释取自类 `<summary>` 首行；无注释条目只显示名称
- [x] 调用链输出 `SQLite（权威源） ←─ DatabaseService ←─ TodoRepository（只读投影） ←─ MainWindowViewModel ←─ View`
- [x] AUTO-README 版本块仍自动更新；非标记块段落原样保留
- [x] 脚本中无硬编码职责文本（SSOT）
- [x] 经 `.husky/pre-commit` 真实提交自动刷新成功
- [x] 提交前已向用户展示并获确认

## 验收标准

- 运行 `node scripts/update-readme.js` 后：目录树含全部当前源条目且不含排除目录；调用链为 4 层装配链；版本块正常；非标记块段落原样保留。
- 经 `.husky/pre-commit` 触发真实提交，README 相应块自动刷新且提交成功。
