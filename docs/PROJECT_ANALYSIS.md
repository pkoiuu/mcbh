# 白鹤服务器启动器（Baihe / mcbh）— 项目分析文档

> 用途：为后续代码修改提供一份「以当前源码为准」的架构地图与操作手册。
> 本文档基于源码实际内容逐文件核验整理（2026-08-26 深度核验版 v3），覆盖前后端结构、IPC 契约、核心流程、持久化文件、构建方式与「如何新增功能」的步骤。
> 核验基准：本地工作区 HEAD=03e0a7c（tag v1.1.25），Baihe.Host AssemblyVersion/FileVersion 1.1.25.0，OnlineInstaller Program.AppVersion "1.1.25"，iss `#define MyAppVersion "1.1.25"`，前端 v0.0.1。⚠️ 本次核验时 GitHub 远程网络不可达（SSL 中断），远程 tags/release/actions 未能复核；涉及「是否已在 GitHub 发布/编译过」的断言仍以远端实况为准（§15 规范）。
> 版本历史：v1（2026-08-18，基线 v1.1.1）→ v2（2026-08-25，核验至 v1.1.9）→ **v3（2026-08-26，核验至 v1.1.25）**。期间主线：v1.1.10 玩家指南维基/资源包默认放行 → v1.1.11 新增在线版安装器 Baihe.OnlineInstaller → v1.1.12 assets 误选修复+selftest → v1.1.15 镜像体系/维基远程化 wiki.json/网页版维基 wiki-site → v1.1.17~20 在线安装器下载链路加固（超时保护/两轮降级/候选线路切换）→ v1.1.18 固定自建加速服务（199.68.217.4:8090，token 编译注入）→ v1.1.21~22 token BOM 清洗/并发16/块级重试 → v1.1.23 主程序两段式升级（update.download）+ETA → v1.1.24 升级实例选择修复 → v1.1.25 移除全部农夫乐事（farm），维基收敛为 8 分类。

---

## 0. 当前工作区状态（改代码前必读）

**仓库已完成「移除 Baihe.Core」的架构瘦身重构**（随 v1.1.2 提交）：`src/Baihe.Core/` 已删除，`Baihe.slnx` 现含 `src/Baihe.Host` 与 `src/Baihe.OnlineInstaller` 两个 .NET 项目。**当前工作区源码干净**；未跟踪项仅工具目录（.temp/.buildtemp/.dsh-vision-toolkit/.trae-html-share-packages/baihe-launcher-analysis）与本文档本身的修订。

**含义与影响**：

1. 当前解决方案（`Baihe.slnx`）= `src/Baihe.Host`（.NET 10 WPF 主程序）+ `src/Baihe.OnlineInstaller`（net48 WinForms 在线安装器）两个 .NET 项目；`Baihe.Core` 是从 PCL2-CE fork 出来的旧核心库，重构时整体移除，业务逻辑已全部下沉/重写到 Host 的 Services/ 里。
2. **不要再创建或引用 `src/Baihe.Core`**。后端新代码一律放 `src/Baihe.Host`，前端放 `src/Baihe.UI`，只有独立轻量工具才考虑放 `src/Baihe.OnlineInstaller`（net48，零第三方依赖）。
3. 老文档（根目录 4 份分析文档）大量描述 Baihe.Core 的架构，已过时；以本文档与 `src/` 源码为准。
4. `PCL2-CE/` 目录仍保留仅作参考（启动/认证逻辑的注释多处「参照 PCL CE」），不参与构建。

**v1.1.10→v1.1.25 形态要点**：

5. **更新体系两级化**：主程序只做版本检查展示横幅；「下载更新」（IPC `update.download`）改为把 40KB 在线安装器下载到 %TEMP% 并启动，主程序随之退出，由在线安装器接管多线程下载完整包 + 启动 Inno 向导（§7.8）。
6. **固定自建加速服务**：发行版下载统一走 `http://199.68.217.4:8090`（命令行 header token）/`:8091`（浏览器 `?token=` 直链）；URL 格式 = 加速地址 + `github.com/<owner>/<repo>/原路径`（**必须保留 github.com 前缀**）。token 经 AssemblyMetadata `OnlineToken` 编译注入（GitHub secret `BAIHE_ONLINE_TOKEN`），Host 与 OnlineInstaller 两处 GetToken 都做 BOM(U+FEFF) 清洗——secret 混入 BOM 曾导致加速服务全量 401（v1.1.21 教训）。
7. **镜像测速已成死代码**：UpdateService 里 mirrors.json 拉取/测速方法还在，但 `CheckForUpdateAsync` 固定返回加速直链，测速路径不可达（§13-A1）；编辑 mirrors.json 不再影响任何下载行为。
8. **维基三方数据流**：`lib/wiki/*.ts`（前端内置兜底）↔ 仓库根 `wiki.json`（远程数据源，WikiService 按 raw/jsDelivr/ghproxy 回退拉取）↔ `wiki-site/`（GitHub Pages 网页版）。v1.1.25 移除农夫乐事后为 8 分类：login / commands / sit / skin / version / map / anticheat / faq。
9. **内置游戏 = 1.21.8**（vanilla + fabric-loader-0.16.14），换内置版本跑 `scripts/update-bundled-game.ps1`；iss `[InstallDelete]` 负责 1.21.3 遗留清理并删除 `{app}\current_instance.txt`，配合 InstanceService 的已安装校验自动回退选择（v1.1.24 升级修复）。
10. **单实例防多开 + 启动预热**（v1.1.9/11）：App.xaml.cs 命名 Mutex（第二实例激活已有窗口后退出自身）；OnStartup 预热 `LaunchService.EnsureLaunchOptions()` 把 `serverResourcePacks:true` 等关键选项写进 options.txt，保证玩家进服前已生效。

---

## 1. 项目定位

一个专为「白鹤服务器」定制的 Minecraft 启动器，核心能力：

- 启动 Minecraft（原版 + Fabric），QuickPlay 直连白鹤服务器
- 下载/安装 Minecraft 版本与 Fabric Loader
- 三种登录方式：离线 / 微软正版（设备码）/ 第三方验证（Yggdrasil / LittleSkin）
- Mod 管理（含中文名映射、图标提取）、光影管理（Iris shaderpacks）、存档备份/导入/恢复、截图浏览、游戏修复
- 首页「最新动态」（拉取仓库 news.json，可远程更新公告）+ 服务器状态 + 更新横幅
- **在线版安装器**（v1.1.11+，src/Baihe.OnlineInstaller）：40KB net48 WinForms，查最新版本（API 三源回退）→ 经自建加速服务 8 线程 Range 分块下载完整安装包（块级重试 ×3 → 失败自动降级单线程全量兜底）→ 自动启动安装向导
- **两级式自动升级**（v1.1.23+）：主程序检查到新版本后，「下载更新」只经加速服务拉 40KB 在线安装器（`update.download` IPC）并自行退出，下载复杂度整体外移（详见 §7.8）
- 内置聊天（WebView2 导航到外部 Element 聊天页，注入返回按钮与消息监控）
- 系统托盘、主题切换（深/浅）、更新检查（镜像测速）、遥测上报、微信名收集、单实例防多开

**技术栈**：

| 层 | 技术 |
|---|---|
| 后端宿主 | C# .NET 10 WPF + WebView2（Microsoft.Web.WebView2 1.0.4078.44，WinForms 托盘） |
| 前端 | Vite 6 + Svelte 5（runes）+ Tailwind CSS 4 + lucide-svelte（实际图标走内联 SVG） |
| 打包 | Inno Setup 6（installer/baihe_installer.iss）+ jlink 最小化 JRE 21（20 模块） |
| CI/CD | GitHub Actions（ci.yml 编译验证、release.yml 打 tag 发版） |

---

## 2. 目录结构（关键路径）

```text
Baihe.slnx                        # 解决方案：src/Baihe.Host + src/Baihe.OnlineInstaller（Core 已移除）
src/
├── Baihe.Host/                   # WPF 宿主进程（.NET 10，v1.1.25 = 26 个服务文件）
│   ├── App.xaml(.cs)             # 应用入口 + 单实例 Mutex（防多开，v1.1.9）+ 启动预热 options.txt（v1.1.11）
│   ├── MainWindow.xaml(.cs)      # 主窗口：WebView2 初始化 + 全部 IPC 命令注册（partial 拆分: MainWindow.Chat.cs）
│   ├── Chrome/TitleBar.xaml(.cs) # 原生标题栏 + 交通灯按钮
│   ├── Ipc/                      # IpcMessage.cs + IpcRouter.cs（IPC 协议与路由）
│   ├── Web/WebViewHost.cs        # WebView2 环境创建 + 虚拟主机映射
│   ├── Models/                   # McAccount / OfflineAccount / GameInstance
│   └── Services/                 # 26 个业务服务文件（v1.1.25 实数；25 静态类 + TrayService 实例类）
├── Baihe.OnlineInstaller/        # 在线版安装器（v1.1.11+，net48 WinForms，独立构建 ~40KB，细节见 §7.8）
│   ├── Program.cs                # 入口：AppVersion 常量 + --selftest / --download-test 自检模式
│   ├── MainForm.cs               # 自绘深色界面 + 流程编排（查版本→分块下载→校验→运行安装向导）+ 速度/剩余时间 ETA
│   ├── UpdateService.cs          # GitHub API 三源回退查版本（raw/ghproxy.net/ghfast.top）+ 固定加速 URL 构造 + token(BOM 清洗)
│   ├── Downloader.cs             # Range 探测→8 线程分块(256KB 缓冲/预分配/SequentialScan/连接上限16/块级重试×3)→单线程全量兜底（两轮策略）
│   └── SimpleJson.cs             # 极简 JSON 解析（不引第三方库，保持体积）
└── Baihe.UI/                     # Svelte 5 前端（构建输出到 ../Baihe.Host/wwwroot）
    ├── vite.config.ts            # outDir: ../Baihe.Host/wwwroot，WebView2 兼容插件
    └── src/
        ├── main.ts / App.svelte  # 入口 + 根组件（路由切换 + 微信名弹窗 + Toast）
        ├── app.css               # 设计令牌系统（Tailwind 4 @theme + CSS 变量双层）
        ├── components/           # WindowShell / Sidebar / WeChatDialog / ShadersPanel(v1.1.3+) / SaveManager(v1.1.3+)
        ├── lib/                  # ipc.ts / router / theme / toast / Icon.svelte + icons/（14 个 svg）+ shaders.ts + wiki/
        └── pages/                # Home / Download / Settings / Tools / Login / Wiki(v1.1.10+)
wiki.json                         # 维基远程数据源（v1.1.15+；scripts/generate-wiki-json.mjs 生成，直接编辑即更新维基）
site/                             # 官方网站（v1.1.26+ 开发中：纯静态单页，Pages 站点根；下载链接配置在 site/js/main.js 的 CONFIG 块）
wiki-site/                        # 网页版维基（v1.1.15+，纯静态单页；Pages 部署为 /wiki/ 子路径，数据源根 wiki.json 由 workflow 复制进部署目录）
PCL2-CE/                          # 上游 Plain Craft Launcher 2 CE 的 fork（仅参考，不参与构建）
installer/                        # Inno Setup 安装脚本
installer_resources/              # 开发期资源：.minecraft、jre、icon.ico、ChineseSimplified.isl
installer_assets/                 # 安装向导图片（wizimage.bmp 等）
scripts/                          # download-build / fork-rename / upload-minecraft-assets / update-bundled-game / generate-wiki-json.mjs
specs/                            # P0-theme-switching、P1-memory-recommendation（各含 spec/tasks/checklist）
docs/                             # telemetry-api-guidelines.md、PROJECT_ANALYSIS.md
news.json / mirrors.json          # 仓库根数据文件：首页公告（运行时拉取）/ ⚠️ mirrors.json 已成死配置（编辑不再生效，§13-A1）
baihe-launcher-analysis/          # 一次性的 HTML 分析快照（可忽略/删除）
```

