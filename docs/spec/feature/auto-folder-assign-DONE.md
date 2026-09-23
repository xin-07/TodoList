# 新建任务按当前视图自动归属文件夹 — complex · DONE

> 单文档：SPEC（Why/What/Impact/Requirements）+ Tasks + Checklist。
> 状态：DONE（用户人工测试通过，功能完结）。

## Why

当前新增任务**必定未归类**：`MainWindowViewModel.AddTask()` 只把标题交给 `_repo.Add(title)`，仓库与 `DatabaseService.Insert` 都不写 `folder_id`。

结果是：用户已在边栏选中某个文件夹（右侧列表只显示该文件夹内容），却在顶部输入框新建任务时，任务落到了「未归类」——**在用户当前可见的列表里直接消失**，必须再手动到行内下拉改归属。这是体验断裂，也是多一步无谓操作。

用户诉求：**边栏选中某文件夹时，新建任务自动归入该文件夹；未选中任何文件夹时，默认归入未归类（因而同时出现在「未归类」与「全部任务」中）。**

### 已与用户确认的两项决策

1. **归属规则（用户 2026-09-23 确认）**：只有边栏选中 `SidebarKind.Folder` 时才自动归属；选中「全部任务」或「未归类」时一律 `folder_id = null`（未归类）。
   - 采用此规则的原因：边栏**永远存在选中项**（启动默认「全部任务」，见 `RebuildSidebar()`），不存在真正的"未选中"状态；只有 `Kind == Folder` 才代表"用户选择了某个文件夹"。不引入"记住上次选中文件夹"这类隐藏状态，避免任务落到用户当前看不见的地方。
2. **接口变更（用户 2026-09-23 批准，宪法 Art.7 契约变更）**：`ITodoRepository.Add` 增加可选参数 `folderId`。
   - 采用此方案的原因：一次 `INSERT` 连同 `folder_id` 写入，只触发一次 `Changed` 通知，投影与权威源原子一致。
   - 被否决方案：保持 `Add(title)` 不变、再调 `SetFolder` 补归属——需两次写库、两次 `Changed` 通知，且 `Add` 不返回 Id，必须在 ViewModel 猜"最新一条"，属补丁式修改（违反 Art.12）。

## What Changes

### 数据层（单一权威源：SQLite → TodoRepository 投影）
- `DatabaseService.Insert(id, title, createdAt)` → `Insert(id, title, createdAt, folderId)`：`INSERT` 语句补写 `folder_id` 列（`folderId` 为 null 时写 `NULL`）。`tasks.folder_id` 列已存在（`folders-add` 阶段引入 + `EnsureColumn` 迁移），本次**不新增表/列/迁移**。
- `TodoRepository.Add(title)` → `Add(title, folderId = null)`：校验通过后，投影对象 `TodoItem.FolderId` 与入库值同源同值。
- `ITodoRepository.Add(title)` → `Add(title, folderId = null)`：默认参数保证既有调用点（27 处测试）零改动。

### 视图模型
- `MainWindowViewModel.AddTask()`：落库前计算归属——`SelectedSidebar?.Kind == SidebarKind.Folder ? SelectedSidebar.Key : null`，作为第二参数传给 `_repo.Add`。
- 其余不变：边栏计数刷新、排序、过滤仍由既有 `OnRepoChanged`/`ApplyFilter` 链路驱动（新条目自带归属，因此会正确出现在当前文件夹视图内）。

### 视图（MainWindow.axaml）
- **不改动**。顶部输入框、边栏、行内归属下拉均保持原样（无新增控件、无文案变更）。

### 测试（TodoList.Tests，沿用临时独立库双路径交叉断言风格）
- 仓库层：
  - `Add` 带 `folderId` → 投影 `FolderId` 与权威源 `folder_id` 一致；重开库后仍保留（落库而非仅内存）。
  - `Add` 不传 `folderId` → `FolderId` 为 null（未归类），权威源 `folder_id IS NULL`。
