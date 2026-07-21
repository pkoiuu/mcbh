// WebView2 封装 — 负责环境初始化、固定版本兜底和资源映射
// 支持 WebView2FixedRuntime 目录作为固定版本运行时的兜底方案

using System;
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
    /// 实际使用的资源路径 — 供诊断日志使用
    /// </summary>
    public static string ResolvedAssetsPath { get; private set; } = "";

    /// <summary>
    /// 设置虚拟主机名到文件夹的映射 — 前端可通过 https://baihe.app/ 访问本地资源
    /// </summary>
    /// <param name="webView">已初始化的 CoreWebView2 实例</param>
    public static void SetupResourceMapping(CoreWebView2 webView)
    {
        // 按优先级尝试多个可能的资源路径
        var candidates = new[]
        {
            // 1. 工作目录下的 wwwroot
            Path.GetFullPath(AssetsFolder),
            // 2. 输出目录下的 wwwroot（发布后的标准位置）
            Path.Combine(AppContext.BaseDirectory, AssetsFolder),
            // 3. 开发时从 bin/Release/net10.0-windows/win-x64/ 回到项目目录
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", AssetsFolder),
        };

        var assetsPath = Array.Find(candidates, Directory.Exists) ?? candidates[0];
        ResolvedAssetsPath = assetsPath;

        // 写入诊断日志 — 记录实际使用的资源路径和所有候选路径
        var debugLog = $"AssetsPath: {assetsPath}\nExists: {Directory.Exists(assetsPath)}\n";
        debugLog += $"IndexHtml exists: {File.Exists(Path.Combine(assetsPath, "index.html"))}\n";
        debugLog += "Candidates:\n";
        foreach (var c in candidates)
            debugLog += $"  {c} -> Exists: {Directory.Exists(c)}\n";
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "debug-paths.txt"), debugLog);
        }
        catch { }

        webView.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            assetsPath,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    /// <summary>
    /// 获取前端入口 URL — 带时间戳缓存清除参数
    /// </summary>
    /// <returns>https://baihe.app/index.html?v={timestamp}</returns>
    public static string GetEntryPointUrl()
    {
        return $"https://{VirtualHostName}/index.html?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    }
}
