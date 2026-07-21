// WPF 应用程序入口 — 实现 IApplicationContainer 接口以解耦 Baihe.Core 对 WPF Application 的依赖
// Baihe.Core 通过 IApplicationContainer 接口控制应用程序生命周期，无需直接引用 WPF

using System.Windows;
using Baihe.Core.App.IoC;

namespace Baihe.Host;

/// <summary>
/// WPF 应用程序入口 — 实现 IApplicationContainer 接口
/// </summary>
public partial class App : Application, IApplicationContainer
{
    /// <summary>
    /// 运行应用程序 — 实现 IApplicationContainer.Run()
    /// 调用基类 Application.Run() 启动 WPF 消息循环
    /// </summary>
    /// <returns>退出状态码</returns>
    int IApplicationContainer.Run()
    {
        return base.Run();
    }

    /// <summary>
    /// 显示主窗口 — 实现 IApplicationContainer.ShowMainWindow()
    /// </summary>
    public void ShowMainWindow()
    {
        MainWindow?.Show();
    }

    /// <summary>
    /// 关闭应用程序 — 实现 IApplicationContainer.Shutdown()
    /// 调用基类 Application.Shutdown() 终止应用程序
    /// </summary>
    void IApplicationContainer.Shutdown()
    {
        base.Shutdown();
    }
}
