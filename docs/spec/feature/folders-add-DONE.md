# 文件夹功能 — complex · DONE

> 单文档：SPEC（Why/What/Impact/Requirements）+ Tasks + Checklist。
> 状态：DONE。方案已确认、代码已实现并通过编译与单测，用户人工测试通过。

## Why

当前应用只有单一待办列表，任务无法归类。用户希望提供"文件夹"作为收纳容器：
把待办条目收纳进不同文件夹，便于按主题组织；文件夹内内容与现有列表条目完全同构（复用等同一条目，不引入新内容类型）。

需求经与用户多轮对齐，关键决策：
- 文件夹内容 = **与现有待办条目相同的条目**（同标题/优先级/截止日期/完成状态），不新增"笔记"类型。
- 一个条目**单归属**：属于一个文件夹或"未归类"。
- **单层平铺**，不支持嵌套子文件夹。
- 界面用**左侧边栏**组织：全部任务 / 各文件夹 / 未归类。
- 顶部「添加」按钮旁新增**文件夹图标**，点击新建可命名的文件夹。
- 删除文件夹时**弹确认**，确认后该文件夹内所有条目**一并删除**（连同 SQLite 记录）。

## What Changes

### 数据层（单一权威源：SQLite → TodoRepository 投影）
- 新增实体 `Models.MyFolder`：`Id`、`Name`。
- `TodoItem` 增加可空字段 `FolderId`（`null` = 未归类）。
- `DatabaseService`：
  - 新建 `folder` 表（`id`、`name`）。
  - `tasks` 表新增 `folder_id` 列（`TEXT NULL`），用既有 `EnsureColumn` 迁移保证老库升级不报错。
  - 新增方法：`LoadFolders()`、`InsertFolder(id, name)`、`RenameFolder(id, name)`、`DeleteFolder(id)`（连同其下任务一并删除）、`UpdateTaskFolder(id, folderId?)`、`LoadAll` 补读 `folder_id`。
- `ITodoRepository` / `TodoRepository`：
  - 暴露文件夹只读投影 `Folders`。
  - 方法：`AddFolder(name)`、`RenameFolder(id, name)`、`DeleteFolder(id)`（内部联删条目，返回被删条目数）、`SetFolder(id, folderId?)`。
  - `SetCompleted/Rename/SetPriority/SetDueDate` 等既有流程不受影响。

### 视图模型
- 新增 `ViewModels/FolderItemViewModel`：`Id`、`Name`、内含条目数等展示。
- `MainWindowViewModel`：
  - 维护文件夹列表、当前选中视图（全部/某文件夹/未归类）。
  - "全部任务/文件夹/未归类"三态，`DisplayItems` 据此过滤。
  - 新建/重命名/删除文件夹命令；删除前通过确认框取得用户确认。

### 视图（MainWindow.axaml）
- 外层改为「左侧边栏 + 右侧内容」布局。
- 顶部工具栏：「添加」按钮旁新增**文件夹图标**按钮。
- 左侧边栏三区：全部任务 / 各文件夹（含改名入口、删除入口）/ 未归类；点选切换右侧。
- 任务行新增「移到文件夹」下拉（含"未归类"选项），单选归属。

### 测试（TodoList.Tests，沿用临时库双路径交叉断言风格）
- 文件夹 CRUD：增删改落库且投影一致。
- 条目归属：`SetFolder` 落库且投影一致；未归类 `folder_id IS NULL`。
- 删文件夹联删条目：`DeleteFolder` 后其下条目从库与投影同时消失，返回的删除数为 N。
- 迁移：无 `folder_id` 列的旧库打开不报错，读取默认未归类。

## Impact

- **Affected code**：
  - 新增 `Models/MyFolder.cs`、`ViewModels/FolderItemViewModel.cs`
  - 修改 `Models/TodoItem.cs`（+`FolderId`）
  - 修改 `Database/DatabaseService.cs`（folder 表、迁移、CRUD）
  - 修改 `Repositories/ITodoRepository.cs`、`Repositories/TodoRepository.cs`（文件夹与归属 API）
  - 修改 `ViewModels/MainWindowViewModel.cs`、`MainWindow.axaml`（侧栏、文件夹图标、归属下拉）
  - 修改 `Tests/TodoList.Tests/TodoRepositoryTests.cs`（新增用例）
- **不变**：既有任务标题/优先级/截止日期/提醒逻辑；`config.json` 数据路径；非文件夹任务的增删改查行为。
- **风险点**：布局改动（外层 DockPanel→Grid+侧栏）需保证既有任务列表交互不回退；删除确认流程需明确。

## Requirements

