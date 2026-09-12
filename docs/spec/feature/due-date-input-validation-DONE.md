# 截止日期输入格式校验与格式提示 — complex · DONE

> 单文档：SPEC（Why/What/Impact/Requirements）+ Tasks + Checklist。
> 日期示例值统一使用**示例当天日期**（当前：`2026-09-12`），需随运行当天日期同步更新。

## Why

现截止日期用宽松 `DateTime.TryParse` 解析（[TodoItemViewModel.cs](d:\Project\C#_project\TodoList\ViewModels\TodoItemViewModel.cs) `ApplyDueDate`），容忍如 `2026-9-12`、`09/12/2026` 等非标准输入，用户难以预期落库格式。同时输入框无任何格式提示，用户不知该输什么。

修复前：日期格式 `yyyy-MM-dd` 曾在 ViewModel 三处硬编码（构造初始化、错误示例、`RefreshDue` 的 `ToString("yyyy-MM-dd")`），违反宪法 Article 9 单一权威源；本次已收敛到 `Models.DueDate`。

## What Changes

- 新增领域助手 `Models.DueDate`：单一权威源定义格式常量 `yyyy-MM-dd`、格式 hint、严格解析 `TryParse`（用 `TryParseExact` 严格按格式，拒绝非标准输入）、标准化格式化方法。遵循既有 `Models.TaskTitle` 的 Validate 模式。
- `TodoItemViewModel`：所有 `ToString("yyyy-MM-dd")` 与解析收敛到 `Models.DueDate` 引用；`ApplyDueDate` 改用严格解析，非法输入给出明确错误。
- `MainWindow.axaml`：截止日期输入框 `PlaceholderText` 绑定格式 hint（`yyyy-MM-dd`），无值时在格子内展示应如何输入。

## Impact

- **Affected code**：
  - 新增 `Models/DueDate.cs`
  - `ViewModels/TodoItemViewModel.cs`（解析/格式化收敛到 DueDate，严格校验）
  - `MainWindow.axaml`（截止日期 TextBox 加 PlaceholderText）
- **不变**：数据库 schema、仓库接口均不涉及；落库值仍为 `DateTime?`。

## Requirements

### Requirement: 严格格式校验
系统 SHALL 对截止日期输入按 `yyyy-MM-dd` 严格校验；格式不符则拒绝并显示错误提示。
- **Scenario: 非法格式**
  - **WHEN** 用户输入 `2026/09/12` 或 `2026-9-12` 并确认
  - **THEN** 不落库，输入框下方显示格式错误提示（含示例）。

### Requirement: 格内格式提示（显示当天日期）
系统 SHALL 在截止日期输入框无值时，以占位文字展示**当天日期**（如 `2026-09-12`）作为输入模板，并随每天日期更新。
- **Scenario: 空输入框提示当天日期**
  - **WHEN** 任务无截止日期，截止日期格子为空
  - **THEN** 输入框内显示占位提示为当天日期，跨天后自动更新为新一天日期。

### Requirement: 单一权威源
系统 SHALL 仅在一处定义日期格式常量与解析逻辑，各处引用。
- **Scenario: 无重复定义**
  - **WHEN** 检查代码
  - **THEN** 格式字符串与解析仅存在于 `Models.DueDate`，其它位置引用之。

---

# Tasks

- [x] T1 新增 `Models.DueDate.cs`：格式常量、FormatHint、`TryParse`（`TryParseExact` 严格）、`ToDisplayString`
- [x] T2 `TodoItemViewModel`：构造/`RefreshDue` 的格式化改用 `DueDate`；`ApplyDueDate` 改用严格解析并更新错误文案
- [x] T3 `TodoItemViewModel`/`MainWindow.axaml`：截止日期 TextBox 占位绑定当天日期（`DueDatePlaceholder`，随 `RefreshDue` 每日更新）
- [x] T4 单一权威源收敛：到期文案简写格式 `MM-dd` 也纳入 `DueDate.DisplayFormat`/`ToShortDisplayString`，ViewModel 无任何日期格式字面量
- [x] 验证：`dotnet build` 0 错误；人工测试通过（非法/合法/空、占位当天日期）

---

### Checklist

- [x] 严格格式 `yyyy-MM-dd` 校验生效，非法输入被拒绝并提示
- [x] 空截止日期格子显示占位为当天日期
- [x] 日期格式与解析仅单一权威源定义（`Models.DueDate`）
- [x] 既有合法日期显示/提醒不受影响
- [x] `dotnet build` 0 错误