> ⚠️ README.md 多处与代码不一致：`scripts/build-all.ps1` 不存在（实际是 download-build/fork-rename/upload-minecraft-assets 三个脚本）；「复制前端到 Host assets」是错的（Vite 直接输出到 wwwroot，且 Host 项目没有 assets/ 目录约定，只有 Assets/icon.ico）。

---

## 3. 架构总览

```text
┌─────────────────────── Baihe.Host (WPF 进程) ───────────────────────┐
│  App.xaml.cs  单实例 Mutex（防多开，激活已有窗口后退出）              │
│  MainWindow (Window)                                                 │
│   ├─ TitleBar.xaml  原生标题栏/交通灯（前端不负责窗口控制）           │
│   ├─ WebView2  ← WebViewHost: 虚拟主机 https://baihe.app/ → wwwroot  │
│   │     │        （WebView2FixedRuntime 固定版本兜底）                │
│   │     │  WebMessageReceived → OnWebMessageReceived                 │
│   └─────┼─────────────── IpcRouter (ConcurrentDictionary 路由)      │
│         │        │                                                   │
│         │   RegisterHostCommands()  所有命令在此注册                 │
│         │        │                                                   │
│         │   ┌────┴─────────────────────────────┐                    │
│         │   │  Services/（25 静态类 + Tray 实例类）│              │
│         │   └──────────────────────────────────┘                    │
│  TrayService (NotifyIcon)   遥测/更新/认证 → 网络                    │
└──────────────────────────────────────────────────────────────────────┘
                       ▲  JSON over postMessage
                       │
┌─────────────────────── Baihe.UI (Svelte 5) ─────────────────────────┐
│  main.ts → App.svelte（WindowShell + Sidebar + 路由 + WeChatDialog） │
│  lib/ipc.ts   ipc<T>(cmd,args) 请求-响应 + on(type,cb) 推送订阅       │
│  lib/router.svelte.ts   轻量路由（$state 页面 key，无路由库）         │
│  lib/theme.svelte.ts    主题（localStorage + 通知后端）               │
│  lib/shaders.ts         预装光影元数据（描述/预览图，v1.1.3+）        │
└──────────────────────────────────────────────────────────────────────┘
```

**关键架构决策**：

1. **WPF + WebView2 + Svelte 混合**：窗口框架/标题栏/托盘/文件系统/进程用原生 C#，UI 全用 Web 技术渲染。前后端完全通过 JSON IPC 解耦，前端不直接碰文件系统。
2. **前端构建产物直接进 Host**：Vite outDir 指向 `../Baihe.Host/wwwroot`，WebView2 通过 `SetVirtualHostNameToFolderMapping` 以 `https://baihe.app/` 加载；入口 URL 带 `?v={unix秒}` 时间戳做缓存清除。csproj 另有 `<Content Include="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest">` 把 wwwroot 复制到输出目录（文档 v1 遗漏此项）。
3. **IPC 为纯 JSON 命令总线**：请求-响应（id 配对 + 15s 超时）+ 主动推送（PushEvent）+ 两条特殊原始消息（见 §7）。
4. **静态服务 + 单账户**：除 TrayService 外全部业务服务是静态类；账户是全局单账户模式。
5. **PCL2-CE 不参与构建**：Baihe.Host.csproj 无任何 PCL 引用，仅作参考源码。

---

## 4. 前端架构（Baihe.UI）

### 4.1 状态与路由

- **路由**：`lib/router.svelte.ts` 用 `$state` 维护 `current: PageKey`（home | download | settings | tools | login），无路由库，页面在 `App.svelte` 里 `{#if}` 条件渲染。聊天页不走路由，通过 `nav.external` IPC 让后端 `Navigate(url)`。
- **主导航 4 项（v1.1.10 起定型）**：`navItems` = 启动 / **指南**（维基）/ 设置 / 工具。**下载页不在主导航**，入口改为 设置 → 开发者 →「版本下载」按钮（`router.navigate('download')`）；登录页不占导航，点侧边栏用户区进入。
- **Svelte 5 runes 约定**：在 .ts 里使用 runes 必须用 `.svelte.ts` 扩展名（router/theme/toast 都是如此）。
- **单例 store**：router、theme、toast 均为模块级单例（class + `$state`）。
- **入口**：`main.ts` 挂载前注册 `window.onerror` / `unhandledrejection` 全局错误捕获，出错时把加载屏替换为错误信息（避免白屏）。
- **头像**：`localStorage['baihe_avatar']` 存 base64 data URL（Settings 页用 canvas 居中裁剪为 64×64 PNG，限 2MB），纯前端实现不落盘后端。

### 4.2 组件与页面 → IPC 使用矩阵（核验自源码 v1.1.9）

