// 在线安装器 — 版本检查与加速线路选择
// 逻辑与主程序 UpdateService 一致：GitHub API 查最新 Release → 拉取仓库 mirrors.json
// → 对真实下载 URL 并行测速（Range 512KB）选最快镜像
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.OnlineInstaller
{
    /// <summary>最新版本信息</summary>
    public class ReleaseInfo
    {
        public string Version = "";
        public string DownloadUrl = "";
        public string ReleaseUrl = "";
        public string Notes = "";
        public string BestUrl = "";
        public string Source = "";
        public double SpeedMbps = 0;
    }

    /// <summary>版本检查与镜像择优服务</summary>
    public static class UpdateService
    {
        private static readonly HttpClient Http = CreateClient();

        private const int ApiTimeoutSec = 8;
        private const int ProbeBytes = 1024 * 1024; // 测速读取量（1MB，更精准）

        /// <summary>GitHub API 源（直连失败自动切镜像）</summary>
        private static readonly string[] ApiUrls =
        {
            $"https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
            $"https://ghproxy.net/https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
            $"https://ghfast.top/https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
        };

        /// <summary>镜像列表来源（仓库 mirrors.json，失败用内置兜底）</summary>
        private static readonly string[] MirrorListUrls =
        {
            $"https://raw.githubusercontent.com/{Program.RepoOwner}/{Program.RepoName}/main/mirrors.json",
            $"https://cdn.jsdelivr.net/gh/{Program.RepoOwner}/{Program.RepoName}@main/mirrors.json",
            $"https://ghproxy.net/https://raw.githubusercontent.com/{Program.RepoOwner}/{Program.RepoName}/main/mirrors.json",
        };

        /// <summary>内置兜底镜像（与仓库 mirrors.json 同步，mirrors.json 拉取失败时使用）</summary>
        private static readonly string[] BuiltinMirrors =
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

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(ApiTimeoutSec),
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BaiheOnlineSetup", Program.AppVersion));
            return client;
        }

        /// <summary>获取最新版本信息（含下载 URL）</summary>
        public static async Task<ReleaseInfo> GetLatestAsync()
        {
            foreach (var apiUrl in ApiUrls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ApiTimeoutSec));
                    using var resp = await Http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    var json = await resp.Content.ReadAsStringAsync();
                    var doc = NewtonsoftFallback(json);
                    if (doc == null)
                        continue;

                    var info = new ReleaseInfo();
                    info.Version = GetString(doc, "tag_name").TrimStart('v', 'V');
                    info.ReleaseUrl = GetString(doc, "html_url");
                    info.Notes = GetString(doc, "body");

                    // 找完整安装包 .exe（排除在线安装器自身！release 里 BaiheOnlineSetup 排在前面，
                    // 若不排除会把"在线安装器"当成"完整安装包"下载，导致下载后又是在线下载界面）
                    foreach (var item in GetArray(doc, "assets"))
                    {
                        var asset = item as Dictionary<string, object>;
                        if (asset == null) continue;
                        var name = GetString(asset, "name");
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            && name.IndexOf("OnlineSetup", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("BaiheOnline", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            info.DownloadUrl = GetString(asset, "browser_download_url");
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(info.DownloadUrl))
                        info.DownloadUrl = info.ReleaseUrl;
                    return info;
                }
                catch
                {
                    // 尝试下一个 API 源
                }
            }
            return null;
        }

        /// <summary>
        /// 选择最快下载线路 — 拉取镜像列表，对每个镜像前缀 + 真实 URL 并行测速（Range 512KB）
        /// 全部失败回退 GitHub 直链；返回含 bestUrl/source/speedMbps 的副本
        /// </summary>
        public static async Task<ReleaseInfo> PickFastestAsync(ReleaseInfo info)
        {
            var mirrors = await FetchMirrorsAsync();
            var candidates = new List<string>();
            if (info.DownloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var m in mirrors)
                    candidates.Add(m.TrimEnd('/') + "/" + info.DownloadUrl);
            }
            candidates.Add(info.DownloadUrl); // GitHub 直链兜底

            var bestUrl = info.DownloadUrl;
            var bestSpeed = 0.0;
            var bestSource = "GitHub 直链";

            // 并行测速，取最快（早退：按完成顺序比较）
            var tasks = candidates.Select(async url =>
            {
                var speed = await MeasureSpeedAsync(url);
                return new { url, speed };
            }).ToList();

            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks);
                tasks.Remove(done);
                var r = await done;
                if (r.speed > bestSpeed)
                {
                    bestSpeed = r.speed;
                    bestUrl = r.url;
                    bestSource = ExtractHost(r.url);
                }
            }

            info.BestUrl = bestUrl;
            info.Source = bestSource;
            info.SpeedMbps = bestSpeed;
            return info;
        }

        /// <summary>测速 — Range 请求读前 1MB 计时（部分镜像不支持 Range 则全量读前段）；
        /// 计时从收到第一个数据字节后开始，排除连接/握手耗时，测纯吞吐更精准</summary>
        private static async Task<double> MeasureSpeedAsync(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, ProbeBytes - 1);

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    return 0;

                using var stream = await resp.Content.ReadAsStreamAsync();
                var buffer = new byte[64 * 1024];

                // 先读第一段（跳过连接/握手耗时），再从数据流开始计时
                var first = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (first <= 0)
                    return 0;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                long total = first;
                while (total < ProbeBytes)
                {
                    var n = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, (int)(ProbeBytes - total)), cts.Token);
                    if (n <= 0)
                        break;
                    total += n;
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

        /// <summary>拉取镜像列表 — 多源回退，失败用内置兜底</summary>
        private static async Task<List<string>> FetchMirrorsAsync()
        {
            foreach (var url in MirrorListUrls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var json = await Http.GetStringAsync(url).WithCancellation(cts.Token);
                    var list = ParseMirrors(json);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch { }
            }
            return new List<string>(BuiltinMirrors);
        }

        private static List<string> ParseMirrors(string json)
        {
            try
            {
                var doc = SimpleJson.Parse(json);
                if (doc == null || !doc.ContainsKey("mirrors"))
                    return null;
                var list = new List<string>();
                foreach (var item in doc["mirrors"] as List<object> ?? new List<object>())
                {
                    var s = item as string;
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s);
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        // ===== 极简 JSON 解析（避免引第三方库，保证小体积）=====

        private static Dictionary<string, object> NewtonsoftFallback(string json)
        {
            return SimpleJson.Parse(json);
        }

        private static string GetString(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is string s ? s : "";
        }

        private static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();
        }

        private static string ExtractHost(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch { return "加速线路"; }
        }
    }

    /// <summary>取消支持扩展（net48 无 GetStringAsync(ct)）</summary>
    internal static class HttpExtensions
    {
        public static async Task<string> WithCancellation(this Task<string> task, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<string>();
            using (token.Register(() => tcs.TrySetCanceled()))
            {
                var done = await Task.WhenAny(task, tcs.Task);
                if (done != task)
                    throw new OperationCanceledException(token);
            }
            return await task;
        }
    }
}
