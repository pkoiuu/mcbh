// Java 检测服务 — 桥接 Baihe.Core 的 JavaManager，提供 IPC 查询接口
// 优先使用捆绑 JRE（jre/bin/java.exe），其次系统 Java

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// Java 检测服务 — 检测捆绑 JRE 和系统 Java
/// </summary>
public static class JavaHostService
{
    /// <summary>
    /// 检测捆绑 JRE — 查找应用目录下的 jre/bin/java.exe
    /// </summary>
    public static Task<object?> DetectBundledJava()
    {
        var javaExe = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "java.exe");
        if (File.Exists(javaExe))
        {
            var version = GetJavaVersion(javaExe);
            return Task.FromResult<object?>(new
            {
                found = true,
                path = javaExe,
                version = version,
            });
        }
        return Task.FromResult<object?>(new { found = false, path = "", version = "" });
    }

    /// <summary>
    /// 检测系统 Java — 通过 java -version 命令
    /// </summary>
    public static async Task<List<object>> DetectSystemJava()
    {
        var results = new List<object>();

        // 尝试从 PATH 查找 java
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = line.Trim();
                    if (File.Exists(path))
                    {
                        var version = GetJavaVersion(path);
                        results.Add(new { path, version, is64Bit = version.Contains("64") });
                    }
                }
            }
        }
        catch
        {
            // where 命令不可用或 java 不在 PATH 中
        }

        return results;
    }

    /// <summary>
    /// 获取 Java 版本信息 — 通过 java -version 命令
    /// </summary>
    private static string GetJavaVersion(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                // java -version 输出到 stderr，第一行包含版本信息
                // 格式: openjdk version "17.0.1" 2021-10-19
                // 或: java version "1.8.0_291"
                var firstLine = output.Split('\n')[0];
                var match = System.Text.RegularExpressions.Regex.Match(
                    firstLine, @"""([^""]+)""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch
        {
            // 获取版本失败，返回空
        }
        return "unknown";
    }
}