| 文件 | 职责 | 用到的 IPC 命令 | 订阅的推送事件 |
|---|---|---|---|
| App.svelte | 根组件：WindowShell + Sidebar + 页面路由 + 微信名弹窗 + Toast 容器 | `wechat.get` | — |
| WindowShell.svelte | 纯内容容器（标题栏已迁到 WPF 原生） | — | — |
| Sidebar.svelte | 240px 毛玻璃侧边栏：用户区 + 导航 + 版本号；监听 router 变化重载账户 | `auth.current`、`app.getVersion` | — |
| WeChatDialog.svelte | 首次启动微信名收集弹窗（IPC 失败也弹） | `wechat.set` | — |
| Icon.svelte | 图标组件（见 4.4） | — | — |
| Home.svelte | 启动主页：实例卡片 + 启动按钮 + 快捷工具 + 新闻列表 + 服务器状态 + 更新横幅（**v1.1.13 已移除服务器选择下拉**；横幅「立即更新」走两段式升级） | `instance.current`、`auth.hasAccount`、`update.check`、`update.download`（v1.1.23+）、`server.status`、`launch.start`、`news.list`、`open.url` | `launch.state`、`launch.started`、`launch.windowShown`、`launch.exited` |
| Wiki.svelte | 玩家指南维基：分级导航 + 全文搜索 + 高亮 + 复制（select-text，8 分类 v1.1.25） | `wiki.get`（远程 wiki.json 优先，失败回退内置 lib/wiki/*.ts） | — |
| Download.svelte | 版本下载 / Fabric 安装（开发者入口） | `version.list`、`instance.list`、`download.start`、`fabric.install` | `download.progress/complete/error`、`fabric.progress/complete/error` |
| Settings.svelte | 账户/游戏/外观/关于/开发者 五分类设置（开发者需密码 `111125hj`） | `auth.current`、`app.getVersion`、`system.memory`、`update.check({force:true})`、`java.bundled`、`java.detect`、`settings.get`、`settings.set`、`auth.offline`（改名）、`open.url` | — |
| Login.svelte | 三种登录方式 Tab（离线/微软/第三方） | `auth.setOffline`、`auth.msLogin`、`auth.msCancel`、`auth.thirdPartyLogin` | `auth.msDeviceCode`、`auth.msLoginResult` |
| Tools.svelte | 工具页 5 Tab：Mod / **光影** / 截图 / 修复 / 聊天（聊天受开发者开关控制） | `mods.list`、`mods.toggle`、`mods.delete`、`mods.openFolder`、`screenshots.list`、`tools.openFolder`、`tools.repair`、`nav.external`（聊天） | — |
| ShadersPanel.svelte | 光影管理面板（v1.1.3+）：列表/启用/关闭/删除/打开文件夹，悬浮显示介绍 | `shaders.list`、`shaders.enable`、`shaders.disable`、`shaders.delete`、`shaders.openFolder` | — |
| SaveManager.svelte | 存档备份面板（v1.1.3+，位于设置→开发者）：存档列表 + 备份/恢复/删除 | `saves.list`、`saves.backups`、`saves.backup`、`saves.restore`、`saves.deleteBackup` | — |

> 注意：Home 不调用 `instance.list`，直接 `instance.current`；Settings 改名走 `auth.offline`；**存档管理（saves.\*）已从工具页移到设置→开发者（SaveManager）**；聊天入口在工具页 Tab（`nav.external → https://chat.hhj520.top`），是否显示由 `localStorage['baihe_chat_enabled']` 控制（开发者选项开关）。

### 4.3 设计令牌系统（改 UI 必读）

`app.css` 是「设计令牌单一入口」，三层结构（已核验）：

1. **Primitive Palette**（`:root`）：`--brand-50..900`（Apple System Blue #007AFF）、`--background-*`、`--text-*`、`--icon-*`、`--traffic-*`（macOS 红绿灯）、`--state-success/error`。
2. **Semantic Roles**：`:root`（亮色）与 `.dark`（暗色）各定义一套 `--background`、`--foreground`、`--card`、`--primary`、`--muted`、`--destructive`、`--border`、`--ring`、`--sidebar-*`、`--success` 等。**默认暗色**。
3. **Tailwind 4 `@theme inline`**：把语义变量映射成 Tailwind 工具类（`--color-card: var(--card)`），所以页面里可用 `bg-[var(--card)]` 或 `bg-card` 风格。

> 改配色/主题 → 只改 app.css 令牌；新增图标 → 放 `lib/icons/*.svg` 并在 Icon.svelte 的 aliasMap 加别名。

### 4.4 Icon 图标系统

- `Icon.svelte` 用 `import.meta.glob('./icons/*.svg', { query: '?raw', eager: true })` 加载全部图标（当前 14 个，命名 `image_N[_hash].svg`），正则 `image_(\d+)` 提取 key。
- `aliasMap` 把语义名映射到 `image_N`（user/circle-play/arrow-down/grip/box/plus/upload/package/search/check-circle/palette/info/download/message-circle/settings 等，`settings` 复用 `image_3`）。
- **已知占位**：`circle-x` 映射到 `image_11`（info 图标），注释说明图标集暂无 close/x 图标——以后补图标时要改这里。
- 新增图标 = 放 svg 文件 + 加 aliasMap 条目，无需其他改动。

### 4.5 主题同步机制（前后端联动）

- 前端 `theme.toggle()`：切 `currentTheme` → 加 `.theme-transitioning` 过渡类（200ms 移除）→ 写 `localStorage('baihe_theme')` → 切 `<html>` 的 `.dark` 类 → `ipc('theme.set', {theme})` 通知后端。
- 后端 `theme.set` 调 `ApplyThemeToWindow`：同步 WebView2 `DefaultBackgroundColor` + 窗口 `Background` + `TitleBarBorder` 背景/边框/文字色（暗色 #1A1A1C / 亮色 #F7F7FA）。
- WebView2 `NavigationCompleted` 时读 `localStorage.getItem('baihe_theme')` 反向同步一次，保证冷启动不闪色（默认 dark）。

---

## 5. 后端架构（Baihe.Host）

### 5.1 服务清单（Services/，全部核验 v1.1.25；26 个文件）

| 服务 | 行数(约) | 职责 | 关键点 |
|---|---|---|---|
| MainWindow.xaml.cs | 791 + Chat partial(166) | 窗口 + WebView2 + **所有 IPC 命令注册**（57 条命令 + IpcRouter 内置 ping） | 新增命令改这里（RegisterHostCommands）；聊天注入在 MainWindow.Chat.cs |
| LaunchService | 982 | 启动管线（最大文件） | 版本 JSON 合并 / natives 提取 / classpath / JVM+Game 参数 / 进程监控；含 CleanLauncherProfilesGameDir；Launch 支持目标服务器覆盖参数（v1.1.10）；EnsureOptionsTxt 含 serverResourcePacks:true 并提供公开 EnsureLaunchOptions()（App 启动预热，v1.1.11） |
| NbtHelper | 479 | Minecraft NBT 格式通用读写 | 大端序（BinaryPrimitives.*BigEndian）；servers.dat 未压缩 |
| ServerListService | 99 | 自动把白鹤服务器加入 servers.dat | 启动游戏前调用；同 ip 条目自动改名「白鹤服务器」，幂等 |
| DownloadService | 468 | 下载管线 | SHA1 校验 + 6 并发 + 进度推送；`.tmp` 临时文件校验后改名 |
| MicrosoftAuthService | 694 | 微软设备码登录 + 令牌刷新 | 6 步流程，Xerr 错误码映射 |
| ThirdPartyAuthService | 371 | Yggdrasil/Authlib-Injector | ALI 指示解析（≤5 次重定向）、LittleSkin 预设 |
| AuthService | 87 | 统一账户管理（缓存 + 持久化） | 单账户模式，account.json；RefreshIfExpired 只处理 Microsoft |
| SettingsService | 204 | 用户设置 + 内存检测/推荐 | P/Invoke GlobalMemoryStatusEx；推荐算法见 §8.5 |
| VersionService | 126 | Mojang 版本清单（24h 缓存） | 自建 HttpClient（注释里的 NetworkService 已随 Core 删除，注释过时） |
| InstanceService | 165 | 扫描实例 + 当前实例选择 | GetMcDirectory() 4 级路径回溯；Fabric 实例优先；**v1.1.24 IsInstalled 收紧**（Fabric 子实例 parent jar 必须存在才算已安装），GetCurrentInstance 对未安装的选中项自动回退到最佳已安装实例 |
| JavaHostService | 123 | 检测捆绑 JRE / 系统 Java | `java -version` 输出在 **stderr** |
| FabricService | 131 | Fabric Loader 安装 | 版本 ID = `{gameVersion}-fabric`；走 DownloadVersionFromJson |
| ServerStatusService | 54 | 服务器在线检测（TCP ping） | 3s 超时，地址从 Settings 动态读 |
| ModService | 405 | Mod 列表/启停/删除 | **v1.1.8 增强：fabric.mod.json 元数据 + 图标提取（base64 data URL）+ 中文名映射表（14 个），均带 mtime/size 缓存**；v1.1.23+ 新增中文介绍映射 `ChineseDescMap`（mod id 精确 → displayName/id 模糊 → 原 description 三级回退）；启停 = 改 `.jar` ↔ `.jar.disabled`；**统一用全局 mods 目录（版本专属目录不被游戏加载，v1.1.2 修复）** |
| SaveService | 280 | 存档备份/导入/恢复 | zip + 临时目录；导入按 level.dat 识别 |
| ShaderService | 230 | 光影管理（v1.1.3+） | 扫描 shaderpacks/*.zip；启用状态读写 `.minecraft/config/iris.properties`（shaderPack= / enableShaders=） |
| ToolService | 144 | 截图列表 / 打开文件夹 / 游戏修复 | 修复 = 完整性检查（报告型） |
| TrayService | 144 | 系统托盘（WinForms NotifyIcon） | **唯一实例类**；三路图标加载兜底 |
| UpdateService | 600 | GitHub Releases 更新检查 + 两段式升级支撑 | **1h 结果缓存（cache/update_check.json，含 CurrentVersion 一致性校验）+ `force` 强制刷新**；v1.1.18+ DownloadUrl 固定加速直链（BuildAcceleratedUrl=8091 `?token=`）；DownloadOnlineInstallerAsync 拉起 40KB 在线安装器（v1.1.23 两段式升级）；GetOnlineToken/CleanToken 做 BOM 清洗；**镜像测速（FetchMirrors/PickFastestMirror*/MeasureSpeedAsync）已是死代码**（§13-A1） |
| TelemetryService | 175 | 遥测上报（每会话首次 + 服务端策略） | 见 §8.6 |
| WeChatService | 68 | 微信名持久化（wechat.json） | 独立于账户 |
| NewsService | 78 | 首页「最新动态」（v1.1.8+） | 拉取仓库 news.json（4s 超时）失败回退内置 3 条；远程增删公告无需发版 |
| MinecraftRules | 67 | 版本 JSON rules 检查（v1.1.6+） | 统一 LaunchService(JsonNode) 与 DownloadService(JsonElement) 的 rules 过滤语义 |
| FormatHelper | 15 | 字节大小格式化 | 被多处复用 |
| ServerEntryService | 138 | 服务器条目增删读（servers.json，首启内置白鹤服务器） | v1.1.10 新增；**v1.1.13 移除 UI 下拉后前端已无 servers.* 调用，现为死功能**（QuickPlay 目标直接用 settings.ServerAddress/Port，§13-A2） |
| WikiService | 69 | 维基远程数据源拉取（wiki.json，v1.1.15+） | raw → jsDelivr → ghproxy 三源回退，单源 5s 超时；全失败返回 null 由前端回退内置数据 |

> 共 26 个服务文件（25 静态类 + TrayService 实例类）。相对文档 v2 新增：**ServerEntryService / WikiService**。

### 5.2 模型（Models/）

- **McAccount**：统一账户（`AccountType.Offline/Microsoft/ThirdParty`），字段含 Username/Uuid/AccessToken/RefreshToken/ExpiresAt(Unix 毫秒)/AuthServer/AuthServerName/Password/Email/IsUserSet；`TypeDisplay` 派生显示名；`ToOfflineAccount()` 兼容旧启动链路。配套静态 **AccountStore**（account.json，JsonStringEnumConverter + WriteIndented）。
- **OfflineAccount**：离线账户，`UUID = MD5("OfflinePlayer:<name>")` 版本 3 变体（`bytes[6]=0x30|..`, `bytes[8]=0x80|..`），32 位 N 格式。
- **GameInstance**：Id/Version/Type/Loader(vanilla/fabric/forge/quilt)/LastPlayed/ModCount/IsInstalled/DisplayName。

---

## 6. IPC 通信协议（最重要的扩展点）

### 6.1 机制

- **请求-响应**：前端 `ipc<T>(cmd, args)` → `window.chrome.webview.postMessage(JSON.stringify({id, cmd, args}))`；后端 `OnWebMessageReceived` → `IpcRouter.HandleAsync` → `PostWebMessageAsString({id, ok, response, error})`。前端 `crypto.randomUUID()` 配对，**15s 超时**。
- **主动推送**：后端 `IpcRouter.PushEvent(type, data)`（静态，`OnPushMessage` 回调由 MainWindow 在 WebView2 初始化后注入，Dispatcher 封送到 UI 线程）→ 前端 `on(type, cb)` 订阅（返回取消函数）。
- **特殊原始消息**（不走 IpcRouter，在 `OnWebMessageReceived` 里字符串拦截）：
  - `"__nav_home__"`：从聊天页返回启动器主页（重新 Navigate 入口 URL）
  - `"__chat_notify__:<msg>"`：聊天新消息 → 托盘通知（窗口在托盘或未激活时）
- **JSON 命名**：后端统一 `JsonSerializerDefaults.Web`（camelCase）；前端字段小驼峰；匿名对象返回时属性名小驼峰。
- **错误处理**：IpcRouter.HandleAsync 捕获一切异常并返回 `{ok:false,error}`，用原始 messageId 保证前端 Promise 能 reject。

### 6.2 完整命令清单（请求-响应，核验自 MainWindow/服务）

