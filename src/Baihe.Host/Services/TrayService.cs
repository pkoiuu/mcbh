// 系统托盘服务 — 管理托盘图标、上下文菜单和窗口最小化到托盘
// 使用 WinForms NotifyIcon 实现，WPF 项目通过 UseWindowsForms 启用

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Baihe.Host.Services;

/// <summary>
/// 系统托盘服务 — 管理托盘图标、右键菜单和最小化到托盘行为
/// </summary>
public class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Window _mainWindow;
    private bool _disposed;

    // Win32 MessageBeep — 用于播放系统提示音
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool MessageBeep(uint type);

    /// <summary>
    /// 是否已最小化到托盘
    /// </summary>
    public bool IsHiddenToTray { get; private set; }

    /// <summary>
    /// 创建托盘服务实例
    /// </summary>
    /// <param name="mainWindow">主窗口</param>
    public TrayService(System.Windows.Window mainWindow)
    {
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "白鹤服务器",
            Visible = true,
            Icon = LoadAppIcon(),
        };

        // 双击托盘图标恢复窗口
        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

        // 右键菜单
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (_, _) => RestoreWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => ExitApp());
        _notifyIcon.ContextMenuStrip = contextMenu;

        // 气球通知点击恢复窗口
        _notifyIcon.BalloonTipClicked += (_, _) => RestoreWindow();
    }

    /// <summary>
    /// 加载应用图标 — 尝试多种方式确保图标可见
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            // 方式1: 从嵌入资源加载（WPF Resource — 编译时打包到 exe）
            var resourceUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
            using var stream = System.Windows.Application.GetResourceStream(resourceUri)?.Stream;
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch { /* 忽略，尝试下一种方式 */ }

        try
        {
            // 方式2: 从 exe 所在目录的 Assets 文件夹加载
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { /* 忽略，尝试下一种方式 */ }

        try
        {
            // 方式3: 从 exe 自身提取关联图标
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
        }
        catch { /* 忽略 */ }

        // 最终回退到系统默认图标
        return SystemIcons.Application;
    }

    /// <summary>
    /// 隐藏窗口到托盘
    /// </summary>
    public void HideToTray()
    {
        IsHiddenToTray = true;
        _mainWindow.Hide();
        _notifyIcon.ShowBalloonTip(
            2000,
            "白鹤服务器",
            "应用已最小化到系统托盘，双击图标恢复",
            ToolTipIcon.Info);
    }

    /// <summary>
    /// 从托盘恢复窗口
    /// </summary>
    public void RestoreWindow()
    {
        IsHiddenToTray = false;
        _mainWindow.Show();
        _mainWindow.WindowState = System.Windows.WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>
    /// 显示托盘气球通知并播放提示音
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="timeout">显示时长（毫秒）</param>
    public void ShowNotification(string title, string message, int timeout = 5000)
    {
        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
        // 播放系统信息提示音 (MB_ICONINFORMATION = 0x00000040)
        MessageBeep(0x00000040);
    }

    /// <summary>
    /// 退出应用
    /// </summary>
    private static void ExitApp()
    {
        Application.Current.Shutdown();
    }

    /// <summary>
    /// 释放托盘资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
