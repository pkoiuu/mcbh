// 更新检查服务 — 检查 GitHub Releases 最新版本，支持国内镜像加速下载
// 通过 GitHub API 获取最新 Release 信息，与当前版本比较

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 更新检查服务 — 检查 GitHub Releases 是否有新版本
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient _httpClient = new();

    private const string RepoOwner = "pkoiuu";
    private const string RepoName = "mcbh";

    /// <summary>国内镜像前缀 — 加速 GitHub 下载</summary>
    private const string MirrorPrefix = "https://gh-proxy.com/";

    static UpdateService()
    {
        // GitHub API 要求设置 User-Agent
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BaiheLauncher", "1.0"));
    }

    /// <summary>
    /// 检查是否有新版本 — 调用 GitHub API 获取最新 Release
    /// </summary>
    public static async Task<UpdateInfo> CheckForUpdateAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "1.0.0";

        try
        {
            // 10 秒超时，避免网络问题阻塞启动
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetStringAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
                cts.Token);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // 解析版本号 (tag_name: "v1.0.0" → "1.0.0")
            var tagName = root.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? "" : "";
            var latestVersion = tagName.TrimStart('v', 'V');

            // Release 页面 URL
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString() ?? "" : "";

            // Release 说明
            var body = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? "" : "";

            // 查找 .exe 安装包下载链接
            string downloadUrl = htmlUrl;
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.GetArrayLength() > 0)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (asset.TryGetProperty("browser_download_url", out var dlProp))
                                downloadUrl = dlProp.GetString() ?? htmlUrl;
                            break;
                        }
                    }
                }
            }

            // 应用国内镜像加速
            if (downloadUrl.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase))
                downloadUrl = MirrorPrefix + downloadUrl;

            var hasUpdate = IsNewerVersion(latestVersion, currentVersion);

            return new UpdateInfo
            {
                HasUpdate = hasUpdate,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseUrl = htmlUrl,
                ReleaseNotes = body,
            };
        }
        catch
        {
            // 网络错误或 API 不可用时静默返回无更新
            return new UpdateInfo
            {
                HasUpdate = false,
                CurrentVersion = currentVersion,
                LatestVersion = currentVersion,
                DownloadUrl = "",
                ReleaseUrl = "",
                ReleaseNotes = "",
            };
        }
    }

    /// <summary>
    /// 比较版本号 — 判断 latest 是否比 current 更新
    /// </summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var latestVer) &&
            Version.TryParse(current, out var currentVer))
        {
            return latestVer > currentVer;
        }
        return false;
    }
}

/// <summary>
/// 更新信息
/// </summary>
public class UpdateInfo
{
    /// <summary>是否有新版本</summary>
    public bool HasUpdate { get; set; }

    /// <summary>当前版本号</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>最新版本号</summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>下载链接（已应用国内镜像）</summary>
    public string DownloadUrl { get; set; } = "";

    /// <summary>Release 页面链接</summary>
    public string ReleaseUrl { get; set; } = "";

    /// <summary>更新说明</summary>
    public string ReleaseNotes { get; set; } = "";
}