| 命令 | 参数 | 返回 | 说明 |
|---|---|---|---|
| ping | — | "pong" | 存活检测（IpcRouter 内置） |
| window.close/minimize/maximize | — | true | 窗口控制（Dispatcher 封送） |
| app.getVersion | — | 版本字符串 | 优先 FileVersion（Release 从 tag 注入），回退 AssemblyVersion（无硬编码） |
| update.check | {force?} | UpdateInfo | GitHub Releases；**1h 结果缓存（CurrentVersion 一致性校验），`force:true` 强制刷新**；失败先给过期缓存再静默无更新。**v1.1.18+ DownloadUrl 固定为加速直链（source="自建加速"），不再镜像测速** |
| update.download | version 字符串 | {success,error?} | **v1.1.23+ 两段式升级**：经 8090 加速服务把 `BaiheOnlineSetup_v{ver}.exe`（约 40KB）下载到 %TEMP% 并启动它，500ms 后主程序自己 Shutdown，由在线安装器接管完整包下载与安装（流程见 §7.8） |
| update.patch | — | {success,started,staged?} | **v1.1.26+ 触发增量更新**：后端 CheckForUpdate 判定 PatchAvailable 后后台下载/校验暂存，进度与结果走 patch.* 推送（§7.9）；无可用补丁返回 success=false |
| update.patchRestart | — | {success,error?} | 应用已暂存补丁：生成 apply 脚本并启动 → 主程序退出 → 脚本换文件+自动重启新版；未暂存时失败 |
| news.list | — | NewsItem[] | 首页最新动态（v1.1.8+）：拉取仓库 news.json，失败回退内置 |
| wiki.get | — | WikiCategory[] | 玩家指南维基（v1.1.15+）：拉取仓库 wiki.json（可远程编辑），失败返回空（前端回退内置） |
| version.list | typeFilter? | {latest, versions[]} | Mojang 清单（24h 缓存） |
| instance.list | — | GameInstance[] | 扫描 versions/ |
| instance.current | — | GameInstance | 当前实例（无实例时后端 `!` 解引用可能抛错，前端 Home 有兜底） |
| auth.current | — | 账户信息对象 | 未设置时 username=null, isUserSet=false |
| auth.hasAccount | — | {hasAccount} | 启动前检查 |
| auth.offline / auth.setOffline | username | {username, uuid, isUserSet} | 两个命令同义（别名） |
| auth.msLogin | — | {started} | **异步**，结果靠 `auth.msLoginResult` 推送 |
| auth.msCancel | — | {cancelled} | 取消微软登录（Cancel _msCts） |
| auth.thirdPartyLogin | {serverUrl,username,password} | {success,username,error} | 同步请求-响应 |
| java.detect | — | 系统 Java 数组 | PATH 查找 |
| java.bundled | — | {found,path,version} | 捆绑 JRE（含开发环境回溯） |
| launch.start | {instanceId?, serverAddress?, serverPort?} | {success,processId,error} | 含账户检查 + 微软令牌刷新 + 遥测上报；serverAddress/serverPort 可选覆盖 QuickPlay 目标（v1.1.10；前端现不传，走 settings 默认值） |
| launch.status | — | {state,message,processId} | 启动状态 |
| download.start | versionId | {success,message} | 异步（Task.Run，不阻塞响应） |
| download.status | — | {isDownloading,error} | 下载状态 |
| fabric.install | gameVersion | {success,message} | 异步安装 |
| fabric.loaders | gameVersion | {gameVersion,loaders[]} | 查询 Loader |
| settings.get | — | LauncherSettings | 读取设置 |
| settings.set | 部分字段对象 | LauncherSettings | 逐字段更新（有上下限钳制） |
| server.status | {serverAddress?, serverPort?} | {online,latency,address,port} | 服务器状态（v1.1.10：可选覆盖目标服务器） |
| servers.list | — | ServerEntry[] | 服务器列表（servers.json，内置白鹤服务器）。⚠️ v1.1.13 后前端无调用方（死功能，§13-A2） |
| servers.add | {name,address,port} | {success,entry?,error} | 新增服务器（同地址同端口去重）⚠️ 同上死功能 |
| servers.remove | id | {success} | 删除服务器（内置默认条目不可删）⚠️ 同上死功能 |
| mods.list | — | ModInfo[] | Mod 列表（含禁用；v1.1.8+ 含 iconDataUrl/chineseName/description） |
| mods.toggle | fileName | {success,enabled} | 启停 Mod |
| mods.delete | fileName | {success} | 删除 Mod |
| mods.openFolder | — | {success,path} | 打开 mods 目录 |
| shaders.list | — | {fileName,displayName,size,sizeText,enabled}[] | 光影列表（v1.1.3+，启用项排前） |
| shaders.enable | fileName | {success,enabled,error} | 启用光影（写 iris.properties；传空串=仅开光影不指定包） |
| shaders.disable | — | {success} | 关闭光影（enableShaders=false） |
| shaders.delete | fileName | {success,error} | 删除光影包（删除当前启用项时同步清空 shaderPack） |
| shaders.openFolder | — | {success,path} | 打开 shaderpacks 目录 |
| saves.list | — | SaveInfo[] | 存档列表 |
| saves.backup | saveName | {success,backupName,...} | 备份为 zip |
| saves.import | zipPath | 导入结果 | 导入存档 |
| saves.backups | — | BackupInfo[] | 备份列表 |
| saves.deleteBackup | fileName | {success} | 删除备份 |
| saves.restore | {backupFileName,saveName?} | 恢复结果 | 恢复备份（旧目录先改名 _old_ 保留） |
| screenshots.list | — | ScreenshotInfo[] | 截图列表 |
| tools.openFolder | folderName | {success,path} | 打开目录（minecraft/saves/screenshots/logs/mods） |
| tools.repair | — | 修复报告 | 完整性检查（version json/jar、libraries、assets、mods、java） |
| open.url | url | {success} | 系统浏览器打开（UseShellExecute） |
| nav.external | url | {success} | WebView 导航到外部站（聊天页） |
| nav.home | — | {success} | 返回启动器主页 |
| theme.set | {theme} | {success} | 同步后端窗口主题 |
| system.memory | — | {totalMB,totalGB,recommendedMB,recommendedGB} | 内存信息 |
| wechat.get | — | {name} | 读取微信名 |
| wechat.set | name | {success,name} | 保存微信名 |

### 6.3 推送事件清单

| 事件 | 数据 | 触发场景 |
|---|---|---|
| launch.state | {state,message} | 启动各阶段（preparing/launching/error） |
| launch.started | {processId} | 游戏进程已启动（窗口未出现，前端保持"启动中..."） |
| launch.windowShown | {processId} | **游戏窗口已出现（v1.1.13+）**——前端此时才显示"运行中" |
| launch.exited | {exitCode,abnormal,error} | 游戏退出（stderr 内容） |
| download.progress | {phase,currentFile,completedFiles,totalFiles,downloadedBytes,totalBytes,percent} | 下载进度 |
| download.complete | {success} | 下载完成 |
| download.error | {error} | 下载失败 |
| fabric.progress | {phase,message,loaderVersion?,intermediaryVersion?} | Fabric 安装进度 |
| fabric.complete | {success,versionId} | Fabric 完成 |
| fabric.error | {error} | Fabric 失败 |
| auth.msDeviceCode | {userCode,verificationUri} | 微软设备码（Login.svelte 展示） |
| auth.msLoginResult | {success,username,error} | 微软登录结果 |
| patch.progress | {percent,receivedMB,totalMB} | 增量补丁下载进度（v1.1.26+） |
| patch.complete | {files,deletes,from,target} | 补丁已暂存就绪，可重启应用 |
| patch.error | {error} | 补丁下载/校验失败 |

---

## 7. 核心业务流程

### 7.1 启动流程（LaunchService.Launch，1109 行）

```text
launch.start（MainWindow 先做账户检查：无账户→报错；微软→RefreshIfExpired）
  → _state=Preparing，PushEvent launch.state
  → GetMcDirectory() 定位 .minecraft（无 versions 目录→报错）
  → 检查 versions/<id>/<id>.json
  → 读原始 JSON 提取 inheritsFrom = parentId（合并前必须先拿，否则丢失）
  → 检查 versions/<parentId>/<parentId>.jar
  → FindJava（设置覆盖 → 捆绑 javaw → 开发回溯 javaw → 捆绑 java → 开发回溯 java → where javaw → where java）
  → LoadAndMergeVersion（递归合并 inheritsFrom，深度≤10；libraries 按 groupId:artifactId:classifier 去重，子版本优先；arguments.jvm/game 子在前父在后；合并后移除 inheritsFrom）
  → ExtractNatives（旧格式 natives 字段 / 新格式 :natives-windows，只抽 .dll，按大小增量提取）
  → BuildClasspath（跳过 native 库 + rules 过滤 + 客户端 jar = versions/<parentId>/<parentId>.jar，分号连接）
  → mainClass = version["mainClass"] ?? net.minecraft.client.main.Main
  → isFabric = mainClass 含 fabric/knot
  → GetLog4jConfigPath（logging.client.file.id → assets/log_configs/）
  → BuildJvmArgs + BuildGameArgs（手动拼，不解析 arguments.jvm/game）
  → EnsureOptionsTxt（onboardAccessibility:false 跳过无障碍引导、joinedFirstServer:true、tutorialStep:none、lang:zh_cn、serverResourcePacks:true 允许服务器资源包〔v1.1.11〕；同逻辑另经 EnsureLaunchOptions() 在 App 启动时预热）
  → 写 launch_cmd.log（完整命令行）
  → Process.Start（javaw，UseShellExecute=false，WorkingDirectory=.minecraft，**APPDATA=.minecraft**）
  → PushEvent launch.started{processId}
  → 异步改窗口标题为「白鹤服务器」（EnumWindows 找可见窗口 + SetWindowText，最长等 30s）
  → 异步监控退出（stderr 写 launch_error.log；PushEvent launch.exited）
  → CloseAfterLaunch=true 时延迟 2s Environment.Exit(0)
```

**启动参数要点**（改启动行为必读）：
- 用户类型固定 `--userType msa`（注释：offline 会触发 "Unrecognized user type" 警告）。
- QuickPlay：`majorVersion >= 21` 用 `--quickPlayMultiplayer host:port`，旧版用 `--server/--port`（主版本号从版本 ID 正则 `1\.(\d+)` 提取）。目标服务器可被 launch.start 的 serverAddress/serverPort 覆盖（v1.1.10），未指定时用 settings.ServerAddress/Port 默认值。
- Fabric 额外加 `-DFabricMcEmu= net.minecraft.client.main.Main`（**等号后有空格**，Fabric Loader 特有设计）。
- 内存：`-Xmx` + `-Xmn`（新生代 = 15%）；log4j 防御 `-Dlog4j2.formatMsgNoLookups=true`；堆转储路径 MojangTricksIntelDrivers...。
- `--accessToken` 直接透传；离线账户 AccessToken="offline-token"。

### 7.2 下载流程（DownloadService）

版本 JSON（VersionService 拿 URL）→ 客户端 JAR → 库文件（Mojang downloads.artifact 优先，Maven name+url 回退，rules 过滤）→ 资源索引（assets/indexes/<id>.json）→ 资源文件（assets/objects/<hash前2位>/<hash>，URL 固定 resources.download.minecraft.net）。全程 SHA1 校验：`.tmp` 下载 → 校验通过改名替换；已存在且校验通过的文件跳过。并发 SemaphoreSlim(6)，进度按文件计数（percent 按 completedFiles/totalFiles）。**Fabric 安装复用 `DownloadVersionFromJson`**。

