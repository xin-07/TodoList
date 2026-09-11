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