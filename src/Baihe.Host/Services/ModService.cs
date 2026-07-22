// Mod 管理服务 — 列出、启用/禁用、删除 Fabric mod
// mods 目录: .minecraft/mods 和 .minecraft/versions/<version>/mods

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

public static class ModService
{
    /// <summary>获取 mods 目录路径 — 优先版本专属目录，其次全局 mods 目录</summary>
    private static async Task<string> GetModsDir()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var current = await InstanceService.GetCurrentInstance();
        if (current != null)
        {
            var versionModsDir = Path.Combine(mcDir, "versions", current.Id, "mods");
            if (Directory.Exists(versionModsDir))
                return versionModsDir;
        }
        var globalModsDir = Path.Combine(mcDir, "mods");
        Directory.CreateDirectory(globalModsDir);
        return globalModsDir;
    }

    /// <summary>列出所有 mod</summary>
    public static async Task<List<ModInfo>> ListMods()
    {
        var modsDir = await GetModsDir();
        var mods = new List<ModInfo>();

        if (!Directory.Exists(modsDir))
            return mods;

        foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
        {
            var info = new FileInfo(file);
            var mod = new ModInfo
            {
                FileName = info.Name,
                Size = info.Length,
                SizeText = FormatSize(info.Length),
                Enabled = true,
                LastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            };
            // 从文件名提取 mod 名称
            mod.DisplayName = ExtractModName(info.Name);
            mods.Add(mod);
        }

        // 列出已禁用的 mod (.jar.disabled)
        foreach (var file in Directory.GetFiles(modsDir, "*.jar.disabled"))
        {
            var info = new FileInfo(file);
            var mod = new ModInfo
            {
                FileName = info.Name,
                Size = info.Length,
                SizeText = FormatSize(info.Length),
                Enabled = false,
                LastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            };
            mod.DisplayName = ExtractModName(info.Name.Replace(".disabled", ""));
            mods.Add(mod);
        }

        return mods;
    }

    /// <summary>切换 mod 启用/禁用状态</summary>
    public static async Task<bool> ToggleMod(string fileName)
    {
        var modsDir = await GetModsDir();

        // 判断当前是启用还是禁用状态
        if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            // 当前是禁用状态，要启用 — 移除 .disabled 后缀
            var disabledPath = Path.Combine(modsDir, fileName);
            var enabledName = fileName.Substring(0, fileName.Length - ".disabled".Length);
            var enabledPath = Path.Combine(modsDir, enabledName);

            if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
                return true; // 现在是启用状态
            }
        }
        else
        {
            // 当前是启用状态，要禁用 — 添加 .disabled 后缀
            var enabledPath = Path.Combine(modsDir, fileName);
            var disabledPath = Path.Combine(modsDir, fileName + ".disabled");

            if (File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
                return false; // 现在是禁用状态
            }
        }

        throw new FileNotFoundException($"未找到 mod 文件: {fileName}");
    }

    /// <summary>删除 mod</summary>
    public static async Task DeleteMod(string fileName)
    {
        var modsDir = await GetModsDir();
        var path = Path.Combine(modsDir, fileName);
        if (File.Exists(path))
            File.Delete(path);
        else
        {
            var disabledPath = Path.Combine(modsDir, fileName + ".disabled");
            if (File.Exists(disabledPath))
                File.Delete(disabledPath);
        }
    }

    /// <summary>打开 mods 文件夹</summary>
    public static async Task<string> OpenModsFolder()
    {
        var modsDir = await GetModsDir();
        System.Diagnostics.Process.Start("explorer.exe", modsDir);
        return modsDir;
    }

    /// <summary>从文件名提取 mod 显示名</summary>
    private static string ExtractModName(string fileName)
    {
        // 去掉 [xxx] 前缀的中文名
        var name = fileName;
        // 去掉方括号前缀: [钠] sodium-fabric-0.6.13+mc1.21.3.jar → sodium-fabric
        if (name.StartsWith("["))
        {
            var closeBracket = name.IndexOf(']');
            if (closeBracket > 0 && closeBracket < name.Length - 1)
                name = name.Substring(closeBracket + 1).Trim();
        }
        // 去掉 .jar 后缀
        name = name.Replace(".jar", "").Replace(".disabled", "");
        // 去掉版本号: sodium-fabric-0.6.13+mc1.21.3 → sodium-fabric
        var dashIndex = name.IndexOf('-');
        if (dashIndex > 0)
        {
            var afterDash = name.Substring(dashIndex + 1);
            // 如果 dash 后面是数字开头，认为是版本号
            if (afterDash.Length > 0 && char.IsDigit(afterDash[0]))
                name = name.Substring(0, dashIndex);
        }
        return name;
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:.0} KB";
        return $"{bytes / (1024.0 * 1024):.1} MB";
    }
}

/// <summary>Mod 信息</summary>
public class ModInfo
{
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Size { get; set; }
    public string SizeText { get; set; } = "";
    public bool Enabled { get; set; }
    public string LastModified { get; set; } = "";
}
