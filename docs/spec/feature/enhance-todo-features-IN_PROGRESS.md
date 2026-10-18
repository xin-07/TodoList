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

### Requirement: 完成置灰展示
系统 SHALL 在任务勾选完成后整行淡化显示（含标题/优先级条/截止日期），区分未完成任务。
- **Scenario: 勾选后整行置灰**
  - **WHEN** 用户勾选某任务为完成
  - **THEN** 该行整行淡化；取消勾选后恢复；重启/重排后状态保持。
- **修根因**：类绑定(pseudo-class)运行时难刷新，改用普通属性绑定 `Opacity` + ViewModel 广播 `RowOpacity`，实时响应勾选。

### Requirement: 分类与标签（[DEFERRED] 用户暂不采用）
系统 SHALL 支持分类（单属）、标签（多对多）与二者筛选。
- **Scenario: 按分类/标签筛选**
  - **WHEN** 用户选择一个分类或标签
  - **THEN** 列表仅显示匹配任务。

### Requirement: 周期性任务
系统 SHALL 支持按日/周/月/年周期自动续期的任务，可设间隔与次数/无限。
- **Scenario: 生成周期实例**
  - **WHEN** 用户完成一个带周期规则且已设置截止日期的任务
  - **THEN** 同一任务的截止日期按规则滚动到下一周期（当前周期基础上加 N 个周期单位），已完成状态重置为未完成，其余属性不变。
- **Scenario: 次数取尽**
  - **WHEN** 剩余次数为 1 的周期任务被完成
  - **THEN** 不再滚动：任务保持完成态，周期规则清除（归为普通任务）。
- **Scenario: 无截止基点**
  - **WHEN** 任务未设置截止日期
  - **THEN** 无法设置周期规则；清空截止日期时周期规则一并清空（两者绑定，不产生不可触发的悬空规则）。

> 2026-10-17 契约修订（Article 7，用户确认）：原设计为模板/实例两级（`is_recurring`/`recurrence_type`/`recurrence_interval`/`recurrence_count`/`parent_task_id`，完成时克隆生成子任务）。**采用滚动式** —— 同条任务滚动到下一期、不保留完成历史：`parent_task_id` 取消；`recurrence_count`（总次数）语义由 `recurrence_remaining`（剩余次数，NULL=无限）取代——滚动式不保存发生历史，无法推算"已发生次数"，剩余次数即可表达"次数取尽"。数据库新增 3 列：`recurrence` / `recurrence_interval` / `recurrence_remaining`（无 `is_recurring`，`recurrence != 'None'` 即开启）。

### Requirement: 搜索 / 筛选 / 排序 / 空状态
系统 SHALL 提供关键词搜索、多条件筛选（状态/优先级）、用户可选排序与空状态引导。

> 2026-10-17 细化（Article 7 契约修订，用户确认后生效）：
> - **关键词搜索已实现**：`MainWindowViewModel.ApplyFilter()` 按标题过滤（忽略大小写），与当前视图（全部/文件夹/未归类）叠加，采用增量 diff（不触发 Reset）。本次不再改动该逻辑。
> - **筛选维度**：新增"状态"（全部/未完成/已完成）与"优先级"（全部/高/中/低/无）两个下拉，与标题搜索、边栏视图**取交集**。
> - **排序**：由写死改为用户可选下拉，三选项各带固定方向：优先级（高→低，默认，保持现状）/ 截止日期（近→远，无日期排最后）/ 创建时间（新→旧）。
> - **分类/标签筛选**：[DEFERRED] 依赖阶段二（用户暂不采用），本轮不实现。
> - **数据边界**：本需求**不新增数据库列、不改仓库接口**，仅在 ViewModel 内对既有只读投影再加工（Article 7：无契约破坏）。

- **Scenario: 状态筛选**
  - **WHEN** 用户在状态下拉选择"未完成"
  - **THEN** 列表仅显示未完成任务；与当前边栏视图、标题搜索取交集。
