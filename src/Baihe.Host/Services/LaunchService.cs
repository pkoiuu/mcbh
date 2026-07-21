// 启动服务 — Minecraft 游戏启动管线
// 解析版本 JSON → 构建 classpath → 组装 JVM/Game 参数 → 启动 Java 进程
// 支持 QuickPlay 直连（1.21+ 用 --quickPlayMultiplayer，旧版用 --server --port）

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 启动服务 — 编排 Minecraft 游戏启动流程
/// </summary>
public static class LaunchService
{
    /// <summary>QuickPlay 服务器地址</summary>
    private const string ServerAddress = "play.simpfun.cn";

    /// <summary>QuickPlay 服务器端口</summary>
    private const int ServerPort = 28230;

    /// <summary>启动状态</summary>
    private static LaunchState _state = LaunchState.Idle;
    private static string _stateMessage = string.Empty;
    private static int? _processId;

    /// <summary>
    /// 启动游戏
    /// </summary>
    public static async Task<object> Launch(string instanceId, OfflineAccount account, bool quickPlay)
    {
        if (_state == LaunchState.Running || _state == LaunchState.Launching)
        {
            return new { success = false, error = "游戏正在运行中" };
        }

        try
        {
            _state = LaunchState.Preparing;
            _stateMessage = "正在准备启动...";

            // 1. 获取 .minecraft 目录
            var mcDir = InstanceService.GetMcDirectory();
            if (!Directory.Exists(mcDir))
            {
                return new { success = false, error = $"未找到 .minecraft 目录: {mcDir}" };
            }

            // 2. 读取版本 JSON
            _stateMessage = "正在解析版本信息...";
            var versionJsonPath = Path.Combine(mcDir, "versions", instanceId, $"{instanceId}.json");
            if (!File.Exists(versionJsonPath))
            {
                return new { success = false, error = $"未找到版本 JSON: {versionJsonPath}" };
            }

            var versionJson = await File.ReadAllTextAsync(versionJsonPath);
            using var doc = JsonDocument.Parse(versionJson);
            var root = doc.RootElement;

            // 3. 查找 Java
            _stateMessage = "正在检测 Java 运行时...";
            var javaPath = await FindJava(root);
            if (javaPath == null)
            {
                return new { success = false, error = "未找到可用的 Java 运行时。请将 JRE 放在 jre/bin/java.exe 或安装系统 Java。" };
            }

            // 4. 构建 classpath
            _stateMessage = "正在构建类路径...";
            var classpath = BuildClasspath(mcDir, root);

            // 5. 获取主类
            var mainClass = root.TryGetProperty("mainClass", out var mc) ? mc.GetString()! : "net.minecraft.client.main.Main";

            // 6. 构建参数
            _stateMessage = "正在组装启动参数...";
            var versionId = root.TryGetProperty("id", out var idProp) ? idProp.GetString()! : instanceId;
            var assetsDir = Path.Combine(mcDir, "assets");
            var gameDir = mcDir;

            // 解析 Minecraft 版本号判断 QuickPlay 模式
            var majorVersion = ExtractMajorVersion(versionId);

            var jvmArgs = BuildJvmArgs(javaPath, classpath, mcDir, root, account);
            var gameArgs = BuildGameArgs(root, versionId, gameDir, assetsDir, account, quickPlay, majorVersion);

            // 7. 启动进程
            _state = LaunchState.Launching;
            _stateMessage = "正在启动游戏...";

            var allArgs = jvmArgs.Concat(new[] { mainClass }).Concat(gameArgs);

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = mcDir,
            };

            foreach (var arg in allArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = psi };
            process.Start();

            _processId = process.Id;
            _state = LaunchState.Running;
            _stateMessage = "游戏已启动";

            // 异步监控进程退出
            _ = Task.Run(() =>
            {
                process.WaitForExit();
                _state = LaunchState.Idle;
                _stateMessage = "游戏已退出";
                _processId = null;
            });

            return new { success = true, processId = process.Id };
        }
        catch (Exception ex)
        {
            _state = LaunchState.Idle;
            _stateMessage = $"启动失败: {ex.Message}";
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 获取当前启动状态
    /// </summary>
    public static object GetStatus()
    {
        return new
        {
            state = _state.ToString().ToLower(),
            message = _stateMessage,
            processId = _processId,
        };
    }

    /// <summary>
    /// 查找 Java — 优先捆绑 JRE，其次系统 Java
    /// </summary>
    private static async Task<string?> FindJava(JsonElement versionJson)
    {
        // 1. 捆绑 JRE
        var bundledJava = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "java.exe");
        if (File.Exists(bundledJava))
            return bundledJava;

        // 2. 捆绑 JRE (javaw.exe)
        var bundledJavaw = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "javaw.exe");
        if (File.Exists(bundledJavaw))
            return bundledJavaw;

