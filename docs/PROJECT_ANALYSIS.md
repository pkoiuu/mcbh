# 白鹤服务器启动器（Baihe / mcbh）— 项目分析文档

> 用途：为后续代码修改提供一份「以当前源码为准」的架构地图与操作手册。
> 本文档基于源码实际内容逐文件核验整理（2026-08-18 深度核验版），覆盖前后端结构、IPC 契约、核心流程、持久化文件、构建方式与「如何新增功能」的步骤。
> 核验基准：工作区当前状态（含未提交的 Baihe.Core 移除重构），AssemblyVersion 1.1.1.0（git tag v1.1.1），前端 v0.0.1。

---

## 0. 当前工作区状态（改代码前必读）

**仓库已完成「移除 Baihe.Core」的架构瘦身重构**（随 v1.1.2 发布提交）：`src/Baihe.Core/`（约 300+ 文件）与 `src/Baihe.Core.SourceGenerators/` 已从解决方案移除，`Baihe.slnx` 只剩 `src/Baihe.Host` 一个 .NET 项目，`App.xaml.cs` 回到纯 `Application`。

**含义与影响**：

1. 当前解决方案（`Baihe.slnx`）只含 `src/Baihe.Host` 一个 .NET 项目；`Baihe.Core` 是从 PCL2-CE fork 出来的核心库（含 Config/EventBus/IoC/Download/IdentityModel 等），本次重构把它整个移除，业务逻辑此前已逐步下沉/重写到 Host 的 Services/ 里。
2. **不要再创建或引用 `src/Baihe.Core`**。所有新代码一律放 `src/Baihe.Host`（后端）或 `src/Baihe.UI`（前端）。
3. 老文档（根目录 4 份分析文档）大量描述 Baihe.Core 的架构，已过时；以本文档与 `src/Baihe.Host` 源码为准。
4. 重构提交后，`PCL2-CE/` 目录仍保留仅作参考（启动/认证逻辑的注释多处「参照 PCL CE」），不参与构建。

---

## 1. 项目定位

一个专为「白鹤服务器」定制的 Minecraft 启动器，核心能力：

- 启动 Minecraft（原版 + Fabric），QuickPlay 直连白鹤服务器
- 下载/安装 Minecraft 版本与 Fabric Loader
- 三种登录方式：离线 / 微软正版（设备码）/ 第三方验证（Yggdrasil / LittleSkin）
- Mod 管理、存档备份/导入/恢复、截图浏览、游戏修复
- 内置聊天（WebView2 导航到外部 Element 聊天页，注入返回按钮与消息监控）
- 系统托盘、主题切换（深/浅）、更新检查、遥测上报、微信名收集

**技术栈**：

| 层 | 技术 |
|---|---|
| 后端宿主 | C# .NET 10 WPF + WebView2（Microsoft.Web.WebView2 1.0.4078.44，WinForms 托盘） |
| 前端 | Vite 6 + Svelte 5（runes）+ Tailwind CSS 4 + lucide-svelte（实际图标走内联 SVG） |
| 打包 | Inno Setup 6（installer/baihe_installer.iss）+ jlink 最小化 JRE 21 |
| CI/CD | GitHub Actions（ci.yml 编译验证、release.yml 打 tag 发版） |

---

## 2. 目录结构（关键路径）