### Requirement: 新建并命名文件夹
系统 SHALL 允许用户点击顶部文件夹图标，弹出输入框新建一个可命名的文件夹。
- **Scenario: 新建文件夹**
  - **WHEN** 用户点击文件夹图标并输入合法名称确认
  - **THEN** 左侧边栏出现该文件夹，右侧显示为空文件夹内容视图。

### Requirement: 文件夹收纳条目
系统 SHALL 允许把待办条目单归属到某文件夹（或"未归类"）。
- **Scenario: 条目移入文件夹**
  - **WHEN** 用户在任务行下拉中选择某文件夹
  - **THEN** 当前选中"全部/该文件夹"时显示该条目，条目 `FolderId` 落库。
- **Scenario: 条目回到未归类**
  - **WHEN** 用户在下拉中选择"未归类"
  - **THEN** 条目 `FolderId` 置空，出现在"未归类"视图。

### Requirement: 左侧边栏三视图
系统 SHALL 提供"全部任务 / 各文件夹 / 未归类"视图切换。
- **Scenario: 切换视图**
  - **WHEN** 用户点击边栏某视图
  - **THEN** 右侧列表仅显示该视图内的条目；筛选/排序在视图内仍生效。

### Requirement: 重命名文件夹
系统 SHALL 允许用户重命名文件夹。
- **Scenario: 重命名**
  - **WHEN** 用户对某文件夹触发重命名并输入新名
  - **THEN** 文件夹名称更新并落库，边栏显示新名。

### Requirement: 删除文件夹并确认
系统 SHALL 在删除文件夹前弹确认框，告知将删除其中条目；确认后该文件夹及其内条目一并从库中删除。
- **Scenario: 删除含条目的文件夹**
  - **WHEN** 用户删除某含 N 条条目的文件夹并确认
  - **THEN** 文件夹与其下 N 条条目一并从 SQLite 与投影移除；其它文件夹及未归类条目不受影响。
- **Scenario: 取消删除**
  - **WHEN** 用户在弹出的确认框选择取消
  - **THEN** 不执行任何删除。

### Requirement: 老库迁移
系统 SHALL 对已存在的旧库自动补齐 `folder_id` 列（幂等，重开不报错）。
- **Scenario: 旧库打开**
  - **WHEN** 打开无 `folder_id` 列的旧库
  - **THEN** 自动迁移成功，既有条目读取为未归类，无异常。

---

# Tasks

- [x] T1 新增 `Models/MyFolder.cs`（Id、Name）
- [x] T2 `Models/TodoItem.cs` 增加可空 `FolderId`
- [x] T3 `DatabaseService`：建 `folder` 表 + `tasks` 加 `folder_id` 列（`EnsureColumn` 迁移）+ 文件夹/归属 CRUD，`LoadAll` 补读 `folder_id`
- [x] T4 `ITodoRepository`/`TodoRepository`：`Folders` 投影、`AddFolder/RenameFolder/DeleteFolder/SetFolder`
- [x] T5 新增 `ViewModels/SidebarItemViewModel.cs`（三态视图 + 原地重命名）
- [x] T6 `MainWindowViewModel`：三态视图过滤 + 文件夹 CRUD 命令 + 删除确认流程
- [x] T7 `MainWindow.axaml`：改造为「左栏+右侧」布局、边栏（全部/各文件夹/未归类）、任务行归属下拉；新建文件夹入口位于边栏未归类下方（长方形按钮＋输入区，"＋新建文件夹"），文件夹删除为 🗑 图标，改名为双击文件夹名；`MainWindow.axaml.cs` 新建/改名按键、双击改名与删除确认对话框
- [x] T8 测试：文件夹 CRUD、条目归属、删文件夹联删条目、旧库迁移（新增 7 用例）
- [x] T9 验证：`dotnet build` 0 错误、`dotnet test` 16/16 全绿；用户人工测试通过

---

### Checklist

- [x] 新建文件夹入口位于边栏"未归类"项下方：长方形「＋新建文件夹」按钮点击展开命名输入（回车/确认创建；点击输入区外自动收起）
- [x] 左侧边栏含"全部任务/各文件夹/未归类"，点选切换右侧（含各自条目数）
- [x] 任务行可下拉归属到文件夹或未归类，单归属落库
- [x] 文件夹可重命名：双击文件夹名进入原地改名（回车保存；点击其它区域自动保存退出）
- [x] 删除文件夹：行内红色 🗑 图标按钮触发确认，确认后内条目一并删除；取消不删（确认框显示被删条数）
- [x] 老库打开自动迁移 `folder_id` 不报错
- [x] `dotnet build --no-restore` 0 错误、`dotnet test` 全绿（16/16）
- [x] 用户运行应用验收通过，本功能完结