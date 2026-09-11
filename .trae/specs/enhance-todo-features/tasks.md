# Tasks

> 分阶段实现；每阶段独立可交付、可验证。所有数据新增遵循单一权威源（DatabaseService → Repository → ViewModel）。WebDAV 不实现。

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

- [ ] 阶段二：分类与标签（多对多）
  - [ ] T2.1 新表 `categories`、`tags`、`task_relations`；模型 `Category`、`Tag` 单一定义
  - [ ] T2.2 `DatabaseService` 分类/标签读写 SQL；分类/标签仓库（只读投影）
  - [ ] T2.3 UI：分类/标签 CRUD + 任务挂载 + 按分类/标签筛选
  - [ ] 验证：分类/标签可增删与筛选，数据落库重启保留

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