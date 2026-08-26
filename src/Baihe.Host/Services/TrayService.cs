// 系统托盘服务 — 管理托盘图标、上下文菜单和窗口最小化到托盘
// 使用 WinForms NotifyIcon 实现，WPF 项目通过 UseWindowsForms 启用
// v1.1.26+: 支持 Recreate()（Explorer 重启后 TaskbarCreated 广播触发的图标重建），
//            构造与图标加载全部防御化 —— 托盘异常不允许拖垮主窗口启动

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Baihe.Host.Services;

/// <summary>
/// 系统托盘服务 — 系统托盘图标、右键菜单和最小化到托盘行为
/// </summary>
public class TrayService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool MessageBeep(uint type);

    private System.Windows.Window _mainWindow;
    private NotifyIcon? _notifyIcon;
    private Icon? _icon;
    private bool _disposed;

    /// <summary>是否已最小化到托盘</summary>
    public bool IsHiddenToTray { get; set; }

    /// <summary>
    /// 创建托盘服务实例 — 构造过程完全防御化，失败时保持无图标状态由调用方延迟重试
    /// </summary>
    public TrayService(System.Windows.Window mainWindow)
    {
        _mainWindow = mainWindow;
        CreateNotifyIcon();
    }

    /// <summary>托盘是否当前可用</summary>
    public bool IsAlive => _notifyIcon != null && !_disposed;

    /// <summary>
    /// 重建托盘图标 — Explorer 重启（TaskbarCreated 广播）或创建失败重试时调用；
    /// 内部先安全释放旧实例再重建，全程吞异常保证窗口不受影响
    /// </summary>
    public bool TryRecreate()
    {
        if (_disposed || _mainWindow == null) return false;
        try
        {
            SafeDisposeIcon();
            return CreateNotifyIcon();
        }
        catch
        {
            return false;
        }
    }

    private bool CreateNotifyIcon()
    {
        try
        {
            if (_icon == null) _icon = LoadAppIcon();

            _notifyIcon = new NotifyIcon
            {
                Text = "白鹤服务器",
                Visible = true,
                Icon = _icon,
            };

            _notifyIcon.DoubleClick += (_, _) => RestoreWindow();
            _notifyIcon.BalloonTipClicked += (_, _) => RestoreWindow();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("显示主窗口", null, (_, _) => RestoreWindow());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (_, _) => ExitApp());
            _notifyIcon.ContextMenuStrip = contextMenu;
            return true;
        }
        catch
        {
            try { _notifyIcon?.Dispose(); } catch { }
            _notifyIcon = null;
            return false;
        }
    }

    /// <summary>
    /// 加载应用图标 — 三级兜底：嵌入资源 → exe 目录 Assets/icon.ico → exe 关联图标 → 系统默认
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var resourceUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
            using var stream = Application.GetResourceStream(resourceUri)?.Stream;
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch { /* 尝试下一种方式 */ }

        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch { /* 尝试下一种方式 */ }

        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
        }
        catch { /* 最终回退 */ }

        return SystemIcons.Application;
    }

    /// <summary>
    /// 隐藏窗口到托盘（弹气泡提示）
    /// </summary>
    public void HideToTray()
    {
        IsHiddenToTray = true;
        _mainWindow.Hide();
        if (_notifyIcon != null)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(
                    2000,
                    "白鹤服务器",
                    "应用已最小化到系统托盘，双击图标恢复",
                    ToolTipIcon.Info);
            }
            catch { /* 托盘暂不可用时静默 */ }
        }
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
    public void ShowNotification(string title, string message, int timeout = 5000)
    {
        if (_notifyIcon == null) return;
        try
        {
            _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
            MessageBeep(0x00000040);
        }
        catch { }
    }

    /// <summary>
    /// 退出应用
    /// </summary>
    private static void ExitApp()
    {
        Application.Current.Shutdown();
    }

    private void SafeDisposeIcon()
    {
        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
        catch { }
        _notifyIcon = null;
    }

    /// <summary>
    /// 释放托盘资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SafeDisposeIcon();
        try { _icon?.Dispose(); } catch { }
        _icon = null;
    }
}
