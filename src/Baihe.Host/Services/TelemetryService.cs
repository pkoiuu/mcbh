// 遥测服务 — 收集客户端环境数据并上报到自建 API
// 遵循 telemetry-api-guidelines.md 规范：异步、静默、单例 HttpClient
// 上报时机：启动器启动、用户登录、游戏启动前

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

    /// <summary>
    /// 上报玩家数据 — 收集环境信息并发送到 API
    /// 失败时静默处理，不影响用户体验
    /// </summary>
    /// <param name="uuid">玩家 UUID</param>
    /// <param name="username">玩家用户名</param>
    public static async Task ReportAsync(string uuid, string username)
    {
        if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(username))
            return;

        try
        {
            var (modCount, modList) = GetModInfo();

            var payload = new
            {
                uuid,
                username,
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
        }
        catch
        {
            // 静默处理，遥测不应影响用户体验
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
            // 使用 RuntimeInformation 获取更详细的 OS 信息
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
                // 路径穿越检查
                if (!name.Contains(".."))
                {
                    modNames.Add(name);
                }

                // 限制最大 200 个
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
}
