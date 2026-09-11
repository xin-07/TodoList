# TodoList 功能增强 Roadmap Spec

## Why

当前项目是最小 MVP（任务增/勾选/删、统计、主题跟随、SQLite 单一权威源）。对照参考仓库 `TangIsLearning/TodoList`，其核心能力（优先级、截止日期、分类、标签、周期性任务、搜索/筛选、系统托盘、任务提醒、统计报表、i18n、数据导出等）本项目缺失。需按统一架构逐阶段补足。

用户明确要求：以 `karpathy-guidelines` 深度实现；**排除 WebDAV 云同步**。

## 架构约束（所有阶段必须遵守）

- **单一权威源（SSOT）**：每一定义只能定义一次。DB 路径等已在 `config.json` 单一定义。新增字段/表：`DatabaseService` 建表+SQL → `TodoRepository` 暴露只读投影 → ViewModel 消费。禁止在 UI/脚本重复硬编码同一事实。
- **宪法**：Art.1 commit 含 Why/What；Art.2 禁止空 `catch{}`/`@ts-ignore`/null-stub；Art.3 技术债写 `// DEFERRED: reason, defer-to, owner`。
- **提交规约**：每次 commit 前向用户展示信息并获明确确认。
- **MVVM 纯净**：模型纯数据，仓库唯一写点，ViewModel 只消费。
- **输入端校验**：任何用户输入（标题等）须校验（空值/去空白/超长），不得写空 catch。

## What Changes

按阶段新增能力（**WebDAV 云同步不实现**）：

- **阶段一 · 任务核心**：编辑标题；优先级（枚举 none/low/medium/high + 排序/高亮）；截止日期字段 + 桌面到期提醒；输入校验；对 `TodoRepository` 的单元测试（临时/内存 DB）。
- **阶段二 · 分类与标签**：`categories`、`tags`、`task_relations`(多对多) 三表；任务挂分类/标签；分类/标签 CRUD 与筛选。
- **阶段三 · 周期性任务**：`is_recurring`/`recurrence_type`/`recurrence_interval`/`recurrence_count`/`parent_task_id`；按 daily/weekly/monthly/yearly 生成子任务。
- **阶段四 · 视图与交互**：关键词搜索、按状态/优先级/分类/标签筛选、排序、空状态提示。
- **阶段五 · 桌面特色**：系统托盘（关窗最小化/托盘退出）、到期通知、窗口置顶、开机自启动（平台抽象）。
- **阶段六 · 设置与主题/i18n**：设置持久化（写 config.json，SSOT）、主题手动切换（浅色/深色）、多语言（中/英）。
- **阶段七 · 统计与导出**：按天/周/月完成趋势报表；JSON/SQLite 备份导出导入（**不导出 Excel 优先，先做数据备份**）。
- **阶段八 · 工程化**：手工 DI 收敛 App 装配、`dotnet publish` 打包 + 图标、README 功能清单自动同步、全程单元测试覆盖。

## Impact

- **Affected specs/capabilities**：任务管理、分类、标签、设置、统计、数据、平台集成（新增）。
- **Affected code（现状）**：
  - `Models/TodoItem.cs`（加优先级/截止/周期字段；新增 Category/Tag 模型）
  - `Database/DatabaseService.cs`（新增表/字段与 SQL）
  - `Repositories/TodoRepository.cs` 与 `ITodoRepository.cs`（新增方法；新分类/标签仓库）
  - `ViewModels/MainWindowViewModel.cs`（新增筛选/搜索/统计/设置命令）
  - `MainWindow.axaml` / `.axaml.cs`（新增控件）
  - `config.json`（设置持久化、SSOT 扩展）
  - 新增：平台抽象、系统托盘/提醒、单元测试工程。

## ADDED Requirements

### Requirement: 任务标题可编辑
系统 SHALL 提供对现有任务标题的原地编辑，改动经仓库写入库。
- **Scenario: 编辑成功**
  - **WHEN** 用户在任务行编辑标题并确认
  - **THEN** 标题更新并持久化到权威源（SQLite），界面刷新；空白标题被拒绝并提示。

### Requirement: 优先级
系统 SHALL 支持 none/low/medium/high 四档优先级，可设置并按优先级排序/高亮。
- **Scenario: 设置并排序**
  - **WHEN** 用户为任务设置"高"
  - **THEN** 该任务标高高亮，并按优先级从高到低排列。

