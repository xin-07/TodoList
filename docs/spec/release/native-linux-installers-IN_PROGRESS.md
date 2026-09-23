# Linux 原生安装包（deb / rpm / apk）SPEC

- **状态**：IN_PROGRESS（实现中）
- **任务分类**：complex（多文件/多模块改动 + 构建流程与运行时契约变更）
- **日期**：2026-09-23
- **问题领域目录**：`docs/spec/release/`（发布/打包工程，与 `feature` 功能类区分）

---

## 1. 背景与现状

`scripts/publish-all.ps1` 当前产出的是**通用产物**：只编译 `linux-x64` / `linux-musl-x64` 两个基线，再按发行版名**复制改名**成 4 份（见 [publish-all.ps1](../../../scripts/publish-all.ps1#L60-L65)）。这 4 份产物没有任何包管理元数据：

- 不能用 `dpkg -i` / `dnf install` / `apk add` 安装；
- 不登记到系统应用菜单，没有卸载入口；
- 不声明运行时依赖，缺库时只在启动瞬间崩溃。

用户要求：**每个系统有自己特定的安装包，不要通用的**。

## 2. 已确认决策（本轮问答记录）

| # | 决策 | 结论 |
|---|---|---|
| D1 | 安装包范围 | 仅 Linux 四发行版：`ubuntu.22.04`(.deb)、`debian.12`(.deb)、`rhel.9`(.rpm)、`alpine.3.18`(.apk) |
| D2 | Windows / macOS | 本次**不出**安装包：Windows 保持自包含单文件 exe，macOS 保持现状（pkg/dmg 无法在 Windows 生成） |
| D3 | 打包工具链 | Docker + fpm（本机已装 Docker Desktop，当前**未启动**）；不引入 WiX |
| D4 | 包粒度 | 按发行版出 **4 个**包，各自声明该发行版真实存在的依赖版本 |
| D5 | 数据目录 | Linux 安装版数据落用户级目录：`$XDG_DATA_HOME/todolist`（缺省 `~/.local/share/todolist`）；Windows / macOS 保持 exe 同目录不变 |
| D6 | 包维护者 / 许可证 | `--maintainer "xin-07 <2074835619@qq.com>"`（用户提供邮箱，名称取仓库账号）、`--license Proprietary` |
| D7 | publish-all.ps1 | 同意收敛：默认 RID 只保留 `win-x64` / `osx-arm64` |

## 3. 目标与非目标

**目标**

- 4 个发行版各自产出可被原生包管理器安装/卸载的安装包，含应用菜单入口与图标。
- 打包元数据（版本、依赖、安装路径）**单一权威源**，不重复定义。
- Linux 安装版数据落到用户可写目录，安装目录只读也能正常保存数据。

**非目标**

- 不做包签名（deb/rpm/apk 均未签名，安装时需 `--allow-untrusted` / `--nogpgcheck`，见 §9）。
- 不做 Windows MSI、macOS pkg/dmg、ARM 架构目标。
- 不做自动更新、不做 APT/YUM 仓库托管。

## 4. 设计

### 4.1 交付矩阵

| 目标发行版 | 包格式 | 基线 RID | 产物文件名 | 架构字段 |
|---|---|---|---|---|
| ubuntu.22.04-x64 | deb | linux-x64 | `TodoList-ubuntu.22.04-x64_<ver>_amd64.deb` | amd64 |
| debian.12-x64 | deb | linux-x64 | `TodoList-debian.12-x64_<ver>_amd64.deb` | amd64 |
| rhel.9-x64 | rpm | linux-x64 | `TodoList-rhel.9-x64-<ver>-1.x86_64.rpm` | x86_64 |
| alpine.3.18-x64 | apk | linux-musl-x64 | `TodoList-alpine.3.18-x64-<ver>-r0.apk` | x86_64 |

产物落 `dist/linux/<目标>/`；Windows/macOS 仍为 `dist/win-x64/`、`dist/osx-arm64/`。

> 说明：包名（包管理器可见名）四者统一为 `todolist`，因此 `dpkg -r todolist` / `dnf remove todolist` / `apk del todolist` 通用；发行版标识只出现在**文件名**上（复用现有"文件名标明目标系统"的约定）。

### 4.2 包元数据与命名（单一权威源，Article 9）

| 元数据 | 权威来源 |
|---|---|
| 版本 `version` | `TodoList.csproj` 的 `<Version>`（本次显式写入 `1.0.0`）；脚本用 `dotnet msbuild TodoList.csproj -getProperty:Version` 读取，不另立版本源 |
| 包名 `todolist`、描述、主页、维护者（`xin-07 <2074835619@qq.com>`）、许可证（`Proprietary`） | `scripts/package-linux.ps1` 顶部的**单一元数据表**（与已有的 RID→基线映射表并列；脚本即打包权威源） |
| 安装路径、各发行版依赖集 | 同上，同一张表 |
| 应用图标 | `Assets/app.png`（256×256，由 `Assets/app.svg` 生成，唯一权威源） |

### 4.3 包内文件布局

| 安装路径 | 模式 | 来源 | 作用 |
|---|---|---|---|
| `/opt/todolist/TodoList` | 0755 | 基线发布产物（单文件自包含） | 主程序 |
| `/opt/todolist/config.json` | 0644 | 仓库根 `config.json` | 数据目录/文件名配置权威源，随包安装 |
| `/usr/bin/todolist` | 0755 | `packaging/todolist`（仓库内脚本） | 启动器：`exec /opt/todolist/TodoList "$@"` |
| `/usr/share/applications/todolist.desktop` | 0644 | `packaging/todolist.desktop` | 应用菜单入口 |
| `/usr/share/icons/hicolor/256x256/apps/todolist.png` | 0644 | `Assets/app.png` | 菜单图标 |

不使用符号链接（Windows 侧构建源无 exec/symlink 语义，`fpm -s dir` 行为依赖宿主文件属性），改用显式启动器脚本，行为确定。

- 不打包 `.pdb`（调试符号不入安装包）。
- 不安装 `/opt/todolist/data/`：数据不在安装目录内（见 §4.5）。

### 4.4 各发行版运行时依赖（fpm `--depends`）

自包含发布**不需要 .NET 运行时**，但仍依赖系统 ICU / X11 / C++ 运行库（Avalonia 的 X11 后端为运行时 `dlopen`，`ldd` 不可见，必须显式声明）：

| 角色 | Ubuntu 22.04 | Debian 12 | RHEL 9 | Alpine 3.18 |
|---|---|---|---|---|
| ICU（.NET 全球化） | libicu70 | libicu72 | libicu | icu-libs |
| C++/压缩 | libstdc++6、zlib1g | libstdc++6、zlib1g | libstdc++、zlib | libstdc++、zlib |
| X11 基础 | libx11-6、libxext6、libxrender1 | libx11-6、libxext6、libxrender1 | libX11、libXext、libXrender | libx11、libxext、libxrender |
| X11 扩展 | libxrandr2、libxi6、libxcursor1 | libxrandr2、libxi6、libxcursor1 | libXrandr、libXi、libXcursor | libxrandr、libxi、libxcursor |
| 会话/字体 | libice6、libsm6、libfontconfig1、libfreetype6 | libice6、libsm6、libfontconfig1、libfreetype6 | libICE、libSM、fontconfig、freetype | libice、libsm、fontconfig、freetype |

> 依赖集已在四个发行版容器内实测校正（T6）：Ubuntu 22.04 与 Debian 12 由 `apt-get install` 全量解析（含 libicu70 / libicu72）、RHEL 9 由 `dnf install` 解析、Alpine 3.18 由 `apk add` 解析 26 个包，`ldd` 均无 `not found`，四者均无多余或缺失依赖，故上表即为最终依赖集。

### 4.5 数据目录契约变更（Article 7 显式契约修订）

**根因**：deb/rpm/apk 由 root 安装到 `/opt`，目录对普通用户只读；现契约把 `todo.db` 定位在 exe 旁（[App.axaml.cs](../../../App.axaml.cs#L43-L73)），安装版将无法写入数据。

**新契约**（数据路径矩阵）：

| 运行模式 | 数据根 | 数据库路径 |
|---|---|---|
| 开发（能向上定位项目根） | 项目根 | `<项目根>/data/todo.db` |
| 发布 · Windows / macOS | exe 所在目录 | `<exeDir>/data/todo.db`（不变） |
| 发布 · Linux | `$XDG_DATA_HOME`（为空或非绝对路径时用 `~/.local/share`） | `<XDG根>/todolist/todo.db` |

Linux 下用户级数据根本身即应用专属目录，不再追加 `data/` 层（避免出现无身份含义的 `~/.local/share/data/todo.db`，Article 8）。

**配置权威源变更**：`config.json` 增加一个键表达 Linux 用户级目录名：

```json
{
  "data": {
    "directory": "data",
    "dbFileName": "todo.db",
    "userDataDirectoryName": "todolist"
  }
}
```

- `directory`：相对"非应用专属数据根"（项目根 / exe 目录）的数据目录名。
- `userDataDirectoryName`：用户级数据根之下的应用目录名（仅 Linux 发布模式使用）。
- 两者语义不同、各自只有一处权威定义，不构成重复。

**config.json 读取位置与数据根解耦**：配置一律从**程序根**（项目根 ?? exe 目录）读取，与数据根分开计算——否则 Linux 下会去 `$XDG_DATA_HOME` 找配置。包内随装 `config.json` 到 `/opt/todolist/config.json` 保证权威文件在运行时存在。

**旧数据迁移**：`MigrateLegacyDatabase` 逻辑保持（源为 `AppContext.BaseDirectory/data/todo.db`）。Linux 下源路径不存在，迁移为 no-op——旧 Linux 产物是可自由放置的便携单文件，其数据位置不可探测；README 说明手动迁移方法（把旧 `data/todo.db` 拷到 `~/.local/share/todolist/todo.db`）。

**可测试性（Article 11 注入边界）**：路径解析从 `App.axaml.cs` 的私有静态方法抽出为纯函数类型 `Database/DataPaths.cs`，平台、`XDG_DATA_HOME`、`HOME`、项目根均由参数注入，宿主环境只在 `App.axaml.cs` 的单处探测点读取。

### 4.6 构建流程与工具链

新增 `scripts/package-linux.ps1`（显式手动执行，**不进 pre-push**）与 `packaging/Dockerfile`（ruby + rpm + binutils + fpm，固定 fpm 1.18.0，镜像 tag `todolist-fpm:local`）。

流程（每个目标）：

1. 前置校验：`docker info` 失败则明确报错"请启动 Docker Desktop"，不静默降级。
2. `docker build` 打包镜像（每次执行，命中缓存近乎瞬时；不做"镜像存在即跳过"，否则 Dockerfile 改动不生效）。
3. `dotnet publish` 对应基线到 `dist/.stage/<基线>/`（`-p:AssemblyName=TodoList-<rid>`）。
4. 组装 fpm 输入树 `dist/.stage/pkg-<目标>/{opt,usr}/...`（§4.3 布局）。
5. 容器内归一化权限（目录 0755 / 文件 0644，再给两个可执行文件 0755）+ `fpm -s dir -t <deb|rpm|apk> ...` 直接输出到 `dist/linux/<目标>/`。
6. 清理 `dist/.stage/`。

**实现中定位并修复的三个真实缺陷**（均为容器内实测暴露）：

| 现象 | 根因 | 修复 |
|---|---|---|
| `fpm -t deb` 报 `Need executable 'ar'` | 镜像未装 binutils，fpm 生成 deb 需 `ar` | Dockerfile 增加 `binutils` |
| 桌面项与图标落成 0777 | Docker Desktop 挂载卷不保留 POSIX 权限（一律 0777），fpm 照抄源文件模式 | 打包前在容器内归一化整棵树的权限 |
| rpm 卸载后残留空目录 `/opt/todolist` | fpm 的 rpm 文件清单只含文件、不含目录项（deb 则含目录项） | rpm 挂 `%postun`（`packaging/postremove.sh`）补一次 `rmdir` |
| 终端里 dotnet 中文输出乱码（`姝ｅ湪纭畾...`） | PowerShell 5.1 用 `[Console]::OutputEncoding`（简体中文 Windows 为 GBK）解码原生命令输出，而 dotnet 写 UTF-8 | 脚本启动处显式把 `[Console]::OutputEncoding` 置为 UTF-8（输出被重定向时跳过，避免无控制台设置代码页失败） |

> 乱码缺陷已用 A/B 实验确证：同一命令以 GBK 解码复现乱码、以 UTF-8 解码正常，故修复点在解码侧而非管道。

另：两个 `.ps1` 以 **UTF-8 BOM** 保存。Windows PowerShell 5.1 对无 BOM 文件按 ANSI 解码，中文注释会被破坏并导致解析失败（本机无 pwsh，`pre-push` 走的正是 5.1）；脚本内原生命令调用统一走 `Invoke-Native` 包装（临时降级 `$ErrorActionPreference`），避免 5.1 在 `Stop` 下把 stderr 当成终止性错误、使"判退出码 + 友好报错"失效。

**pre-push 影响**：`.husky/pre-push` 每次 push 都会跑 `publish-all.ps1`。若把 Docker 打包并入该脚本，未启动 Docker 就无法推送。因此：

- `publish-all.ps1` 默认 RID 收敛为 `win-x64`、`osx-arm64`（4 个 Linux 发行版目标移交给 `package-linux.ps1`，其发行版→基线映射表随之**只在新脚本中存在一处**，无重复定义）。
- `publish-all.ps1` 保留 `-Rids` 覆盖能力（需要通用 Linux 目录产物时仍可 `-Rids linux-x64`）。

## 5. 代码与文件改动清单

| 文件 | 改动 |
|---|---|
| `TodoList.csproj` | 新增显式 `<Version>1.0.0</Version>` |
| `config.json` | 新增 `data.userDataDirectoryName = "todolist"` |
| `Database/DataPaths.cs` | 新增：数据根/数据库路径解析纯函数（含平台枚举与宿主探测入口） |
| `App.axaml.cs` | 配置改从程序根读取；数据根改由 `DataPaths` 计算；删除内联解析逻辑 |
| `Tests/TodoList.Tests/DataPathsTests.cs` | 新增：开发/Windows 发布/Linux 发布（有 XDG、无 XDG）等路径用例 |
| `scripts/package-linux.ps1` | 新增：Docker + fpm 打包四发行版 |
| `scripts/publish-all.ps1` | 默认 RID 收敛为 win-x64/osx-arm64；删除发行版→基线映射与"暂存后复制改名"逻辑；文件头说明同步更新 |
| `.gitattributes` | 新增 `packaging/** text eol=lf`（启动器/桌面项/Dockerfile 必须 LF，否则容器内 shebang 与桌面项解析失败） |
| `packaging/Dockerfile` | 新增：fpm 打包镜像（ruby + rpm + binutils + fpm 1.18.0） |
| `packaging/postremove.sh` | 新增：rpm `%postun`，清理卸载后残留的空目录 |
| `packaging/todolist.desktop` | 新增：桌面入口（唯一权威源） |
| `packaging/todolist` | 新增：`/usr/bin/todolist` 启动器脚本 |
| `README.md` | 更新打包章节、平台支持矩阵、项目结构树 |
| `docs/spec/release/native-linux-installers-*.md` | 本 SPEC（状态推进时改名） |

## 6. 任务清单（tasks）

1. **T1 数据路径重构**（完成）：新增 `DataPaths.cs`；`config.json` 加键；`App.axaml.cs` 接入。
2. **T2 路径单测**（完成）：`DataPathsTests.cs` 覆盖路径矩阵与 config 读取，共 11 个用例。
3. **T3 打包资产**（完成）：`packaging/Dockerfile`、`packaging/todolist.desktop`、`packaging/todolist`、`packaging/postremove.sh`。
4. **T4 打包脚本**（完成）：`scripts/package-linux.ps1`。
5. **T5 发布脚本收敛**（完成）：`publish-all.ps1` 默认 RID 调整 + 原生命令调用健壮化。
6. **T6 容器内实测**（完成）：四发行版容器安装/卸载/依赖解析全部通过，依赖集无需改动。
7. **T7 文档同步**（完成）：README 打包章节、平台矩阵、数据位置、结构树。
8. **T8 变更后验证**（完成）：`dotnet build --no-restore` 0 错误 0 警告；`dotnet test` 45/45。

## 7. 验收清单（checklist）

- [x] `dotnet build --no-restore` 0 错误（0 警告）。
- [x] `dotnet test` 全绿：45/45（原 34 个用例 + 新增 11 个路径与配置用例）。
- [x] `scripts/package-linux.ps1` 一次运行产出 4 个包，文件名符合 §4.1（deb 37MB / rpm 37MB / apk 38MB）。
- [x] Ubuntu 22.04 容器：`apt-get install ./TodoList-ubuntu.22.04-x64_*.deb` 依赖解析通过、五个文件就位且权限 755/644、`ldd` 无缺失、`dpkg -r todolist` 干净卸载（`/opt/todolist` 一并移除）。
- [x] Debian 12 容器：同上（libicu72 解析通过）。
- [x] RHEL 9 容器（`dnf install --nogpgcheck`）：rpm 元数据正确（License/Vendor/URL/Packager）、依赖解析通过、卸载无残留。
- [x] Alpine 3.18 容器（`apk add --allow-untrusted`）：26 个依赖解析通过、`apk del todolist` 干净卸载。
- [x] `publish-all.ps1` 收敛后仍正常产出 `win-x64`（91.8MB exe）与 `osx-arm64`（100.5MB）。
- [ ] 用户实机测试：安装后在桌面环境启动应用、新增任务、确认数据写入 `~/.local/share/todolist/todo.db` 并重启后仍存在。
- [ ] 用户实机测试：Windows 版行为与改动前一致（`<exeDir>/data/todo.db`）。
- [x] README 与脚本头注释已同步。

> 待用户实机验证完成后，本 SPEC 改名 `-DONE`。

## 8. 已确认输入（原待确认项，均已落定）

1. 维护者：`xin-07 <2074835619@qq.com>`（邮箱由用户提供；名称取仓库账号 `xin-07`，如与实际不符请指出）。
2. 许可证：`Proprietary`。
3. `publish-all.ps1` 默认 RID 收敛为 `win-x64` / `osx-arm64`：已同意。

## 9. 风险与限制

- **未签名**：deb/rpm/apk 均未签名，安装需 `--allow-untrusted`（apk）或 `--nogpgcheck`（dnf）或本地 deb 直接安装；签名需密钥管理与私钥托管，属独立任务范围。
- **Docker 依赖**：打包必须本机 Docker Desktop 运行中；pre-push 不涉及（§4.6）。
- **首次镜像构建**：需要网络下载 ruby 基础镜像与 fpm gem。
- **体积**：单文件自包含产物较大（数十 MB），安装包体积随之；不因此引入框架依赖发布。
- **依赖集准确性**：§4.4 必须经 T6 实测校正，未校正前不视为完成。
- **Alpine 无桌面环境**：Alpine 安装包面向已装 X11 的场景，纯服务器环境无法运行 GUI，属预期。