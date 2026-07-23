# P0 Spec: 启动器主题切换（深色/浅色）

## 1. 概述

为白鹤启动器添加深色/浅色主题切换功能。用户可在设置 > 外观中切换主题，选择会持久化保存，重启后保持一致。

### 当前状态

- `app.css` 已定义完整的 `:root`（亮色）和 `.dark`（暗色）两套语义 CSS 变量
- `app.html` 中 `<html class="dark">` 硬编码为暗色
- `TitleBar.xaml` 背景色 `#CC1A1A1A` 和文字颜色 `White` 硬编码
- `MainWindow.xaml.cs` WebView2 背景色 `0x1A1A1C` 硬编码
- Settings 外观页有占位 UI（静态文本"暗色（默认）"）
- 所有 Svelte 组件均使用 CSS 变量，无硬编码颜色

### 目标

- 用户可一键切换深色/浅色主题
- 主题选择持久化到 localStorage
- 启动时无白屏闪烁（在 HTML 渲染前应用主题）
- WPF 标题栏背景色和 WebView2 背景色随主题同步变化

## 2. 架构设计

### 2.1 分层架构

```
┌─────────────────────────────────────────────────┐
│  前端 (Svelte)                                    │
│  ├─ theme.svelte.ts    主题状态 store (runes)     │
│  ├─ app.html           启动时内联脚本读取 localStorage │
│  ├─ Settings.svelte    外观页切换开关              │
│  └─ app.css            已有完整 CSS 变量定义       │
├─────────────────────────────────────────────────┤
│  IPC 通信                                         │
│  ├─ theme.get          获取当前主题                │
│  └─ theme.set          通知后端主题变更             │
├─────────────────────────────────────────────────┤
│  后端 (C# WPF)                                    │
│  ├─ MainWindow.xaml.cs 处理 theme.set IPC         │
│  │   ├─ 更新 WebView2 DefaultBackgroundColor       │
│  │   └─ 更新 TitleBar 背景色和文字颜色              │
│  └─ TitleBar.xaml      背景色改为动态绑定           │
└─────────────────────────────────────────────────┘
```

### 2.2 主题 Store 设计

新建 `src/Baihe.UI/src/lib/theme.svelte.ts`:

```typescript
type Theme = 'dark' | 'light'

// 响应式状态
let currentTheme = $state<Theme>('dark')

// 从 localStorage 读取初始值
function init(): void {
  const saved = localStorage.getItem('baihe_theme') as Theme | null
  if (saved === 'light' || saved === 'dark') {
    currentTheme = saved
  }
  applyTheme(currentTheme)
}

// 应用主题到 DOM
function applyTheme(theme: Theme): void {
  const html = document.documentElement
  if (theme === 'dark') {
    html.classList.add('dark')
  } else {
    html.classList.remove('dark')
  }
}

// 切换主题
function toggle(): void {
  currentTheme = currentTheme === 'dark' ? 'light' : 'dark'
  localStorage.setItem('baihe_theme', currentTheme)
  applyTheme(currentTheme)
  // 通知后端同步 WebView2 背景色和标题栏
  ipc('theme.set', { theme: currentTheme })
}

export const theme = {
  get current() { return currentTheme },
  init,
  toggle,
}
```

### 2.3 防闪烁启动脚本

在 `app.html` 的 `<head>` 中注入内联脚本，在 Svelte 渲染前读取 localStorage 并设置 `.dark` 类：

```html
<script>
  (function() {
    var t = localStorage.getItem('baihe_theme');
    if (t !== 'light') {
      document.documentElement.classList.add('dark');
    }
  })();
</script>
```

同时将 `<html class="dark">` 改为 `<html>`（默认无 dark 类 = 亮色），由内联脚本动态添加。

### 2.4 IPC 通信

#### theme.set

前端 → 后端，通知主题变更。

**请求参数:**
```json
{ "theme": "dark" | "light" }
```

**后端处理 (`MainWindow.xaml.cs`):**
```csharp
_ipcRouter.Register("theme.set", async (args) =>
{
    var theme = args.GetProperty("theme").GetString();
    if (theme == "light")
    {
        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xF7, 0xF7, 0xFA);
        TitleBar.SetTheme(false);
    }
    else
    {
        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1C);
        TitleBar.SetTheme(true);
    }
    return new { success = true };
});
```

#### theme.get

后端 → 前端，获取当前主题（从 localStorage 同步，或后端维护状态）。

实际实现：前端 theme store 自行管理 localStorage，`theme.get` 可省略。后端在启动时通过 `WebView.NavigationCompleted` 注入脚本读取前端当前主题，同步 WebView2 背景色。

### 2.5 TitleBar 动态主题

`TitleBar.xaml.cs` 新增 `SetTheme(bool isDark)` 方法：

