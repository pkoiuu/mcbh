// 工具服务 — 截图管理、游戏修复、打开文件夹

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

public static class ToolService
{
    /// <summary>列出截图</summary>
    public static Task<List<ScreenshotInfo>> ListScreenshots()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var screenshotsDir = Path.Combine(mcDir, "screenshots");
        var screenshots = new List<ScreenshotInfo>();

        if (!Directory.Exists(screenshotsDir))
            return Task.FromResult(screenshots);

        foreach (var file in Directory.GetFiles(screenshotsDir, "*.png"))
        {
            var info = new FileInfo(file);
            screenshots.Add(new ScreenshotInfo
            {
                FileName = info.Name,
                FilePath = info.FullName,
                SizeText = FormatSize(info.Length),
                CreatedTime = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }

        screenshots.Sort((a, b) => string.Compare(b.CreatedTime, a.CreatedTime, StringComparison.Ordinal));
        return Task.FromResult(screenshots);
    }

    /// <summary>打开文件夹</summary>
    public static async Task<string> OpenFolder(string folderName)
    {
        var mcDir = InstanceService.GetMcDirectory();
        string path;
        string? openedPath = null;

        if (folderName == "mods")
        {
            // OpenModsFolder 内部会启动 explorer，无需重复启动
            openedPath = await ModService.OpenModsFolder();
            return openedPath;
        }

        path = folderName switch
        {
            "minecraft" => mcDir,
            "saves" => Path.Combine(mcDir, "saves"),
            "screenshots" => Path.Combine(mcDir, "screenshots"),
            "logs" => Path.Combine(mcDir, "logs"),
            _ => mcDir,
        };

        if (Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
        else
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        return path;
    }

    /// <summary>检查游戏文件完整性</summary>
    public static async Task<object> RepairGame()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var results = new List<object>();

        // 1. 检查版本 JSON 和 JAR
        var versionsDir = Path.Combine(mcDir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var verDir in Directory.GetDirectories(versionsDir))
            {
                var verName = Path.GetFileName(verDir);
                var jsonPath = Path.Combine(verDir, $"{verName}.json");
                var jarPath = Path.Combine(verDir, $"{verName}.jar");

                if (!File.Exists(jsonPath))
                    results.Add(new { type = "missing", file = $"{verName}/{verName}.json", message = "缺少版本配置文件" });
                if (!File.Exists(jarPath))
                {
                    // 检查是否有 inheritsFrom (Fabric 版本不需要 jar)
                    if (File.Exists(jsonPath))
                    {
                        var json = File.ReadAllText(jsonPath);
                        if (!json.Contains("inheritsFrom"))
                            results.Add(new { type = "missing", file = $"{verName}/{verName}.jar", message = "缺少游戏主程序文件" });
                    }
                }
            }
        }

        // 2. 检查 libraries
        var libsDir = Path.Combine(mcDir, "libraries");
        var libCount = Directory.Exists(libsDir) ? Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories).Length : 0;
        results.Add(new { type = "info", file = "libraries", message = $"库文件数量: {libCount}" });

        // 3. 检查 assets
        var assetsDir = Path.Combine(mcDir, "assets", "objects");
        var assetCount = Directory.Exists(assetsDir) ? Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories).Length : 0;
        results.Add(new { type = "info", file = "assets", message = $"资源文件数量: {assetCount}" });

        // 4. 检查 mods
        var modsDir = Path.Combine(mcDir, "mods");
        var modCount = Directory.Exists(modsDir) ? Directory.GetFiles(modsDir, "*.jar").Length : 0;
        results.Add(new { type = "info", file = "mods", message = $"Mod 数量: {modCount}" });

        // 5. 检查 Java
        var settings = await SettingsService.GetAsync();
        var javaPath = await LaunchService.FindJava(settings);
        var javaExists = File.Exists(javaPath);
        results.Add(new { type = javaExists ? "info" : "error", file = "java", message = javaExists ? $"Java: {javaPath}" : "未找到 Java 运行时" });

        var hasErrors = results.Any(r => ((dynamic)r).type == "missing" || ((dynamic)r).type == "error");
        return Task.FromResult<object>(new
        {
            success = true,
            hasErrors,
            message = hasErrors ? "发现问题，请查看详情" : "游戏文件完整，无需修复",
            details = results,
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:.0} KB";
        return $"{bytes / (1024.0 * 1024):.1} MB";
    }
}

/// <summary>截图信息</summary>
public class ScreenshotInfo
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string CreatedTime { get; set; } = "";
}
