// 应用程序容器接口 — 解耦 Baihe.Core 对 WPF Application 的依赖
// WPF 宿主 (Baihe.Host) 需实现此接口

namespace Baihe.Core.App.IoC;

/// <summary>
/// 应用程序容器接口。由宿主层实现，提供应用程序生命周期控制。
/// </summary>
public interface IApplicationContainer
{
    /// <summary>
    /// 运行应用程序。
    /// </summary>
    /// <returns>退出状态码</returns>
    int Run();

    /// <summary>
    /// 显示主窗口。
    /// </summary>
    void ShowMainWindow();

    /// <summary>
    /// 关闭应用程序。
    /// </summary>
    void Shutdown();
}
