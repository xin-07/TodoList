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

<!-- AUTO-TREE:START -->
```text
TodoList/
├── .husky
│   ├── commit-msg
│   └── pre-commit
├── Database                                           # SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
│   └── DatabaseService.cs                             # SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
├── docs
│   └── spec
│       └── complex
│           ├── enhance-todo-features-IN_PROGRESS.md
│           └── readme-auto-sync-DONE.md
├── Models                                             # 任务优先级。低→高代表紧急/重要程度的递增。
│   ├── TaskPriority.cs                                # 任务优先级。低→高代表紧急/重要程度的递增。
│   ├── TaskTitle.cs                                   # 任务标题的"单一权威源"校验助手：Trim + 非空 + MaxLength。
│   └── TodoItem.cs                                    # 任务数据模型（纯数据，无业务逻辑）。
├── Repositories                                       # 任务仓库接口：定义"任务数据"的唯一边界。
│   ├── ITodoRepository.cs                             # 任务仓库接口：定义"任务数据"的唯一边界。
│   └── TodoRepository.cs                              # 任务仓库实现：应用内唯一权威数据源。
├── scripts
│   ├── check-commit-msg.js
│   └── update-readme.js
├── Tests                                              # TodoRepository 集成测试：全部使用临时独立的 SQLite 库（不触碰 data/todo.db），
│   └── TodoList.Tests                                 # TodoRepository 集成测试：全部使用临时独立的 SQLite 库（不触碰 data/todo.db），
│       ├── TodoList.Tests.csproj
│       └── TodoRepositoryTests.cs                     # TodoRepository 集成测试：全部使用临时独立的 SQLite 库（不触碰 data/todo.db），
├── ViewModels                                         # 到期提醒事件参数。
│   ├── MainWindowViewModel.cs                         # 到期提醒事件参数。
│   ├── RelayCommand.cs                                # 极简 ICommand 实现，避免引入额外 MVVM 框架。
│   ├── TodoItemViewModel.cs                           # 优先级下拉的单个选项。
│   └── ViewModelBase.cs                               # MVVM 基类，提供属性变更通知能力。
├── .gitignore
├── AGENTS.md
├── AI_CONSTITUTION.md
├── App.axaml
├── App.axaml.cs                                       # 解析数据库绝对路径。
├── app.manifest
├── CLAUDE.md
├── config.json
├── MainWindow.axaml
├── MainWindow.axaml.cs                                # 回车等同于点击"添加"。
├── Program.cs                                         # Initialization code. Don't use any Avalonia, third-party APIs or any
├── README.md
├── TodoList.csproj
└── TodoList.slnx
```
<!-- AUTO-TREE:END -->

> 注：`<!-- AUTO-TREE:START/END -->` 之间的内容由 `scripts/update-readme.js` 在每次 commit 时按实际文件系统自动生成（经 `.husky/pre-commit` 钩子触发），请勿手改。

## 🧠 架构：单一权威源

数据真相只存在于 **SQLite 数据库**：

- `DatabaseService` 是唯一直接与 SQLite 打交道的类。
- `TodoRepository` 是应用的**唯一权威数据源**，对外暴露**只读投影**，任何变更都「先落库、再更新内存投影」，保证二者一致。
- `MainWindowViewModel` 只消费仓库，不持有权威数据，杜绝「两份真相」。

```
<!-- AUTO-FLOW:START -->
SQLite（权威源） ←─ DatabaseService ←─ TodoRepository（只读投影） ←─ MainWindowViewModel ←─ View
<!-- AUTO-FLOW:END -->
```

> 注：`<!-- AUTO-FLOW:START/END -->` 之间的内容由 `scripts/update-readme.js` 在每次 commit 时从 `App.axaml.cs` 装配代码自动推导（经 `.husky/pre-commit` 钩子触发），请勿手改。

## 📂 数据存储位置

数据目录与数据库文件名在 `config.json` **单一权威定义**一次（`data.directory`、`data.dbFileName`），`App.axaml.cs` 与 `scripts/update-readme.js` 均读取它，不在代码里重复硬编码。

实际数据库文件位于项目根 `/被定义的目录/文件名`（默认 `data/todo.db`）。`App.axaml.cs` 的 `ResolveProjectRoot()` 从输出目录向上定位项目根；发布环境无 `.csproj` 标记时回退到程序输出目录。

`data/` 已被 `.gitignore` 排除，数据库不会进入版本库（`config.json` 本身会提交）。

### 版本与提交信息

<!-- AUTO-README:START -->
| 项目 | 值 |
| --- | --- |
| 最近提交 | `db02654` — chore: refresh README auto version block to HEAD |
| 提交时间 | 2026-09-11 |
| 数据库文件 | `data/todo.db` |
<!-- AUTO-README:END -->

> 注：`<!-- AUTO-README:START/END -->` 之间的内容由 `scripts/update-readme.js` 在每次 commit 时自动重写（经 `.husky/pre-commit` 钩子触发），请勿手改。

## 📝 项目规约

- 遵循 `AI_CONSTITUTION.md`：代码变更的 commit 必须包含 `Why:` 与 `What:`（根因三选一：design/code/test wrong）。
- commit 门禁由 `scripts/check-commit-msg.js` 经 `.husky/commit-msg` 强制执行。
- 技术债须标注 `// DEFERRED:`，禁止裸 TODO。