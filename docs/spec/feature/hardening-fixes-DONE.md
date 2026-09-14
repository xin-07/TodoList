# 加固修复（前后端一致性修复与重构） — complex · DONE

> 单文档：SPEC（Why/What/Impact/Requirements）+ Tasks + Checklist。
> 状态：DONE。分批实现完毕，每批编译 + 单测通过并由用户人工验收后提交（B1..B5 五笔）。
> 统一硬约束：**每批修复都保持修改点之外的既有行为完全不变**；含用户可见行为变化的批次（B2/B3/B4/B5）各自交付用户测试。
> 已确认决策：分批逐步；B4 旧库复制到 exe 旁目录并保留原文件；B6 时间注入纳入技术债 DEFERRED，本次不做。

## Why

深度审查 (karpathy) 发现 12 处潜在问题。按风险与影响分 5 个批次落地，每批独立 commit。根因分类：多数为 **code/design wrong**，个别为 **test gap**。

## What Changes（分批）

### B1 — 零行为影响的诊断/工程层修复（独立提交，无需用户重测）
- **P5 全局未处理异常日志**：`Program.cs` 挂 `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`，把异常写成日志文件（输出目录 `logs/`）。纯增量，仅在异常时写盘，正常行为零变化。
- **P9 测试工程 SDK 对齐**：`Tests/TodoList.Tests.csproj` 由 `net10.0` 降至 `net8.0`，与主工程一致（主工程不升，避免提升 SDK 门槛）。仅影响构建环境，不涉及应用逻辑。
- **P10 folder_id 索引**：`Initialize` 加 `CREATE INDEX IF NOT EXISTS idx_tasks_folder_id ON tasks(folder_id)`。只加索引，**不加外键**（外键需兼容含悬挂引用的老库，风险中转收益低，排除）。
- **P11 清理死代码**：删除 `TodoItemViewModel.SetPriorityCommand`（当前 XAML 未使用）。属 public 撤销，低风险。

