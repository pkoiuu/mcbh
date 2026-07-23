# P0 Tasks: 启动器主题切换（深色/浅色）

## 任务概览

共 8 个任务，分为 3 个并行层 + 1 个集成层。

## T1: 前端 — 主题 Store [前端层]

**文件**: `src/Baihe.UI/src/lib/theme.svelte.ts` (新建)

**内容**:
- 定义 `Theme` 类型 (`'dark' | 'light'`)
- 创建 `$state` 响应式变量 `currentTheme`，默认 `'dark'`
- `init()`: 从 localStorage 读取 `baihe_theme`，同步到 currentTheme 和 DOM
- `applyTheme(theme)`: 切换 `<html>` 元素的 `.dark` 类
- `toggle()`: 切换主题，写入 localStorage，调用 `applyTheme`，通过 IPC 通知后端
- 导出 `theme` 对象（getter `current` + `init` + `toggle`）

**依赖**: 无
**验证**: TypeScript 编译无错误，store 逻辑正确

## T2: 前端 — 防闪烁启动脚本 [前端层]

**文件**: `src/Baihe.UI/src/app.html` (修改)

**内容**:
- 将 `<html lang="zh-CN" class="dark">` 改为 `<html lang="zh-CN">`
- 在 `<head>` 中添加内联 `<script>`，在 Svelte 渲染前读取 localStorage
- 逻辑: `localStorage.getItem('baihe_theme')` 不等于 `'light'` 时添加 `.dark` 类
- 默认（无 localStorage 值时）为暗色

**依赖**: 无
**验证**: 首次加载（无 localStorage）时为暗色，设置浅色后刷新仍为浅色

## T3: 前端 — CSS 过渡动画 [前端层]

**文件**: `src/Baihe.UI/src/app.css` (修改)

**内容**:
- 添加 `.theme-transitioning *` 选择器，包含 `transition: background-color 0.2s, border-color 0.2s, color 0.2s`
- 此类在主题切换时由 JS 临时添加到 `<html>`，切换完成后移除
- 不影响日常 hover/active 等微交互

**依赖**: 无
**验证**: 主题切换时有平滑过渡，日常操作不受影响

## T4: 前端 — App.svelte 初始化 [前端层]

**文件**: `src/Baihe.UI/src/App.svelte` (修改)

**内容**:
- 导入 `theme` from `./lib/theme.svelte`
- 在组件挂载时调用 `theme.init()`（使用 `$effect` 或顶层调用）

**依赖**: T1
**验证**: 应用启动时 theme store 正确初始化

## T5: 前端 — Settings 外观页 UI [前端层]

**文件**: `src/Baihe.UI/src/pages/Settings.svelte` (修改)

**内容**:
- 导入 `theme` from `../lib/theme.svelte`
- 将外观页"主题模式"行从静态文本改为 toggle 开关
- Toggle 样式与"启动后自动全屏"开关完全一致（h-7 w-12, rounded-full, transition-colors）
- Toggle 右侧显示当前主题文字（`theme.current === 'dark' ? '暗色' : '亮色'`）
- 点击 toggle 调用 `theme.toggle()`
- 添加 `aria-label="切换主题模式"` 和 `role="switch"` + `aria-checked`

**依赖**: T1, T4
**验证**: 外观页显示主题开关，切换即时生效

## T6: 后端 — TitleBar 动态主题 [后端层]

**文件**:
- `src/Baihe.Host/Chrome/TitleBar.xaml` (修改)
- `src/Baihe.Host/Chrome/TitleBar.xaml.cs` (修改)

**内容**:
- TitleBar.xaml: 给 TextBlock 添加 `x:Name="TitleText"`
- TitleBar.xaml.cs: 添加 `SetTheme(bool isDark)` 方法
  - isDark=true: Background = `#CC1A1A1C`, Foreground = White
  - isDark=false: Background = `#CCF7F7FA`, Foreground = `#1D1D1F`
- 移除 TitleBar.xaml 中硬编码的 `Background="#CC1A1A1A"` 和 `Foreground="White"`（改为代码设置默认值）

**依赖**: 无（可与 T1-T5 并行）
**验证**: 调用 SetTheme(true/false) 后标题栏颜色正确变化

## T7: 后端 — IPC 注册 + 启动同步 [后端层]

**文件**: `src/Baihe.Host/MainWindow.xaml.cs` (修改)

**内容**:
- 注册 `theme.set` IPC 命令:
  - 解析 `args.GetProperty("theme").GetString()`
  - light: WebView.DefaultBackgroundColor = `0xF7F7FA`, TitleBar.SetTheme(false)
  - dark: WebView.DefaultBackgroundColor = `0x1A1A1C`, TitleBar.SetTheme(true)
- 在 `NavigationCompleted` 事件处理中添加主题同步:
  - 执行 `localStorage.getItem('baihe_theme') || 'dark'` 读取前端主题
  - 根据结果设置 WebView2 背景色和 TitleBar 主题
- 获取 TitleBar 引用（确保可通过 `FindName` 或字段访问）

**依赖**: T6
**验证**: IPC 调用成功，WebView2 背景色和标题栏同步变化

## T8: 集成验证 [集成层]

**内容**:
- 前端构建 (`pnpm build`) 无错误
- 检查所有修改文件的一致性
- 验证 IPC 命令注册格式正确
- 检查 a11y 合规性（aria 属性、role）

**依赖**: T1-T7 全部完成
**验证**: 编译通过，无警告

## 并行执行策略

```
T1 ─┬─ T4 ─ T5 (前端 UI)
T2 ─┤
T3 ─┘
T6 ─── T7 (后端)
        └── T8 (集成)
```

T1, T2, T3, T6 可同时开始（4 个并行）。T4 依赖 T1，T5 依赖 T1+T4，T7 依赖 T6，T8 依赖全部。

---

> **触发下一阶段**: 所有任务完成后，对照 checklist.md 逐项验证，然后整体编译验证。全部通过后自动进入 P1 spec 编写。