- **Scenario: 优先级筛选**
  - **WHEN** 用户在优先级下拉选择"高"
  - **THEN** 列表仅显示高优先级任务。
- **Scenario: 切换排序**
  - **WHEN** 用户把排序从"优先级"改为"截止日期"
  - **THEN** 列表按截止日期由近到远重排，无截止日期者排最后；不触发集合 Reset（保持增量 diff）。
- **Scenario: 无匹配**
  - **WHEN** 筛选或搜索后无结果
  - **THEN** 显示空状态引导文案而非空白列表，并区分三种情形：
    1. 库内无任何任务 → "暂无任务，输入标题后回车创建第一条"；
    2. 库内有任务但当前筛选/搜索无匹配 → "没有符合筛选条件的任务" + "清除筛选"按钮（重置状态/优先级/搜索，保留排序与边栏视图）；
    3. 库内有任务但当前视图为空（如空文件夹）→ "此视图暂无任务"。
- **边界（非目标）**：边栏条目数与底部"已办/总数"统计保持全库口径，不随筛选联动。

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
系统 SHALL 将用户设置持久化（写入 `config.json` 单一权威源）并在重启后保持生效。
- **Scenario: 设置重启保留**
  - **WHEN** 用户修改设置后重启应用
  - **THEN** 设置保持生效。

> 2026-10-18 契约修订（Article 7，用户确认后生效）：`config.json` 从"只读数据节"扩展为**含外观节的读写文件**。新增顶层节 `appearance`，含字符串键 `theme`，合法值 `System` / `Light` / `Dark`（`System`=跟随系统，未配置即默认）。**读取宽容**：文件/节/键缺失或值非法时回退 `System`；**写入保守**：经 `JsonNode` 只增改 `appearance.theme`，完整保留 `data` 等其他既有节（写回为标准缩进格式）；写盘失败按 best-effort 处理（即时生效不回滚、不中断）。本阶段不收录"置顶 / 自启动"（归阶段五），不收录语言键（i18n 拆后）。

### Requirement: 主题手动切换
系统 SHALL 支持浅色/深色（及跟随系统）手动切换，即时生效并持久化。
- **Scenario: 切换主题**
  - **WHEN** 用户在主题下拉选择"深色"
  - **THEN** 界面即时切换到深色，`config.json` 的 `appearance.theme` 写为 `"Dark"`，重启后保持。
- **Scenario: 跟随系统**
  - **WHEN** 用户选择"跟随系统"（默认）或配置值非法/缺失
  - **THEN** 界面主题跟随操作系统明暗设置。

### Requirement: 多语言（中/英）
系统 SHALL 支持简体中文与英文切换。
- **Scenario: 切换语言**
  - **WHEN** 用户选择 English
  - **THEN** 界面文案切换到英文。**[DEFERRED 2026-10-18：i18n 单独拆为后续阶段，本轮不实现]**

### Requirement: 统计报表
系统 SHALL 提供基于权威源的任务完成统计：完成率与天/周完成量，供用户查看。

> 2026-10-18 契约修订（Article 7，用户确认）：`tasks` 表新增 `completed_at TEXT NULL` 列（ISO "o"，幂等迁移）——**完成时写入当前时间，取消完成时置 NULL**。数据库层写库时间由仓库注入时钟提供（Art.11 可测）。**旧库已完成任务该值为 NULL，用户选定口径 A：保持 NULL、不做回填**——此类任务不计入按日期完成量（不伪造历史），仅计入总数/完成率。

- **Scenario: 查看报表**
  - **WHEN** 用户打开底部"统计与备份"弹层
  - **THEN** 展示全库口径（不随筛选联动）：完成率，今日/本周/本月完成数，近 14 天每日完成量迷你条形（纯控件绘制，不引图表库）；打开与数据变更后实时刷新。本周自周一开始、本月为自然月，时钟可注入。

### Requirement: 数据备份导出/导入
系统 SHALL 支持整库备份导出与导入（2026-10-18 用户确认：**SQLite 整库副本形态**，JSON 拆出后续阶段）。
- **Scenario: 导出备份**
  - **WHEN** 用户执行导出并选择目标路径
  - **THEN** 当前库文件复制为副本（带时间戳默认名 `todolist-backup-yyyyMMdd-HHmmss.db`），权威源不变，导出可在任意时刻重新导入。
