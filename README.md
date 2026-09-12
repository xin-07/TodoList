# TodoList

一个基于 **Avalonia (12.1.1) + .NET 8** 的跨平台桌面待办事项应用，采用 **SQLite 作为单一权威数据源** 的 MVVM 架构。

## ✨ 功能

- 任务管理：输入 + 添加、勾选完成、删除
- 任务标题原地编辑（双击 / F2 进入编辑，Enter 提交、Esc 取消、失焦保存）
- 优先级：无 / 低 / 中 / 高 四档，行首高亮条，列表按优先级从高到低排序
- 截止日期：严格 `yyyy-MM-dd` 格式校验，输入框占位提示当天日期，到期展示「今天到期 / 已过期」
- 到期桌面通知提醒（应用运行期间每分钟检查未完成且已到期的任务）
- 输入校验：新增与编辑统一校验（空 / 去空白 / 超长 100 字符）
- 完成置灰展示
- 已办 / 总数统计
- 任务列表超出窗口高度时上下滚动
- `TodoRepository` 集成测试（隔离临时 SQLite，不触碰 `data/todo.db`）

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
│   └── commit-msg
├── Database                                           # SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
│   └── DatabaseService.cs                             # SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
├── dist
│   ├── linux-x64
│   │   ├── TodoList
│   │   └── TodoList.pdb
│   ├── osx-arm64
│   │   ├── TodoList
│   │   └── TodoList.pdb
│   └── win-x64
│       ├── libHarfBuzzSharp.pdb
│       ├── libSkiaSharp.pdb
│       ├── TodoList.exe
│       └── TodoList.pdb
├── docs
│   └── spec
│       ├── docs
│       │   └── readme-auto-sync-DONE.md
│       └── feature
│           ├── due-date-input-validation-DONE.md
│           └── enhance-todo-features-IN_PROGRESS.md
├── Models                                             # 截止日期格式的"单一权威源"助手：定义全应用统一的日期格式常量与严格解析逻辑。
│   ├── DueDate.cs                                     # 截止日期格式的"单一权威源"助手：定义全应用统一的日期格式常量与严格解析逻辑。
│   ├── TaskPriority.cs                                # 任务优先级。低→高代表紧急/重要程度的递增。
│   ├── TaskTitle.cs                                   # 任务标题的"单一权威源"校验助手：Trim + 非空 + MaxLength。
│   └── TodoItem.cs                                    # 任务数据模型（纯数据，无业务逻辑）。
├── Repositories                                       # 任务仓库接口：定义"任务数据"的唯一边界。
│   ├── ITodoRepository.cs                             # 任务仓库接口：定义"任务数据"的唯一边界。
│   └── TodoRepository.cs                              # 任务仓库实现：应用内唯一权威数据源。
├── scripts
│   ├── check-commit-msg.js
│   └── publish-all.ps1
├── Tests                                              # 测试项目：TodoRepository 集成测试。
│   └── TodoList.Tests                                 # TodoRepository 集成测试。
│       ├── TodoList.Tests.csproj
│       └── TodoRepositoryTests.cs                     # 使用临时 SQLite，不触碰 data/todo.db。
├── ViewModels                                         # MVVM 视图模型层：主窗口 VM、行 VM、命令与基类。
│   ├── MainWindowViewModel.cs                         # 主窗口 ViewModel：消费仓库只读投影，不持有权威数据。
│   ├── RelayCommand.cs                                # 极简 ICommand 实现，避免引入额外 MVVM 框架。
│   ├── TodoItemViewModel.cs                           # 行 ViewModel：承载行内编辑状态与优先级/截止日期派生展示。
│   └── ViewModelBase.cs                               # MVVM 基类，提供属性变更通知能力。
├── .gitignore
├── AGENTS.md
├── AI_CONSTITUTION.md
├── App.axaml
├── App.axaml.cs                                       # 装配：SQLite(单一权威源) → Repository → ViewModel。
├── app.manifest
├── CLAUDE.md
├── config.json
├── MainWindow.axaml
├── MainWindow.axaml.cs                                # 主窗口代码后置：到期通知弹窗与回车快速添加。
├── Program.cs                                         # Avalonia 程序入口：构建 App 并启动桌面生命周期。
├── README.md
├── TodoList.csproj
└── TodoList.slnx
```
<!-- AUTO-TREE:END -->

> 注：`<!-- AUTO-TREE:START/END -->` 之间的目录树与 `# 注释` 均为**人工维护**，提交时不再有程序自动改写。

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

> 注：`<!-- AUTO-FLOW:START/END -->` 之间的调用链为**人工维护**，提交时不再有程序自动改写。

## 📂 数据存储位置

数据目录与数据库文件名在 `config.json` **单一权威定义**一次（`data.directory`、`data.dbFileName`），`App.axaml.cs` 读取它，不在代码里重复硬编码。

实际数据库文件位于项目根 `/被定义的目录/文件名`（默认 `data/todo.db`）。`App.axaml.cs` 的 `ResolveProjectRoot()` 从输出目录向上定位项目根；发布环境无 `.csproj` 标记时回退到程序输出目录。

`data/` 已被 `.gitignore` 排除，数据库不会进入版本库（`config.json` 本身会提交）。

### 版本与提交信息

<!-- AUTO-README:START -->
| 项目 | 值 |
| --- | --- |
| 最近提交 | `1bdcee1` — feat: 跨平台单文件发布（publish-all 脚本 + PublishSingleFile 配置） |
| 提交时间 | 2026-09-12 |
| 数据库文件 | `data/todo.db` |
<!-- AUTO-README:END -->

> 注：`<!-- AUTO-README:START/END -->` 之间的版本信息为**人工维护**，提交时不再有程序自动改写。

## 📁 文档目录

| 目录 | 用途 |
| --- | --- |
| `docs/AI-workflow/` | AI 协作工作流相关文档 |
| `docs/rule/` | 存储 `AI_CONSTITUTION.md` 中每一条的详细规则 |
| `docs/spec/<问题领域>/` | 按问题领域分类的 SPEC（feature / docs / test ...），每个问题只允许一个文档 |

## 📝 项目规约

- 遵循 `AI_CONSTITUTION.md`（共 13 条）：涵盖通用软件工程准则（文档-代码共同维护、根因分析、设计契约、单一权威源、no-patchwork 等）与本项目特定约定（commit 前展示确认、任务三分法、spec 领域目录）。
- 代码变更的 commit 必须包含 `Why:` 与 `What:`（根因三选一：design/code/test wrong）。
- commit 门禁由 `scripts/check-commit-msg.js` 经 `.husky/commit-msg` 强制执行。
- 技术债须标注 `// DEFERRED:`，禁止裸 TODO。