```text
Baihe.slnx                        # 解决方案，仅引用 src/Baihe.Host（Core 已移除）
src/
├── Baihe.Host/                   # WPF 宿主进程（唯一 .NET 项目）
│   ├── App.xaml(.cs)             # 应用入口（StartupUri 启动 MainWindow）
│   ├── MainWindow.xaml(.cs)      # 主窗口：WebView2 初始化 + 全部 IPC 命令注册（partial 拆分: MainWindow.Chat.cs）
│   ├── Chrome/TitleBar.xaml(.cs) # 原生标题栏 + 交通灯按钮
│   ├── Ipc/                      # IpcMessage.cs + IpcRouter.cs（IPC 协议与路由）
│   ├── Web/WebViewHost.cs        # WebView2 环境创建 + 虚拟主机映射
│   ├── Models/                   # McAccount / OfflineAccount / GameInstance
│   └── Services/                 # 19 个业务服务（见 §6）
└── Baihe.UI/                     # Svelte 5 前端（构建输出到 ../Baihe.Host/wwwroot）
    ├── vite.config.ts            # outDir: ../Baihe.Host/wwwroot，WebView2 兼容插件
    └── src/
        ├── main.ts / App.svelte  # 入口 + 根组件（路由切换 + 微信名弹窗 + Toast）
        ├── app.css               # 设计令牌系统（Tailwind 4 @theme + CSS 变量双层）
        ├── components/           # WindowShell / Sidebar / WeChatDialog
        ├── lib/                  # ipc.ts / router / theme / toast / Icon.svelte + icons/（14 个 svg）
        └── pages/                # Home / Download / Settings / Tools / Login
PCL2-CE/                          # 上游 Plain Craft Launcher 2 CE 的 fork（仅参考，不参与构建）
installer/                        # Inno Setup 安装脚本
installer_resources/              # 开发期资源：.minecraft、jre、icon.ico、ChineseSimplified.isl
installer_assets/                 # 安装向导图片（wizimage.bmp 等）
scripts/                          # download-build / fork-rename / upload-minecraft-assets
specs/                            # P0-theme-switching、P1-memory-recommendation（各含 spec/tasks/checklist）
docs/                             # telemetry-api-guidelines.md、PROJECT_ANALYSIS.md
baihe-launcher-analysis/          # 一次性的 HTML 分析快照（可忽略/删除）
```

> ⚠️ README.md 多处与代码不一致：`scripts/build-all.ps1` 不存在（实际是 download-build/fork-rename/upload-minecraft-assets 三个脚本）；「复制前端到 Host assets」是错的（Vite 直接输出到 wwwroot，且 Host 项目没有 assets/ 目录约定，只有 Assets/icon.ico）。

---

## 3. 架构总览

```text
┌─────────────────────── Baihe.Host (WPF 进程) ───────────────────────┐
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
│         │   │  Services/（18 静态服务 + Tray 实例）│                │
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
└──────────────────────────────────────────────────────────────────────┘
```

**关键架构决策**：

1. **WPF + WebView2 + Svelte 混合**：窗口框架/标题栏/托盘/文件系统/进程用原生 C#，UI 全用 Web 技术渲染。前后端完全通过 JSON IPC 解耦，前端不直接碰文件系统。
2. **前端构建产物直接进 Host**：Vite outDir 指向 `../Baihe.Host/wwwroot`，WebView2 通过 `SetVirtualHostNameToFolderMapping` 以 `https://baihe.app/` 加载；入口 URL 带 `?v={unix秒}` 时间戳做缓存清除。
3. **IPC 为纯 JSON 命令总线**：请求-响应（id 配对 + 15s 超时）+ 主动推送（PushEvent）+ 两条特殊原始消息（见 §7）。
4. **静态服务 + 单账户**：所有业务服务是静态类（`TrayService` 除外，MainWindow 持有实例）；账户是全局单账户模式。
5. **PCL2-CE 不参与构建**：Baihe.Host.csproj 无任何 PCL 引用，仅作参考源码。

---

## 4. 前端架构（Baihe.UI）

### 4.1 状态与路由

- **路由**：`lib/router.svelte.ts` 用 `$state` 维护 `current: PageKey`（home | download | settings | tools | login），无路由库，页面在 `App.svelte` 里 `{#if}` 条件渲染。聊天页不走路由，通过 `nav.external` IPC 让后端 `Navigate(url)`。
- **Svelte 5 runes 约定**：在 .ts 里使用 runes 必须用 `.svelte.ts` 扩展名（router/theme/toast 都是如此）。
- **单例 store**：router、theme、toast 均为模块级单例（class + `$state`）。
- **入口**：`main.ts` 挂载前注册 `window.onerror` / `unhandledrejection` 全局错误捕获，出错时把加载屏替换为错误信息（避免白屏）。

### 4.2 组件与页面 → IPC 使用矩阵（核验自源码）

