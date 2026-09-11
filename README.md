# TodoList

一个基于 **Avalonia (12.1.1) + .NET 8** 的跨平台桌面待办事项应用，采用 **SQLite 作为单一权威数据源** 的 MVVM 架构。

## ✨ 功能（当前 MVP）

- 任务管理：输入 + 添加、勾选完成、删除
- 完成置灰展示
- 已办/总数统计
- 深色 / 浅色主题跟随系统

### 规划中（后续迭代）
分类、标签、优先级、截止日期、提醒、云同步/WebDAV、数据导入导出、移动端。

## 🔧 技术栈

- **GUI**：Avalonia 12.1.1（XAML MVVM）
- **运行时**：.NET 8
- **数据库**：SQLite（`Microsoft.Data.Sqlite`）

## 🚀 快速开始

```bash
# 还原并运行
dotnet restore
dotnet run
```

> 说明：启动操作请由你自己执行（本项目约定 AI 不负责启动程序）。

打包（可选）：

```bash
dotnet publish -c Release
```

## 🖥️ 项目结构

```
TodoList/
├── Database/            # SQLite 数据访问层（唯一接触 SQLite 的类）
├── Models/              # 纯数据模型（TodoItem）
├── Repositories/        # 单一权威数据源（只读投影 + 落库）
├── ViewModels/          # MVVM 视图模型
├── MainWindow.axaml     # 主窗口 UI（纯绑定）
├── App.axaml.cs         # 装配：SQLite → Repository → ViewModel
├── scripts/             # git 钩子辅助脚本（commit 检查、README 更新）
├── AI_CONSTITUTION.md   # AI 协作宪法（提交等规约）
└── CLAUDE.md / AGENTS.md
```

## 🧠 架构：单一权威源

数据真相只存在于 **SQLite 数据库**：

- `DatabaseService` 是唯一直接与 SQLite 打交道的类。
- `TodoRepository` 是应用的**唯一权威数据源**，对外暴露**只读投影**，任何变更都「先落库、再更新内存投影」，保证二者一致。
- `MainWindowViewModel` 只消费仓库，不持有权威数据，杜绝「两份真相」。

```
SQLite（权威源） ←─ DatabaseService ←─ TodoRepository（只读投影） ←─ ViewModel ←─ View
```

## 📂 数据存储位置

数据目录与数据库文件名在 `config.json` **单一权威定义**一次（`data.directory`、`data.dbFileName`），`App.axaml.cs` 与 `scripts/update-readme.js` 均读取它，不在代码里重复硬编码。

实际数据库文件位于项目根 `/被定义的目录/文件名`（默认 `data/todo.db`）。`App.axaml.cs` 的 `ResolveProjectRoot()` 从输出目录向上定位项目根；发布环境无 `.csproj` 标记时回退到程序输出目录。

`data/` 已被 `.gitignore` 排除，数据库不会进入版本库（`config.json` 本身会提交）。

### 版本与提交信息

<!-- AUTO-README:START -->
| 项目 | 值 |
| --- | --- |
| 最近提交 | `9d43675` — chore: replace commit-msg checker with full conventional-type gate |
| 提交时间 | 2026-09-11 |
| 数据库文件 | `data/todo.db` |
<!-- AUTO-README:END -->

> 注：`<!-- AUTO-README:START/END -->` 之间的内容由 `scripts/update-readme.js` 在每次 commit 时自动重写（经 `.husky/pre-commit` 钩子触发），请勿手改。

## 📝 项目规约

- 遵循 `AI_CONSTITUTION.md`：代码变更的 commit 必须包含 `Why:` 与 `What:`（根因三选一：design/code/test wrong）。
- commit 门禁由 `scripts/check-commit-msg.js` 经 `.husky/commit-msg` 强制执行。
- 技术债须标注 `// DEFERRED:`，禁止裸 TODO。