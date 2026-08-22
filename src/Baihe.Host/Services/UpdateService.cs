// 更新检查服务 — 检查 GitHub Releases 最新版本，支持国内镜像加速下载
// 特性:
//  1. 镜像列表自动更新 — 每次检查更新时从仓库 mirrors.json 拉取最新加速站列表
//     (https://raw.githubusercontent.com/pkoiuu/mcbh/main/mirrors.json)，
//     拉取失败时回退内置默认列表（内置列表兜底，无需发版即可更新镜像）
//  2. 速度优先 — 对真实下载 URL 并行测速（下载前 512KB 计时），选择最快镜像；
//     所有镜像不可用时回退 GitHub 直链
//  3. 中文文件名支持 — 部分镜像不支持中文文件名（安装包为中文名），测速逻辑自动淘汰

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>镜像列表数据源 — 仓库根目录 mirrors.json（raw 直连，失败回退内置）</summary>
    private static readonly string MirrorsJsonUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/mirrors.json";

    /// <summary>
    /// 内置默认镜像前缀列表（兜底）— 仅在 mirrors.json 拉取失败时使用。
    /// 完整列表维护在仓库 mirrors.json，可自动更新无需发版。
    /// </summary>
    private static readonly string[] BuiltinMirrorPrefixes =
    {
        "https://ghfast.top/",
        "https://ghproxy.net/",
        "https://gh-proxy.com/",
        "https://ghproxy.link/",
    };

    /// <summary>测速下载的字节数（512KB）</summary>
    private const int SpeedProbeBytes = 512 * 1024;

    /// <summary>镜像测速总超时（秒）</summary>
    private const int SpeedProbeTimeoutSeconds = 10;

    /// <summary>单个镜像测速超时（秒）</summary>
    private const int SingleProbeTimeoutSeconds = 4;

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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // 并行: 查最新 Release + 拉取最新镜像列表
            var releaseTask = FetchLatestReleaseAsync(cts.Token);
            var mirrorsTask = FetchMirrorsAsync(cts.Token);

            var root = await releaseTask;
            var mirrors = await mirrorsTask;

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

            // 速度优先 — 对真实下载 URL 并行测速，选最快镜像
            double speedMbps = 0;
            string source = "GitHub 直链";
            if (downloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                var best = await PickFastestMirrorAsync(downloadUrl, mirrors, cts.Token);
                if (best != null)
                {
                    downloadUrl = best.Url;
                    speedMbps = best.SpeedMbps;
                    source = best.MirrorHost;
                }
            }

            var hasUpdate = IsNewerVersion(latestVersion, currentVersion);

            return new UpdateInfo
            {
                HasUpdate = hasUpdate,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseUrl = htmlUrl,
                ReleaseNotes = body,
                DownloadSpeedMBps = speedMbps,
                DownloadSource = source,
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

    // =========================================================================
    // GitHub API
    // =========================================================================

    /// <summary>
    /// 获取最新 Release 的 JSON 根元素
    /// </summary>
    private static async Task<JsonElement> FetchLatestReleaseAsync(CancellationToken token)
    {
        var response = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
            token);
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.Clone();
    }

    // =========================================================================
    // 镜像列表自动更新
    // =========================================================================

    /// <summary>
    /// 拉取最新镜像列表 — 从仓库 mirrors.json 获取；失败回退内置默认列表
    /// </summary>
    private static async Task<List<string>> FetchMirrorsAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MirrorsJsonUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return BuiltinMirrorPrefixes.ToList();

            var json = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("mirrors", out var mirrorsProp)
                || mirrorsProp.ValueKind != JsonValueKind.Array)
                return BuiltinMirrorPrefixes.ToList();

            var mirrors = new List<string>();
            foreach (var m in mirrorsProp.EnumerateArray())
            {
                var prefix = m.GetString();
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    // 规范化: 确保以 / 结尾
                    if (!prefix.EndsWith("/", StringComparison.Ordinal))
                        prefix += "/";
                    mirrors.Add(prefix);
                }
            }
            return mirrors.Count > 0 ? mirrors : BuiltinMirrorPrefixes.ToList();
        }
        catch
        {
            return BuiltinMirrorPrefixes.ToList();
        }
    }

    // =========================================================================
    // 速度探测 — 速度优先，选最快镜像
    // =========================================================================

    /// <summary>镜像测速结果</summary>
    private sealed class MirrorSpeedResult
    {
        public string Url { get; init; } = "";
        public string MirrorHost { get; init; } = "";
        public double SpeedMbps { get; init; }
    }

    /// <summary>
    /// 并行测速所有镜像 — 对真实下载 URL 下载前 512KB 计时，返回最快的镜像；
    /// 全部失败返回 null（调用方回退直链）
    /// </summary>
    private static async Task<MirrorSpeedResult?> PickFastestMirrorAsync(
        string downloadUrl, List<string> mirrors, CancellationToken token)
    {
        var candidates = mirrors
            .Select(m => new { Url = m + downloadUrl.Substring("https://".Length), Host = ExtractHost(m) })
            .ToList();

        // 并行测速（每镜像 4s 超时）
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        probeCts.CancelAfter(TimeSpan.FromSeconds(SpeedProbeTimeoutSeconds));

        var tasks = candidates
            .Select(c => MeasureSpeedAsync(c.Url, probeCts.Token)
                .ContinueWith(t => t.Status == TaskStatus.RanToCompletion && t.Result > 0
                    ? new MirrorSpeedResult { Url = c.Url, MirrorHost = c.Host, SpeedMbps = t.Result }
                    : null, TaskContinuationOptions.ExecuteSynchronously))
            .ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // 忽略个别失败
        }

        // 选最快的镜像（>0 表示测速成功）
        var best = tasks
            .Where(t => t.Status == TaskStatus.RanToCompletion && t.Result != null)
            .Select(t => t.Result!)
            .OrderByDescending(r => r.SpeedMbps)
            .FirstOrDefault();

        return best;
    }

    /// <summary>
    /// 测量单个镜像的下载速度 — 普通 GET 读取前 512KB 后立即断开，返回 MB/s；失败返回 0。
    /// 不使用 Range 头：部分镜像不支持 Range（返回全量但被截断），普通 GET 读取固定字节更可靠；
    /// 读取够 512KB 后释放响应流即中止连接，不会真正下载完整安装包。
    /// </summary>
    private static async Task<double> MeasureSpeedAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return 0;

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (total < SpeedProbeBytes)
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read <= 0) break;
                total += read;
            }
            sw.Stop();

            if (total <= 0 || sw.ElapsedMilliseconds <= 0)
                return 0;

            return total / 1024.0 / 1024.0 / (sw.ElapsedMilliseconds / 1000.0);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>从镜像前缀提取主机名（用于展示）</summary>
    private static string ExtractHost(string prefix)
    {
        try
        {
            return new Uri(prefix).Host;
        }
        catch
        {
            return prefix;
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

    /// <summary>测得的下载速度 (MB/s)，直链时为 0</summary>
    public double DownloadSpeedMBps { get; set; }

    /// <summary>下载来源描述（如镜像主机名或 "GitHub 直链"）</summary>
    public string DownloadSource { get; set; } = "";
}
