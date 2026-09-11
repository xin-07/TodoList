# AI Collaboration Constitution

> This document is the supreme law for all AI-assisted development. Every modification must comply. Violations are errors, not   suggestions. Treat this as permanent infrastructure, not a guideline.

## Article 1.

每次代码变更的 commit 必须含 Why/What（根因三选一： design/code/test wrong）

纯文档变更（docs）单独提交时，可省略 Why/What。



## Article 2.

禁止 `@ts-ignore`、空 `catch{}`、返回 null 的 stub（静默三件套）



## Article 3.

所有技术债必须写 `// DEFERRED：reason，defer-to：YYYY-MM-DD，owner：name`，禁止裸TODO