### 7.3 认证流程（三种）

- **离线**：`auth.setOffline(name)` → OfflineAccount UUID 算法 → 存 account.json。
- **微软**（MicrosoftAuthService，6 步）：
  1. devicecode（client_id 用 Prism Launcher 公开注册 ID `c36a9fb6-...`，scope 含 `XboxLive.SignIn offline_access openid email`）→ 回调推送 `auth.msDeviceCode`
  2. 轮询 token（处理 authorization_pending/slow_down(+5s)/expired_token/access_denied/bad_verification_code）
  3. Xbox Live 认证（RPS）→ 4. XSTS（SandboxId=RETAIL，RelyingParty=rp://api.minecraftservices.com/）→ 5. Minecraft token → 6. Profile
  - **Xerr 错误码映射**：2148916233 无 Xbox 档案 / 2148916235 地区不支持 / 2148916238 未成年 / 2148916236 需验证 / 2148916234 国家/地区不支持。
  - 邮箱从 id_token JWT payload 提取（email 或 preferred_username）。
  - 刷新：`refresh_token` → 重跑 3-6 步；`IsTokenExpired` 提前 60s 判定。
- **第三方**（ThirdPartyAuthService，Yggdrasil）：
  - ALI 解析：HEAD（405 回退 GET）→ `X-Authlib-Injector-Api-Location` 头跟随，或 3xx Location 手动跟随，≤5 次；无头则当前地址即 API。
  - authenticate → 无 selectedProfile 时取 availableProfiles[0] 并 refresh 绑定角色；clientToken 存入 `RefreshToken` 字段；`Password` 存入账户对象（**明文，见 §10 问题**）。
  - 预设服务器：LittleSkin `https://littleskin.cn/api/yggdrasil`。

### 7.4 更新检查流程（UpdateService，v1.1.18+ 固定加速直链）

1. `CheckForUpdateAsync(force)` 先读缓存 `cache/update_check.json`（TTL 1h，且要求缓存 CurrentVersion == 当前程序集版本——防升级后旧缓存误报横幅），非 force 命中即秒回。
2. 未命中：`GET api.github.com/repos/pkoiuu/mcbh/releases/latest`（5s 超时）解析 tag_name；当前版本读 `Assembly.GetName().Version`（app.getVersion 走 FileVersion，Release 下两者都由 tag 注入一般一致）。
3. **下载链接 = `BuildAcceleratedUrl(version)`**：`http://199.68.217.4:8091/github.com/pkoiuu/mcbh/releases/download/v{ver}/BaiheServer_Setup_v{ver}.exe?token={编译注入}`（8091 为浏览器可直接打开的 query-token 形式）。返回 speedMbps=0、source="自建加速"；**不再构造镜像、不再测速**。
4. 失败兜底：优先返回过期缓存（allowStale，防误报「无更新」），否则静默 hasUpdate=false。

### 7.8 两段式自升级与在线安装器（v1.1.11 新增项目，v1.1.23 起为主升级通道）

```text
主程序（Home 更新横幅「立即更新」）
  → ipc('update.download', version)
  → UpdateService.DownloadOnlineInstallerAsync(version)
      GET http://199.68.217.4:8090/github.com/{owner}/{repo}/releases/download/v{ver}/BaiheOnlineSetup_v{ver}.exe
      （8090 = header "token: {编译注入}"，支持断点续传；UseProxy=false 禁系统代理；30s 总超时）
      写入 %TEMP%\BaiheOnlineSetup_v{ver}.exe
  → Process.Start(exe)，Task.Delay(500ms) 后 Application.Current.Shutdown()
在线安装器（MainForm 编排，net48 自绘深色界面）
  → UpdateService.GetLatestAsync()：api.github.com → ghproxy.net → ghfast.top（仅查版本号，单源 8s）
      BestUrl = http://199.68.217.4:8090/github.com/{owner}/{repo}/releases/download/v{ver}/BaiheServer_Setup_v{ver}.exe
  → Downloader(candidates=[BestUrl], threads=8, authHeaderName="token", token)
      探测：bytes=0-0（30s 超时）；206+Content-Range ⇒ 得总大小+Range 支持；200 ⇒ 该轮转单线程
      多线程分块：ServicePointManager.DefaultConnectionLimit=16（net48 默认 2 是速度瓶颈）；
        预分配 SetLength(total)；256KB 缓冲 + FileOptions.SequentialScan；
        每块失败最多重试 3 次（1s*n 退避），块级重试不整包重来（v1.1.22）；
        读停滞超时 60s（ReadWithStallTimeout，v1.1.17 加固）；HttpClient 总超时 5min；
        结束校验 文件长度==total，不符抛 IOException → 记该线路失败触发上层重试（v1.1.19）
      两轮策略：第 1 轮逐候选 URL 多线程 → 全部失败删临时文件进第 2 轮 forceSingle 单线程全量兜底
        （GET 不带 Range 直接读到 EOF，最可靠）（v1.1.20）
  → 成功后运行 BaiheServer_Setup_v{ver}.exe → Inno 向导接管（杀进程/迁移逻辑见 §9.4）
自检命令：--selftest（验证 BestUrl 指向加速服务的完整安装包而非在线安装器 + 带 token probe 可达性 +
          双不可达线路 stall 保护 <60s PASS；写 %TEMP%\baihe_selftest.log，退出码 0/1）
         --download-test（真实下载全链路实测，写 %TEMP%\baihe_download_test.log）
```

**token 细节（v1.1.21 教训，改这里必读）**：
- GetToken() 优先环境变量 `BAIHE_ONLINE_TOKEN`（本地调试），否则反射读 AssemblyMetadata("OnlineToken")（release.yml `-p:OnlineToken=$env:BAIHE_ONLINE_TOKEN` 注入，Host 与 OnlineInstaller 各注入一次，源码不含令牌）。
- `CleanToken()` 必须 Trim + 去 U+FEFF/U+FFFE —— 曾因 GitHub secret 带 BOM 导致加速服务全量 401。
- URL 必须保留 `github.com/` 原路径前缀（加速服务按路径转发）；换加速服务地址需同步改 Host UpdateService（AcceleratorBrowserBase/AcceleratorCliBase）与 OnlineInstaller UpdateService（AcceleratorHost）两处。

### 7.6 光影流程（ShaderService，v1.1.3+）

光影包 = 放入 `.minecraft/shaderpacks/*.zip` 的 zip 文件，Iris 识别；当前启用项记录在 `.minecraft/config/iris.properties`（`enableShaders=true` + `shaderPack=<文件名>`，无该文件时自动创建默认）。`shaders.enable` 写两个字段；`shaders.disable` 写 `enableShaders=false` + 清空 shaderPack；删除当前启用项时同步清空。**Iris 需作为 Mod 启用**，且光影生效需重启游戏（前端有提示）。预装 4 个光影包（Complementary/BSL/Sildur's/MakeUp）的说明与预览图在前端 `lib/shaders.ts`（按 fileName 匹配，用户自装包无元数据显示通用信息）。

### 7.7 Mod 元数据与中文名（ModService，v1.1.8+）

`mods.list` 时逐个打开 jar 读 `fabric.mod.json`/`quilt.mod.json`：`name`（真实显示名）+ `description`（介绍）+ `icon`（图标文件，支持字符串或 sizes 对象取最大，提取为 base64 data URL，≤1MB）。**中文名映射表 `ChineseNameMap`**（14 个：sodium→钠、iris→Iris 光影、imblocker→输入法冲突修复 等）按 mod id 精确匹配 → displayName 模糊匹配 → 回退原名。解析结果按文件名缓存（mtime/size 变化才失效），避免每次列表都解压 jar。

### 7.9 智能增量更新（相邻版本补丁，v1.1.26 引入）

```text
发布侧（release.yml / test-release.yml 均执行）
  Publish/Copy resources 后 → scripts/generate-update-patch.ps1
    stage 树 = dist\launcher\* + dist\.minecraft\*
    规则源: iss [Files] 主条目 Excludes 动态解析(glob '\'/' 等价, * 单段)
          + .minecraft 仅 versions|libraries|assets|mods|shaderpacks 可打
          + 硬保护: options.txt/servers.dat/config/**/saves/**/screenshots/**/
            logs/**/crash-reports/**/downloads/**/current_instance.txt/
            settings.json/account.json/servers.json/wechat.json/*.log/cache/**
    产出 BaiheManifest_v{ver}.json(全量 rel→size+sha256) — 必产
         BaihePatch_v{prev}_to_{ver}.zip(+_meta.json 含 files/deletes/hashes)
         条件: 存在同通道上一版 manifest 且 versions 树无变化(否则 exit 2 只出清单)

启动器侧
  update.check → UpdateService.CheckForUpdateAsync
    ├─ 正式构建: /releases/latest → assets 精确名匹配 BaihePatch_v{cur}_to_{latest}.zip
    └─ 测试构建(Channel=test, TagFull 注入): /releases 列表挑同族更高 -test 预发布,
       匹配 BaihePatch_v{self}_to_{target}.zip;完整包 URL 兜底照常
  Home 横幅双态: 增量更新·约XMB → (update.patch)→进度条←patch.progress
                →patch.complete→「重启完成更新」(update.patchRestart)
  PatchService.DownloadAndStageAsync: 8090+token 单流下载 → zip-slip 防护解压至
    %TEMP%\baihe_patch_stage_{tag} → 逐文件 SHA256 对账 _meta.hashes → staged
  TryPrepareAndLaunch: 生成 baihe_apply_update.ps1(Sleep3→deletes→rename-then-copy×5重试
    →done.marker→Start Baihe.exe) → powershell 启动 → 主程序 Shutdown

回退矩阵
  无上一版 manifest | 隔多版 | versions 树变化 | 补丁缺失/校验失败
      ↓ 全部自动落回既有完整安装链路(update.download 两级式),该链路未动
```

生效条件：**同一通道连续两个带 manifest 的版本才产生第一批差量包**（首个 manifest 版只产清单）。

### 7.5 遥测流程（TelemetryService）


- 端点：`https://bh-telemetry.hhj520.top`，ApiKey 编译期常量 `5818...`，`X-Api-Key` 头。
- 会话级去重（`_hasReportedThisSession`）；首次上报前 `GET /api/track/policy` 取服务端策略，**策略明确禁用才跳过；策略不可达按 fail-open（允许上报）**；失败静默。
- payload：uuid/username/email/wechatName/accountType/launcherVersion/os/language。在 `launch.start` 时统一上报（accountType 为当前账户类型，Offline/Microsoft/ThirdParty）。

