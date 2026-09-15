# 日历选择器优化 — complex · DONE

> 本文档依据《AI Collaboration Constitution》Article 5 创建。

---

## 变更一：优化截止日期选择框间距避免图标遮挡字体

### Why

用户反馈：截止日期选择框中的日历图标与日期数字（如 `2026-09-15`）发生重叠遮挡，视觉体验不佳。

原因分析：选择框内部使用 Grid 两列布局，按钮固定宽度较窄，且图标与文本之间缺少确定的固定间距 `Spacing`。

解决方式：将按钮内部布局重构为居中对齐且带 `Spacing="6"` 的 `StackPanel`，并微调选择框宽度为 `122`，确保日期字符串与图标之间保持足够的间距，彻底解决遮挡重叠问题。

### What Changes

1. **View 层 (`MainWindow.axaml`)**：
   - 将 `Button.dueSelect` 的宽度由 `112` 调整为 `122`。
   - 将按钮内部模板重构为 `StackPanel (Orientation=Horizontal, HorizontalAlignment=Center, Spacing=6)`，确保日期文本与 `🗓` 图标保持固定 6px 间距，绝不遮挡重叠。

2. **测试层 (`Tests/TodoList.Tests/`)**：
   - 运行全量单元测试（30/30 通过）。

### Impact

- **Affected Files**:
  - `MainWindow.axaml`
  - `docs/spec/feature/calendar-date-picker-DONE.md`
- **不变**：ViewModel 与数据库逻辑保持不变。

### Tasks

- [x] T1 更新 SPEC 为 IN_PROGRESS 状态
- [x] T2 修改 `MainWindow.axaml`：重构 `Button.dueSelect` 内部布局为 `StackPanel (Spacing=6)` 并调大宽度至 `122`
- [x] T3 运行 `dotnet build` 与 `dotnet test` 验证（30/30 测试全通过）
- [x] T4 重命名 SPEC 为 `calendar-date-picker-DONE.md` 并更新文档

### Checklist

- [x] 日期数字与日历图标绝不发生重叠遮挡，间隔清晰
- [x] 选择框布局在不同长度日期下均居中对齐
- [x] 单元测试 100% 通过（30/30），无编译错误

---

## 变更二：日历月份翻页箭头改为左右方向

### Why

用户反馈：截止日期日历控件中的月份翻页按钮显示为上下箭头（`^`/`v`），不符合左右切换月份的直觉。

原因分析：Avalonia 11.3.22 FluentTheme 内置 `CalendarItem` 模板中，`PART_PreviousButton`/`PART_NextButton` 的 `Path.Data` 分别为 `M 0,9 L 9,0 L 18,9`（`^`）和 `M 0,0 L 9,9 L 18,0`（`v`），硬编码为上下方向。

技术难点：`Path.Data` 是模板本地值，Avalonia 的 Style 无法覆盖本地值。`RenderTransform` 旋转方案因渲染裁剪导致箭头显示不完整。

解决方式：在 `Application.Resources` 中整体重定义 `CalendarItem` ControlTheme，直接在模板内将 Path.Data 改为左右方向：
- `PART_PreviousButton`：`M 9,0 L 0,9 L 9,18`（`<`）
- `PART_NextButton`：`M 0,0 L 9,9 L 0,18`（`>`）

### What Changes

1. **主题层 (`App.axaml`)**：
   - 移除之前无效的 `RenderTransform` 样式（Style 无法覆盖模板本地值）。
   - 在 `Application.Resources` 中添加完整的 `CalendarItem` ControlTheme（基于 Avalonia 11.3.22 FluentTheme 源码），仅修改两处 `Path.Data`：
     - `PART_PreviousButton` 的 `Path.Data` 从 `M 0,9 L 9,0 L 18,9`（`^`）改为 `M 9,0 L 0,9 L 9,18`（`<`）。
     - `PART_NextButton` 的 `Path.Data` 从 `M 0,0 L 9,9 L 18,0`（`v`）改为 `M 0,0 L 9,9 L 0,18`（`>`）。
   - 保留 hover/pressed 变色样式，与原主题行为一致。

2. **测试层 (`Tests/TodoList.Tests/`)**：
   - 运行全量单元测试（30/30 通过）。

### Impact

- **Affected Files**:
  - `App.axaml`
  - `docs/spec/feature/calendar-date-picker-DONE.md`
- **不变**：`MainWindow.axaml`、ViewModel 与数据库逻辑保持不变。

### Tasks

- [x] T1 尝试 Style 覆盖 Path.Data（失败：本地值不可覆盖）
- [x] T2 尝试 RenderTransform 旋转方案（失败：箭头被裁剪）
- [x] T3 整体重定义 CalendarItem ControlTheme，直接修改模板内 Path.Data
- [x] T4 运行 `dotnet build` 与 `dotnet test` 验证（30/30 测试全通过）

### Checklist

- [x] 月份翻页按钮显示为左右箭头（`<` / `>`）
- [x] 箭头完整显示，无裁剪
- [x] 悬停/按下变色行为与原主题一致
- [x] 单元测试 100% 通过（30/30），无编译错误