        // 3. 系统 Java (通过 where 命令)
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
                var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstLine != null && File.Exists(firstLine.Trim()))
                    return firstLine.Trim();
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 构建 classpath — 从版本 JSON 的 libraries 数组中提取
    /// </summary>
    private static string BuildClasspath(string mcDir, JsonElement root)
    {
        var libs = new List<string>();
        var librariesDir = Path.Combine(mcDir, "libraries");

        if (root.TryGetProperty("libraries", out var libsArray))
        {
            foreach (var lib in libsArray.EnumerateArray())
            {
                // 跳过有 rules 且不匹配的库
                if (lib.TryGetProperty("rules", out var rules))
                {
                    if (!CheckRules(rules))
                        continue;
                }

                // 获取库文件路径
                string? libPath = null;

                // 优先使用 downloads.artifact.path
                if (lib.TryGetProperty("downloads", out var downloads)
                    && downloads.TryGetProperty("artifact", out var artifact)
                    && artifact.TryGetProperty("path", out var pathProp))
                {
                    libPath = Path.Combine(librariesDir, pathProp.GetString()!);
                }
                // 回退到 name 字段解析
                else if (lib.TryGetProperty("name", out var nameProp))
                {
                    libPath = ResolveLibraryPath(librariesDir, nameProp.GetString()!);
                }

                if (libPath != null && File.Exists(libPath))
                {
                    libs.Add(libPath);
                }
            }
        }

        // 添加客户端 JAR
        var versionId = root.TryGetProperty("id", out var idProp) ? idProp.GetString()! : "";
        var clientJar = Path.Combine(mcDir, "versions", versionId, $"{versionId}.jar");
        if (File.Exists(clientJar))
        {
            libs.Add(clientJar);
        }

        return string.Join(Path.PathSeparator, libs);
    }

    /// <summary>
    /// 从 Maven 坐标解析库文件路径
    /// 格式: group:artifact:version → group/artifact/version/artifact-version.jar
    /// </summary>
    private static string ResolveLibraryPath(string librariesDir, string mavenName)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return string.Empty;

        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];

        return Path.Combine(librariesDir, groupPath, artifact, version, $"{artifact}-{version}.jar");
    }

    /// <summary>
    /// 检查 rules 是否匹配当前平台
    /// </summary>
    private static bool CheckRules(JsonElement rules)
    {
        var allowed = true; // 默认允许

        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : "allow";

            if (rule.TryGetProperty("os", out var os))
            {
                var osName = os.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                var isWindows = osName == "windows";

                if (action == "allow")
                    allowed = isWindows;
                else
                    allowed = !isWindows;
            }
        }

        return allowed;
    }

    /// <summary>
    /// 构建 JVM 参数
    /// </summary>
    private static List<string> BuildJvmArgs(string javaPath, string classpath, string mcDir, JsonElement root, OfflineAccount account)
    {
        var args = new List<string>();

        // 内存分配
        args.Add("-Xmx2G");
        args.Add("-Xms512M");

        // 日4j2 配置（如果存在）
        var log4jPath = Path.Combine(mcDir, "assets", "log_configs", "client-xml-1.12.xml");
        if (File.Exists(log4jPath))
        {
            args.Add($"-Dlog4j.configurationFile={log4jPath}");
        }

        // 从版本 JSON 解析 JVM 参数（1.13+）
        if (root.TryGetProperty("arguments", out var arguments)
            && arguments.TryGetProperty("jvm", out var jvmArgs))
        {
            foreach (var arg in jvmArgs.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                {
                    var str = arg.GetString()!;
                    // 替换变量
                    str = str.Replace("${natives_directory}", Path.Combine(mcDir, "versions", GetVersionId(root), "natives"))
                             .Replace("${launcher_name}", "Baihe")
                             .Replace("${launcher_version}", "1.0.0")
                             .Replace("${classpath}", classpath);
                    args.Add(str);
                }
                else if (arg.ValueKind == JsonValueKind.Object)
                {
                    // 检查 rules
                    if (arg.TryGetProperty("rules", out var rules) && !CheckRules(rules))
                        continue;

                    if (arg.TryGetProperty("value", out var value))
                    {
                        if (value.ValueKind == JsonValueKind.String)
                            args.Add(value.GetString()!);
                        else if (value.ValueKind == JsonValueKind.Array)
                            foreach (var v in value.EnumerateArray())
                                if (v.ValueKind == JsonValueKind.String)
                                    args.Add(v.GetString()!);
                    }
                }
            }
        }
        else
        {
            // 旧版本默认 JVM 参数
            args.Add($"-Djava.library.path={Path.Combine(mcDir, "versions", GetVersionId(root), "natives")}");
            args.Add("-cp");
            args.Add(classpath);
        }

        return args;
    }

    /// <summary>
    /// 构建游戏参数
    /// </summary>
    private static List<string> BuildGameArgs(JsonElement root, string versionId, string gameDir, string assetsDir, OfflineAccount account, bool quickPlay, int majorVersion)
    {
        var args = new List<string>();

        // 从版本 JSON 解析游戏参数
        if (root.TryGetProperty("arguments", out var arguments)
            && arguments.TryGetProperty("game", out var gameArgsArray))
        {
            // 1.13+ 使用 arguments.game 数组
            foreach (var arg in gameArgsArray.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                {
                    var str = ReplaceVariables(arg.GetString()!, versionId, gameDir, assetsDir, account);
                    args.Add(str);
                }
                else if (arg.ValueKind == JsonValueKind.Object)
                {
                    if (arg.TryGetProperty("rules", out var rules) && !CheckRules(rules))
                        continue;

                    if (arg.TryGetProperty("value", out var value))
                    {
                        if (value.ValueKind == JsonValueKind.String)
                            args.Add(ReplaceVariables(value.GetString()!, versionId, gameDir, assetsDir, account));
                        else if (value.ValueKind == JsonValueKind.Array)
                            foreach (var v in value.EnumerateArray())
                                if (v.ValueKind == JsonValueKind.String)
                                    args.Add(ReplaceVariables(v.GetString()!, versionId, gameDir, assetsDir, account));
                    }
                }
            }
        }
        else if (root.TryGetProperty("minecraftArguments", out var mcArgs))
        {
            // 旧版本使用 minecraftArguments 模板字符串
            var template = mcArgs.GetString()!;
            var parts = template.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                args.Add(ReplaceVariables(part, versionId, gameDir, assetsDir, account));
            }
        }

        // 添加 QuickPlay 参数
        if (quickPlay)
        {
            if (majorVersion >= 21)
            {
                // 1.21+ 原生 QuickPlay
                args.Add("--quickPlayMultiplayer");
                args.Add($"{ServerAddress}:{ServerPort}");
            }
            else
            {
                // 旧版本使用 --server --port
                args.Add("--server");
                args.Add(ServerAddress);
                args.Add("--port");
                args.Add(ServerPort.ToString());
            }
        }

        return args;
    }

    /// <summary>
    /// 替换变量占位符
    /// </summary>
    private static string ReplaceVariables(string template, string versionId, string gameDir, string assetsDir, OfflineAccount account)
    {
        return template
            .Replace("${auth_player_name}", account.Username)
            .Replace("${version_name}", versionId)
            .Replace("${game_directory}", gameDir)
            .Replace("${assets_root}", assetsDir)
            .Replace("${assets_index_name}", GetAssetsIndex(versionId))
            .Replace("${auth_uuid}", account.Uuid)
            .Replace("${auth_access_token}", account.AccessToken)
            .Replace("${user_type}", "legacy")
            .Replace("${version_type}", "Baihe");
    }

    /// <summary>
    /// 获取版本 ID
    /// </summary>
    private static string GetVersionId(JsonElement root)
    {
        return root.TryGetProperty("id", out var idProp) ? idProp.GetString()! : "unknown";
    }

    /// <summary>
    /// 获取 assets index 名称
    /// </summary>
    private static string GetAssetsIndex(string versionId)
    {
        // 简化: 用版本号作为 index 名
        // 实际应从版本 JSON 的 assetIndex.id 字段获取
        return versionId;
    }

    /// <summary>
    /// 从版本号提取主版本号（如 "1.20.4" → 20, "1.21" → 21）
    /// </summary>
    private static int ExtractMajorVersion(string versionId)
    {
        var parts = versionId.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
        {
            return minor;
        }
        return 0;
    }

    /// <summary>启动状态枚举</summary>
    private enum LaunchState
    {
        Idle,
        Preparing,
        Launching,
        Running,
    }
}