- ViewModel 层：
  - 边栏选中某文件夹 → `AddTaskCommand` 新建的任务归入该文件夹，且出现在该文件夹视图的 `DisplayItems` 中、「未归类」视图中不出现。
  - 边栏选中「全部任务」/「未归类」→ 新建的任务 `FolderId` 为 null。

## Impact

- **Affected code**：
  - `Database/DatabaseService.cs`（`Insert` 增参并写 `folder_id`）
  - `Repositories/ITodoRepository.cs`、`Repositories/TodoRepository.cs`（`Add` 增可选参）
  - `ViewModels/MainWindowViewModel.cs`（`AddTask` 计算归属）
  - `Tests/TodoList.Tests/TodoRepositoryTests.cs`、`Tests/TodoList.Tests/B3NotificationAndFilterTests.cs`（新增用例）
- **不变**：`MainWindow.axaml` / `.axaml.cs`；`TodoItem`、`MyFolder`、`SidebarItemViewModel`、`TodoItemViewModel`；文件夹 CRUD 与删除确认流程；`config.json` 数据路径；DB schema（不新增列，无需迁移）。
- **兼容性**：`Add` 新参数为可选且默认为 null，既有调用语义（未归类）与编译结果均不变。
- **风险点**：
  - 顶部输入框兼作**关键字搜索框**（输入即过滤列表）。选中文件夹后输入新任务标题时，列表会先按该文字过滤；添加成功即清空输入框，过滤随之解除。属既有行为，本次不改、不影响归属正确性。

## Requirements

### Requirement: 新建任务按当前选中文件夹自动归属
系统 SHALL 在用户于顶部输入框新增任务时，依据边栏当前选中的视图自动确定归属，并**在落库时一次写入**该归属。
- **Scenario: 选中文件夹时新建**
  - **WHEN** 边栏选中文件夹「工作」，用户在顶部输入框输入标题并回车/点击「添加」
  - **THEN** 新任务 `folder_id` = 「工作」的 Id，出现在「工作」视图与「全部任务」视图，不出现在「未归类」视图；重启应用后归属保持。
- **Scenario: 未选中文件夹时新建**
  - **WHEN** 边栏选中「全部任务」或「未归类」，用户新增任务
  - **THEN** 新任务 `folder_id` 为 NULL（未归类），出现在「未归类」视图与「全部任务」视图。
- **Scenario: 标题非法时不写库**
  - **WHEN** 输入为空白或超长
  - **THEN** 拒绝新增并提示错误，不产生任何记录（既有校验行为保持不变）。

---

# Tasks

- [x] T1 `DatabaseService.Insert` 增加 `folderId` 参数，`INSERT` 补写 `folder_id`（null 写 SQL NULL）
- [x] T2 `ITodoRepository.Add` / `TodoRepository.Add` 增加可选参数 `folderId = null`；投影与入库同源
- [x] T3 `MainWindowViewModel.AddTask`：按 `SelectedSidebar.Kind == Folder` 取归属，否则 null
- [x] T4 测试：仓库层 2 用例（带归属落库 + 重开保留；不传参为未归类）+ VM 层 2 用例（选中文件夹归属正确且进入该视图；选中全部/未归类为未归类）
- [x] T5 验证：`dotnet build --no-restore` 0 错误、`dotnet test` 34/34 全绿（基线 30 + 新增 4）
- [x] T6 用户人工测试：选中文件夹新建 / 未选中新建 / 重启保留（2026-09-23 通过）

# Checklist

- [x] 边栏选中具体文件夹时，顶部新增的任务归入该文件夹并立即出现在当前列表
- [x] 边栏选中「全部任务」或「未归类」时，新增任务为未归类，同时出现在「未归类」与「全部任务」
- [x] 归属在 `Add` 时一次落库（单次 INSERT、单次 `Changed`），非"先建后改"
- [x] 重启应用后归属保持
- [x] 空白/超长标题仍被拒绝，不写库
- [x] `Add` 既有调用点（27 处测试）因默认参数无需改动
- [x] `dotnet build --no-restore` 0 错误、`dotnet test` 全绿（34/34）
- [x] 用户运行应用验收通过（2026-09-23）