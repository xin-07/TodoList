# TodoList 功能增强 Roadmap — complex · IN_PROGRESS

> 本文件为单一文档：SPEC（Why/What/Impact/Requirements）+ Tasks + Checklist。状态：阶段一完成，阶段二暂不采用，后续阶段待实现。

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
- **阶段二 · 分类与标签**：`categories`、`tags`、`task_relations`(多对多) 三表；任务挂分类/标签；分类/标签 CRUD 与筛选。**【已按用户决定暂不采用 2026-09-11，代码已回退】**
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

### Requirement: 分类与标签（[DEFERRED] 用户暂不采用）
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

---

# Tasks

> 分阶段实现；每阶段独立可交付、可验证。所有数据新增遵循单一权威源（DatabaseService → Repository → ViewModel）。WebDAV 不实现。
>
> **阶段二（分类与标签）已按用户决定暂不做**（2026-09-11，已彻底回退，不保留代码）；如需可后续重新评估。

- [x] 阶段一：任务核心增强
  - [x] T1.1 模型扩展：`TodoItem` 增加 `Priority`（枚举 none/low/medium/high）、`DueDate?`、编辑用可变属性；`Priority` 枚举定义一次（放在 Models）
  - [x] T1.2 `DatabaseService`：`tasks` 表新增 `priority`、`due_date` 列 + 迁移逻辑；`UpdateTitle`、`UpdatePriority`、`UpdateDueDate` SQL
  - [x] T1.3 `ITodoRepository`/`TodoRepository`：新增 `Rename`、`SetPriority`、`SetDueDate`
  - [x] T1.4 输入校验：提炼标题校验（空/去空白/超长限制），Add 与 Rename 复用；拒绝空白并提示（统一为 `Models.TaskTitle`）
  - [x] T1.5 UI：列表行标题可原地编辑（Enter 提交/失焦保存/Focus 编辑）；优先级下拉/选择控件与高亮排序
  - [x] T1.6 截止日期 UI：日期选择器 + 展示待到期/逾期样式
  - [x] T1.7 桌面到期提醒：到期检查 + 系统通知（桌面端；不引入 WebDAV）
  - [x] T1.8 单元测试：搭建测试工程，对 `TodoRepository` 增/改/删/持久化用隔离 DB 测试
  - [x] 验证：`dotnet build` 0 错误；`dotnet test` 通过（9/9）；手动走编辑/优先级/截止

- [ ] 阶段二：分类与标签（[DEFERRED] 用户暂不采用）
  - [ ] [DEFERRED] T2.1 新表 `categories`、`tags`、`task_relations`；模型 `Category`、`Tag` 单一定义
  - [ ] [DEFERRED] T2.2 `DatabaseService` 分类/标签读写 SQL；分类/标签仓库（只读投影）
  - [ ] [DEFERRED] T2.3 UI：分类/标签 CRUD + 任务挂载 + 按分类/标签筛选
  - [ ] [DEFERRED] 验证：分类/标签可增删与筛选，数据落库重启保留

- [ ] 阶段三：周期性任务
  - [ ] T3.1 `TodoItem` 增加周期字段；`tasks` 表列 + 迁移
  - [ ] T3.2 周期规则引擎（daily/weekly/monthly/yearly + interval + count/无限），生成实例
  - [ ] T3.3 UI：周期设置入口；完成时按规则生成下一实例
  - [ ] 验证：周期生成/次数取尽逻辑正确

- [ ] 阶段四：视图与交互
  - [ ] T4.1 关键词搜索（FilteredItems 投影建立在仓库只读源上）
  - [ ] T4.2 多条件筛选（状态/优先级/分类/标签）与排序（优先级/截止/创建）
  - [ ] T4.3 空状态提示控件
  - [ ] 验证：搜索/筛选/排序正确；无结果显示空状态

- [ ] 阶段五：桌面特色
  - [ ] T5.1 平台抽象：常量接口/工厂（借鉴参考仓库思路，轻量）
  - [ ] T5.2 系统托盘：关闭最小化 + 托盘恢复/退出
  - [ ] T5.3 到期通知（迁移/复用 T1.7）、窗口置顶、开机自启动
  - [ ] 验证：托盘/置顶/自启动在桌面端生效