---

## 8. 持久化文件清单（均在 AppContext.BaseDirectory，即 exe 目录）

| 文件 | 内容 | 读写方 |
|---|---|---|
| settings.json | LauncherSettings（9 字段，首启用内存推荐初始化） | SettingsService |
| account.json | McAccount（**明文 JSON，含第三方密码**） | AccountStore |
| wechat.json | 微信名 | WeChatService |
| servers.json | 服务器列表 ServerEntry[]（首启用内置白鹤服务器；前端无 UI 入口，死功能 §13-A2） | ServerEntryService |
| cache/version_manifest.json | Mojang 清单（24h） | VersionService |
| cache/update_check.json | 更新检查结果（1h，v1.1.5+） | UpdateService |
| current_instance.txt | 选中实例 ID | InstanceService |
| launch_cmd.log / launch_error.log | 启动命令/错误诊断 | LaunchService |
| servers.dat（.minecraft/） | Minecraft 多人游戏服务器列表（NBT） | ServerListService 启动时确保「白鹤服务器」在列 |
| .minecraft/config/iris.properties | 光影开关 + 当前启用包（v1.1.3+） | ShaderService |
| .minecraft/shaderpacks/ | 光影包 zip（预装 4 个） | ShaderService / 用户 |
| natives_debug.log / debug-paths.txt | 调试日志 | LaunchService / WebViewHost |
| .minecraft/** | 游戏目录（.minecraft 由 GetMcDirectory 定位） | 各服务 |

> 所有服务都是静态类，把状态缓存在 static 字段里（`_cached`、`_currentAccount`、`_state`、`_isDownloading`、`_hasReportedThisSession`），单例便利但线程安全与测试性差（见 §10）。v1.1.6 起部分服务已加锁/并发集合（SettingsService `_cacheLock`、ModService ConcurrentDictionary、UpdateService 缓存），但整体仍是静态状态模式。

---

## 9. 构建与打包

### 9.1 本地开发

```powershell
# 1. 前端（输出直接进 ../Baihe.Host/wwwroot）
cd src/Baihe.UI
pnpm install
pnpm build
cd ../..

# 2. 后端
dotnet build src/Baihe.Host/Baihe.Host.csproj -c Release

# 3. 运行（WebView2 加载 https://baihe.app/ 映射的 wwwroot）
dotnet run --project src/Baihe.Host/Baihe.Host.csproj
```

### 9.2 CI（.github/workflows/ci.yml）

push/PR → windows-latest：setup .NET 10 + Node 22 + pnpm 11 → 并行（pnpm install 后台 + dotnet restore 前台）→ pnpm build → dotnet build -c Release -m --no-restore → **仅 main push**：7z 打包 `baihe-build.zip` 并以 prerelease tag `latest` 发布（国内可用 `scripts/download-build.ps1` 镜像下载）。

### 9.3 Release（.github/workflows/release.yml）

打 tag `v*` → windows-latest：
1. **并行准备**：① jlink 构建 JRE（20 模块，`--compress=2 --strip-debug`）② 下载 WebView2 离线安装包（fwlink）③ `gh release download v1.0-assets --pattern minecraft.7z` 解压出 .minecraft ④ dotnet restore ⑤ pnpm install + build ⑥ **dotnet publish src/Baihe.OnlineInstaller → dist\online，`-p:OnlineToken=$onlineToken`**（secrets.BAIHE_ONLINE_TOKEN；空值也可编译，仅本地调试走环境变量）。
2. `dotnet publish src/Baike.Host/Baihe.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:AssemblyVersion=$version -p:FileVersion=$version -p:OnlineToken=$env:BAIHE_ONLINE_TOKEN`（版本号从 tag 注入；**OnlineToken 同步注入 Host 程序集**供两段式升级的 GetOnlineToken 读取）。
3. 复制 icon.ico、fabric-installer.jar → dist/launcher；信息性步骤校验 dist 内容。
4. `ISCC.exe /DMyAppVersion=$version installer\baihe_installer.iss`（Inno Setup 6）产出完整安装包；随后 `Copy dist\online\BaiheOnlineSetup.exe → dist\BaiheOnlineSetup_v$version.exe`。
5. softprops/action-gh-release 上传 **两个资产**：`dist/BaiheServer_Setup_v*.exe`（完整安装包）+ `dist/BaiheOnlineSetup_v*.exe`（在线安装器）（**ASCII 文件名**；主程序与在线安装器都靠 tag→exeName 直接构造下载 URL，不走 assets 遍历，见 §7.8 / §10-22）。
6. 另有独立工作流 **pages.yml**（Deploy Official Site）：push 到 main 且触碰 site/** 或 wiki 相关路径时自动部署——官网（site/）为站点根 https://pkoiuu.github.io/mcbh/ ，网页版维基迁至 /wiki/ 子路径（wiki-site/index.html + 根 wiki.json 副本），news.json 复制到站点根供官网公告区与启动器共用。
7. **test-release.yml**（Test Release，v1.1.26 开发期引入）：推送 `vX.Y.Z-test<N>` 标签（如 v1.1.26-test1）或手动 dispatch → 构建双安装包并发布为**预发布 Release**。核心机制：GitHub `/releases/latest` 端点天然排除预发布，故主程序 UpdateService 与正式在线安装器均不会把测试包推给玩家（稳定通道零感知）。版本号映射：资产名保留完整 tag（`BaiheServer_Setup_v1.1.26-test1.exe`），Host FileVersion=基础版.序号=1.1.26.1（纯数值可比较），Inno 显示版=基础版；测试在线安装器经 AssemblyMetadata("AppVersion") 注入 tag 全值 + Program.IsTestBuild 判定走 `/releases?per_page=15` 列表挑同族 `-test` 预发布（GetLatestTestAsync），确保拉到同族测试完整包而非稳定包；Release job 有 `!contains(ref,'-test')` 守卫防双流水线重复跑。注意事项见 §15.4。

> .minecraft（约 1.3GB）不进 git，存于 release asset `v1.0-assets`，更新游戏文件后跑 `scripts/upload-minecraft-assets.ps1` 重新上传（7z -mx=9，排除 logs/crash-reports/downloads/servers.dat_old 等）。
> CI 与 Release 均已加 **pnpm store / node_modules 与 NuGet 缓存**（actions/cache，按 lock/csproj 哈希做 key）。

### 9.4 安装器（installer/baihe_installer.iss）

- 安装目录 `%LOCALAPPDATA%\BaiheServer`，`PrivilegesRequired=lowest`（免管理员）；默认版本宏 `MyAppVersion "1.1.25"`（Release 用 /D 覆盖；发新版需同步 Host csproj / OnlineInstaller Program.AppVersion / 此处三处版本号，见 §13-A4）。
- **升级检测**：固定 AppId（`{8F2B7A3C-...}`）+ UsePreviousAppDir。
- **升级/安装前杀进程**：`InitializeSetup` / `PrepareToInstall` / `InitializeUninstall` 三重检查 `tasklist` + `taskkill /T /F`（含 WebView2 子进程），用户可取消；PrepareToInstall 最多重试 3 次。
- **文件分层**：
  - 启动器本体 `dist\launcher\*` 排除 jre、WebView2 安装包、`settings.json`、`account.json`、`current_instance.txt`、`*.log`、`debug-*.txt`、`cache\*`、`Baihe.exe.WebView2\*`（**升级保留用户配置**；current_instance.txt 在 [InstallDelete] 一并删除——v1.1.24 升级后强制重选实例的修复）。
  - `.minecraft\versions|libraries|assets|mods|shaderpacks` 始终覆盖（ignoreversion；mods/shaderpacks 只更新同名文件，用户自加的不受影响）。
  - `options.txt`、`servers.dat`、`config\*`、`launcher_profiles.json` 用 `onlyifdoesntexist uninsneveruninstall`（首装写入、升级不覆盖、卸载保留）。
  - **[InstallDelete]**：升级时清理旧版内置版本目录（`versions\1.21.3`、`fabric-loader-0.16.14-1.21.3`；当前内置为 **1.21.8**）、旧版 1.21.3/1.21.2 通配与精确文件名模组（防新老版本模组共存崩溃）、以及 `{app}\current_instance.txt`（配合 InstanceService 已安装校验自动回退到 1.21.8 实例，v1.1.24）——**换内置 MC 版本需同步维护此段**（配套脚本 scripts/update-bundled-game.ps1）。
- **卸载保留**用户数据（saves/options.txt/screenshots/config 等），只删 logs/crash-reports/downloads。
- [Run] 检测 WebView2（注册表 `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`）未装则静默安装；快捷方式用 `{userdesktop}`（lowest 权限下 commondesktop 不可写）。

### 9.5 脚本（scripts/）

- `download-build.ps1`：镜像（ghfast.top > ghproxy.net > gh-proxy.com > ghproxy.link > 直连）下载 CI 的 `latest/baihe-build.zip`，解压部署到 `src/Baihe.Host/bin/CI/...` 并启动（本地体验 CI 构建）。
- `fork-rename.ps1`：把 PCL.Core fork 成 Baihe.Core 的重命名/裁剪脚本（Core 已移除，基本不再用）。
- `upload-minecraft-assets.ps1`：打包 installer_resources/.minecraft 为 minecraft.7z 并上传到 `v1.0-assets` release。
- `update-bundled-game.ps1 [-GameVersion 1.21.8]`：一键重建内置游戏 —— Mojang manifest 下 vanilla 版本 + Fabric Meta 装 loader + Modrinth API 装模组，写入 installer_resources/.minecraft（含 SHA1 校验、旧版本目录清理）；完成后仍需 upload-minecraft-assets.ps1 重传 minecraft.7z，并同步维护 iss [InstallDelete] 的旧版本迁移条目。
- `generate-wiki-json.mjs`：从 `src/Baihe.UI/src/lib/wiki/*.ts` 生成仓库根 `wiki.json`（维基免发版更新的生成端；也可手工直接编辑 wiki.json）。

---

## 10. 关键约定与踩坑（修改前必读）

1. **csproj 手动列文件**：因 .NET 10 SDK glob 展开 bug，`EnableDefaultCompileItems=false` / `EnableDefaultPageItems=false`，**新增 .cs 必须手动加 `<Compile Include>`，新增 XAML 加 `<Page Include>`**。漏加会出现「文件存在但没被编译」的诡异问题。
2. **前端产物目录命名**：用 `wwwroot` 而非 assets，避免与 `Assets/icon.ico` 在 Windows 不区分大小写下冲突。csproj 已含 `<Content Include="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest">`，发布时 wwwroot 自动进输出。
3. **WebView2 兼容插件**：vite.config.ts 的 removeCrossOrigin 移除 crossorigin、`type="module"` 换 `defer`（虚拟主机映射对 ESM 支持不完整）。
4. **.svelte.ts 扩展名**：在 .ts 里用 Svelte 5 runes 必须用该扩展名（router/theme/toast）。
5. **Fabric 合并后丢失 parentId**：启动逻辑在 LoadAndMergeVersion 之前先读原始 JSON 拿 inheritsFrom。
6. **Java 检测**：`java -version` 输出到 stderr，不是 stdout。
7. **微软登录是异步推送模式**：`auth.msLogin` 立即返回，结果靠 `auth.msLoginResult` 推送，前端不能在请求响应里等。
8. **窗口关闭 = 最小化到托盘**：OnClosing 拦截首次关闭，真正退出走托盘「退出」菜单（Application.Shutdown）；配合 App.xaml.cs 单实例 Mutex（v1.1.9）。
9. **CSP**：前端大量 `javascript:void(0)` 与内联 onclick（Svelte 事件），WebView2 未启用严格 CSP，本项目可放心用内联事件（与 fuwari-blog 的严格 CSP 完全不同）。v1.1.8 已清理大部分 `javascript:void(0)`。
10. **静态服务 + 静态状态**：所有服务是静态类，IPC 多线程（IpcRouter 用 ConcurrentDictionary），改状态时注意竞态。v1.1.6 起 Settings/Download/Launch 已加锁，Mod/Update 用 ConcurrentDictionary，新增服务请照此办理。
11. **`vite build` 不会清空 wwwroot 旧文件**：`emptyOutDir: true` 只对项目根内的 outDir 生效，而 wwwroot 在 Baihe.UI 项目根**之外**（`../Baihe.Host/wwwroot`）。**核验时（v1.1.9）wwwroot 是干净的（仅 1 套哈希资产）**，但换版本构建时建议 build 前手动清空 wwwroot 或改 outDir 策略，避免历史资产堆积进安装包。
12. **明文密码**：`McAccount.Password` 注释声称「加密存储」，但 AccountStore 只是 JSON 序列化，第三方密码以明文写入 account.json——安全风险。
13. **第三方令牌刷新是死代码**：`ThirdPartyAuthService.Refresh()` 存在但从未被调用；`AuthService.RefreshIfExpired` 只处理 Microsoft。第三方 accessToken 过期后没有刷新路径（Yggdrasil token 通常长期有效，但规范上应支持）。
14. **注释过时**：VersionService.cs 头部注释提到「使用 NetworkService 预配置的 HttpClient」，但 NetworkService 已随 Baihe.Core 删除，实际是自建 HttpClient。
15. ~~**app.getVersion 硬编码回退 "1.1.1"**~~ **已修复（v1.1.6+）**：现优先 FileVersion、回退 AssemblyVersion，无硬编码。改版本号只需 csproj 的 AssemblyVersion/FileVersion + tag + iss 默认值（/DMyAppVersion 覆盖）。
16. **instance.current 无实例时**：后端 `GetCurrentInstance()` 返回 null 时命令用 `!` 强制解引用可能抛异常（前端 Home 有「暂无游戏实例」兜底）。
17. **WeChat 弹窗**：`wechat.get` IPC 失败也会弹窗（onMount 中 catch 分支），属有意设计。
18. **开发者密码硬编码在前端**（Settings.svelte `111125hj`）——不是安全机制，只是防误触；开发者选项含聊天开关（`localStorage['baihe_chat_enabled']`）、版本下载入口、存档备份面板。
19. **单实例 Mutex 名称硬编码**（App.xaml.cs `BaiheServerLauncher_SingleInstance_Mutex_8F2B7A3C`）与 Inno AppId 前缀一致；改名/换 AppId 时两者要同步。
20. **光影只认 `.minecraft/mods`（全局）**：ModService 明确注释版本专属 mods 目录不被游戏加载（v1.1.2 修复）；ShadersPanel 提示需启用 Iris mod 且重启游戏生效。
21. **工具页 Tab 数据按需加载**（v1.1.9）：mods/screenshots 首次进入才拉取且带 `loaded` 缓存标志（防切 tab 卡顿），刷新按钮强制重拉——新增 Tab 时照此模式。
22. **Release 多 .exe 资产的匹配坑（v1.1.12）**：v1.1.11 起 release 同时含 `BaiheOnlineSetup_vX.Y.Z.exe`（在线安装器）与 `BaiheServer_Setup_vX.Y.Z.exe`（完整安装包），GitHub API assets 数组按名称排序时**在线安装器排在前面**，两处 `.exe` 匹配（主程序 UpdateService 与在线安装器 UpdateService）若取第一个会**下载到在线安装器**——必须在匹配时排除 `OnlineSetup`/`BaiheOnline`，只匹配完整安装包。在线安装器支持 `--selftest` 无界面自检（写 %TEMP%\baihe_selftest.log，退出码 0/1）。（现状注：v1.1.18+ 之后两侧都不再遍历 assets 数组，而是由 tag_name 直接构造 exeName，本坑已被结构性规避；未来新增其它 exe 资产时仍需留意匹配。）
23. **加速服务地址双项目硬编码**：`199.68.217.4:8090/8091` 同时存在于 Host `Services/UpdateService.cs`（AcceleratorBrowserBase/AcceleratorCliBase）与 OnlineInstaller `UpdateService.cs`（AcceleratorHost）——换地址必须两处同步；header 名固定 `token`（8090）/query `?token=`（8091）；URL 必须保留 `github.com/` 前缀；协议是 http 非 https（明文传输 token，§13-A3）。
24. **csproj 手动列文件的适用范围**：Host 项目 `EnableDefaultCompileItems/PageItems=false`（36 个 `<Compile Include>` 全手动）；OnlineInstaller 是常规 SDK net48 项目用默认 glob，新增 .cs 无需登记。
25. **发版三处版本号手动同步**：Host csproj AssemblyVersion/FileVersion ↔ OnlineInstaller `Program.AppVersion` ↔ iss `#define MyAppVersion`（v1.1.25 三处一致）——漏一处在两段式升级里会导致在线安装器资产名对不上而 404。

---

## 11. 如何新增一个功能（标准流程）

以「新增一个 IPC 命令」为例：

1. **后端**（若需要新逻辑）：在 `Services/` 新建/修改服务方法；**在 `Baihe.Host.csproj` 手动加 `<Compile Include>`**。
2. **注册命令**：在 `MainWindow.xaml.cs` 的 `RegisterHostCommands()` 里：
   ```csharp
   _ipcRouter.Register("xxx.do", async args => {
       var param = args?.ValueKind == JsonValueKind.String ? args.Value.GetString() : "";
       return await XxxService.DoSomething(param);
   });
   ```
3. **前端调用**：`import { ipc } from '../lib/ipc'`，`const r = await ipc<T>('xxx.do', arg)`。
4. **需要主动通知**：后端 `IpcRouter.PushEvent("xxx.event", data)`（静态，随处可调）；前端 `on('xxx.event', cb)` 订阅（在 `$effect`/onMount 里注册并返回清理函数）。
5. **验证**：`pnpm build`（前端）→ `dotnet build`（后端）→ 运行预览。

**新增页面/组件**：pages/ 新建 .svelte → router.svelte.ts 的 `PageKey` 加 key + navItems 加导航项（可选）→ App.svelte 加 `{#if}` 分支 → 图标放 lib/icons/ + Icon.svelte aliasMap。

**改 UI 样式**：颜色令牌只改 `app.css`；字体/间距直接 Tailwind 类。

**改首页公告（无需发版）**：编辑仓库根 `news.json`（数组 `news`：date/title/desc），启动器每次进主页拉取（4s 超时失败回退内置）。**改更新加速镜像（无需发版）**：编辑 `mirrors.json` 的 `mirrors` 数组。

**新增预装光影包**：① 把 zip 放进 `installer_resources/.minecraft/shaderpacks/`（随 .minecraft 打包）② 前端 `lib/shaders.ts` 加元数据（fileName 必须与 zip 文件名一致）+ 预览图放 `src/assets/shaders/` ③ 如需改默认启用项，改 `installer_resources/.minecraft/config/iris.properties`。

**新增预装 Mod**：① 放进 `installer_resources/.minecraft/mods/` ② 需要中文显示名时在 ModService `ChineseNameMap` 加映射（key 为 fabric.mod.json 的 id）。

**发新版本**：csproj 版本号 → 更新 `.minecraft`（如需要）→ `scripts/upload-minecraft-assets.ps1` → 打 tag `vX.Y.Z` → release.yml 自动出安装包（`BaiheServer_Setup_vX.Y.Z.exe`）。

---

## 12. 既有文档索引（深入某方向时查阅）

| 文档 | 侧重 | 时效 |
|---|---|---|
| 白鹤启动器_全栈架构分析报告.md | 全栈架构、14 服务映射、ADR、风险登记、重构路线图 | ⚠️ 描述含 Baihe.Core，已部分过时 |
| 深度代码分析报告.md | 启动正确性、版本 JSON 合并、库/资源完整性、与 PCL2-CE 对比 | ⚠️ 同上 |
| 白鹤启动器_架构方案_v1.0.md | 架构决策、备选方案、IPC schema 设计 | ⚠️ 同上 |
| 白鹤服务器启动器_开发规格文档.md | 产品规格、定制改动清单、打包、踩坑记录 | 部分仍有效 |
| docs/telemetry-api-guidelines.md | 遥测 API 规范 | 有效（与 TelemetryService 一致） |
| baihe-launcher-analysis/ | 一次性 HTML 分析快照 | 可忽略 |
| specs/P0-theme-switching、P1-memory-recommendation | 需求 spec + tasks + checklist（均已实现） | 参考 |

---

## 13. 已知问题与改进建议（按优先级；1~19 核验至 v1.1.9，A1~A5 为 v1.1.25 追加核验）

**高优先级（正确性/安全）**：
1. 第三方密码明文存储（account.json）→ 用 Windows DPAPI（`ProtectedData`）加密（仍未处理）。
2. 第三方令牌刷新未接入 → 在 `launch.start` 里对 ThirdParty 也调 `Refresh()`（仍未处理）。
3. `instance.current` 的 `!` 解引用在无实例时会抛错 → 返回 null 让前端处理（前端已有兜底，后端仍待修）。
4. **开发者密码硬编码在前端**（Settings.svelte `111125hj`）→ 移到后端或去掉密码（本就不该是安全边界，至少别扩散）。
5. 内置游戏（.minecraft）经 Inno 打包分发，涉及 Mojang EULA 与版权，属产品决策需注意。
6. ~~升级后仍显示更新横幅~~ **已修复（v1.1.11）**：UpdateService 缓存命中增加 CurrentVersion 一致性校验，升级后旧缓存自动作废。
7. ~~维基文字不能复制 / 搜索框文字错位~~ **已修复（v1.1.11）**：Wiki 根容器加 select-text；搜索框 input 改 h-full/min-w-0/flex-1 垂直对齐。
8. ~~最新动态不更新~~ **已修复（v1.1.11）**：NewsService 多源回退（raw → jsDelivr → ghproxy），news.json 同步 v1.1.10 内容。
9. ~~拒绝服务器资源包~~ **已修复（v1.1.10+11）**：EnsureOptionsTxt 写 serverResourcePacks:true + App 启动预热 EnsureLaunchOptions()；初始 options.txt 已加字段（随下次 minecraft.7z 打包生效）。
10. ~~在线安装器早期形态~~ 已演进为固定加速直链 + 两轮降级策略 + selftest 自检（§7.8）；原「镜像测速仅测前缀」机制已随加速体系废弃。

**中优先级（工程化）**：
11. 静态服务静态状态仍普遍（v1.1.6 起部分服务已加锁/并发集合，但 Auth/Launch 等仍是裸 static 字段）→ 逐步收敛。
12. 更新检查与 app.getVersion 版本获取方式不一致（AssemblyVersion vs FileVersion，Release 下一般一致）。
13. 硬编码散落：遥测地址/ApiKey、GitHub repo（pkoiuu/mcbh）、开发者密码、聊天站点 hhj520.top → 收敛到配置。
14. `OnWebMessageReceived` 是 `async void`，异常只进 Debug 输出 → 前端可能静默超时。
15. README 构建说明与代码不一致（build-all.ps1、复制 assets 步骤）。
16. App.svelte 注释称页面「懒加载」但实际是静态 import（`{#if}` 全量渲染），页面体积大时考虑 Svelte 动态组件。

**低优先级（体验）**：
17. Icon 缺 close/x 图标（circle-x 占位到 info）。
18. `saves.restore` 的旧目录改名逻辑与卸载保留策略需在 UI 上说明。
19. 光影仅 zip 扫描，不支持目录型/展开型光影包（Iris 也支持文件夹式 shaderpack）；shaderpacks 子目录会被漏列。

**追加核验发现（2026-08-26，基于 v1.1.25 源码）**：

A1. **镜像测速死代码**：UpdateService 的 FetchMirrorsAsync / PickFastestMirrorCachedAsync / PickFastestMirrorAsync / MeasureSpeedAsync / SpeedCache 及 mirrors.json 相关常量在当前调用图不可达（CheckForUpdateAsync 直接 BuildAcceleratedUrl）——**编辑 mirrors.json 不再影响任何下载行为**。建议整体删除，或改造为「加速服务故障时的 fallback 线路」。
A2. **servers.* 死功能**：ServerEntryService（138 行）与 `servers.list/add/remove` IPC 注册正常，但前端 grep 零调用（v1.1.13 移除下拉后未恢复）。launch.start 的 serverAddress/serverPort 覆盖参数保留可用，QuickPlay 目标实际来自 settings.ServerAddress/Port。
A3. **加速服务单点 + 明文 HTTP**：IP 直连无域名、token 明文传输、Downloader 只有同一 BaseUrl 一个候选——服务宕机 = 无法更新无法在线安装；建议加 HTTPS/域名与至少一条 fallback 候选。
A4. **发版三处版本号手动同步**：Host csproj ↔ OnlineInstaller Program.AppVersion ↔ iss #define（踩坑 25）。
A5. 老问题复核（v1.1.25 未变）：第三方密码明文存 account.json、ThirdPartyAuthService.Refresh() 死代码、开发者密码前端硬编码（111125hj）、instance.current 后端仍可能 null 解引用。

---

## 14. 快速定位表（改什么 → 看哪里）

| 想改什么 | 看哪里 |
|---|---|
| 加 IPC 命令 | MainWindow.xaml.cs RegisterHostCommands + Services/ + ipc.ts |
| 玩家指南/维基内容 | src/Baihe.UI/src/lib/wiki/*.ts（按章节拆分）+ pages/Wiki.svelte |
| QuickPlay 目标地址 | SettingsService.LauncherSettings.ServerAddress/Port + LaunchService.Launch(serverAddress?, serverPort?)（servers.json/ServerEntryService 为无 UI 死功能） |
| 启动参数/QuickPlay/内存 | LaunchService.BuildJvmArgs / BuildGameArgs |
| 下载逻辑/并发/校验 | DownloadService |
| 登录（微软/第三方/离线） | MicrosoftAuthService / ThirdPartyAuthService / AuthService |
| 设置项 | SettingsService.LauncherSettings + Settings.svelte |
| 光影管理 | ShaderService + ShadersPanel.svelte + lib/shaders.ts（元数据） |
| 首页「最新动态」 | NewsService + 仓库根 news.json + Home.svelte |
| Mod 列表/图标/中文名 | ModService（ChineseNameMap / 缓存）+ Tools.svelte mods Tab |
| 主题/配色/图标 | app.css / Icon.svelte + lib/icons/ |
| 页面布局/导航 | pages/ + Sidebar.svelte + router.svelte.ts（navItems） |
| 开发者选项/存档备份 | Settings.svelte developer 分类 + SaveManager.svelte |
| 安装包内容/升级策略 | installer/baihe_installer.iss（含 [InstallDelete]） |
| 版本号 | csproj + tag + iss /DMyAppVersion |
| 聊天页行为 | MainWindow InjectBackButtonAsync / InjectChatMonitorScriptAsync |
| 防多开/单实例 | App.xaml.cs Mutex |
| 更新横幅/两级式自升级 | UpdateService（check/download.download）+ Home.svelte + 在线安装器整个子项目（§7.8） |
| 加速服务/token/下载多线程 | Host UpdateService.BuildAcceleratedUrl·DownloadOnlineInstallerAsync；OnlineInstaller.UpdateService.GetToken/CleanToken + Downloader.cs |
| 维基内容更新（免发版） | 编辑 lib/wiki/*.ts → `node scripts/generate-wiki-json.mjs` 生成 wiki.json（或手编），网页版同步 wiki-site/ |
| 内置游戏更换 | scripts/update-bundled-game.ps1 → upload-minecraft-assets.ps1 → iss [InstallDelete] 迁移条目 |

---

## 15. 交付规范与教训记录（2026-08-25，v1.1.10 发布前）

> 本节记录一次真实发生的交付错误及纠正后的规范，避免重犯。

### 15.1 错误经过

- 在未查证 GitHub 的情况下，仅凭**本地** `git tag`、csproj、工作区状态，就对外断言「当前版本号」「是否已在 GitHub 编译」。
- 交付总结中把「开发中的功能」误称为 `v1.1.10`（实际当时版本仍为 1.1.9），随后在修正说明的方案里又出现 `1.1.10` 字样，前后反复，被用户质疑「版本号乱说、一派胡言」。
- 用户要求「去 GitHub 上查找」后才补做查证：远程 tags / `releases/latest` API / `actions/runs` 三处核验。

### 15.2 已核实的 GitHub 事实（2026-08-25 查证）

| 项 | 事实 |
|---|---|
| 最新 release | v1.1.9（2026-08-24 由用户发布，Release 工作流 success） |
| 远程 tags | 最高 v1.1.9，**无 v1.1.10**（本地未推送任何新 tag） |
| main 分支 | 本地与 origin/main 一致（bafbff8），新功能改动仅存在于本地工作区 |
| Actions | v1.1.9 Release/CI 均 success；新功能改动未推送，**未在 GitHub 编译过** |

### 15.3 纠正后的规范（涉及版本/发布/CI 时必须遵守）

1. **先查证 GitHub，再下结论**：任何「当前/最新版本号」「某版本是否已发布」「是否已在 GitHub 编译」的断言，必须先执行：
   - `git ls-remote --tags origin`（远程 tags）
   - GitHub API `releases/latest`（最新 release）
   - GitHub API `actions/runs`（编译记录）
2. **本地 ≠ 远程**：本地工作区 csproj 版本、本地 tag、本地 CI 状态均不等于 GitHub 上的事实；未提交/未推送的改动不得声称「已在 GitHub 编译」。
3. **不预设版本号**：未发布的版本号不得写进交付总结或文档作为已发生事实；方案中若要提及目标版本号，必须注明「待 bump / 需用户确认」。
4. **发布流程**：版本号 bump（csproj AssemblyVersion/FileVersion + iss 默认值）→ 提交推送触发 CI → 打 tag 触发 release.yml；每一步在 GitHub Actions 上确认 success 后再向用户汇报。

### 15.4 测试渠道发版 SOP（test-release.yml，v1.1.26 引入）

1. **打测试标签**：`git tag v1.1.26-test1 && git push origin v1.1.26-test1`——tag 的基础版必须是**尚未发布的下一个版本号**（不能复用已发布的 Z），序号每轮递增。
2. **盯 Actions**：「Test Release」success；同时确认「Release」被守卫跳过（skipped）。
3. **验证预发布**：`gh api repos/pkoiuu/mcbh/releases --jq '.[0] | {tag_name, prerelease, assets:[.assets[].name]}'` 应返回该 test tag、prerelease=true、双资产齐全；`releases/latest` 应仍指向最新**正式**版（隐身生效）。
4. **本机测试**：从 Releases 页下载 `BaiheOnlineSetup_v*-test*.exe` 运行（会拉同族测试完整包）或直接下完整离线包装上验证修复点。
5. **迭代或转正**：再修一轮 → 打 `test2` 重跑；确认无误后走正常正式发版流程（bump 三处版本号 → 正式 tag）。⚠️ 转正后测试机可能收不到正式版的更新横幅（FileVersion 相对关系，如 1.1.26.3 > 1.1.26.0），手动重装一次正式包即可恢复同步。
6. **边界**：测试版与正式版同 AppId 同安装目录，安装即覆盖；`.minecraft` 数据按 iss 既有的升级保留规则处理不受影响。
7. **增量更新与测试渠道（v1.1.26+）**：test-release.yml 每次都产出 BaiheManifest（补丁资产视条件生成，§7.9）；测试构建注入 AppVersionOverride/ChannelOverride=test → 测试机横幅自动查预发布同族版本并优先走增量；正式玩家链路自「首个带 manifest 的正式版的下一版」起生效。注意：当前最新正式版 v1.1.25 无 manifest 资产 → 从它出发的第一跳（无论到 test1 还是 v1.1.26）必为全量。
