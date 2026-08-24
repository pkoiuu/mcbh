// 更新检查服务 — 检查 GitHub Releases 最新版本，支持国内镜像加速下载
// 特性:
//  1. 结果缓存 — cache/update_check.json，TTL 1 小时；无参调用直接返回缓存（秒回），
//     传 force=true 强制重新检查（设置页「检查更新」按钮）
//  2. 镜像列表自动更新 — 每次完整检查时从仓库 mirrors.json 拉取最新加速站列表
//     (raw.githubusercontent.com，3s 超时)，失败回退内置默认列表，无需发版即可更新镜像
//  3. 速度优先 — 对真实下载 URL 并行测速（普通 GET 读前 512KB 计时），
//     一旦有镜像测速 > 3MB/s 立即返回（早退），最多等 3s 取最快；全部失败回退 GitHub 直链
//  4. 耗时控制 — GitHub API 直连 3s 超时；总预算典型 1-3s，最坏 ~5s

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

    /// <summary>内置默认镜像前缀列表（兜底）— 仅在 mirrors.json 拉取失败时使用</summary>
    private static readonly string[] BuiltinMirrorPrefixes =
    {
        "https://ghfast.top/",
        "https://ghproxy.net/",
        "https://gh-proxy.com/",
        "https://ghproxy.link/",
    };

    /// <summary>测速下载的字节数（512KB）</summary>
    private const int SpeedProbeBytes = 512 * 1024;

    /// <summary>测速早退阈值（MB/s）— 有镜像超过此速度立即返回</summary>
    private const double EarlyExitSpeedMbps = 3.0;

    /// <summary>测速总等待上限（毫秒）</summary>
    private const int SpeedProbeWaitMs = 3000;

    /// <summary>测速并发探测的镜像数上限</summary>
    private const int MaxProbeMirrors = 12;

    /// <summary>GitHub API 单次超时（毫秒）— 直连国内常 >3s，放宽到 5s 避免误判；缓存后无感</summary>
    private const int ApiTimeoutMs = 5000;

    /// <summary>mirrors.json 拉取超时（毫秒）</summary>
    private const int MirrorsFetchTimeoutMs = 3000;

    /// <summary>缓存文件路径</summary>
    private static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "cache", "update_check.json");

    /// <summary>缓存有效期</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    /// <summary>测速结果缓存 — 按真实下载 URL 缓存，避免频繁重测镜像</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SpeedProbeCache> SpeedCache = new();

    /// <summary>测速结果缓存有效期（10 分钟）</summary>
    private static readonly TimeSpan SpeedCacheTtl = TimeSpan.FromMinutes(10);

    private sealed class SpeedProbeCache
    {
        public DateTime TimestampUtc;
        public MirrorSpeedResult? Best;
    }

    static UpdateService()
    {
        // GitHub API 要求设置 User-Agent
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BaiheLauncher", "1.0"));
    }

    /// <summary>
    /// 检查是否有新版本 — 缓存优先；force=true 时强制重新检查
    /// </summary>
    /// <param name="force">是否忽略缓存强制检查（设置页手动检查时传 true）</param>
    public static async Task<UpdateInfo> CheckForUpdateAsync(bool force = false)
    {
        var currentVersion = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "1.0.0";

        // 缓存优先 — 缓存新鲜且非强制时直接返回
        // 注意: 必须校验缓存的 CurrentVersion 与当前程序集版本一致，
        //       否则升级后 1h 内启动会命中旧缓存（旧版本时查到的 hasUpdate=true），错误显示更新横幅
        if (!force)
        {
            var cached = LoadCache();
            if (cached != null && cached.CurrentVersion == currentVersion)
                return cached;
        }

        try
        {
            // 并行: 查最新 Release（3s 超时）+ 拉取最新镜像列表（3s 超时）
            var releaseTask = FetchLatestReleaseAsync();
            var mirrorsTask = FetchMirrorsAsync();

            var releaseJson = await releaseTask;
            var mirrors = await mirrorsTask;

            var root = releaseJson;

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

            // 速度优先 — 对真实下载 URL 并行测速（早退），选最快镜像；结果短缓存 10 分钟
            double speedMbps = 0;
            string source = "GitHub 直链";
            if (downloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                var best = await PickFastestMirrorCachedAsync(downloadUrl, mirrors);
                if (best != null)
                {
                    downloadUrl = best.Url;
                    speedMbps = best.SpeedMbps;
                    source = best.MirrorHost;
                }
            }

            var hasUpdate = IsNewerVersion(latestVersion, currentVersion);

            var info = new UpdateInfo
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

            // 写缓存（成功拿到最新版本信息才缓存；网络失败不缓存）
            SaveCache(info);
            return info;
        }
        catch
        {
            // 网络错误或 API 不可用时 — 优先返回缓存（允许过期，避免误报"无更新"），否则静默返回无更新
            var stale = LoadCache(allowStale: true);
            if (stale != null)
                return stale;

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
    /// 获取最新 Release 的 JSON 根元素 — 3s 超时，失败抛出（由调用方兜底）
    /// </summary>
    private static async Task<JsonElement> FetchLatestReleaseAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ApiTimeoutMs));
        var response = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
            cts.Token);
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.Clone();
    }

    // =========================================================================
    // 镜像列表自动更新
    // =========================================================================

    /// <summary>
    /// 拉取最新镜像列表 — 从仓库 mirrors.json 获取（3s 超时）；失败回退内置默认列表
    /// </summary>
    private static async Task<List<string>> FetchMirrorsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(MirrorsFetchTimeoutMs));
            using var request = new HttpRequestMessage(HttpMethod.Get, MirrorsJsonUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return BuiltinMirrorPrefixes.ToList();

            var json = await response.Content.ReadAsStringAsync(cts.Token);
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
    // 速度探测 — 速度优先 + 早退
    // =========================================================================

    /// <summary>镜像测速结果</summary>
    private sealed class MirrorSpeedResult
    {
        public string Url { get; init; } = "";
        public string MirrorHost { get; init; } = "";
        public double SpeedMbps { get; init; }
    }

    /// <summary>
    /// 测速选择最快镜像（带缓存）— 10 分钟内同一 URL 的测速结果直接复用，
    /// 避免每次检查更新（含 force）都重新下载 512KB 探测镜像
    /// </summary>
    private static async Task<MirrorSpeedResult?> PickFastestMirrorCachedAsync(string downloadUrl, List<string> mirrors)
    {
        // 缓存命中且新鲜 → 直接复用
        if (SpeedCache.TryGetValue(downloadUrl, out var cached)
            && DateTime.UtcNow - cached.TimestampUtc < SpeedCacheTtl
            && cached.Best != null)
        {
            return cached.Best;
        }

        var best = await PickFastestMirrorAsync(downloadUrl, mirrors);
        if (best != null)
        {
            SpeedCache[downloadUrl] = new SpeedProbeCache
            {
                TimestampUtc = DateTime.UtcNow,
                Best = best,
            };
        }
        return best;
    }

    /// <summary>
    /// 并行测速所有镜像 — 对真实下载 URL 下载前 512KB 计时。
    /// 早退策略: 一旦有镜像测速 > 3MB/s 立即返回该镜像；否则最多等 3s 取最快。
    /// 全部失败返回 null（调用方回退直链）
    /// </summary>
    private static async Task<MirrorSpeedResult?> PickFastestMirrorAsync(string downloadUrl, List<string> mirrors)
    {
        // 限制并发探测数量，避免过多无效流量
        var probeMirrors = mirrors.Take(MaxProbeMirrors).ToList();

        var candidates = probeMirrors
            .Select(m => new { Url = m + downloadUrl.Substring("https://".Length), Host = ExtractHost(m) })
            .ToList();

        using var probeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(SpeedProbeWaitMs));

        var tasks = candidates
            .Select(c => MeasureSpeedAsync(c.Url, probeCts.Token)
                .ContinueWith(t => t.Status == TaskStatus.RanToCompletion && t.Result > 0
                    ? new MirrorSpeedResult { Url = c.Url, MirrorHost = c.Host, SpeedMbps = t.Result }
                    : null, TaskContinuationOptions.ExecuteSynchronously))
            .ToArray();

        // 轮询: 每个任务完成时检查是否达到早退阈值
        var remaining = new HashSet<Task<MirrorSpeedResult?>>(tasks);
        MirrorSpeedResult? best = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(SpeedProbeWaitMs);

        while (remaining.Count > 0)
        {
            var done = await Task.WhenAny(remaining);
            remaining.Remove(done);

            if (done.Status == TaskStatus.RanToCompletion && done.Result != null)
            {
                var r = done.Result;
                if (best == null || r.SpeedMbps > best.SpeedMbps)
                    best = r;
                // 早退: 达到速度阈值立即返回
                if (r.SpeedMbps >= EarlyExitSpeedMbps)
                    return r;
            }

            if (remaining.Count == 0 || DateTime.UtcNow >= deadline)
                break;
        }

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

    // =========================================================================
    // 结果缓存
    // =========================================================================

    /// <summary>
    /// 读取缓存 — allowStale=false 时仅返回 TTL 内的新鲜缓存；allowStale=true 时过期缓存也返回
    /// </summary>
    private static UpdateInfo? LoadCache(bool allowStale = false)
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            var json = File.ReadAllText(CachePath);
            var cached = JsonSerializer.Deserialize<CachedUpdateInfo>(json, JsonOptions);
            if (cached == null || string.IsNullOrEmpty(cached.LatestVersion))
                return null;

            // 检查新鲜度
            var age = DateTime.UtcNow - cached.CheckedAtUtc;
            if (!allowStale && age > CacheTtl)
                return null;

            return new UpdateInfo
            {
                HasUpdate = cached.HasUpdate,
                CurrentVersion = cached.CurrentVersion,
                LatestVersion = cached.LatestVersion,
                DownloadUrl = cached.DownloadUrl,
                ReleaseUrl = cached.ReleaseUrl,
                ReleaseNotes = cached.ReleaseNotes,
                DownloadSpeedMBps = cached.DownloadSpeedMBps,
                DownloadSource = cached.DownloadSource,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>写入缓存</summary>
    private static void SaveCache(UpdateInfo info)
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (dir != null) Directory.CreateDirectory(dir);

            var cached = new CachedUpdateInfo
            {
                CheckedAtUtc = DateTime.UtcNow,
                HasUpdate = info.HasUpdate,
                CurrentVersion = info.CurrentVersion,
                LatestVersion = info.LatestVersion,
                DownloadUrl = info.DownloadUrl,
                ReleaseUrl = info.ReleaseUrl,
                ReleaseNotes = info.ReleaseNotes,
                DownloadSpeedMBps = info.DownloadSpeedMBps,
                DownloadSource = info.DownloadSource,
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cached, JsonOptions));
        }
        catch
        {
            // 缓存写入失败不影响功能
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>缓存模型</summary>
    private sealed class CachedUpdateInfo
    {
        public DateTime CheckedAtUtc { get; set; }
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public double DownloadSpeedMBps { get; set; }
        public string DownloadSource { get; set; } = "";
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