| 文件 | 职责 | 用到的 IPC 命令 | 订阅的推送事件 |
|---|---|---|---|
| App.svelte | 根组件：WindowShell + Sidebar + 页面路由 + 微信名弹窗 + Toast 容器 | `wechat.get` | — |
| WindowShell.svelte | 纯内容容器（标题栏已迁到 WPF 原生） | — | — |
| Sidebar.svelte | 240px 毛玻璃侧边栏：用户区 + 导航 + 版本号；监听 router 变化重载账户 | `auth.current` | — |
| WeChatDialog.svelte | 首次启动微信名收集弹窗（IPC 失败也弹） | `wechat.set` | — |
| Icon.svelte | 图标组件（见 4.4） | — | — |
| Home.svelte | 启动主页：当前实例卡片 + 启动按钮 + 快捷工具 + 新闻 + 服务器状态 + 更新横幅 | `instance.current`、`auth.hasAccount`、`update.check`、`server.status`、`launch.start`、`open.url` | `launch.state`、`launch.started`、`launch.exited` |
| Download.svelte | 版本下载 / Fabric 安装 | `version.list`、`instance.list`、`download.start`、`fabric.install` | `download.progress/complete/error`、`fabric.progress/complete/error` |
| Settings.svelte | 账户/游戏/外观/关于/开发者 五分类设置 | `auth.current`、`app.getVersion`、`system.memory`、`update.check`、`java.bundled`、`java.detect`、`settings.get`、`settings.set`、`auth.offline`（改名）、`open.url` | — |
| Login.svelte | 三种登录方式 Tab（离线/微软/第三方） | `auth.setOffline`、`auth.msLogin`、`auth.msCancel`、`auth.thirdPartyLogin` | `auth.msDeviceCode`、`auth.msLoginResult` |
| Tools.svelte | Mod/存档/截图/修复 + 聊天入口 | `mods.list`、`saves.list`、`saves.backups`、`screenshots.list`、`mods.toggle`、`mods.delete`、`mods.openFolder`、`saves.backup`、`saves.restore`、`saves.deleteBackup`、`tools.openFolder`、`tools.repair`、`nav.external`（聊天） | — |

> 注意：Home 不调用 `instance.list`，直接 `instance.current`（后端返回当前选中实例）；Settings 改名走 `auth.offline`；聊天入口只在 Tools 页（`nav.external → https://chat.hhj520.top`）。

### 4.3 设计令牌系统（改 UI 必读）

`app.css` 是「设计令牌单一入口」，三层结构（已核验）：

1. **Primitive Palette**（`:root`）：`--brand-50..900`（Apple System Blue #007AFF）、`--background-*`、`--text-*`、`--icon-*`、`--traffic-*`（macOS 红绿灯）、`--state-success/error`。
2. **Semantic Roles**：`:root`（亮色）与 `.dark`（暗色）各定义一套 `--background`、`--foreground`、`--card`、`--primary`、`--muted`、`--destructive`、`--border`、`--ring`、`--sidebar-*`、`--success` 等。**默认暗色**。
3. **Tailwind 4 `@theme inline`**：把语义变量映射成 Tailwind 工具类（`--color-card: var(--card)`），所以页面里可用 `bg-[var(--card)]` 或 `bg-card` 风格。

> 改配色/主题 → 只改 app.css 令牌；新增图标 → 放 `lib/icons/*.svg` 并在 Icon.svelte 的 aliasMap 加别名。

### 4.4 Icon 图标系统

- `Icon.svelte` 用 `import.meta.glob('./icons/*.svg', { query: '?raw', eager: true })` 加载全部图标（当前 14 个，命名 `image_N[_hash].svg`），正则 `image_(\d+)` 提取 key。
- `aliasMap` 把语义名映射到 `image_N`（user/circle-play/arrow-down/grip/box/plus/upload/package/search/check-circle/palette/info/download/message-circle 等）。
- **已知占位**：`circle-x` 映射到 `image_11`（info 图标），注释说明图标集暂无 close/x 图标——以后补图标时要改这里。
- 新增图标 = 放 svg 文件 + 加 aliasMap 条目，无需其他改动。

### 4.5 主题同步机制（前后端联动）

- 前端 `theme.toggle()`：切 `currentTheme` → 加 `.theme-transitioning` 过渡类（200ms 移除）→ 写 `localStorage('baihe_theme')` → 切 `<html>` 的 `.dark` 类 → `ipc('theme.set', {theme})` 通知后端。
- 后端 `theme.set` 调 `ApplyThemeToWindow`：同步 WebView2 `DefaultBackgroundColor` + 窗口 `Background` + `TitleBarBorder` 背景/边框/文字色（暗色 #1A1A1C / 亮色 #F7F7FA）。
- WebView2 `NavigationCompleted` 时读 `localStorage.getItem('baihe_theme')` 反向同步一次，保证冷启动不闪色（默认 dark）。

---

## 5. 后端架构（Baihe.Host）

### 5.1 服务清单（Services/，全部核验）

