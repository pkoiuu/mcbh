// 实例管理服务 — 扫描 .minecraft/versions/ 目录，列出已安装的游戏实例
// 同时管理当前选中实例的持久化

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 实例管理服务 — 扫描和管理游戏实例
/// </summary>
public static class InstanceService
{
    /// <summary>.minecraft 目录名</summary>
    private const string McDirName = ".minecraft";

    /// <summary>实例配置文件名</summary>
    private const string InstanceConfigFile = "instance.json";

    /// <summary>
    /// 获取 .minecraft 目录路径
    /// 优先查找应用目录下的 .minecraft，其次查找 installer_resources（开发环境）
    /// </summary>
    public static string GetMcDirectory()
    {
        var candidates = new[]
        {
            // 1. 应用目录下的 .minecraft（正式部署位置）
            Path.Combine(AppContext.BaseDirectory, McDirName),
            // 2. 当前工作目录下的 .minecraft
            Path.Combine(Directory.GetCurrentDirectory(), McDirName),
            // 3. 开发环境：从 bin 输出目录回溯到 installer_resources
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "installer_resources", McDirName),
            // 4. 标准 Minecraft 安装位置
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), McDirName),
        };

        // 优先选择包含 versions 子目录的 .minecraft（排除空目录误匹配）
        var withVersions = candidates.FirstOrDefault(p => Directory.Exists(Path.Combine(p, "versions")));
        if (withVersions != null)
            return withVersions;

        // 退回到第一个存在的 .minecraft 目录
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    /// <summary>
    /// 列出已安装的游戏实例 — 扫描 .minecraft/versions/ 目录
    /// </summary>
    public static Task<List<GameInstance>> ListInstances()
    {
        var mcDir = GetMcDirectory();
        var versionsDir = Path.Combine(mcDir, "versions");
        var instances = new List<GameInstance>();

        if (!Directory.Exists(versionsDir))
        {
            return Task.FromResult(instances);
        }

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = Path.Combine(dir, $"{id}.json");
            var jarPath = Path.Combine(dir, $"{id}.jar");

            if (!File.Exists(jsonPath))
                continue;

            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                // 判断是否已安装 — 有 jar 文件或有 inheritsFrom（Fabric/Forge 等加载器版本通过继承使用原版 jar）
                var hasJar = File.Exists(jarPath);
                var hasInheritsFrom = root.TryGetProperty("inheritsFrom", out var inheritsProp)
                    && inheritsProp.ValueKind == JsonValueKind.String;
                var instance = new GameInstance
                {
                    Id = id,
                    Version = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? id : id,
                    Type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "release" : "release",
                    IsInstalled = hasJar || hasInheritsFrom,
                };

                // 检测加载器类型
                if (root.TryGetProperty("mainClass", out var mainClassProp))
                {
                    var mainClass = mainClassProp.GetString() ?? "";
                    if (mainClass.Contains("fabric"))
                        instance.Loader = "fabric";
                    else if (mainClass.Contains("forge"))
                        instance.Loader = "forge";
                    else if (mainClass.Contains("quilt"))
                        instance.Loader = "quilt";
                }

                // 检查 Mod 数量
                var modsDir = Path.Combine(mcDir, "mods");
                if (Directory.Exists(modsDir))
                {
                    instance.ModCount = Directory.GetFiles(modsDir, "*.jar").Length;
                }

                // 检查最后游玩时间（从 launcher_profiles.json 或自定义文件）
                var profilesPath = Path.Combine(mcDir, "launcher_profiles.json");
                if (File.Exists(profilesPath))
                {
                    try
                    {
                        var profilesJson = File.ReadAllText(profilesPath);
                        using var profilesDoc = JsonDocument.Parse(profilesJson);
                        if (profilesDoc.RootElement.TryGetProperty("profiles", out var profiles))
                        {
                            foreach (var profile in profiles.EnumerateObject())
                            {
                                if (profile.Value.TryGetProperty("lastVersionId", out var lastVer)
                                    && lastVer.GetString() == id
                                    && profile.Value.TryGetProperty("lastUsed", out var lastUsed))
                                {
                                    instance.LastPlayed = lastUsed.GetString() ?? "未知";
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 解析 launcher_profiles.json 失败不影响功能
                    }
                }

                instances.Add(instance);
            }
            catch
            {
                // 解析版本 JSON 失败，跳过此实例
            }
        }

        return Task.FromResult(instances);
    }

    /// <summary>
    /// 获取当前选中的实例
    /// </summary>
    public static async Task<GameInstance?> GetCurrentInstance()
    {
        var instances = await ListInstances();
        if (instances.Count == 0)
            return null;

        // 读取选中状态
        var configPath = Path.Combine(AppContext.BaseDirectory, "current_instance.txt");
        if (File.Exists(configPath))
        {
            var selectedId = await File.ReadAllTextAsync(configPath);
            var selected = instances.FirstOrDefault(i => i.Id == selectedId.Trim());
            if (selected != null)
                return selected;
        }

        // 参照 PCL CE [启航定制]: 优先选择 Fabric 实例
        return instances.FirstOrDefault(i => i.IsInstalled && i.Id.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            ?? instances.FirstOrDefault(i => i.IsInstalled)
            ?? instances[0];
    }
}
