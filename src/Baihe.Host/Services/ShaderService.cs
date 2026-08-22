// 光影管理服务 — 管理 Iris 光影包 (.minecraft/shaderpacks)
// 光影包是 zip 文件，放入 shaderpacks 目录即可被 Iris 识别
// 当前启用的光影记录在 .minecraft/config/iris.properties 的 shaderPack 字段

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 光影管理服务 — 列出、启用、删除、打开光影包文件夹
/// </summary>
public static class ShaderService
{
    /// <summary>
    /// 获取 shaderpacks 目录路径（不存在则创建）
    /// </summary>
    public static string GetShadersDir()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var dir = Path.Combine(mcDir, "shaderpacks");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 获取 Iris 配置文件路径（不存在则创建默认配置）
    /// </summary>
    private static string GetIrisConfigPath()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var configDir = Path.Combine(mcDir, "config");
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "iris.properties");
        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, "enableShaders=true\nshaderPack=\n");
            }
            catch { }
        }
        return path;
    }

    /// <summary>
    /// 读取当前启用的光影包名称（无则返回 null/空）
    /// </summary>
    private static string GetActiveShaderPack()
    {
        try
        {
            var path = GetIrisConfigPath();
            if (!File.Exists(path)) return string.Empty;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("shaderPack=", StringComparison.OrdinalIgnoreCase))
                    return line["shaderPack=".Length..].Trim();
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// 列出光影包 — 扫描 shaderpacks 目录中的 zip 文件
    /// </summary>
    public static Task<List<object>> ListShaders()
    {
        var shadersDir = GetShadersDir();
        var active = GetActiveShaderPack();
        var shaders = new List<(string fileName, string displayName, long size, string sizeText, bool enabled)>();

        if (!Directory.Exists(shadersDir))
            return Task.FromResult(new List<object>());

        foreach (var file in Directory.GetFiles(shadersDir, "*.zip"))
        {
            var info = new FileInfo(file);
            var fileName = info.Name;
            shaders.Add((
                fileName,
                ExtractDisplayName(fileName),
                info.Length,
                FormatHelper.FormatSize(info.Length),
                string.Equals(fileName, active, StringComparison.OrdinalIgnoreCase)));
        }

        // 按启用优先，再按名称排序
        var sorted = shaders
            .OrderByDescending(s => s.enabled)
            .ThenBy(s => s.fileName, StringComparer.OrdinalIgnoreCase)
            .Select(s => (object)new
            {
                fileName = s.fileName,
                displayName = s.displayName,
                size = s.size,
                sizeText = s.sizeText,
                enabled = s.enabled,
            })
            .ToList();

        return Task.FromResult(sorted);
    }

    /// <summary>
    /// 启用光影包 — 写入 iris.properties 的 shaderPack 字段
    /// </summary>
    /// <param name="fileName">光影包文件名（zip）；传空表示仅开启光影不指定包</param>
    public static async Task<object> EnableShader(string fileName)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            var path = Path.Combine(GetShadersDir(), fileName);
            if (!File.Exists(path))
                return new { success = false, error = $"未找到光影包: {fileName}" };
        }

        try
        {
            var configPath = GetIrisConfigPath();
            var lines = File.Exists(configPath)
                ? File.ReadAllLines(configPath).ToList()
                : new List<string> { "enableShaders=true", "shaderPack=" };

            SetIrisProperty(lines, "enableShaders", "true");
            SetIrisProperty(lines, "shaderPack", fileName ?? "");

            await File.WriteAllLinesAsync(configPath, lines);
            return new { success = true, enabled = string.IsNullOrEmpty(fileName) ? false : true, fileName = fileName ?? "" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 关闭光影 — 设置 enableShaders=false
    /// </summary>
    public static async Task<object> DisableShaders()
    {
        try
        {
            var configPath = GetIrisConfigPath();
            var lines = File.Exists(configPath)
                ? File.ReadAllLines(configPath).ToList()
                : new List<string> { "enableShaders=true", "shaderPack=" };

            SetIrisProperty(lines, "enableShaders", "false");
            SetIrisProperty(lines, "shaderPack", "");

            await File.WriteAllLinesAsync(configPath, lines);
            return new { success = true };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 删除光影包文件
    /// </summary>
    public static async Task<object> DeleteShader(string fileName)
    {
        var path = Path.Combine(GetShadersDir(), fileName);
        if (!File.Exists(path))
            return new { success = false, error = $"未找到光影包: {fileName}" };

        try
        {
            File.Delete(path);

            // 如果删除的是当前启用的光影，同步清空 shaderPack
            var active = GetActiveShaderPack();
            if (string.Equals(active, fileName, StringComparison.OrdinalIgnoreCase))
            {
                var configPath = GetIrisConfigPath();
                var lines = File.ReadAllLines(configPath).ToList();
                SetIrisProperty(lines, "shaderPack", "");
                await File.WriteAllLinesAsync(configPath, lines);
            }

            return new { success = true };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 打开光影包文件夹
    /// </summary>
    public static Task<string> OpenShadersFolder()
    {
        var dir = GetShadersDir();
        System.Diagnostics.Process.Start("explorer.exe", dir);
        return Task.FromResult(dir);
    }

    /// <summary>
    /// 设置/更新 iris.properties 中的键值
    /// </summary>
    private static void SetIrisProperty(List<string> lines, string key, string value)
    {
        var prefix = key + "=";
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = prefix + value;
                return;
            }
        }
        lines.Add(prefix + value);
    }

    /// <summary>
    /// 从文件名提取显示名 — 去掉 .zip 后缀
    /// </summary>
    private static string ExtractDisplayName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        // 去掉常见的版本号后缀便于阅读（保留原名也不影响功能）
        return name;
    }
}
