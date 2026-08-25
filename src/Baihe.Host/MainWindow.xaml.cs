// 主窗口代码后置 — WebView2 初始化、资源映射和 IPC 消息转发
// 负责将前端 WebView2 与后端 IpcRouter 连接起来

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Baihe.Host.Ipc;
using Baihe.Host.Models;
using Baihe.Host.Services;
using Baihe.Host.Web;
using JsonValueKind = System.Text.Json.JsonValueKind;

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

    /// <summary>是否正在外部网站导航中（聊天页面等）</summary>
    private bool _isExternalNav = false;

    /// <summary>微软登录取消令牌</summary>
    private CancellationTokenSource? _msCts;

    /// <summary>系统托盘服务</summary>
    private TrayService? _trayService;

    /// <summary>
    /// 创建主窗口实例
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        // 注册窗口控制和应用信息命令
        RegisterHostCommands();
        // 创建系统托盘服务
        _trayService = new TrayService(this);
        // 异步初始化 WebView2，不阻塞窗口显示
        _ = InitializeWebViewAsync();
    }

    /// <summary>
    /// 窗口关闭拦截 — 最小化到托盘而非直接关闭
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_trayService != null)
        {
            // 首次关闭 → 最小化到托盘
            e.Cancel = true;
            _trayService.HideToTray();
            return;
        }

        // 真正退出时释放托盘资源
        _trayService?.Dispose();
        base.OnClosing(e);
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

        // 应用信息命令 — 版本号单一来源: csproj 的 AssemblyVersion/FileVersion（Release 从 tag 注入），去掉末尾 .0
        _ipcRouter.Register("app.getVersion", _ =>
        {
            var assembly = Assembly.GetExecutingAssembly();

            // 优先从 AssemblyFileVersion 读取（Release 构建通过 -p:FileVersion 注入）
            var fileVer = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVer != null && Version.TryParse(fileVer.Version, out var parsed))
            {
                return Task.FromResult<object>(parsed.ToString(3)); // 只取主.次.修，去掉末尾的 .0
            }

            // 回退到程序集版本（编译期属性，必定存在 — 无需硬编码）
            var asmVer = assembly.GetName().Version ?? new Version(1, 0, 0);
            return Task.FromResult<object>(asmVer.ToString(3));
        });

        // 检查更新 — 查询 GitHub Releases 最新版本（支持国内镜像下载）
        // 参数可选: {force:true} 强制忽略缓存（设置页手动检查）
        _ipcRouter.Register("update.check", async args =>
        {
            var force = false;
            if (args?.ValueKind == JsonValueKind.Object
                && args.Value.TryGetProperty("force", out var forceProp))
                force = forceProp.ValueKind == JsonValueKind.True;
            return await UpdateService.CheckForUpdateAsync(force);
        });

        // 最新动态 — 拉取仓库 news.json（首页公告，可自动更新）
        _ipcRouter.Register("news.list", async _ =>
        {
            return await NewsService.GetNewsAsync();
        });

        // 玩家指南维基 — 拉取仓库 wiki.json（可远程编辑，失败前端回退内置）
        _ipcRouter.Register("wiki.get", async _ =>
        {
            var categories = await WikiService.GetWikiAsync();
            return categories ?? new List<object>();
        });

        // ===== Stage 2: 启动核心命令 =====

        // 版本清单
        _ipcRouter.Register("version.list", async args =>
        {
            var typeFilter = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString() : null;
            return await VersionService.GetVersionList(typeFilter);
        });

        // 实例管理
        _ipcRouter.Register("instance.list", async _ =>
        {
            return await InstanceService.ListInstances();
        });

        _ipcRouter.Register("instance.current", async _ =>
        {
            return (await InstanceService.GetCurrentInstance())!;
        });

        // 认证 — 返回账户信息或空标记（未设置时 username 为 null）
        _ipcRouter.Register("auth.current", async _ =>
        {
            var account = await AuthService.GetCurrentAccount();
            if (account == null)
                return new { username = (string?)null, uuid = (string?)null, type = "offline", typeDisplay = "离线", isUserSet = false };
            return new
            {
                username = account.Username,
                uuid = account.Uuid,
                type = account.Type.ToString().ToLowerInvariant(),
                typeDisplay = account.TypeDisplay,
                isUserSet = account.IsUserSet,
            };
        });

        // 快速检查是否已设置账户 — 供前端启动前检查
        _ipcRouter.Register("auth.hasAccount", async _ =>
        {
            return new { hasAccount = await AuthService.HasAccount() };
        });

        _ipcRouter.Register("auth.offline", async args =>
        {
            var username = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "Player";
            var account = await AuthService.SetOfflineAccount(username);
            return new { username = account.Username, uuid = account.Uuid, isUserSet = account.IsUserSet };
        });

        // 别名 — Login.svelte 使用 auth.setOffline
        _ipcRouter.Register("auth.setOffline", async args =>
        {
            var username = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "Player";
            var account = await AuthService.SetOfflineAccount(username);
            return new { username = account.Username, isUserSet = account.IsUserSet };
        });

        // 微软正版登录 — 设备码流程，通过事件推送状态
        _ipcRouter.Register("auth.msLogin", _args =>
        {
            _msCts?.Cancel();
            _msCts = new CancellationTokenSource();
            var cts = _msCts;

            _ = Task.Run(async () =>
            {
                try
                {
                    var account = await MicrosoftAuthService.LoginWithDeviceCode(
                        (userCode, verificationUri) =>
                        {
                            IpcRouter.PushEvent("auth.msDeviceCode", new { userCode, verificationUri });
                        },
                        cts.Token);

                    AuthService.SaveAccount(account);
                    IpcRouter.PushEvent("auth.msLoginResult", new { success = true, username = account.Username });
                }
                catch (OperationCanceledException)
                {
                    // 用户取消，不推送事件
                }
                catch (Exception ex)
                {
                    IpcRouter.PushEvent("auth.msLoginResult", new { success = false, error = ex.Message });
                }
            });

            return Task.FromResult<object>(new { started = true });
        });

        // 取消微软登录
        _ipcRouter.Register("auth.msCancel", _ =>
        {
            _msCts?.Cancel();
            _msCts = null;
            return Task.FromResult<object>(new { cancelled = true });
        });

        // 第三方验证登录
        _ipcRouter.Register("auth.thirdPartyLogin", async args =>
        {
            if (args?.ValueKind != System.Text.Json.JsonValueKind.Object)
                return new { success = false, error = "参数错误" };

            string serverUrl = "";
            string username = "";
            string password = "";

            if (args.Value.TryGetProperty("serverUrl", out var urlProp))
                serverUrl = urlProp.GetString() ?? "";
            if (args.Value.TryGetProperty("username", out var userProp))
                username = userProp.GetString() ?? "";
            if (args.Value.TryGetProperty("password", out var passProp))
                password = passProp.GetString() ?? "";

            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return new { success = false, error = "请填写所有字段" };

            try
            {
                var account = await ThirdPartyAuthService.Login(serverUrl, username, password);
                AuthService.SaveAccount(account);
                return new { success = true, username = account.Username };
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        // Java 检测
        _ipcRouter.Register("java.detect", async _ =>
        {
            return await JavaHostService.DetectSystemJava();
        });

        _ipcRouter.Register("java.bundled", async _ =>
        {
            return (await JavaHostService.DetectBundledJava())!;
        });

        // 启动 — 加载设置并传递给启动服务
        // 参数可选: {instanceId?, serverAddress?, serverPort?} — 服务器列表选择时覆盖 QuickPlay 目标
        _ipcRouter.Register("launch.start", async args =>
        {
            string instanceId = "";
            string? serverAddress = null;
            int? serverPort = null;

            if (args?.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (args.Value.TryGetProperty("instanceId", out var idProp))
                    instanceId = idProp.GetString() ?? "";
                if (args.Value.TryGetProperty("serverAddress", out var saProp))
                    serverAddress = saProp.ValueKind == System.Text.Json.JsonValueKind.String ? saProp.GetString() : null;
                if (args.Value.TryGetProperty("serverPort", out var spProp)
                    && spProp.TryGetInt32(out var port))
                    serverPort = port;
            }

            // 检查用户是否已设置账户
            var account = await AuthService.GetCurrentAccount();
            if (account == null || !account.IsUserSet)
                return new { success = false, error = "请先登录账户" };

            // 微软账户自动刷新令牌
            if (account.Type == AccountType.Microsoft)
            {
                account = await AuthService.RefreshIfExpired();
                if (account == null || !account.IsUserSet)
                    return new { success = false, error = "登录已过期，请重新登录" };
            }

            // 如果未指定实例，使用当前实例
            if (string.IsNullOrEmpty(instanceId))
            {
                var current = await InstanceService.GetCurrentInstance();
                instanceId = current?.Id ?? "";
            }

            var settings = await SettingsService.GetAsync();

            // 遥测上报 — 游戏启动时统一上报所有信息（仅会话首次发送）
            var wechatName = await WeChatService.GetAsync();
            _ = TelemetryService.ReportAsync(account.Uuid, account.Username, account.Email, wechatName, account.Type.ToString());

            return await LaunchService.Launch(instanceId, account, settings, serverAddress, serverPort);
        });

        _ipcRouter.Register("launch.status", _ =>
        {
            return Task.FromResult(LaunchService.GetStatus());
        });

        // ===== Stage 3: 下载与安装命令 =====

        // 下载版本
        _ipcRouter.Register("download.start", async args =>
        {
            var versionId = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            if (string.IsNullOrEmpty(versionId))
                return new { success = false, error = "未指定版本 ID" };

            // 异步执行下载，不阻塞 IPC 响应
            _ = Task.Run(() => DownloadService.DownloadVersion(versionId));
            return new { success = true, message = "下载已开始" };
        });

        _ipcRouter.Register("download.status", _ =>
        {
            return Task.FromResult(DownloadService.GetStatus());
        });

        // Fabric 安装
        _ipcRouter.Register("fabric.install", async args =>
        {
            var gameVersion = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            if (string.IsNullOrEmpty(gameVersion))
                return new { success = false, error = "未指定游戏版本" };

            // 异步执行安装
            _ = Task.Run(() => FabricService.Install(gameVersion));
            return new { success = true, message = "Fabric 安装已开始" };
        });

        _ipcRouter.Register("fabric.loaders", async args =>
        {
            var gameVersion = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            return await FabricService.GetLoaders(gameVersion);
        });

        // ===== Stage 5: 设置与服务器状态命令 =====

        // 获取设置
        _ipcRouter.Register("settings.get", async _ =>
        {
            return await SettingsService.GetAsync();
        });

        // 更新设置
        _ipcRouter.Register("settings.set", async args =>
        {
            return await SettingsService.SetAsync(args ?? default);
        });

        // 服务器状态检查 — 可选参数 {serverAddress?, serverPort?} 覆盖默认服务器
        _ipcRouter.Register("server.status", async args =>
        {
            string? address = null;
            int? port = null;
            if (args?.ValueKind == JsonValueKind.Object)
            {
                if (args.Value.TryGetProperty("serverAddress", out var saProp))
                    address = saProp.ValueKind == JsonValueKind.String ? saProp.GetString() : null;
                if (args.Value.TryGetProperty("serverPort", out var spProp)
                    && spProp.TryGetInt32(out var portVal))
                    port = portVal;
            }
            return await ServerStatusService.CheckStatus(address, port);
        });

        // ===== 服务器列表（QuickPlay 目标选择）=====

        _ipcRouter.Register("servers.list", async _ =>
        {
            return await ServerEntryService.GetServersAsync();
        });

        // 新增服务器 — {name, address, port}
        _ipcRouter.Register("servers.add", async args =>
        {
            if (args?.ValueKind != JsonValueKind.Object)
                return new { success = false, error = "参数错误" };

            string name = "";
            string address = "";
            int port = 25565;
            if (args.Value.TryGetProperty("name", out var nProp))
                name = nProp.GetString() ?? "";
            if (args.Value.TryGetProperty("address", out var aProp))
                address = aProp.GetString() ?? "";
            if (args.Value.TryGetProperty("port", out var pProp) && pProp.TryGetInt32(out var portVal))
                port = portVal;

            var entry = await ServerEntryService.AddServerAsync(name, address, port);
            return entry == null
                ? new { success = false, error = "参数无效或服务器已存在" }
                : new { success = true, entry };
        });

        // 删除服务器 — 内置默认条目不可删
        _ipcRouter.Register("servers.remove", async args =>
        {
            var id = args?.ValueKind == JsonValueKind.String ? args.Value.GetString() ?? "" : "";
            var removed = await ServerEntryService.RemoveServerAsync(id);
            return new { success = removed };
        });

        // ===== Stage 6: 工具功能命令 =====

        // Mod 管理
        _ipcRouter.Register("mods.list", async _ =>
        {
            return await ModService.ListMods();
        });

        _ipcRouter.Register("mods.toggle", async args =>
        {
            var fileName = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            var enabled = await ModService.ToggleMod(fileName);
            return new { success = true, enabled };
        });

        _ipcRouter.Register("mods.delete", async args =>
        {
            var fileName = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            await ModService.DeleteMod(fileName);
            return new { success = true };
        });

        _ipcRouter.Register("mods.openFolder", async _ =>
        {
            var path = await ModService.OpenModsFolder();
            return new { success = true, path };
        });

        // 存档管理
        _ipcRouter.Register("saves.list", async _ =>
        {
            return await SaveService.ListSaves();
        });

        _ipcRouter.Register("saves.backup", async args =>
        {
            var saveName = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            return await SaveService.BackupSave(saveName);
        });

        _ipcRouter.Register("saves.import", async args =>
        {
            var zipPath = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            return await SaveService.ImportSave(zipPath);
        });

        _ipcRouter.Register("saves.backups", async _ =>
        {
            return await SaveService.ListBackups();
        });

        _ipcRouter.Register("saves.deleteBackup", async args =>
        {
            var fileName = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "";
            await SaveService.DeleteBackup(fileName);
            return new { success = true };
        });

        _ipcRouter.Register("saves.restore", async args =>
        {
            if (args?.ValueKind == System.Text.Json.JsonValueKind.Object
                && args.Value.TryGetProperty("backupFileName", out var fnProp))
            {
                var backupFileName = fnProp.GetString() ?? "";
                string? saveName = null;
                if (args.Value.TryGetProperty("saveName", out var snProp))
                    saveName = snProp.GetString();
                return await SaveService.RestoreBackup(backupFileName, saveName);
            }
            return new { success = false, error = "参数错误" };
        });

        // ===== 光影管理 (ShaderService) =====

        _ipcRouter.Register("shaders.list", async _ =>
        {
            return await ShaderService.ListShaders();
        });

        _ipcRouter.Register("shaders.enable", async args =>
        {
            var fileName = args?.ValueKind == JsonValueKind.String
                ? args.Value.GetString() ?? "" : "";
            return await ShaderService.EnableShader(fileName);
        });

        // 关闭光影 — 设置 enableShaders=false
        _ipcRouter.Register("shaders.disable", async _ =>
        {
            return await ShaderService.DisableShaders();
        });

        _ipcRouter.Register("shaders.delete", async args =>
        {
            var fileName = args?.ValueKind == JsonValueKind.String
                ? args.Value.GetString() ?? "" : "";
            return await ShaderService.DeleteShader(fileName);
        });

        _ipcRouter.Register("shaders.openFolder", async _ =>
        {
            var path = await ShaderService.OpenShadersFolder();
            return new { success = true, path };
        });

        // 截图管理
        _ipcRouter.Register("screenshots.list", async _ =>
        {
            return await ToolService.ListScreenshots();
        });

        // 打开文件夹
        _ipcRouter.Register("tools.openFolder", async args =>
        {
            var folderName = args?.ValueKind == System.Text.Json.JsonValueKind.String
                ? args.Value.GetString()! : "minecraft";
            var path = await ToolService.OpenFolder(folderName);
            return new { success = true, path };
        });

        // 游戏修复
        _ipcRouter.Register("tools.repair", async _ =>
        {
            return await ToolService.RepairGame();
        });

        // 在系统默认浏览器中打开 URL
        _ipcRouter.Register("open.url", async args =>
        {
            var url = args?.ValueKind == JsonValueKind.String
                ? args.Value.GetString()! : "";
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL 不能为空");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            return new { success = true };
        });

        // 导航到外部网站 — 用于聊天页面
        _ipcRouter.Register("nav.external", async args =>
        {
            var url = args?.ValueKind == JsonValueKind.String
                ? args.Value.GetString()! : "";
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL 不能为空");

            _isExternalNav = true;
            Dispatcher.Invoke(() =>
            {
                WebView.CoreWebView2?.Navigate(url);
            });
            return new { success = true };
        });

        // 导航回启动器主页
        _ipcRouter.Register("nav.home", async _ =>
        {
            _isExternalNav = false;
            Dispatcher.Invoke(() =>
            {
                var url = WebViewHost.GetEntryPointUrl();
                WebView.CoreWebView2?.Navigate(url);
            });
            return new { success = true };
        });

        // 主题切换 — 前端通知后端同步 WebView2 背景色和标题栏
        _ipcRouter.Register("theme.set", async (args) =>
        {
            try
            {
                var themeVal = args.Value.GetProperty("theme").GetString();
                var isDark = themeVal != "light";

                Dispatcher.Invoke(() =>
                {
                    ApplyThemeToWindow(isDark);
                });

                return new { success = true };
            }
            catch
            {
                return new { success = false };
            }
        });

        // 系统内存信息 — 返回总内存和推荐分配值
        _ipcRouter.Register("system.memory", async _ =>
        {
            var totalMB = SettingsService.GetTotalPhysicalMemoryMB();
            var recommendedMB = SettingsService.CalculateRecommendedMemory(totalMB);
            return new
            {
                totalMB,
                totalGB = totalMB / 1024,
                recommendedMB,
                recommendedGB = recommendedMB / 1024
            };
        });

        // ===== 微信名管理 =====

        // 获取已保存的微信名 — 前端启动时检查是否已填写
        _ipcRouter.Register("wechat.get", async _ =>
        {
            var name = await WeChatService.GetAsync();
            return new { name };
        });

        // 保存微信名 — 用户首次填写后持久化
        _ipcRouter.Register("wechat.set", async args =>
        {
            var name = args?.ValueKind == JsonValueKind.String
                ? args.Value.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                await WeChatService.SaveAsync(name.Trim());
            }
            return new { success = true, name = name.Trim() };
        });
    }

    /// <summary>
    /// 应用主题到窗口 — 同步 WebView2 背景色、标题栏背景色和文字颜色
    /// </summary>
    /// <param name="isDark">是否为暗色主题</param>
    private void ApplyThemeToWindow(bool isDark)
    {
        WebView.DefaultBackgroundColor = isDark
            ? System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1C)
            : System.Drawing.Color.FromArgb(0xF7, 0xF7, 0xFA);

        if (isDark)
        {
            this.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C));
            TitleBarBorder.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x20));
            TitleBarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2C));
            if (TitleText != null)
                TitleText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            this.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xFA));
            TitleBarBorder.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF7, 0xF7, 0xFA));
            TitleBarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xEA));
            if (TitleText != null)
                TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
        }
    }

    /// <summary>
    /// 关闭按钮 — 关闭窗口
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 最小化按钮 — 最小化窗口
    /// </summary>
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 最大化按钮 — 切换最大化/还原
    /// </summary>
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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

            // 设置 WebView2 背景色为深色，避免加载期间白屏
            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1C);

            // 禁用 WebView2 默认右键菜单 — 桌面应用不需要浏览器上下文菜单
            coreWebView.Settings.AreDefaultContextMenusEnabled = false;
            // 禁用 WebView2 默认开发者工具快捷键 (F12)
            coreWebView.Settings.AreDevToolsEnabled = false;

            // 设置虚拟主机名到文件夹映射 — 前端通过 https://baihe.app/ 访问本地资源
            WebViewHost.SetupResourceMapping(coreWebView);

            // 导航完成事件 — 注入返回按钮和消息监控，或捕获加载失败
            coreWebView.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView2] 导航失败: {e.WebErrorStatus}");
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"前端页面加载失败: {e.WebErrorStatus}\n\n资源路径: {WebViewHost.GetEntryPointUrl()}",
                            "白鹤服务器",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                    return;
                }

                // 外部网站导航完成后 — 注入返回按钮和消息监控
                if (_isExternalNav)
                {
                    await InjectBackButtonAsync();
                    await InjectChatMonitorScriptAsync();
                }

                // 同步前端主题到后端 — 读取 localStorage 中的主题设置
                try
                {
                    var themeResult = await coreWebView.ExecuteScriptAsync(
                        "localStorage.getItem('baihe_theme') || 'dark'");
                    var isDark = themeResult?.Trim('"') != "light";

                    Dispatcher.Invoke(() =>
                    {
                        ApplyThemeToWindow(isDark);
                    });
                }
                catch { }
            };

            // 设置 IPC 推送回调 — 后端主动向前端推送事件 (下载进度等)
            IpcRouter.OnPushMessage = json =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (WebView.CoreWebView2 != null)
                        WebView.CoreWebView2.PostWebMessageAsString(json);
                });
            };

            // 加载前端入口页面
            var url = WebViewHost.GetEntryPointUrl();
            System.Diagnostics.Debug.WriteLine($"[WebView2] 导航到: {url}");
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

            // 返回启动器主页 — 从聊天页面导航回启动器
            if (json == "__nav_home__")
            {
                _isExternalNav = false;
                Dispatcher.Invoke(() =>
                {
                    var url = WebViewHost.GetEntryPointUrl();
                    WebView.CoreWebView2?.Navigate(url);
                });
                return;
            }

            // 聊天消息通知 — 窗口在托盘或未激活时显示系统通知
            if (json != null && json.StartsWith("__chat_notify__:"))
            {
                var messageContent = json["__chat_notify__:".Length..];
                var shouldNotify = (_trayService is { IsHiddenToTray: true }) || !IsActive;
                if (shouldNotify)
                {
                    _trayService?.ShowNotification("白鹤聊天", $"收到新消息: {messageContent}", 5000);
                }
                return;
            }

            // 路由到 IpcRouter 处理并获取响应
            var response = await _ipcRouter.HandleAsync(json ?? string.Empty);

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