### B2 — 定时器不覆盖用户输入（独立提交，1 轮人工验收）
- **P3**：[TodoItemViewModel.RefreshDue](file:///../../../ViewModels/TodoItemViewModel.cs) 在回写 `DueDateText` 前判断"该行的截止日期输入框是否处于编辑聚焦中"；聚焦中则跳过回写。失焦/回车路径（`OnDueDateLostFocus`/`OnDueDateKeyDown`）仍按原逻辑刷新。

### B3 — 消除选中链重入 + 补齐变更通知（结对重构，需单测 + 单独验收）
- **P2** `MainWindowViewModel.ApplyFilter` 由 `Clear + 全量重建` 改为对 `_filterDisplay` 的**增量 diff**（仅增删移动变化的项）。根治 ComboBox 选中回调整链内的集合重置。
- **P4** `TodoItem.Title`、`TodoItem.FolderId` 改用 `SetProperty` 发出通知，令 [TodoItemViewModel.OnItemPropertyChanged](file:///../../../ViewModels/TodoItemViewModel.cs) 与 [MainWindowViewModel.OnItemPropertyChanged](file:///../../../ViewModels/MainWindowViewModel.cs) 中对应分支真正生效，视图不再依赖"全量重建"隐式刷新。
- **必须结对**：只做 P2 或只做 P4 都会引入"标题/归属改完不刷新"新 bug。

### B4 — 单文件发布数据路径 + 迁移（独立任务，单独验收）
- **P1** `App.ResolveDatabasePath`：发布版回退到 exe 所在目录；启动时若新路径库不存在且旧临时位置存在，则**复制迁移旧库**（保留原文件不删，仅复制）。
- **P6** 日期读写对齐：读取用 `DateTime.TryParseExact("o", InvariantCulture, RoundTripKind)`，与写入的 `"o"` 格式对称。随 B4 一并处理，跑一次"旧库重开"用例。

### B5 — 重命名/新建输入区"点击外部退出"交互修复（独立提交，人工验收）
- **P7** 将退出逻辑从"根 Grid 的 `PointerPressed`"（常被 Button/ListBox 标记 handled 而收不到）改为可靠触发点（输入框 `LostFocus` + `ListBox.SelectionChanged`），消除编辑态残留。

### B6 — 时间依赖注入（低优先，默认列入技术债，除非用户要求本次实现）
- **P8** 给 `MainWindowViewModel`/`TodoItemViewModel` 以可选参数注入 `Func<DateTime>` 时钟（默认 `DateTime.Now`/`Today`），为到期/过期逻辑加可测边界。改动面较大，**建议纳入 DEFERRED** 而非本次强制；若本次做，单独一个 commit。

## Impact

- **Affected code**：`Program.cs`、`Tests/TodoList.Tests.csproj`、`Database/DatabaseService.cs`、`ViewModels/TodoItemViewModel.cs`、`ViewModels/MainWindowViewModel.cs`、`Models/TodoItem.cs`、`App.axaml.cs`、`MainWindow.axaml.cs`、`Tests/.../TodoRepositoryTests.cs`。
- **不变**：任务增删改查、优先级/截止日期/完成状态语义、文件夹 CRUD 与归属、`config.json` 单一权威源、数据模型字段。
- **风险与还原点**：B3 最大（回归面），需单测锁定；B4 涉及老库迁移须显式测试；B1 的 P11 撤销一个未用命令。

## Requirements

### Requirement: 全局未处理异常落盘（B1）
系统 SHALL 在任一未处理异常触发时写出日志文件，不影响正常路径。
- **Scenario**: 任一未捕获异常发生
  - **WHEN** 运行时抛出未处理异常
  - **THEN** 异常与堆栈写入 `logs/` 下日志，程序按未处理异常既有行为结束。

### Requirement: 定时器不打断截止日期输入（B2）
- **Scenario**: 输入截止日期中途到点
  - **WHEN** 用户正在编辑某任务截止日期且下一分钟 tick 到达
  - **THEN** 该输入框内容不被覆盖；失焦/回车后仍正确刷新显示。

### Requirement: 增量过滤且标题/归属实时刷新（B3）
- **Scenario**: 修改标题或归属
  - **WHEN** 用户修改某任务标题，或在下拉切换归属
  - **THEN** 视图实时反映，ComboBox 选中链内不重建，无崩溃。
- **Scenario**: 搜索/切视图
  - **WHEN** 输入搜索或切换边栏视图
  - **THEN** 结果与原行为一致（仅变化项增删移动）。

### Requirement: 发布版数据落在稳定位置且老库迁移（B4）
- **Scenario**: 单文件发布启动
  - **WHEN** 以单文件 exe 启动且新路径无库、旧临时位置有库
  - **THEN** 自动复制旧库到新路径；原旧库保留。
- **Scenario**: 旧库重开
  - **WHEN** 打开含历史日期记录的旧库
  - **THEN** 日期正确读取，无解析异常。

### Requirement: 编辑态点击外部可退出（B5）
- **Scenario**: 重命名/新建中输入时点击其它区域
  - **WHEN** 用户处于文件夹重命名或新建输入态，点击其它区域
  - **THEN** 正确退出（保存或放弃），无编辑态残留。

---

# Tasks

- [x] B1-T1 `Program.cs`：挂 `UnhandledException`/`UnobservedTaskException` 写日志（输出目录 `logs/`）
- [x] B1-T2 `Tests/TodoList.Tests.csproj`：`net10.0` → `net8.0`
- [x] B1-T3 `DatabaseService.Initialize`：加 `idx_tasks_folder_id` 索引（仅索引，不加外键）
- [x] B1-T4 删除 `TodoItemViewModel.SetPriorityCommand`
- [x] B2-T5 `RefreshDue` 加"聚焦时跳过回写"守卫
- [x] B3-T6 `TodoItem.Title/FolderId` 改 `SetProperty`
- [x] B3-T7 `ApplyFilter` 增量 diff 重写
- [x] B3-T8 单测：`TodoItem` 通知 + 切换归属无整体重建（新增 2 用例）
- [x] B4-T9 `App.ResolveDatabasePath`：发布版路径回退 + 旧库复制迁移
- [x] B4-T10 日期读取改严格 `TryParseExact("o", InvariantCulture, RoundTripKind)`
- [x] B4-T11 单测：非固定文化下日期正确解析（新增 1 用例；发布版回退/迁移走人工验收）
- [x] B5-T12 重命名退出改 `LostFocus` + 边栏 `SelectionChanged`（纯增量新增可靠触发点，保留原指针兜底）
- [x] B6-T13（DEFERRED）时间依赖注入时钟 → 见下方技术债登记
- [x] 每批后：`dotnet build --no-restore` + `dotnet test` 全绿（16→19 用例）
- [x] 人工验收：B2/B3/B4/B5 由用户运行验证通过

## 技术债登记（宪法 Article 3）

- `// DEFERRED：到期/过期逻辑中的 DateTime.Now/Today 无注入边界，不可测；defers-to：2026-12-31；owner：xiny`。范围：`MainWindowViewModel.ScanDueAlarms`、`TodoItemViewModel.RefreshDue`、`DueDatePlaceholder` 三处时间源。本次为控制回归风险、遵循"不急改"决策而缓做；届时给两个 VM 注入可选 `Func<DateTime>` 时钟并补到期逻辑单测。

---

### Checklist
- [x] B1：编译通过、测试全绿、常规运行无行为差异、异常可写 `logs/`（用户已随后续批次验收运行）
- [x] B2：输入截止日期途中 tick 不覆盖（人工验收通过）
- [x] B3：改标题/切归属实时刷新、下拉不崩溃、搜索与切视图结果不变（人工验收通过）
- [x] B4：开发模式回归无差异、单文件 exe 数据落 exe 旁目录、旧数据迁移（人工验收通过）
- [x] B5：编辑态点外部可靠退出（人工验收通过）
- [x] 所有批次 `dotnet build --no-restore` 0 错误、`dotnet test` 全绿（19/19）
- [x] 每批 commit 前向用户展示拟提交信息并获确认（宪法 Article 1/4）