- [ ] 阶段六：设置与主题 / i18n
  - [ ] T6.1 设置持久化：主题、置顶、自启动等写入 `config.json`（SSOT 扩展）
  - [ ] T6.2 主题手动切换（浅色/深色）即时生效并持久化
  - [ ] T6.3 i18n：中/英资源，运行时切换
  - [ ] 验证：设置重启保留；主题/语言实时切换

- [ ] 阶段七：统计与数据导出
  - [ ] T7.1 完成趋势统计（天/周/月、完成率），基于仓库只读源
  - [ ] T7.2 导出/导入备份（JSON 或 SQLite 备份）；导入以权威源为准，避免两份真相
  - [ ] 验证：统计正确；导出→清空→导入恢复一致

- [ ] 阶段八：工程化收尾
  - [ ] T8.1 装配收敛（手工 DI / 轻量容器），App.axaml.cs 单一入口
  - [ ] T8.2 `dotnet publish` 打包 + 应用图标
  - [ ] T8.3 README 功能清单自动同步（更新 update-readme.js）
  - [ ] T8.4 全程回归：`dotnet build`/`dotnet test` 全绿
  - [ ] 验证：发布可运行；测试全绿；文档一致

# Task Dependencies

- 阶段二(T2) 依赖 阶段一(T1) 的模型/仓库基线
- 阶段三(T3) 依赖 T1 的 deadline/仓库结构
- 阶段四(T4) 依赖 T1、T2（筛选需要优先级/分类/标签）
- 阶段五(T5) 依赖 T1.7 提醒；平台抽象可独立
- 阶段六(T6) 依赖 config.json 扩展，可与 阶段二~四 并行推进
- 阶段七(T7) 依赖 T1、T2 数据
- 阶段八(T8) 依赖全模块就绪，收尾

---

# Checklist

## 阶段一 · 任务核心
- [x] 任务标题可原地编辑，保存落库，空白被拒绝
- [x] 优先级 four-level 可设置，排序与高亮正确
- [x] 截止日期可设置，过期/临期有样式
- [x] 到期触发桌面提醒（不引入 WebDAV）
- [x] 输入校验覆盖空值/去空白/超长（统一 `TaskTitle` 单一定义）
- [x] `TodoRepository` 单元测试通过（增/改/删/持久化）
- [x] `dotnet build` 0 错误

## 阶段二 · 分类与标签（[DEFERRED] 用户暂不采用）
- [ ] [DEFERRED] 分类/标签可 CRUD，数据落库重启保留
- [ ] [DEFERRED] 任务可挂分类/标签
- [ ] [DEFERRED] 按分类/标签筛选正确

## 阶段三 · 周期性任务
- [ ] 周期规则生成实例正确（daily/weekly/monthly/yearly + interval + count/无限）
- [ ] 次数取尽后停止

## 阶段四 · 视图与交互
- [ ] 关键词搜索正确
- [ ] 多条件筛选与排序正确
- [ ] 无结果显示空状态引导

## 阶段五 · 桌面特色
- [ ] 关闭最小化到托盘，托盘可恢复/退出
- [ ] 到期通知、窗口置顶、开机自启动生效

## 阶段六 · 设置 / 主题 / i18n
- [ ] 设置写 config.json，重启保留
- [ ] 主题手动切换即时生效
- [ ] 中/英语言切换生效

## 阶段七 · 统计与导出
- [ ] 完成趋势与完成率正确
- [ ] 导出→清空→导入恢复一致（以权威源为准）

## 阶段八 · 工程化
- [ ] 装配单一入口（DI 收敛）
- [ ] 发布可运行 + 图标
- [ ] README 功能清单自动同步
- [ ] `dotnet build`/`dotnet test` 全绿

## 全局约束（贯穿所有阶段）
- [ ] 全阶段遵循单一权威源：定义仅一次，UI/脚本不重复硬编码
- [ ] 无空 `catch{}`/stub（宪法 Art.2）
- [ ] 技术债均标注 `// DEFERRED: ...`（Art.3）
- [ ] 每次 commit 前已向用户展示并获确认
