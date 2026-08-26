// 在线安装器 — 版本检查与固定加速服务下载
// 版本检查：GitHub API 查最新 Release（多源回退，仅用于获取版本号）
// 下载：固定使用自建加速服务（199.68.217.4:8090，HTTP header 鉴权），不做镜像测速
using System;
using System.Collections.Generic;
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
        /// <summary>固定加速服务的下载 URL</summary>
        public string BestUrl = "";
        public string Source = "自建加速";
        public double SpeedMbps = 0;
        /// <summary>候选下载 URL（固定加速服务单条）</summary>
        public string[] Candidates = new string[0];
    }

    /// <summary>版本检查与下载地址构造服务</summary>
    public static class UpdateService
    {
        private static readonly HttpClient Http = CreateClient();

        private const int ApiTimeoutSec = 8;

        /// <summary>加速服务地址（8090：HTTP header 鉴权，支持断点续传）</summary>
        private const string AcceleratorHost = "http://199.68.217.4:8090";

        /// <summary>鉴权 header 名（自建服务约定）</summary>
        private const string TokenHeader = "token";

        /// <summary>GitHub API 源（直连失败自动切镜像，仅用于获取版本号）</summary>
        private static readonly string[] ApiUrls =
        {
            $"https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
            $"https://ghproxy.net/https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
            $"https://ghfast.top/https://api.github.com/repos/{Program.RepoOwner}/{Program.RepoName}/releases/latest",
        };

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false, // 禁用系统代理，直连加速服务器（避免用户系统代理导致连接失败）
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(ApiTimeoutSec),
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BaiheOnlineSetup", Program.AppVersion));
            return client;
        }

        /// <summary>
        /// 获取最新版本信息（仅查版本号与 Release 信息，不测速）
        /// 测试构建走 GetLatestTestAsync 通道，正式构建查 /releases/latest（天然排除预发布）
        /// </summary>
        public static async Task<ReleaseInfo> GetLatestAsync()
        {
            if (Program.IsTestBuild)
                return await GetLatestTestAsync();

            foreach (var apiUrl in ApiUrls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ApiTimeoutSec));
                    using var resp = await Http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    var json = await resp.Content.ReadAsStringAsync();
                    var doc = SimpleJson.Parse(json);
                    if (doc == null)
                        continue;

                    var info = new ReleaseInfo();
                    info.Version = GetString(doc, "tag_name").TrimStart('v', 'V');
                    info.ReleaseUrl = GetString(doc, "html_url");
                    info.Notes = GetString(doc, "body");

                    // 固定加速服务下载地址（完整安装包）
                    // 格式: 加速地址 + 原链接去掉 https:// → .../github.com/pkoiuu/mcbh/releases/download/...
                    var exeName = $"BaiheServer_Setup_v{info.Version}.exe";
                    info.BestUrl = $"{AcceleratorHost}/github.com/{Program.RepoOwner}/{Program.RepoName}/releases/download/v{info.Version}/{exeName}";
                    info.Candidates = new[] { info.BestUrl };
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
        /// 获取鉴权 token（编译注入的 AssemblyMetadata 或环境变量 BAIHE_ONLINE_TOKEN）
        /// 注意: 去除可能混入的 BOM(U+FEFF) 和首尾空白 — secret 设置/编译注入时可能带入 BOM 导致鉴权失败
        /// </summary>
        public static string GetToken()
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

        private static string GetString(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is string s ? s : "";
        }

        // =========================================================================
        // 测试通道 — 仅 IsTestBuild=true 的构建使用
        // 从 /releases 列表里挑与本构建同基础版本的最新 -test 预发布，
        // 使测试在线安装器能拉到同族的测试完整包（/releases/latest 看不到预发布）
        // =========================================================================

        /// <summary>测试通道版本获取：返回同族最新测试预发布的下载信息；无匹配时返回 null</summary>
        private static async Task<ReleaseInfo> GetLatestTestAsync()
        {
            // 自身基础版本："1.1.26-test3" → "1.1.26"（家族前缀 v1.1.26-test*）
            var selfVer = Program.AppVersion;
            var dashIdx = selfVer.IndexOf('-');
            var basePart = dashIdx > 0 ? selfVer.Substring(0, dashIdx) : selfVer;
            var familyPrefix = "v" + basePart + "-test";

            string listJson = null;
            foreach (var apiUrl in ApiUrls)
            {
                try
                {
                    // /releases/latest → /releases?per_page=15（镜像源同样适用列表路径）
                    var listUrl = apiUrl.EndsWith("/releases/latest", StringComparison.Ordinal)
                        ? apiUrl.Substring(0, apiUrl.Length - "/latest".Length) + "?per_page=15"
                        : apiUrl;
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ApiTimeoutSec));
                    using var resp = await Http.GetAsync(listUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!resp.IsSuccessStatusCode)
                        continue;
                    listJson = await resp.Content.ReadAsStringAsync();
                    if (SimpleJson.ParseArray(listJson) is { Count: > 0 })
                        break;
                    listJson = null;
                }
                catch
                {
                    // 尝试下一个 API 源
                }
            }
            if (listJson == null)
                return null;

            var arr = SimpleJson.ParseArray(listJson);
            // 列表按创建时间倒序——取第一个同族且标记为预发布的条目
            foreach (var itemObj in arr)
            {
                if (itemObj is not Dictionary<string, object> item)
                    continue;
                var tagRaw = GetString(item, "tag_name");
                var tagNoV = tagRaw.TrimStart('v', 'V');
                if (string.IsNullOrEmpty(tagNoV))
                    continue;
                var isPrerelease = item.TryGetValue("prerelease", out var pv) && pv is bool pb && pb;
                if (!isPrerelease || !tagNoV.StartsWith(basePart + "-test", StringComparison.OrdinalIgnoreCase))
                    continue;

                var exeName = $"BaiheServer_Setup_v{tagNoV}.exe";
                return new ReleaseInfo
                {
                    Version = tagNoV,
                    ReleaseUrl = GetString(item, "html_url"),
                    Notes = GetString(item, "body"),
                    BestUrl = $"{AcceleratorHost}/github.com/{Program.RepoOwner}/{Program.RepoName}/releases/download/v{tagNoV}/{exeName}",
                    Candidates = new[] { $"{AcceleratorHost}/github.com/{Program.RepoOwner}/{Program.RepoName}/releases/download/v{tagNoV}/{exeName}" },
                    Source = "自建加速·测试通道",
                };
            }

            // 没有同族测试预发布 —— 让上层明确报「无法获取」而不是静默拉错稳定包
            return null;
        }
    }
}
