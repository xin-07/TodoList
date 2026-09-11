# AI Collaboration Constitution

> This document is the supreme law for all AI-assisted development. Every modification must comply. Violations are errors, not   suggestions. Treat this as permanent infrastructure, not a guideline.

## Article 1.

每次代码变更的 commit 必须含 Why/What（根因三选一： design/code/test wrong）

纯文档变更（docs）单独提交时，可省略 Why/What。



## Article 2.

禁止 `@ts-ignore`、空 `catch{}`、返回 null 的 stub（静默三件套）



## Article 3.

所有技术债必须写 `// DEFERRED：reason，defer-to：YYYY-MM-DD，owner：name`，禁止裸TODO



## Article 4.

执行 commit 前，必须先向用户展示拟提交的 commit 信息（subject + Why/What），等待用户明确确认后，才能执行 `git commit`；禁止未经用户审查直接提交。

纯文档（docs / chore / style）变更若用户已事先知晓其内容，可省略逐条展示，但仍需告知用户后再提交。

## Article 5.

每个任务在执行前必须归为三类之一，并按对应方式执行：

- **simple**：单文件修改。直接小改，不写 SPEC。
- **complex**：多文件/多模块改动。先写 SPEC，用户确认后再做代码实现。SPEC 存放于项目根 `docs/spec/<任务类型>/`（类型为 simple / complex / research），**每个问题只允许一个文档**，文件直接以「任务名-状态」命名，状态必须为 `DRAFT` / `IN_PROGRESS` / `DONE` 之一，如：
  - `docs/spec/complex/task-DRAFT.md`（起草中）
  - `docs/spec/complex/task-IN_PROGRESS.md`（实现中）
  - `docs/spec/complex/task-DONE.md`（已完成，状态推进时同步改名）
- **research（研究）**：只写文档/研究报告，不写实现代码。

任何功能修改/新增完成后，都必须交给用户测试验证，获得用户反馈后才算完成；AI 不自行启动应用。