```csharp
public void SetTheme(bool isDark)
{
    if (isDark)
    {
        this.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1C));
        TitleText.Foreground = new SolidColorBrush(Colors.White);
    }
    else
    {
        this.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xF7, 0xF7, 0xFA));
        TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
    }
}
```

需要在 `TitleBar.xaml` 中给 `TextBlock` 命名 `x:Name="TitleText"`。

### 2.6 后端启动时主题同步

在 `MainWindow.xaml.cs` 的 `NavigationCompleted` 事件中，注入脚本读取前端主题并同步后端：

```csharp
coreWebView.NavigationCompleted += async (_, e) =>
{
    // 读取前端当前主题
    var themeResult = await coreWebView.ExecuteScriptAsync(
        "localStorage.getItem('baihe_theme') || 'dark'");
    var isDark = themeResult?.Trim('"') != "light";
    WebView.DefaultBackgroundColor = isDark
        ? System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1C)
        : System.Drawing.Color.FromArgb(0xF7, 0xF7, 0xFA);
    TitleBar.SetTheme(isDark);
};
```

## 3. UI 设计

### 3.1 设置 > 外观页

将现有的静态文本替换为 macOS 风格 toggle 开关（与同页其他开关一致）：

```
┌─────────────────────────────────────────────┐
│ 外观                                         │
│ 自定义启动器外观                              │
│─────────────────────────────────────────────│
│ 主题模式                          [●━━] 暗色  │
│                                   [━━○] 亮色  │
│─────────────────────────────────────────────│
│ 主题色                            ● (蓝色)    │
└─────────────────────────────────────────────┘
```

- Toggle 开关样式与"启动后自动全屏"等开关完全一致
- Toggle 右侧显示当前主题文字（"暗色"或"亮色"）
- 切换即时生效，无需保存按钮

### 3.2 动画过渡

主题切换时添加 0.2s 颜色过渡动画，避免突兀：

```css
html, body, * {
  transition: background-color 0.2s, border-color 0.2s, color 0.2s;
}
```

注意：此过渡只应在主题切换时触发，不应影响日常 hover 效果。可通过临时添加 `.theme-transitioning` 类实现。

## 4. 约束与边界

### 4.1 不修改的内容
- CSS 变量定义（`app.css` 中的 `:root` 和 `.dark` 已完整定义）
- 现有组件中的 CSS 变量引用（无需逐个修改）
- Primitive Palette（基础色阶不随主题变化）

### 4.2 边界情况
- **首次安装**: 默认暗色主题（localStorage 无值时回退到 dark）
- **localStorage 被清除**: 回退到暗色默认值
- **IPC 通信失败**: 前端主题仍可切换，后端背景色在下次启动时同步
- **WebView2 背景色**: 必须在页面加载前设置，避免白屏闪烁

### 4.3 性能要求
- 主题切换响应时间 < 50ms（纯 CSS 变量切换，无重渲染）
- 启动时内联脚本执行时间 < 1ms
- 不引入任何外部依赖

## 5. 涉及文件

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `src/Baihe.UI/src/lib/theme.svelte.ts` | 新建 | 主题状态 store |
| `src/Baihe.UI/src/app.html` | 修改 | 移除硬编码 dark 类，添加防闪烁内联脚本 |
| `src/Baihe.UI/src/pages/Settings.svelte` | 修改 | 外观页添加主题切换开关 |
| `src/Baihe.UI/src/App.svelte` | 修改 | 启动时调用 theme.init() |
| `src/Baihe.UI/src/app.css` | 修改 | 添加主题过渡动画类 |
| `src/Baihe.Host/Chrome/TitleBar.xaml` | 修改 | TextBlock 添加 x:Name |
| `src/Baihe.Host/Chrome/TitleBar.xaml.cs` | 修改 | 添加 SetTheme 方法 |
| `src/Baihe.Host/MainWindow.xaml.cs` | 修改 | 注册 theme.set IPC，启动时同步主题 |

## 6. 验收标准

- [ ] 设置 > 外观中可切换深色/浅色主题
- [ ] 切换即时生效，所有页面颜色正确变化
- [ ] 主题选择持久化，重启后保持
- [ ] 启动时无白屏闪烁
- [ ] WebView2 背景色与主题一致
- [ ] WPF 标题栏背景色和文字颜色与主题一致
- [ ] 浅色主题下所有文字可读，对比度达标
- [ ] 浅色主题下滚动条样式正确
- [ ] 切换主题有 0.2s 过渡动画，无突兀感
- [ ] 无 TypeScript 编译错误
- [ ] 无 Svelte 编译警告（a11y）

---

> **触发下一阶段**: 本 spec 获批后，自动进入 tasks.md 任务拆解和 checklist.md 验证清单编写。
