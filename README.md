# TodoList

一个基于 **Avalonia (11.3.22) + .NET 8** 的跨平台桌面待办事项应用，采用 **SQLite 作为单一权威数据源** 的 MVVM 架构。

## ✨ 功能

- 任务管理：输入 + 添加、勾选完成、删除
- 文件夹收纳：任务行下拉归属到文件夹或「未归类」（单归属），左侧边栏「全部任务 / 各文件夹 / 未归类」三视图切换，各视图显示条目数
- 文件夹管理：「＋ 新建文件夹」按钮展开命名输入（回车 / 确认创建，点击输入区外收起）；双击文件夹名原地重命名；🗑 图标触发删除确认，确认后连同其内条目一并删除
- 实时过滤：在顶部输入框键入关键词，当前视图内任务列表即时按标题过滤（忽略大小写），清空即恢复当前视图全量
- 任务标题原地编辑（双击 / F2 进入编辑，Enter 提交、Esc 取消、失焦保存）
- 优先级：无 / 低 / 中 / 高 四档，行首高亮条，列表按优先级从高到低排序
- 截止日期：严格 `yyyy-MM-dd` 格式校验，输入框占位提示当天日期，到期展示「今天到期 / 已过期」
- 到期桌面通知提醒（应用运行期间每分钟检查未完成且已到期的任务；到期判断由可注入时钟驱动，便于测试）
- 全局异常日志：未处理异常自动落盘输出目录 `logs/`（`crash-日期.log`，追加写盘），便于离线诊断
- 输入校验：新增与编辑统一校验（空 / 去空白 / 标题超长 100 字符、文件夹名超长 50 字符）
- 完成置灰展示
- 已办 / 总数统计
- 任务列表超出窗口高度时上下滚动
- 单元/集成测试共 45 个（隔离临时 SQLite，不触碰 `data/todo.db`）：`TodoRepository` 覆盖任务与文件夹增删改、归属、联删与旧库迁移；另有通知属性/增量过滤回归、到期逻辑（注入时钟）单测，以及数据路径解析（开发 / Windows·macOS 发布 / Linux 发布）与 config.json 读取用例

## 🔧 技术栈

- **GUI**：Avalonia 11.3.22（XAML MVVM）
- **运行时**：.NET 8
- **数据库**：SQLite（`Microsoft.Data.Sqlite`）

## 🚀 快速开始

```bash
# 还原并运行
dotnet restore
dotnet run
```

> 说明：启动操作请由你自己执行（本项目约定 AI 不负责启动程序）。

打包（可选，跨平台单文件发布）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-all.ps1
```

默认以 Release + 自包含发布 `win-x64` / `osx-arm64` 到 `dist/<rid>/`（单文件配置见 `TodoList.csproj`，仅在指定 `RuntimeIdentifier` 时生效）。发布产物运行文件按 `TodoList-<rid>` 命名（如 `dist/win-x64/TodoList-win-x64.exe`），文件名本身标明目标系统与架构。需要通用 Linux 便携产物时显式传入 `-Rids linux-x64`（glibc）或 `-Rids linux-musl-x64`（musl）。单平台等效命令：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:AssemblyName=TodoList-win-x64 -o dist/win-x64
```

## 📦 Linux 安装包

