// 设置服务 — 启动器用户设置持久化
// 保存到 settings.json，支持内存分配、窗口尺寸、全屏等配置

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 启动器设置模型
/// </summary>
public class LauncherSettings
{
    /// <summary>分配给游戏的内存 (MB)</summary>
    public int MemoryMB { get; set; } = 4096;

    /// <summary>游戏窗口宽度</summary>
    public int WindowWidth { get; set; } = 1280;

    /// <summary>游戏窗口高度</summary>
    public int WindowHeight { get; set; } = 720;

    /// <summary>启动后自动全屏</summary>
    public bool AutoFullscreen { get; set; } = false;

    /// <summary>Java 路径覆盖 (为空则自动检测)</summary>
    public string? JavaPathOverride { get; set; }

    /// <summary>启动游戏后关闭启动器</summary>
    public bool CloseAfterLaunch { get; set; } = false;

    /// <summary>服务器地址</summary>
    public string ServerAddress { get; set; } = "play.simpfun.cn";

    /// <summary>服务器端口</summary>
    public int ServerPort { get; set; } = 28230;

    /// <summary>QuickPlay 自动连接</summary>
    public bool QuickPlayEnabled { get; set; } = true;
}

/// <summary>
/// 设置服务 — 加载和保存用户设置
/// </summary>
public static class SettingsService
{
    /// <summary>设置文件路径</summary>
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>JSON 序列化选项</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>当前设置 (内存缓存)</summary>
    private static LauncherSettings? _cached;

    // ===== 系统内存检测 (P/Invoke: GlobalMemoryStatusEx) =====

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>
    /// 获取系统总物理内存 (MB)
    /// </summary>
    public static int GetTotalPhysicalMemoryMB()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return (int)(memStatus.ullTotalPhys / (1024UL * 1024UL));
            }
            return 8192; // 检测失败时回退到 8GB
        }
        catch
        {
            return 8192; // 检测失败时回退到 8GB
        }
    }

    /// <summary>
    /// 根据系统总内存计算推荐分配给 Minecraft 的内存 (MB)
    /// 算法: max(2GB, min(总内存×50%, 总内存-4GB, 16GB))
    /// </summary>
    public static int CalculateRecommendedMemory(int totalMB)
    {
        var half = (int)(totalMB * 0.5);
        var reserved = totalMB - 4096;
        var recommended = Math.Min(half, Math.Min(reserved, 16384));
        return Math.Max(recommended, 2048);
    }

    /// <summary>
    /// 加载设置 — 优先使用缓存，不存在则创建默认设置
    /// </summary>
    public static Task<LauncherSettings> GetAsync()
    {
        if (_cached != null)
            return Task.FromResult(_cached);

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _cached = JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions) ?? new LauncherSettings();
            }
            else
            {
                _cached = new LauncherSettings
                {
                    MemoryMB = CalculateRecommendedMemory(GetTotalPhysicalMemoryMB())
                };
                SaveAsync(_cached).Wait();
            }
        }
        catch
        {
            _cached = new LauncherSettings();
        }

        return Task.FromResult(_cached);
    }

    /// <summary>
    /// 保存设置 — 接收 JSON 参数并更新对应字段
    /// </summary>
    public static async Task<LauncherSettings> SetAsync(JsonElement args)
    {
        var settings = await GetAsync();

        if (args.ValueKind != JsonValueKind.Object)
            return settings;

        // 逐字段更新 (只更新提供的字段)
        if (args.TryGetProperty("memoryMB", out var mem) && mem.TryGetInt32(out var memVal))
            settings.MemoryMB = Math.Max(512, Math.Min(memVal, 32768));

        if (args.TryGetProperty("windowWidth", out var ww) && ww.TryGetInt32(out var wwVal))
            settings.WindowWidth = Math.Max(640, wwVal);

        if (args.TryGetProperty("windowHeight", out var wh) && wh.TryGetInt32(out var whVal))
            settings.WindowHeight = Math.Max(480, whVal);

        if (args.TryGetProperty("autoFullscreen", out var af) && af.ValueKind == JsonValueKind.False)
            settings.AutoFullscreen = false;
        else if (af.ValueKind == JsonValueKind.True)
            settings.AutoFullscreen = true;

        if (args.TryGetProperty("javaPathOverride", out var jp) && jp.ValueKind == JsonValueKind.String)
            settings.JavaPathOverride = string.IsNullOrEmpty(jp.GetString()) ? null : jp.GetString();

        if (args.TryGetProperty("closeAfterLaunch", out var cl) && cl.ValueKind == JsonValueKind.False)
            settings.CloseAfterLaunch = false;
        else if (cl.ValueKind == JsonValueKind.True)
            settings.CloseAfterLaunch = true;

        if (args.TryGetProperty("quickPlayEnabled", out var qp) && qp.ValueKind == JsonValueKind.False)
            settings.QuickPlayEnabled = false;
        else if (qp.ValueKind == JsonValueKind.True)
            settings.QuickPlayEnabled = true;

        if (args.TryGetProperty("serverAddress", out var sa) && sa.ValueKind == JsonValueKind.String)
            settings.ServerAddress = sa.GetString() ?? "play.simpfun.cn";

        if (args.TryGetProperty("serverPort", out var sp) && sp.TryGetInt32(out var spVal))
            settings.ServerPort = Math.Max(1, Math.Min(spVal, 65535));

        await SaveAsync(settings);
        return settings;
    }

    /// <summary>
    /// 保存设置到文件
    /// </summary>
    private static Task SaveAsync(LauncherSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return File.WriteAllTextAsync(SettingsPath, json);
    }
}