| 服务 | 行数(约) | 职责 | 关键点 |
|---|---|---|---|
| MainWindow.xaml.cs | 719 + Chat partial | 窗口 + WebView2 + **所有 IPC 命令注册** | 新增命令改这里（RegisterHostCommands）；聊天注入在 MainWindow.Chat.cs |
| LaunchService | 1113 | 启动管线（最大文件） | 版本 JSON 合并 / natives 提取 / classpath / JVM+Game 参数 / 进程监控 |
| NbtHelper | — | Minecraft NBT 格式通用读写 | 大端序（BinaryPrimitives.*BigEndian）；servers.dat 未压缩 |
| ServerListService | — | 自动把白鹤服务器加入 servers.dat | 启动游戏前调用；同 ip 条目自动改名「白鹤服务器」，幂等 |
| DownloadService | 490 | 下载管线 | SHA1 校验 + 6 并发 + 进度推送；`.tmp` 临时文件校验后改名 |
| MicrosoftAuthService | 706 | 微软设备码登录 + 令牌刷新 | 6 步流程，Xerr 错误码映射 |
| ThirdPartyAuthService | 384 | Yggdrasil/Authlib-Injector | ALI 指示解析（≤5 次重定向）、LittleSkin 预设 |
| AuthService | 82 | 统一账户管理（缓存 + 持久化） | 单账户模式，account.json |
| SettingsService | 203 | 用户设置 + 内存检测/推荐 | P/Invoke GlobalMemoryStatusEx；推荐算法见 §8.5 |
| VersionService | 130 | Mojang 版本清单（24h 缓存） | 自建 HttpClient（注释里的 NetworkService 已随 Core 删除，注释过时） |
| InstanceService | 175 | 扫描实例 + 当前实例选择 | GetMcDirectory() 4 级路径回溯；Fabric 实例优先 |
| JavaHostService | 125 | 检测捆绑 JRE / 系统 Java | `java -version` 输出在 **stderr** |
| FabricService | 137 | Fabric Loader 安装 | 版本 ID = `{gameVersion}-fabric`；走 DownloadVersionFromJson |
| ServerStatusService | 58 | 服务器在线检测（TCP ping） | 3s 超时，地址从 Settings 动态读 |
| ModService | 171 | Mod 列表/启停/删除 | 启停 = 改 `.jar` ↔ `.jar.disabled` 后缀；版本专属 mods 目录优先 |
| SaveService | 284 | 存档备份/导入/恢复 | zip + 临时目录；导入按 level.dat 识别 |
| ToolService | 145 | 截图列表 / 打开文件夹 / 游戏修复 | 修复 = 完整性检查（报告型） |
| TrayService | 154 | 系统托盘（WinForms NotifyIcon） | **唯一实例类**；三路图标加载兜底 |
| UpdateService | 153 | GitHub Releases 更新检查 | 镜像列表自动更新（mirrors.json）+ 运行时测速选最快 |
| TelemetryService | 182 | 遥测上报（每会话首次 + 服务端策略） | 见 §8.6 |
| WeChatService | 71 | 微信名持久化（wechat.json） | 独立于账户 |
| FormatHelper | 15 | 字节大小格式化 | 被多处复用 |

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
| app.getVersion | — | 版本字符串 | 优先 FileVersion（Release 从 tag 注入），`ToString(3)` 去尾 .0 |
| update.check | — | UpdateInfo | GitHub Releases，10s 超时，失败静默返回无更新 |
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
| launch.start | {instanceId?} | {success,processId,error} | 含账户检查 + 微软令牌刷新 + 遥测上报 |
| launch.status | — | {state,message,processId} | 启动状态 |
| download.start | versionId | {success,message} | 异步（Task.Run，不阻塞响应） |
| download.status | — | {isDownloading,error} | 下载状态 |
| fabric.install | gameVersion | {success,message} | 异步安装 |
| fabric.loaders | gameVersion | {gameVersion,loaders[]} | 查询 Loader |
| settings.get | — | LauncherSettings | 读取设置 |
| settings.set | 部分字段对象 | LauncherSettings | 逐字段更新（有上下限钳制） |
| server.status | — | {online,latency,address,port} | 服务器状态 |
| mods.list | — | ModInfo[] | Mod 列表（含禁用） |
| mods.toggle | fileName | {success,enabled} | 启停 Mod |
| mods.delete | fileName | {success} | 删除 Mod |
| mods.openFolder | — | {success,path} | 打开 mods 目录 |
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
| launch.started | {processId} | 游戏进程已启动 |
| launch.exited | {exitCode,abnormal,error} | 游戏退出（stderr 内容） |
| download.progress | {phase,currentFile,completedFiles,totalFiles,downloadedBytes,totalBytes,percent} | 下载进度 |
| download.complete | {success} | 下载完成 |
| download.error | {error} | 下载失败 |
| fabric.progress | {phase,message,loaderVersion?,intermediaryVersion?} | Fabric 安装进度 |
| fabric.complete | {success,versionId} | Fabric 完成 |
| fabric.error | {error} | Fabric 失败 |
| auth.msDeviceCode | {userCode,verificationUri} | 微软设备码（Login.svelte 展示） |
| auth.msLoginResult | {success,username,error} | 微软登录结果 |

