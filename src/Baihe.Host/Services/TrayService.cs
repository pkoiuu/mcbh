// 系统托盘服务 — 管理托盘图标、上下文菜单和窗口最小化到托盘
// 使用 WinForms NotifyIcon 实现，WPF 项目通过 UseWindowsForms 启用

using System;
using System.Drawing;
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
    /// 加载应用图标 — 从嵌入资源或文件加载
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            // 尝试从 exe 所在目录加载 icon.ico
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
                return new Icon(iconPath);

            // 回退到默认系统图标
            return SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
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
    /// 显示托盘气球通知
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="timeout">显示时长（毫秒）</param>
    public void ShowNotification(string title, string message, int timeout = 3000)
    {
        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
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
