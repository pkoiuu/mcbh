// 主窗口代码后置 — WebView2 初始化、资源映射和 IPC 消息转发
// 负责将前端 WebView2 与后端 IpcRouter 连接起来

using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Baihe.Host.Ipc;
using Baihe.Host.Web;

namespace Baihe.Host;

/// <summary>
/// 主窗口 — 承载 WebView2 加载前端
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// IPC 路由器 — 处理前端发来的命令
    /// </summary>
    private readonly IpcRouter _ipcRouter = new();

    /// <summary>
    /// 创建主窗口实例
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        // 注册窗口控制和应用信息命令
        RegisterHostCommands();
        // 异步初始化 WebView2，不阻塞窗口显示
        _ = InitializeWebViewAsync();
    }

    /// <summary>
    /// 注册宿主层命令 — 窗口控制、应用信息等
    /// </summary>
    private void RegisterHostCommands()
    {
        // 窗口控制命令
        _ipcRouter.Register("window.close", _ =>
        {
            Dispatcher.Invoke(Close);
            return Task.FromResult<object>(true);
        });

        _ipcRouter.Register("window.minimize", _ =>
        {
            Dispatcher.Invoke(() => WindowState = WindowState.Minimized);
            return Task.FromResult<object>(true);
        });

        _ipcRouter.Register("window.maximize", _ =>
        {
            Dispatcher.Invoke(() =>
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            });
            return Task.FromResult<object>(true);
        });

        // 应用信息命令
        _ipcRouter.Register("app.getVersion", _ =>
        {
            var version = Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "1.0.0";
            return Task.FromResult<object>(version);
        });
    }

    /// <summary>
    /// 初始化 WebView2 — 包括环境创建、资源映射和消息处理
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        try
        {
            // 优先使用固定版本运行时，不存在则使用系统运行时
            var environment = await WebViewHost.CreateEnvironmentAsync();
            if (environment != null)
            {
                await WebView.EnsureCoreWebView2Async(environment);
            }
            else
            {
                await WebView.EnsureCoreWebView2Async();
            }

            var coreWebView = WebView.CoreWebView2;

            // 设置虚拟主机名到文件夹映射 — 前端通过 https://baihe.app/ 访问本地资源
            WebViewHost.SetupResourceMapping(coreWebView);

            // 加载前端入口页面
            var url = WebViewHost.GetEntryPointUrl();
            coreWebView.Navigate(url);

            // 注册 WebMessageReceived 事件 — 转发到 IpcRouter 处理
            WebView.WebMessageReceived += OnWebMessageReceived;
        }
        catch (Exception ex)
        {
            // 初始化失败时在窗口中显示错误信息
            MessageBox.Show(
                $"WebView2 初始化失败:\n\n{ex.Message}\n\n请确保系统已安装 WebView2 Runtime。",
                "白鹤服务器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 处理前端发来的 IPC 消息 — 转发到 IpcRouter 并将响应回传前端
    /// </summary>
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // 获取前端发来的 JSON 消息
            var json = e.TryGetWebMessageAsString();

            // 路由到 IpcRouter 处理并获取响应
            var response = await _ipcRouter.HandleAsync(json);

            // 将响应回传给前端
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.PostWebMessageAsString(response);
            }
        }
        catch (Exception ex)
        {
            // 记录错误到调试输出，避免静默失败
            System.Diagnostics.Debug.WriteLine($"[IPC] 处理消息失败: {ex}");
        }
    }
}