---

## 7. 核心业务流程

### 7.1 启动流程（LaunchService.Launch，1113 行）

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
  → EnsureOptionsTxt（onboardAccessibility:false 跳过无障碍引导、joinedFirstServer:true、tutorialStep:none、lang:zh_cn）
  → 写 launch_cmd.log（完整命令行）
  → Process.Start（javaw，UseShellExecute=false，WorkingDirectory=.minecraft，**APPDATA=.minecraft**）
  → PushEvent launch.started{processId}
  → 异步改窗口标题为「白鹤服务器」（EnumWindows 找可见窗口 + SetWindowText，最长等 30s）
  → 异步监控退出（stderr 写 launch_error.log；PushEvent launch.exited）
  → CloseAfterLaunch=true 时延迟 2s Environment.Exit(0)
```

**启动参数要点**（改启动行为必读）：
- 用户类型固定 `--userType msa`（注释：offline 会触发 "Unrecognized user type" 警告）。
- QuickPlay：`majorVersion >= 21` 用 `--quickPlayMultiplayer host:port`，旧版用 `--server/--port`（主版本号从版本 ID 正则 `1\.(\d+)` 提取）。
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

### 7.4 更新流程（UpdateService）

`GET https://api.github.com/repos/pkoiuu/mcbh/releases/latest`（15s 超时，带 UA）→ 解析 tag_name（去 v）、找 .exe asset 的 browser_download_url → **镜像列表自动更新**（并行拉取仓库 `mirrors.json`，失败用内置兜底列表）→ 对真实下载 URL **并行测速**（普通 GET 读前 512KB 计时，不用 Range——部分镜像不支持）选**最快**镜像 → 全部失败回退 GitHub 直链 → `Version` 比较 → 失败静默返回无更新。返回含 `DownloadSource`（加速源主机名）与 `DownloadSpeedMBps`（测速结果，前端展示）。注意：**这里只用 `Assembly.GetName().Version`，而 app.getVersion 用 FileVersion——两处取版本的方式不同**（Release 下 FileVersion 由 tag 注入，AssemblyVersion 同样由 tag 注入，一般一致）。

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
| cache/version_manifest.json | Mojang 清单（24h） | VersionService |
| current_instance.txt | 选中实例 ID | InstanceService |
| launch_cmd.log / launch_error.log | 启动命令/错误诊断 | LaunchService |
| servers.dat（.minecraft/） | Minecraft 多人游戏服务器列表（NBT） | ServerListService 启动时确保「白鹤服务器」在列 |
| natives_debug.log / debug-paths.txt | 调试日志 | LaunchService / WebViewHost |
| .minecraft/** | 游戏目录（.minecraft 由 GetMcDirectory 定位） | 各服务 |

> 所有服务都是静态类，把状态缓存在 static 字段里（`_cached`、`_currentAccount`、`_state`、`_isDownloading`、`_hasReportedThisSession`），单例便利但线程安全与测试性差（见 §10）。

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
1. **五路并行准备**：① jlink 构建 JRE（20 模块，`--compress=2 --strip-debug`）② 下载 WebView2 离线安装包（fwlink）③ `gh release download v1.0-assets --pattern minecraft.7z` 解压出 .minecraft ④ dotnet restore ⑤ pnpm install + build（前台）。
2. `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:AssemblyVersion=$version -p:FileVersion=$version`（**版本号从 tag 注入**：v1.1.1 → 1.1.1）。
3. 复制 icon.ico、fabric-installer.jar → dist/launcher。
4. `ISCC.exe /DMyAppVersion=$version installer\baihe_installer.iss`（Inno Setup 6）。
5. softprops/action-gh-release 上传 `dist/白鹤服务器启动器_Setup_*.exe`。

> .minecraft（约 1.3GB）不进 git，存于 release asset `v1.0-assets`，更新游戏文件后跑 `scripts/upload-minecraft-assets.ps1` 重新上传（7z -mx=9，排除 logs/crash-reports/downloads/servers.dat_old 等）。

### 9.4 安装器（installer/baihe_installer.iss）

- 安装目录 `%LOCALAPPDATA%\BaiheServer`，`PrivilegesRequired=lowest`（免管理员）。
- **升级检测**：固定 AppId（`{8F2B7A3C-...}`）+ UsePreviousAppDir。
- **升级/安装前杀进程**：`InitializeSetup` / `PrepareToInstall` / `InitializeUninstall` 三重检查 `tasklist` + `taskkill /T /F`（含 WebView2 子进程），用户可取消。
- **文件分层**：
  - 启动器本体 `dist\launcher\*` 排除 jre、WebView2 安装包、`settings.json`、`account.json`、`current_instance.txt`、`*.log`、`debug-*.txt`、`cache\*`、`Baihe.exe.WebView2\*`（**升级保留用户配置**）。
  - `.minecraft\versions|libraries|assets|.fabric` 始终覆盖（ignoreversion）。
  - `mods\*` 始终更新同名文件（用户自加 mod 不受影响）。
  - `options.txt`、`servers.dat`、`config\*`、`launcher_profiles.json` 用 `onlyifdoesntexist uninsneveruninstall`（首装写入、升级不覆盖、卸载保留）。
- **卸载保留**用户数据（saves/options.txt/screenshots/config 等），只删 logs/crash-reports/downloads。
- [Run] 检测 WebView2（注册表 `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`）未装则静默安装。

### 9.5 脚本（scripts/）

- `download-build.ps1`：镜像（ghfast.top > ghproxy.net > gh-proxy.com > ghproxy.link > 直连）下载 CI 的 `latest/baihe-build.zip`，解压部署到 `src/Baihe.Host/bin/CI/...` 并启动（本地体验 CI 构建）。
- `fork-rename.ps1`：把 PCL.Core fork 成 Baihe.Core 的重命名/裁剪脚本（**本次重构已废弃 Core，此脚本基本不再用**）。
- `upload-minecraft-assets.ps1`：打包 installer_resources/.minecraft 为 minecraft.7z 并上传到 `v1.0-assets` release。

---

## 10. 关键约定与踩坑（修改前必读）

1. **csproj 手动列文件**：因 .NET 10 SDK glob 展开 bug，`EnableDefaultCompileItems=false` / `EnableDefaultPageItems=false`，**新增 .cs 必须手动加 `<Compile Include>`，新增 XAML 加 `<Page Include>`**。漏加会出现「文件存在但没被编译」的诡异问题。
2. **前端产物目录命名**：用 `wwwroot` 而非 assets，避免与 `Assets/icon.ico` 在 Windows 不区分大小写下冲突。
3. **WebView2 兼容插件**：vite.config.ts 的 removeCrossOrigin 移除 crossorigin、`type="module"` 换 `defer`（虚拟主机映射对 ESM 支持不完整）。
4. **.svelte.ts 扩展名**：在 .ts 里用 Svelte 5 runes 必须用该扩展名（router/theme/toast）。
5. **Fabric 合并后丢失 parentId**：启动逻辑在 LoadAndMergeVersion 之前先读原始 JSON 拿 inheritsFrom。
6. **Java 检测**：`java -version` 输出到 stderr，不是 stdout。
7. **微软登录是异步推送模式**：`auth.msLogin` 立即返回，结果靠 `auth.msLoginResult` 推送，前端不能在请求响应里等。
8. **窗口关闭 = 最小化到托盘**：OnClosing 拦截首次关闭，真正退出走托盘「退出」菜单（Application.Shutdown）。
9. **CSP**：前端大量 `javascript:void(0)` 与内联 onclick（Svelte 事件），WebView2 未启用严格 CSP，本项目可放心用内联事件（与 fuwari-blog 的严格 CSP 完全不同）。
10. **静态服务 + 静态状态**：所有服务是静态类，IPC 多线程（IpcRouter 用 ConcurrentDictionary），改状态时注意竞态。
11. **`vite build` 不会清空 wwwroot 旧文件**：`emptyOutDir: true` 只对项目根内的 outDir 生效，而 wwwroot 在 Baihe.UI 项目根**之外**（`../Baihe.Host/wwwroot`）。实测 wwwroot 里堆积了 60+ 个历史哈希资产（index-*.js/css 多版本并存）。建议：build 前手动清空 wwwroot，或把 outDir 改为项目内目录再复制。
12. **明文密码**：`McAccount.Password` 注释声称「加密存储」，但 AccountStore 只是 JSON 序列化，第三方密码以明文写入 account.json——安全风险。
13. **第三方令牌刷新是死代码**：`ThirdPartyAuthService.Refresh()` 存在但从未被调用；`AuthService.RefreshIfExpired` 只处理 Microsoft。第三方 accessToken 过期后没有刷新路径（Yggdrasil token 通常长期有效，但规范上应支持）。
14. **注释过时**：VersionService.cs 头部注释提到「使用 NetworkService 预配置的 HttpClient」，但 NetworkService 已随 Baihe.Core 删除，实际是自建 HttpClient。
15. **app.getVersion 硬编码回退** "1.1.1"（MainWindow），改版本号要同步改 csproj 的 AssemblyVersion/FileVersion + tag + iss 默认值（/DMyAppVersion 覆盖）。
16. **instance.current 无实例时**：后端 `GetCurrentInstance()` 返回 null 时命令用 `!` 强制解引用可能抛异常（前端 Home 有「暂无游戏实例」兜底）。
17. **WeChat 弹窗**：`wechat.get` IPC 失败也会弹窗（onMount 中 catch 分支），属有意设计。

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

**发新版本**：csproj 版本号 → 更新 `.minecraft`（如需要）→ `scripts/upload-minecraft-assets.ps1` → 打 tag `vX.Y.Z` → release.yml 自动出安装包。

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

## 13. 已知问题与改进建议（按优先级）

**高优先级（正确性/安全）**：
1. 第三方密码明文存储（account.json）→ 用 Windows DPAPI（`ProtectedData`）加密。
2. 第三方令牌刷新未接入 → 在 `launch.start` 里对 ThirdParty 也调 `Refresh()`。
3. `instance.current` 的 `!` 解引用在无实例时会抛错 → 返回 null 让前端处理。
4. wwwroot 旧资产堆积（见 §10.11）→ 构建脚本清空或改 outDir 策略，减小安装包体积。

**中优先级（工程化）**：
5. 静态服务无锁竞态（Settings/Auth/Launch/Download 的状态字段）→ 上锁或单例类。
6. 更新检查与 app.getVersion 版本获取方式不一致（AssemblyVersion vs FileVersion）。
7. 硬编码散落：遥测地址/ApiKey、GitHub repo、默认服务器 play.simpfun.cn:28230、聊天站点 hhj520.top → 收敛到配置。
8. `OnWebMessageReceived` 是 `async void`，异常只进 Debug 输出 → 前端可能静默超时。
9. README 构建说明与代码不一致（build-all.ps1、复制 assets 步骤）。

**低优先级（体验）**：
10. Icon 缺 close/x 图标（circle-x 占位到 info）。
11. `saves.restore` 的旧目录改名逻辑与卸载保留策略需在 UI 上说明。
12. 版本号回退硬编码 "1.1.1" 与 csproj 不同步会显示错版本。

---

## 14. 快速定位表（改什么 → 看哪里）

| 想改什么 | 看哪里 |
|---|---|
| 加 IPC 命令 | MainWindow.xaml.cs RegisterHostCommands + Services/ + ipc.ts |
| 启动参数/QuickPlay/内存 | LaunchService.BuildJvmArgs / BuildGameArgs |
| 下载逻辑/并发/校验 | DownloadService |
| 登录（微软/第三方/离线） | MicrosoftAuthService / ThirdPartyAuthService / AuthService |
| 设置项 | SettingsService.LauncherSettings + Settings.svelte |
| 主题/配色/图标 | app.css / Icon.svelte + lib/icons/ |
| 页面布局/导航 | pages/ + Sidebar.svelte + router.svelte.ts |
| 安装包内容/升级策略 | installer/baihe_installer.iss |
| 版本号 | csproj + tag + iss /DMyAppVersion |
| 聊天页行为 | MainWindow InjectBackButtonAsync / InjectChatMonitorScriptAsync |