### Requirement: 截止日期与到期提醒
系统 SHALL 支持为任务设置截止日期，并在到期时于桌面端发出提醒通知。
- **Scenario: 到期提醒**
  - **WHEN** 未完成任务到达截止时间且应用在运行
  - **THEN** 触发一次桌面系统通知提示该任务到期。
- **备注**：提醒仅桌面端；不引入 WebDAV。

### Requirement: 分类与标签
系统 SHALL 支持分类（单属）、标签（多对多）与二者筛选。
- **Scenario: 按分类/标签筛选**
  - **WHEN** 用户选择一个分类或标签
  - **THEN** 列表仅显示匹配任务。

### Requirement: 周期性任务
系统 SHALL 支持按日/周/月/年周期生成任务实例，可设间隔与次数/无限。
- **Scenario: 生成周期实例**
  - **WHEN** 用户完成/达到一个周期任务实例
  - **THEN** 按规则生成下一个实例（次数取尽则停止）。

### Requirement: 搜索 / 筛选 / 排序 / 空状态
系统 SHALL 提供关键词搜索、多条件筛选、排序与空状态引导。
- **Scenario: 无匹配**
  - **WHEN** 筛选或搜索后无结果
  - **THEN** 显示空状态引导文案而非空白列表。

### Requirement: 系统托盘与关闭行为
系统 SHALL 关闭窗口时最小化到系统托盘，托盘菜单可恢复窗口或退出。
- **Scenario: 托盘退出**
  - **WHEN** 用户在托盘菜单选择退出
  - **THEN** 应用彻底退出。

### Requirement: 窗口置顶与开机自启动
系统 SHALL 支持切换窗口置顶与配置开机自启动（桌面端，平台相关）。
- **Scenario: 置顶切换**
  - **WHEN** 用户开启置顶
  - **THEN** 窗口保持最前。

### Requirement: 设置持久化（SSOT）
系统 SHALL 将用户设置（主题、置顶、自启动等）持久化，写入 `config.json` 单一权威源。
- **Scenario: 设置重启保留**
  - **WHEN** 用户修改设置后重启应用
  - **THEN** 设置保持生效。

### Requirement: 主题手动切换
系统 SHALL 支持浅色/深色手动切换。
- **Scenario: 切换主题**
  - **WHEN** 用户选择深色
  - **THEN** 界面即时切换且持久化。

### Requirement: 多语言（中/英）
系统 SHALL 支持简体中文与英文切换。
- **Scenario: 切换语言**
  - **WHEN** 用户选择 English
  - **THEN** 界面文案切换到英文。

### Requirement: 统计报表
系统 SHALL 提供任务完成统计（按天/周/月趋势、完成率）。
- **Scenario: 查看报表**
  - **WHEN** 用户打开统计视图
  - **THEN** 展示基于权威源的完成趋势数据。

### Requirement: 数据备份导出/导入
系统 SHALL 支持将数据库导出为 JSON/备份文件并可从备份导入。
- **Scenario: 导出备份**
  - **WHEN** 用户执行导出
  - **THEN** 生成备份文件；导入时以权威源为准合并/覆盖（单一定义，避免两份真相）。

### Requirement: 单元测试覆盖仓库
系统 SHALL 对 `TodoRepository` 提供单元测试，使用隔离 DB，验证 SSOT 行为。
- **Scenario: 测试通过**
  - **WHEN** 运行 `dotnet test`
  - **THEN** 增/改/删/持久化用例全部通过。

## MODIFIED Requirements

### Requirement: 装配收敛（DI）
现有 `App.axaml.cs` 手动装配各处对象，改为集中依赖装配（手工 DI 或轻量容器），保持 SSOT 与可测。
- **Scenario**: 装配单一入口，ViewModel 依赖通过构造注入。

### Requirement: 数据路径 SSOT 保持
维持 `config.json` 唯一定义数据目录/文件名；所有新增设置同样只定义一次、各处引用。

## REMOVED Requirements

### Requirement: WebDAV 云同步
**Reason**: 用户明确不实现（需可回滚、避免两份真相，复杂度高且非当前诉求）。
**Migration**: 不引入 WebDAV 相关包或代码；同步能力留待后续单独立项评估。