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

    /// <summary>测试通道列表端点（releases 列表含预发布）— 与稳定 API 同源策略回退</summary>
    private static readonly string[] ApiUrls2 =
    {
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=15",
        $"https://ghproxy.net/https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=15",
        $"https://ghfast.top/https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=15",
    };

    /// <summary>内置默认镜像前缀列表（兜底）— 仅在 mirrors.json 拉取失败时使用，与主数据源同步</summary>
    private static readonly string[] BuiltinMirrorPrefixes =
    {
        "https://ghfast.top/",
        "https://ghproxy.net/",
        "https://gh-proxy.com/",
        "https://ghproxy.link/",
        "https://gh.ddlc.top/",
        "https://ghproxy.cn/",
        "https://gh.llkk.cc/",
        "https://ghproxy.cxkpro.top/",
        "https://gh.xxooo.cf/",
        "https://github.limoruirui.com/",
        "https://ghproxy.monkeyray.net/",
        "https://gh.xx9527.cn/",
    };

    /// <summary>测速下载的字节数（1MB — 更大采样窗口，速度更精准）</summary>
    private const int SpeedProbeBytes = 1024 * 1024;

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
    /// 检查是否有新版本 — 缓存优先；force=true 时强制重新检查。
    /// 测试渠道构建(AssemblyMetadata Channel=test)自动改走 CheckTestUpdateAsync(预发布列表)。
    /// </summary>
    /// <param name="force">是否忽略缓存强制检查（设置页手动检查时传 true）</param>
    public static async Task<UpdateInfo> CheckForUpdateAsync(bool force = false)
    {
        if (BuildInfo.IsTestChannel && !string.IsNullOrEmpty(BuildInfo.TagFull))
            return await CheckTestUpdateAsync(force);
        return await CheckStableUpdateAsync(force);
    }

    private static async Task<UpdateInfo> CheckStableUpdateAsync(bool force)
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

            // 下载链接固定使用自建加速服务（8091 浏览器直链，URL 带 token）
            // 说明: 加速 URL 不是 https://github.com/ 开头，下方镜像测速逻辑自动跳过
            string downloadUrl = BuildAcceleratedUrl(latestVersion);
            double speedMbps = 0;
            string source = "自建加速";

            // ---- 增量补丁探测: 在 assets 数组里精确名匹配 BaihePatch_v{当前}_to_{最新}.zip ----
            // (严格相等匹配,规避多 .exe 排序坑;未命中则用户回退完整安装链路)
            string? patchUrl = null;
            long patchBytes = 0;
            var hasUpdateFlag = IsNewerVersion(latestVersion, currentVersion);
            if (hasUpdateFlag && root.TryGetProperty("assets", out var assetsEl)
                && assetsEl.ValueKind == JsonValueKind.Array)
            {
                var expectPatchName = $"BaihePatch_v{currentVersion}_to_{latestVersion}.zip";
                foreach (var a in assetsEl.EnumerateArray())
                {
                    var an = a.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    if (!string.Equals(an, expectPatchName, StringComparison.OrdinalIgnoreCase)) continue;
                    patchBytes = a.TryGetProperty("size", out var sEl) && sEl.ValueKind == JsonValueKind.Number
                        ? sEl.GetInt64() : 0;
                    if (patchBytes > 0) patchUrl = BuildAcceleratedAssetUrl($"releases/download/v{latestVersion}/{expectPatchName}");
                    break;
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
                PatchAvailable = hasUpdate && !string.IsNullOrEmpty(patchUrl),
                PatchUrl = patchUrl,
                PatchSizeBytes = patchBytes,
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
    /// 测量单个镜像的下载速度 — 普通 GET 读取前 1MB 后立即断开，返回 MB/s；失败返回 0。
    /// 不使用 Range 头：部分镜像不支持 Range（返回全量但被截断），普通 GET 读取固定字节更可靠；
    /// 读取够 1MB 后释放响应流即中止连接，不会真正下载完整安装包。
    /// 计时从收到第一个数据字节后开始（排除 DNS/连接/TLS 握手，测纯吞吐更精准）
    /// </summary>
    private static async Task<double> MeasureSpeedAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return 0;

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[64 * 1024];

            // 先读第一段，跳过连接/握手耗时，从数据流开始计时
            var first = await stream.ReadAsync(buffer, token);
            if (first <= 0)
                return 0;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long total = first;
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
                PatchAvailable = cached.PatchAvailable,
                PatchUrl = cached.PatchUrl,
                PatchSizeBytes = cached.PatchSizeBytes,
                PatchCheckedFor = cached.PatchCheckedFor,
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
                PatchAvailable = info.PatchAvailable,
                PatchUrl = info.PatchUrl ?? "",
                PatchSizeBytes = info.PatchSizeBytes,
                PatchCheckedFor = info.PatchCheckedFor ?? "",
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
        public bool PatchAvailable { get; set; }
        public string PatchUrl { get; set; } = "";
        public long PatchSizeBytes { get; set; }
        public string PatchCheckedFor { get; set; } = "";
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

    /// <summary>自建加速服务浏览器直链（8091，URL 带 token，浏览器可直接下载）</summary>
    private const string AcceleratorBrowserBase = "http://199.68.217.4:8091";

    /// <summary>自建加速服务命令行直链（8090，header 鉴权，支持断点续传）</summary>
    private const string AcceleratorCliBase = "http://199.68.217.4:8090";

    /// <summary>
    /// 下载在线安装器（BaiheOnlineSetup，仅 40KB）到临时目录并返回路径。
    /// 主程序"下载更新"用此方法：下载 40KB 在线安装器 → 运行它 → 主程序退出 → 在线安装器接管多线程下载完整包+安装
    /// </summary>
    public static async Task<string?> DownloadOnlineInstallerAsync(string version)
    {
        try
        {
            var token = GetOnlineToken();
            var exeName = $"BaiheOnlineSetup_v{version}.exe";
            var url = $"{AcceleratorCliBase}/github.com/{RepoOwner}/{RepoName}/releases/download/v{version}/{exeName}";
            var destPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), exeName);

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("token", token);

            using var resp = await http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            using (var fs = new System.IO.FileStream(destPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            }
            return destPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构造自建加速服务的完整安装包下载 URL（格式: 加速地址 + github.com/原路径 + ?token=）
    /// </summary>
    private static string BuildAcceleratedUrl(string version)
    {
        var token = GetOnlineToken();
        var exeName = $"BaiheServer_Setup_v{version}.exe";
        var baseUrl = $"{AcceleratorBrowserBase}/github.com/{RepoOwner}/{RepoName}/releases/download/v{version}/{exeName}";
        return string.IsNullOrEmpty(token) ? baseUrl : baseUrl + "?token=" + Uri.EscapeDataString(token);
    }

    /// <summary>
    /// 构造自建加速服务的任意 release 资产 URL（格式: 加速地址 + github.com/<owner>/<repo>/ + 相对路径）。
    /// 调用方(PatchService)自行附加 token header;浏览器场景请用 BuildAcceleratedUrl(?token=)。
    /// </summary>
    private static string BuildAcceleratedAssetUrl(string relativePath)
        => $"{AcceleratorCliBase}/github.com/{RepoOwner}/{RepoName}/{relativePath}";

    // =========================================================================
    // 测试通道 — 仅 ChannelOverride=test 构建使用(测试机自升级入口)
    // 从 /releases 预发布列表挑同族 vX.Y.Z-testN 最新版;/releases/latest 看不到预发布,
    // 普通玩家构建不含该元数据,永远走 CheckStableUpdateAsync,行为不受影响。
    // =========================================================================

    /// <summary>test tag → 数值 Version("1.1.26-test12" → 1.1.26.12;"1.1.26" → 1.1.26.0)</summary>
    private static bool TryParseTestVersion(string tagNoV, out Version ver)
    {
        ver = new Version(0, 0);
        var basePart = tagNoV;
        var seq = "0";
        var idx = tagNoV.IndexOf("-test", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            basePart = tagNoV.Substring(0, idx);
            seq = tagNoV.Substring(idx + 5).TrimStart('.');
            if (seq.Length == 0) seq = "0";
        }
        return Version.TryParse(basePart + "." + seq, out ver);
    }

    private static async Task<JsonElement?> FetchStableAndTestTargetsAsync()
    {
        // /releases?per_page=15 — 多源回退与 APIUrls 同源(把 /latest 替换为列表路径)
        foreach (var apiUrl in ApiUrls2)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ApiTimeoutMs));
                using var response = await _httpClient.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    continue;
                return doc.RootElement.Clone();
            }
            catch { /* 下一个源 */ }
        }
        return null;
    }

    private static async Task<UpdateInfo> CheckTestUpdateAsync(bool force)
    {
        var selfTag = BuildInfo.TagFull!;                      // 如 "1.1.26-test1"
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? selfTag;

        if (!force)
        {
            var cached = LoadCache();
            if (cached != null && cached.CurrentVersion == currentVersion && cached.PatchCheckedFor == selfTag)
                return cached;
        }

        try
        {
            var listRoot = await FetchStableAndTestTargetsAsync()
                ?? throw new HttpRequestException("releases 列表不可达");
            var idx = selfTag.IndexOf("-test", StringComparison.OrdinalIgnoreCase);
            var basePart = idx > 0 ? selfTag.Substring(0, idx) : selfTag;
            var familyPrefix = basePart + "-test";

            string? targetTag = null;
            JsonElement targetEl = default;
            bool foundSelf = false;

            foreach (var rel in listRoot.EnumerateArray())
            {
                var tagRaw = rel.TryGetProperty("tag_name", out var tEl) ? tEl.GetString() ?? "" : "";
                var tagNoV = tagRaw.TrimStart('v', 'V');
                var isPrerelease = rel.TryGetProperty("prerelease", out var pEl) && pEl.ValueKind == JsonValueKind.True;
                if (!isPrerelease || !tagNoV.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(tagNoV, selfTag, StringComparison.OrdinalIgnoreCase)) { foundSelf = true; break; }
                if (!TryParseTestVersion(tagNoV, out var tv) || !TryParseTestVersion(selfTag, out var sv)) continue;
                if (tv <= sv) continue;
                if (targetTag == null)
                {
                    targetTag = tagNoV;
                    targetEl = rel.Clone();
                }
                break; // 列表倒序: 第一个更高的同族预发布即最新目标
            }

            if (targetTag == null)
            {
                // 已是最新测试版(或没有更高版本): 返回无更新语义但保留通道信息
                return new UpdateInfo
                {
                    HasUpdate = false,
                    CurrentVersion = currentVersion,
                    LatestVersion = foundSelf ? selfTag : currentVersion,
                    DownloadSource = "测试通道",
                    PatchAvailable = false,
                };
            }

            // assets 精确匹配补丁与完整包
            string? patchUrl = null; long patchBytes = 0;
            if (targetEl.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                var expectPatchName = $"BaihePatch_v{selfTag}_to_{targetTag}.zip";
                foreach (var a in assetsEl.EnumerateArray())
                {
                    var an = a.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    if (!string.Equals(an, expectPatchName, StringComparison.OrdinalIgnoreCase)) continue;
                    patchBytes = a.TryGetProperty("size", out var sEl) && sEl.ValueKind == JsonValueKind.Number ? sEl.GetInt64() : 0;
                    if (patchBytes > 0) patchUrl = BuildAcceleratedAssetUrl($"releases/download/v{targetTag}/{expectPatchName}");
                    break;
                }
            }

            var body = targetEl.TryGetProperty("body", out var bEl) ? bEl.GetString() ?? "" : "";
            var info = new UpdateInfo
            {
                HasUpdate = true,
                CurrentVersion = currentVersion,
                LatestVersion = targetTag,
                DownloadUrl = BuildAcceleratedUrl(targetTag),
                ReleaseUrl = "",
                ReleaseNotes = body,
                DownloadSource = "测试通道",
                PatchAvailable = !string.IsNullOrEmpty(patchUrl),
                PatchUrl = patchUrl,
                PatchSizeBytes = patchBytes,
                PatchCheckedFor = selfTag,
            };
            SaveCache(info);
            return info;
        }
        catch
        {
            var stale = LoadCache(allowStale: true);
            if (stale != null && stale.PatchCheckedFor == selfTag) return stale;
            return new UpdateInfo
            {
                HasUpdate = false,
                CurrentVersion = currentVersion,
                LatestVersion = currentVersion,
                DownloadSource = "测试通道",
            };
        }
    }

    /// <summary>
    /// 读取自建加速服务 token — 优先编译注入的 AssemblyMetadata（release.yml -p:OnlineToken），
    /// 其次环境变量 BAIHE_ONLINE_TOKEN（本地调试）；源码不含令牌
    /// 注意: 去除可能混入的 BOM(U+FEFF) 和首尾空白 — secret 设置/编译注入时可能带入 BOM 导致鉴权 401
    /// </summary>
    public static string GetOnlineToken()
    {
        var env = Environment.GetEnvironmentVariable("BAIHE_ONLINE_TOKEN");
        if (!string.IsNullOrEmpty(env))
            return CleanToken(env);
        try
        {
            foreach (var attr in System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
            {
                var m = (System.Reflection.AssemblyMetadataAttribute)attr;
                if (m.Key == "OnlineToken" && !string.IsNullOrEmpty(m.Value))
                    return CleanToken(m.Value);
            }
        }
        catch { }
        return "";
    }

    /// <summary>去除 BOM(U+FEFF) 和首尾空白 — 防止 secret/编译注入时混入不可见字符导致鉴权 401</summary>
    private static string CleanToken(string raw)
    {
        return raw.Trim().TrimStart('\uFEFF', '\uFFFE').Trim();
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

    /// <summary>增量补丁是否可用（对应 BaihePatch_v{当前}_to_{最新}.zip 资产已发布）</summary>
    public bool PatchAvailable { get; set; }

    /// <summary>补丁下载 URL（走 8090 加速服务，PatchService 请求时附加 token header）</summary>
    public string? PatchUrl { get; set; }

    /// <summary>补丁包字节数（前端展示「约 X MB」）</summary>
    public long PatchSizeBytes { get; set; }

    /// <summary>测试通道缓存键：本次检查针对的自身 tag（测试构建专用；正式构建为 null）</summary>
    public string? PatchCheckedFor { get; set; }
}
