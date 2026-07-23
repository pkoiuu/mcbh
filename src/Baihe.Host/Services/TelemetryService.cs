// 遥测服务 — 收集客户端环境数据并上报到自建 API
// 遵循 telemetry-api-guidelines.md 规范：异步、静默、单例 HttpClient
// 上报策略：每会话仅首次上报，首次上报前请求服务端策略决定是否允许

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 遥测服务 — 收集客户端环境数据并上报到自建 API
/// </summary>
public static class TelemetryService
{
    /// <summary>API 服务地址（不带尾部斜杠）</summary>
    private const string ServerUrl = "https://bh-telemetry.hhj520.top";

    /// <summary>API Key — 编译时嵌入，禁止写入配置文件</summary>
    private const string ApiKey = "58180655c0a4bb076c31f18a7f0a9d6c";

    /// <summary>单例 HttpClient，5 秒超时</summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>JSON 序列化选项（驼峰命名）</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>会话级标志 — 每会话仅首次上报，首次后不再重复</summary>
    private static bool _hasReportedThisSession = false;

    /// <summary>服务端策略缓存（null 表示未获取到，按 fail-open 处理）</summary>
    private static TelemetryPolicy? _cachedPolicy;

    /// <summary>是否已尝试获取策略（避免同一会话反复请求）</summary>
    private static bool _policyFetched = false;

    /// <summary>
    /// 上报玩家数据 — 每会话仅首次上报。
    /// 首次上报前请求服务端策略，策略明确禁止时跳过。
    /// 策略不可达时按 fail-open 处理（允许上报）。
    /// 失败时静默处理，不影响用户体验。
    /// </summary>
    /// <param name="uuid">玩家 UUID</param>
    /// <param name="username">玩家用户名</param>
    /// <param name="email">用户邮箱（微软正版和第三方验证登录时有值，离线模式为 null）</param>
    public static async Task ReportAsync(string uuid, string username, string? email = null)
    {
        if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(username))
            return;

        // 会话级去重 — 每会话仅首次上报
        if (_hasReportedThisSession)
            return;

        // 首次上报前获取服务端策略
        if (!_policyFetched)
        {
            _cachedPolicy = await GetTelemetryPolicyAsync();
            _policyFetched = true;
        }

        // 策略明确禁止上报时跳过（null = 策略不可达，按 fail-open 允许）
        if (_cachedPolicy is { Enabled: false })
        {
            _hasReportedThisSession = true;
            return;
        }

        try
        {
            var (modCount, modList) = GetModInfo();

            var payload = new
            {
                uuid,
                username,
                email = email ?? string.Empty,
                launcherVersion = GetLauncherVersion(),
                os = GetOsInfo(),
                language = CultureInfo.CurrentUICulture.Name,
                modCount,
                modList
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.Add("X-Api-Key", ApiKey);

            await _httpClient.PostAsync($"{ServerUrl}/api/track/report", content);
            _hasReportedThisSession = true;
        }
        catch (Exception ex)
        {
            // 记录失败原因便于排查，但不影响用户体验
            System.Diagnostics.Debug.WriteLine($"[Telemetry] 上报失败: {ex.Message}");
            // 即使失败也标记为已尝试，避免同一会话反复重试
            _hasReportedThisSession = true;
        }
    }

    /// <summary>
    /// 获取服务端遥测策略 — 动态控制是否允许上报
    /// 请求 GET /api/track/policy，返回 { "enabled": true/false }
    /// </summary>
    /// <returns>策略对象；不可达时返回 null（fail-open）</returns>
    private static async Task<TelemetryPolicy?> GetTelemetryPolicyAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerUrl}/api/track/policy");
            request.Headers.Add("X-Api-Key", ApiKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Telemetry] 策略接口返回 HTTP {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TelemetryPolicy>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Telemetry] 获取策略失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取启动器版本号（三段格式：主.次.修）
    /// </summary>
    private static string GetLauncherVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVer = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVer != null && Version.TryParse(fileVer.Version, out var parsed))
                return parsed.ToString(3);

            var asmVer = assembly.GetName().Version;
            return asmVer != null ? asmVer.ToString(3) : "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    /// <summary>
    /// 获取操作系统信息
    /// </summary>
    private static string GetOsInfo()
    {
        try
        {
            var osDesc = RuntimeInformation.OSDescription;
            var arch = RuntimeInformation.OSArchitecture.ToString();
            return $"{osDesc} ({arch})";
        }
        catch
        {
            return Environment.OSVersion.ToString();
        }
    }

    /// <summary>
    /// 扫描 .minecraft/mods/ 目录，获取模组列表和数量
    /// </summary>
    private static (int count, List<string> list) GetModInfo()
    {
        try
        {
            var mcDir = InstanceService.GetMcDirectory();
            var modsDir = Path.Combine(mcDir, "mods");

            if (!Directory.Exists(modsDir))
                return (0, new List<string>());

            var jarFiles = Directory.GetFiles(modsDir, "*.jar");
            var modNames = new List<string>();

            foreach (var jar in jarFiles)
            {
                var name = Path.GetFileName(jar);
                if (!name.Contains(".."))
                {
                    modNames.Add(name);
                }

                if (modNames.Count >= 200)
                    break;
            }

            return (modNames.Count, modNames);
        }
        catch
        {
            return (0, new List<string>());
        }
    }

    /// <summary>遥测策略模型 — 服务端下发</summary>
    private sealed class TelemetryPolicy
    {
        /// <summary>是否允许遥测上报</summary>
        public bool Enabled { get; set; } = true;
    }
}
