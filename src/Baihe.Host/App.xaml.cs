// WPF 应用程序入口 — 单实例 + 启动 MainWindow
// 防多开: 使用命名 Mutex 保证只有一个实例；第二个实例启动时激活已有窗口并退出自身

using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Baihe.Host;

/// <summary>
/// WPF 应用程序入口
/// </summary>
public partial class App : Application
{
    /// <summary>单实例互斥体 — 命名唯一，保证托盘只有一个图标</summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>单实例互斥体名称（基于程序集 GUID 生成，确保唯一且跨用户唯一）</summary>
    private const string MutexName = "BaiheServerLauncher_SingleInstance_Mutex_8F2B7A3C";

    /// <summary>
    /// 应用启动 — 检查单实例，重复则激活已有窗口并退出
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // 创建互斥体；createdNew=false 表示已有实例在运行
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);

        if (!createdNew)
        {
            // 已有实例在运行 — 激活它的主窗口，然后退出本实例
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 启动时预热 options.txt — 保证允许服务器资源包等关键选项在玩家进服前已生效
        Baihe.Host.Services.LaunchService.EnsureLaunchOptions();
    }

    /// <summary>
    /// 应用退出 — 释放互斥体
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 激活已有实例的主窗口（通过 Win32 查找同名进程窗口并置前）
    /// </summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            // 查找同名进程（Baihe.exe）的可见主窗口
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var processName = current.ProcessName;

            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                if (proc.Id == current.Id)
                    continue;

                var handle = proc.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    // 还原并置前窗口
                    ShowWindow(handle, 9 /* SW_RESTORE */);
                    SetForegroundWindow(handle);
                    return;
                }
            }
        }
        catch
        {
            // 激活失败不影响（本实例直接退出）
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