Linux 按发行版出**原生安装包**（非通用产物），每个包声明该发行版自身的依赖、登记应用菜单、可用原生包管理器卸载：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-linux.ps1
```

> 前置条件：Docker 已启动（脚本用 fpm 在容器内打包）；Docker 不可用时脚本直接报错退出，不静默降级。该脚本不进 `pre-push`，由发布者显式执行。

| 目标系统 | 产物 | 安装 / 卸载 |
| --- | --- | --- |
| Ubuntu 22.04+ | `dist/linux/ubuntu.22.04-x64/TodoList-ubuntu.22.04-x64_<版本>_amd64.deb` | `sudo apt install ./<包>` / `sudo apt remove todolist` |
| Debian 12+ | `dist/linux/debian.12-x64/TodoList-debian.12-x64_<版本>_amd64.deb` | 同上 |
| RHEL 9+ | `dist/linux/rhel.9-x64/TodoList-rhel.9-x64-<版本>-1.x86_64.rpm` | `sudo dnf install --nogpgcheck ./<包>` / `sudo dnf remove todolist` |
| Alpine 3.18+ | `dist/linux/alpine.3.18-x64/TodoList-alpine.3.18-x64-<版本>-r0.apk` | `sudo apk add --allow-untrusted ./<包>` / `sudo apk del todolist` |

- 安装布局：程序 `/opt/todolist/`（含 `config.json`）、启动器 `/usr/bin/todolist`、桌面入口与图标进 `/usr/share/`。
- 包名四者统一为 `todolist`，发行版与架构只体现在文件名上。
- 包**未签名**，故 rpm/apk 安装需 `--nogpgcheck` / `--allow-untrusted`。
- 目标矩阵、依赖集与包元数据（含版本号来源）见 `scripts/package-linux.ps1`。

## 💾 数据位置

| 运行方式 | 数据库路径 |
| --- | --- |
| 开发（`dotnet run`） | `<项目根>/data/todo.db` |
| Windows / macOS 便携版 | `<可执行文件所在目录>/data/todo.db` |
| Linux 安装版 | `$XDG_DATA_HOME/todolist/todo.db`（缺省 `~/.local/share/todolist/todo.db`） |

目录名与文件名由根目录 `config.json` 权威定义，解析规则集中在 `Database/DataPaths.cs`。Linux 安装版数据不在安装目录内（`/opt` 只读），从旧便携版迁移时把旧 `data/todo.db` 复制到 `~/.local/share/todolist/` 即可。

## 🖥️ 项目结构

<!-- AUTO-TREE:START -->
```text
TodoList/
├── .husky
│   └── commit-msg
├── Assets                                             # 应用资源：app.svg 为窗口图标唯一权威源，生成 ico/png。
│   ├── app.ico
│   ├── app.png
│   └── app.svg
├── Database                                           # SQLite 数据访问层与数据路径权威源。
│   ├── DatabaseService.cs                             # SQLite 数据访问层：负责建库建表，以及底层增删改查 SQL。
│   └── DataPaths.cs                                   # 配置与数据路径的"单一权威源"：读 config.json，推导各平台数据库落点（可注入、可单测）。
├── dist                                               # 发布产物（已 gitignore）：便携单文件 + Linux 原生安装包。
│   ├── linux
│   │   ├── alpine.3.18-x64
│   │   │   └── TodoList-alpine.3.18-x64-1.0.0-r0.apk
│   │   ├── debian.12-x64
│   │   │   └── TodoList-debian.12-x64_1.0.0_amd64.deb
│   │   ├── rhel.9-x64
│   │   │   └── TodoList-rhel.9-x64-1.0.0-1.x86_64.rpm
│   │   └── ubuntu.22.04-x64
│   │       └── TodoList-ubuntu.22.04-x64_1.0.0_amd64.deb
│   ├── osx-arm64
│   │   ├── TodoList-osx-arm64
│   │   └── TodoList-osx-arm64.pdb
│   └── win-x64
│       ├── TodoList-win-x64.exe
│       └── TodoList-win-x64.pdb
├── docs
│   ├── rule
│   │   └── article-05-post-change-verification.md      # 宪法 Article 5 细则：变更后验证（编译 + 单元测试）
│   └── spec
│       ├── docs
│       │   └── readme-auto-sync-DONE.md
│       ├── feature
│       │   ├── due-date-input-validation-DONE.md
│       │   ├── enhance-todo-features-IN_PROGRESS.md
│       │   ├── folders-add-DONE.md                    # 文件夹收纳功能 SPEC：三视图边栏、归属下拉、新建/重命名/删除确认、联删与旧库迁移。
│       │   └── hardening-fixes-DONE.md                # 加固修复 SPEC：全局异常日志、定时器守卫、增量过滤、发布版数据落盘与迁移、时间注入。
│       └── release
│           └── native-linux-installers-IN_PROGRESS.md # Linux 原生安装包 SPEC：四发行版 deb/rpm/apk、包内布局与依赖、Linux 数据落 XDG 目录。
├── Models                                             # 数据模型层：任务与文件夹模型、优先级枚举、标题/文件夹名/截止日期校验助手。
│   ├── DueDate.cs                                     # 截止日期格式的"单一权威源"助手：定义全应用统一的日期格式常量与严格解析逻辑。
│   ├── FolderName.cs                                  # 文件夹名称的"单一权威源"校验助手：Trim + 非空 + MaxLength。
│   ├── MyFolder.cs                                    # 文件夹数据模型（纯数据，无业务逻辑）。
│   ├── TaskPriority.cs                                # 任务优先级。低→高代表紧急/重要程度的递增。
│   ├── TaskTitle.cs                                   # 任务标题的"单一权威源"校验助手：Trim + 非空 + MaxLength。
│   └── TodoItem.cs                                    # 任务数据模型（纯数据，无业务逻辑）。
├── packaging                                          # Linux 打包资产：工具链与安装进系统的文件（均强制 LF 检出）。
│   ├── Dockerfile                                     # 打包镜像：ruby + rpm + binutils + 固定版本 fpm（打包工具链唯一权威源）。
│   ├── postremove.sh                                  # rpm %postun：补清理卸载后残留的空目录 /opt/todolist。
│   ├── todolist                                       # /usr/bin/todolist 启动器脚本。
│   └── todolist.desktop                               # 应用菜单入口（桌面项唯一权威源）。
├── Repositories                                       # 任务与文件夹仓库：接口定义唯一边界，实现为应用内唯一权威数据源。
│   ├── ITodoRepository.cs                             # 任务与文件夹仓库接口：定义"任务数据"的唯一边界。
│   └── TodoRepository.cs                              # 任务与文件夹仓库实现：应用内唯一权威数据源。
├── scripts
│   ├── check-commit-msg.js
│   ├── package-linux.ps1                              # Docker + fpm 生成四发行版原生安装包；目标矩阵/依赖集/包元数据唯一权威源。
│   └── publish-all.ps1                                # 发布便携自包含单文件（win-x64 / osx-arm64），pre-push 会执行。
├── Tests                                              # 测试项目：仓库集成测试 + VM 单测 + 路径解析单测。
│   └── TodoList.Tests
│       ├── B3NotificationAndFilterTests.cs            # 通知属性与增量过滤回归用例。
│       ├── B6ClockInjectionTests.cs                   # 到期逻辑（注入时钟）单测。
│       ├── DataPathsTests.cs                          # 数据路径解析与 config.json 读取单测（注入平台与环境变量）。
│       ├── TodoList.Tests.csproj
│       └── TodoRepositoryTests.cs                     # 任务与文件夹用例，使用临时 SQLite，不触碰 data/todo.db。
├── ViewModels                                         # MVVM 视图模型层：主窗口 VM、边栏项 VM、行 VM、命令与基类。
│   ├── MainWindowViewModel.cs                         # 主窗口 ViewModel：消费仓库只读投影，负责三视图过滤/搜索/统计与到期提醒。
│   ├── RelayCommand.cs                                # 极简 ICommand 实现，避免引入额外 MVVM 框架。
│   ├── SidebarItemViewModel.cs                        # 边栏项 ViewModel：全部/文件夹/未归类三态视图、文件夹原地重命名与条目数。
│   ├── TodoItemViewModel.cs                           # 行 ViewModel：承载行内编辑状态与优先级/截止日期/文件夹归属派生展示。
│   └── ViewModelBase.cs                               # MVVM 基类，提供属性变更通知能力。
├── .gitattributes                                     # 强制 .husky/**、*.sh、packaging/** 以 LF 检出（CRLF 会破坏 shell/桌面项）。
├── .gitignore
├── AGENTS.md
├── AI_CONSTITUTION.md
├── App.axaml
├── App.axaml.cs                                       # 装配：SQLite(单一权威源) → Repository → ViewModel；数据落点交给 Database/DataPaths 解析。
├── app.manifest
├── CLAUDE.md
├── config.json
├── MainWindow.axaml
├── MainWindow.axaml.cs                                # 主窗口代码后置：到期通知、回车快速添加、行内编辑/截止日期/文件夹交互键盘与失焦事件、删除文件夹确认框。
├── Program.cs                                         # Avalonia 程序入口：构建 App 并启动桌面生命周期。
├── README.md
├── TodoList.csproj
└── TodoList.slnx
```
<!-- AUTO-TREE:END -->

> 注：`<!-- AUTO-TREE:START/END -->` 之间的目录树与 `# 注释` 均为**人工维护**，提交时不再有程序自动改写。