- **Scenario: 导入备份**
  - **WHEN** 用户选择备份文件并确认覆盖
  - **THEN** 校验所选文件（含 tasks 表）→ 当前库自动改名留 `.bak` → 备份复制替换当前库 → 提示**重启应用生效**（运行中内存投影不热重载，避免两份真相冲突）。

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

- [x] 阶段三：周期性任务（滚动式，2026-10-17 用户确认方案 A + 截止日期弹层内 UI）——**[DEFERRED 2026-10-18 用户决定：功能注释停用]**
  - [x] T3.1 模型与库：`Models/RecurrenceType.cs`（None/Daily/Weekly/Monthly/Yearly 单一权威源）；`TodoItem` 增 `Recurrence`/`RecurrenceInterval`/`RecurrenceRemaining?`；`tasks` 表 3 列建表 + `EnsureColumn` 幂等迁移；`UpdateRecurrence`/`RollRecurrence` SQL，`UpdateDueDate(null)` 联清周期（**休眠保留：枚举/纯函数/模型字段/库列与读写方法无害，勿删**）
  - [x] T3.2 规则引擎：`Models/Recurrence.cs` 纯函数 `NextDue(due, type, interval)` 与 `ToDisplayText(...)`（单测覆盖；月末 clamp 验证通过）
  - [x] T3.3 仓库：`SetRecurrence(id, type, interval, remaining)`（bool 语义校验，休眠保留）；`SetCompleted(true)` 滚动分支——**已注释停用（TodoRepository.cs 内 [DEFERRED] 标记）**
  - [x] T3.4 UI：截止日期弹层"重复"区块 + 行内规则摘要——**已注释停用（MainWindow.axaml 内 [DEFERRED] 标记，共 2 处）**
  - [x] T3.5 单测：16 用例入 `RecurrenceTests.cs`；4 个滚动行为用例挂 `Skip` 随功能停用，其余照常全绿
  - [ ] 验证：仓库层滚动逻辑 16 用例覆盖全绿，但**用户人工实测发现缺陷未定位**——"完成周期任务后截止日期未推进到下一期"（实时 UI 链路或边缘路径嫌疑；未复现根因前不重新启用）；恢复使用步骤见 TodoRepository.cs / MainWindow.axaml 注释块

- [x] 阶段四：视图与交互
  - [x] T4.1 关键词搜索（FilteredItems 投影建立在仓库只读源上）—— **2026-10-17 核对：已实现**（`ApplyFilter()` 标题过滤+视图叠加+增量 diff，README 已列为交付功能），补记勾选
  - [x] T4.2 多条件筛选（状态/优先级）与排序下拉（优先级/截止/创建）；分类/标签筛选 [DEFERRED] 依赖阶段二
    - [x] T4.2.1 ViewModel：新增 `StatusFilter`/`PriorityFilter`/`SortBy` 属性 + 选项列表 + 变更时重跑 `ApplyFilter()`/`Reorder()`；`ApplyFilter` 叠加状态与优先级条件（保持增量 diff，禁 Reset）
    - [x] T4.2.2 UI：顶部工具条加两个筛选下拉 + 一个排序下拉，绑定上述属性
    - [x] T4.2.3 `Reorder()` 支持三种排序方向：优先级高→低（默认）/ 截止近→远（无日期最后）/ 创建新→旧
  - [x] T4.3 空状态提示控件
    - [x] T4.3.1 ViewModel：`IsEmpty`/`EmptyStateText`/`HasActiveFilter` + `ClearFiltersCommand`（重置状态/优先级/搜索，保留排序与边栏视图）
    - [x] T4.3.2 UI：列表为空时显示引导文案（三态区分），有筛选时附"清除筛选"按钮
  - [x] T4.4 单元测试：`FilterSortAndEmptyStateTests` 共 11 个用例（筛选/三排序/空状态三态/清除筛选/不触发 Reset）
  - [x] 验证：搜索/筛选/排序正确；无结果显示空状态 —— 编译 0 错误 0 警告、`dotnet test` 56/56（原 45 + 新增 11）；**用户人工测试通过（2026-10-17）**

