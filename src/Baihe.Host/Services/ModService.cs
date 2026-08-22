// Mod 管理服务 — 列出、启用/禁用、删除 Fabric mod
// mods 目录: .minecraft/mods（游戏实际加载的全局模组目录）
// 注意: 游戏(启动参数 gameDir=.minecraft)只从 .minecraft/mods 加载模组，
//       版本专属目录 .minecraft/versions/<version>/mods 不会被游戏加载。
//       之前优先返回版本专属目录会导致模组管理显示错误的模组列表，现统一使用全局目录。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

public static class ModService
{
    /// <summary>获取 mods 目录路径 — 游戏实际加载的全局 mods 目录 (.minecraft/mods)</summary>
    private static string GetModsDir()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var globalModsDir = Path.Combine(mcDir, "mods");
        Directory.CreateDirectory(globalModsDir);
        return globalModsDir;
    }

    /// <summary>列出所有 mod</summary>
    public static async Task<List<ModInfo>> ListMods()
    {
        var modsDir = GetModsDir();
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
            // 从 jar 内 fabric.mod.json 提取图标（base64 data URL）
            mod.IconDataUrl = TryExtractModIcon(file);
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
            mod.IconDataUrl = TryExtractModIcon(file);
            mods.Add(mod);
        }

        return mods;
    }

    /// <summary>
    /// 从 Mod jar 中提取图标 — 读取 fabric.mod.json / quilt.mod.json 的 icon 字段，
    /// 取出对应文件并转为 base64 data URL。失败返回 null。
    /// </summary>
    private static string? TryExtractModIcon(string jarPath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(jarPath);

            // 查找 mod 元数据文件
            var metaEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase)
                || e.FullName.Equals("quilt.mod.json", StringComparison.OrdinalIgnoreCase));
            if (metaEntry == null)
                return null;

            string? iconPath = null;
            using (var reader = new StreamReader(metaEntry.Open()))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
                if (doc.RootElement.TryGetProperty("icon", out var iconProp))
                {
                    if (iconProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        iconPath = iconProp.GetString();
                    }
                    else if (iconProp.ValueKind == System.Text.Json.JsonValueKind.Object
                             && iconProp.TryGetProperty("sizes", out var sizes)
                             && sizes.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // 选择最大尺寸的图标
                        string? best = null;
                        int bestSize = -1;
                        foreach (var sizeProp in sizes.EnumerateObject())
                        {
                            if (int.TryParse(sizeProp.Name, out var size) && size > bestSize)
                            {
                                bestSize = size;
                                best = sizeProp.Value.GetString();
                            }
                        }
                        iconPath = best;
                    }
                }
            }

            if (string.IsNullOrEmpty(iconPath))
                return null;

            // 从 jar 中读取图标文件
            var iconEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(iconPath, StringComparison.OrdinalIgnoreCase)
                || e.FullName.EndsWith("/" + iconPath, StringComparison.OrdinalIgnoreCase));
            if (iconEntry == null)
                return null;

            using var iconStream = iconEntry.Open();
            using var ms = new MemoryStream();
            iconStream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0 || bytes.Length > 1024 * 1024)
                return null;

            // 根据扩展名推断 MIME
            var mime = iconPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                : iconPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || iconPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg"
                : "image/png";

            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>切换 mod 启用/禁用状态</summary>
    public static async Task<bool> ToggleMod(string fileName)
    {
        var modsDir = GetModsDir();

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
        var modsDir = GetModsDir();
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
        var modsDir = GetModsDir();
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
    private static string FormatSize(long bytes) => FormatHelper.FormatSize(bytes);
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
    /// <summary>Mod 图标 (base64 data URL)，无图标时为 null</summary>
    public string? IconDataUrl { get; set; }
}
