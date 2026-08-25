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
        /// </summary>
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
    }
}