## 🧠 架构：单一权威源

数据真相只存在于 **SQLite 数据库**：

- `DatabaseService` 是唯一直接与 SQLite 打交道的类。
- `TodoRepository` 是应用的**唯一权威数据源**，对外暴露**只读投影**，任何变更都「先落库、再更新内存投影」，保证二者一致。
- `MainWindowViewModel` 只消费仓库，不持有权威数据，杜绝「两份真相」。

```
<!-- AUTO-FLOW:START -->
SQLite（权威源） ←─ DatabaseService ←─ TodoRepository（只读投影） ←─ MainWindowViewModel ←─ View
<!-- AUTO-FLOW:END -->
```

> 注：`<!-- AUTO-FLOW:START/END -->` 之间的调用链为**人工维护**，提交时不再有程序自动改写。

## 📂 数据存储位置

数据目录与数据库文件名在 `config.json` **单一权威定义**一次（`data.directory`、`data.dbFileName`），`App.axaml.cs` 读取它，不在代码里重复硬编码。

实际数据库文件位于项目根 `/被定义的目录/文件名`（默认 `data/todo.db`）。`App.axaml.cs` 的 `ResolveProjectRoot()` 从输出目录向上定位项目根；发布环境无 `.csproj`/`.slnx` 标记时回退到程序输出目录。

`data/` 已被 `.gitignore` 排除，数据库不会进入版本库（`config.json` 本身会提交）。

## 📁 文档目录

| 目录 | 用途 |
| --- | --- |
| `docs/rule/` | 存储 `AI_CONSTITUTION.md` 中每一条的详细规则 |
| `docs/spec/<问题领域>/` | 按问题领域分类的 SPEC（feature / docs / test ...），每个问题只允许一个文档 |

## 📝 项目规约

- 遵循 `AI_CONSTITUTION.md`（共 13 条）：涵盖通用软件工程准则（文档-代码共同维护、根因分析、设计契约、单一权威源、no-patchwork 等）与本项目特定约定（commit 前展示确认、任务三分法、spec 领域目录）。
- 代码变更的 commit 必须包含 `Why:` 与 `What:`（根因三选一：design/code/test wrong）。
- commit 门禁由 `scripts/check-commit-msg.js` 经 `.husky/commit-msg` 强制执行。
- 技术债须标注 `// DEFERRED:`，禁止裸 TODO。