- [ ] 阶段五：桌面特色
  - [ ] T5.1 平台抽象：常量接口/工厂（借鉴参考仓库思路，轻量）
  - [ ] T5.2 系统托盘：关闭最小化 + 托盘恢复/退出
  - [ ] T5.3 到期通知（迁移/复用 T1.7）、窗口置顶、开机自启动
  - [ ] 验证：托盘/置顶/自启动在桌面端生效

- [x] 阶段六：设置与主题 / i18n（2026-10-18 用户确认：先做设置持久化 + 主题切换，i18n 单独拆）
  - [x] T6.1 设置持久化：`Models/UiTheme.cs`（System/Light/Dark 单一权威源）；`DataPaths.ReadTheme(programRoot)`（宽容回退 System；损坏 JSON 抛异常与 ReadDataSettings 行为一致）与 `DataPaths.WriteTheme(programRoot, theme)`（JsonNode 保留其他节、写回缩进格式、best-effort 返回 bool）；置顶/自启动归阶段五
  - [x] T6.2 主题切换接线：`App.axaml.cs` 启动读配置设置 `RequestedThemeVariant` 并把切换 λ 注入 `MainWindowViewModel`（含 `initialTheme` 回填下拉显示）；顶部工具条末尾"主题"下拉；切换即时生效 + 落盘
  - [ ] T6.3 i18n：中/英资源，运行时切换 **[DEFERRED，单独阶段]**
  - [ ] 验证：主题重启保留；切换即时生效；`data` 节不被写坏 —— 编译 0 错误 0 警告，`dotnet test` 共 81（新增 9 全绿；另 4 个为阶段三停用挂起）；**用户人工测试通过（2026-10-18）**

- [x] 阶段七：统计与数据导出（2026-10-18 用户确认：completed_at 保持 NULL 不回填；底部按钮+弹层；SQLite 整库副本）
  - [x] T7.1 契约：`TodoItem.CompletedAt`；`tasks` 表 `completed_at` 列 + `EnsureColumn` 迁移；`UpdateCompleted(id, isCompleted, completedAt)` 单语句写库；`TodoRepository` 注入时钟；LoadAll 读取（NULL 容忍）
  - [x] T7.2 统计计算：VM 全库口径完成率/今日/本周（周一起）/本月完成数 + 近 14 天每日条形数据（注入时钟）；底部"统计与备份"弹层 UI（纯控件）
  - [x] T7.3 备份：`DatabaseService` 暴露库文件路径与 `ExportTo`/`ImportFrom`（src 校验 tasks 表、当前库自动 `.bak`、File.Copy 替换）；VM 抽 `IDatabaseBackup`（App 注入）；View 层文件对话框 + 覆盖确认；导入成功提示重启生效
  - [x] T7.4 单测：SetCompleted 写入/清 NULL（双路径）；旧库 NULL 行不崩且不计入日趋势；统计口径（今日/周/月/率/14 天分桶）；导出落盘与 ImportFrom 替换生效、非法源拒绝、.bak 生成 —— `StatsAndBackupTests.cs` 9 用例；编译 0 警 0 错，`dotnet test` 共 90（86 过 + 4 阶段三停用挂起，0 失败）
  - [ ] 验证：统计正确；导出→导入恢复一致；旧数据不受影响 —— 编译 0 警告 0 错误，`dotnet test` 共 90（86 过 + 4 阶段三停用挂起，0 失败）；**用户人工测试通过（2026-10-18）**

