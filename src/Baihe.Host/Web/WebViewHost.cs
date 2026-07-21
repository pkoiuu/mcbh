// WebView2 封装 — 负责环境初始化、固定版本兜底和资源映射
// 支持 WebView2FixedRuntime 目录作为固定版本运行时的兜底方案

using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Baihe.Host.Web;

/// <summary>
/// WebView2 宿主封装 — 提供环境创建和资源映射的静态工具方法
/// </summary>
public static class WebViewHost
{
    /// <summary>
    /// 固定版本 WebView2 运行时目录名（相对于工作目录）
    /// 如果此目录存在，则使用固定版本运行时而非系统安装的运行时
    /// </summary>
    private const string FixedRuntimeFolder = "WebView2FixedRuntime";

    /// <summary>
    /// 虚拟主机名 — 前端通过此域名访问本地资源
    /// </summary>
    private const string VirtualHostName = "baihe.app";

    /// <summary>
    /// 前端资源目录名（相对于工作目录）
    /// 注意: 使用 wwwroot 而非 assets，避免与 Assets/icon.ico 在 Windows 上冲突
    /// </summary>
    private const string AssetsFolder = "wwwroot";

    /// <summary>
    /// 创建 WebView2 环境 — 优先使用固定版本运行时，不存在则返回 null 使用系统运行时
    /// </summary>
    /// <returns>WebView2 环境实例；若使用系统运行时则返回 null</returns>
    public static async Task<CoreWebView2Environment?> CreateEnvironmentAsync()
    {
        // 检测固定版本运行时目录是否存在
        var fixedRuntimePath = Path.GetFullPath(FixedRuntimeFolder);
        if (Directory.Exists(fixedRuntimePath))
        {
            // 使用固定版本运行时创建环境
            return await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: fixedRuntimePath);
        }

        // 返回 null，调用方将使用系统安装的 WebView2 运行时
        return null;
    }

    /// <summary>
    /// 设置虚拟主机名到文件夹的映射 — 前端可通过 https://baihe.app/ 访问本地资源
    /// </summary>
    /// <param name="webView">已初始化的 CoreWebView2 实例</param>
    public static void SetupResourceMapping(CoreWebView2 webView)
    {
        var assetsPath = Path.GetFullPath(AssetsFolder);
        webView.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            assetsPath,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    /// <summary>
    /// 获取前端入口 URL
    /// </summary>
    /// <returns>https://baihe.app/index.html</returns>
    public static string GetEntryPointUrl()
    {
        return $"https://{VirtualHostName}/index.html";
    }
}