- [ ] 阶段八：工程化收尾
  - [ ] T8.1 装配收敛（手工 DI / 轻量容器），App.axaml.cs 单一入口
  - [ ] T8.2 `dotnet publish` 打包 + 应用图标
  - [ ] T8.3 ~~README 功能清单自动同步（更新 update-readme.js）~~ **（已取消 2026-09-12：README 改为完全人工维护，脚本与钩子已移除）**
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
- [x] 已完成任务整行置灰（勾选实时淡化；绑定行 Opacity + ViewModel 广播 RowOpacity）
- [x] `dotnet build` 0 错误

## 阶段二 · 分类与标签（[DEFERRED] 用户暂不采用）
- [ ] [DEFERRED] 分类/标签可 CRUD，数据落库重启保留
- [ ] [DEFERRED] 任务可挂分类/标签
- [ ] [DEFERRED] 按分类/标签筛选正确

## 阶段三 · 周期性任务（滚动式）[DEFERRED 2026-10-18：功能注释停用，重新启用前须先定位"完成未推进"缺陷]
- [x] 完成即滚动到下一期正确（daily/weekly/monthly/yearly + 间隔 N；月末 clamp；单步推进）——仓库层 16 用例验证通过，实时链路存疑（见缺陷记录）
- [x] 剩余次数递减正确；取尽后完成定格、周期清除（归为普通任务）——同上
- [x] 未设截止日期不可设周期（UI 禁用 + 仓库拒绝）；清空截止日期联清周期——休眠保留，校验可用例覆盖
- [x] 行内可见规则摘要（如"每周 · 剩 3 次"）；列表不堆积重复实例——UI 已注释停用
- **缺陷记录（2026-10-18 用户实测）**：完成任务后截止日期未更新到下一周期。仓库层滚动（含双路径落库交叉验证）测试全绿，根因疑在实时 UI 链路或边缘路径（如单步滚动后下一期仍在过去导致显示"已过期"），未定位；重新启用必须先复现修复。

## 阶段四 · 视图与交互
- [x] 关键词搜索正确（2026-10-17 核对：已实现，增量 diff 无 Reset）
- [x] 状态筛选（全部/未完成/已完成）与优先级筛选正确，与视图/搜索取交集
- [x] 三种排序（优先级高→低 / 截止近→远无日期最后 / 创建新→旧）切换正确，不触发集合 Reset
- [x] 无结果显示空状态引导（三态文案区分：无任务/无匹配/空视图），有筛选时"清除筛选"可用
- [x] 边栏计数与底部统计保持全库口径，不随筛选联动
- [x] 用户人工测试通过（2026-10-17）；分类/标签筛选维持 [DEFERRED] 待后续重新评估

## 阶段五 · 桌面特色
- [ ] 关闭最小化到托盘，托盘可恢复/退出
- [ ] 到期通知、窗口置顶、开机自启动生效

## 阶段六 · 设置 / 主题 / i18n
- [x] 设置写 config.json（`appearance.theme`），重启保留；`data` 节完整保留（2026-10-18 人工验证通过）
- [x] 主题手动切换（跟随系统/浅色/深色）即时生效（2026-10-18 人工验证通过）
- [ ] i18n 中/英切换生效 **[DEFERRED，单独阶段]**

## 阶段七 · 统计与导出
- [x] 完成趋势与完成率正确（今日/本周/本月 + 近 14 天；全库口径；旧 NULL 不回填不计日趋势）（2026-10-18 人工验证通过）
- [x] 导出→导入恢复一致（SQLite 整库副本；导入覆盖 + .bak + 重启生效提示）（2026-10-18 人工验证通过）

## 阶段八 · 工程化
- [ ] 装配单一入口（DI 收敛）
- [ ] 发布可运行 + 图标
- [ ] ~~README 功能清单自动同步~~（已取消：README 全人工维护）
- [ ] `dotnet build`/`dotnet test` 全绿

## 全局约束（贯穿所有阶段）
- [ ] 全阶段遵循单一权威源：定义仅一次，UI/脚本不重复硬编码
- [ ] 无空 `catch{}`/stub（宪法 Art.2）
- [ ] 技术债均标注 `// DEFERRED: ...`（Art.3）
- [ ] 每次 commit 前已向用户展示并获确认
