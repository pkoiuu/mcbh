// 启动服务 — Minecraft 游戏启动管线
// 解析版本 JSON → 合并 inheritsFrom → 构建 classpath → 提取 natives → 组装 JVM/Game 参数 → 启动 Java 进程
// 参考 PCL CE 启动流程，支持 Fabric/原版、QuickPlay 直连、离线账户

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Baihe.Host.Ipc;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 启动服务 — 编排 Minecraft 游戏启动流程
/// </summary>
public static class LaunchService
{
    /// <summary>启动器品牌名</summary>
    private const string LauncherBrand = "Baihe";

    /// <summary>启动器版本</summary>
    private const string LauncherVersion = "1.0";

    /// <summary>原版主类名</summary>
    private const string VanillaMainClass = "net.minecraft.client.main.Main";

    /// <summary>启动状态</summary>
    private static LaunchState _state = LaunchState.Idle;
    private static string _stateMessage = string.Empty;
    private static int? _processId;

    /// <summary>
    /// 启动游戏
    /// </summary>
    /// <param name="instanceId">实例 ID（版本目录名）</param>
    /// <param name="account">离线账户</param>
    /// <param name="settings">启动器设置，为 null 时自动加载</param>
    public static async Task<object> Launch(string instanceId, OfflineAccount account, LauncherSettings? settings = null)
    {
        if (_state == LaunchState.Running || _state == LaunchState.Launching)
        {
            return new { success = false, error = "游戏正在运行中" };
        }

        try
        {
            _state = LaunchState.Preparing;
            _stateMessage = "正在准备启动...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });

            // 加载设置（如果未提供）
            settings ??= await SettingsService.GetAsync();

            // 使用 settings.QuickPlayEnabled 决定是否启用 QuickPlay
            var enableQuickPlay = settings.QuickPlayEnabled;

            // 1. 获取 .minecraft 目录
            var mcDir = InstanceService.GetMcDirectory();
            if (!Directory.Exists(mcDir))
                return new { success = false, error = "未找到游戏目录，请检查安装" };

            // 2. 检查版本 JSON 是否存在
            var versionJsonPath = Path.Combine(mcDir, "versions", instanceId, $"{instanceId}.json");
            if (!File.Exists(versionJsonPath))
                return new { success = false, error = $"未找到版本 {instanceId} 的配置文件" };

            // 3. 在 LoadAndMergeVersion 之前，先读取原始 JSON 获取 parentId
            //    (LoadAndMergeVersion 合并后会移除 inheritsFrom，导致无法获取 parentId)
            var rawJson = await File.ReadAllTextAsync(versionJsonPath);
            var rawNode = JsonNode.Parse(rawJson);
            var parentId = rawNode?["inheritsFrom"]?.GetValue<string>() ?? instanceId;

            // 4. 检查客户端 jar (parentId 对应的 jar 文件)
            var clientJarPath = Path.Combine(mcDir, "versions", parentId, $"{parentId}.jar");
            if (!File.Exists(clientJarPath))
                return new { success = false, error = $"未找到游戏主程序文件 {parentId}.jar" };

            // 5. 查找 Java — 优先使用设置中的覆盖路径，其次捆绑 JRE，最后系统 Java
            _stateMessage = "正在检测 Java 运行时...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });
            var javaPath = await FindJava(settings);
            if (!File.Exists(javaPath))
                return new { success = false, error = "未找到 Java 运行时，请检查 Java 安装" };

            // 6. 读取并合并版本 JSON (处理 inheritsFrom 递归合并)
            _stateMessage = "正在解析版本信息...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });
            var version = await LoadAndMergeVersion(mcDir, instanceId);
            if (version == null)
            {
                return new { success = false, error = $"未找到版本 JSON: {instanceId}" };
            }

            // 7. 提取 natives 库 — 从有 natives 字段的库中提取 .dll 文件
            _stateMessage = "正在提取 natives 库...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });
            var nativesDir = Path.Combine(mcDir, "versions", instanceId, "natives");
            ExtractNatives(mcDir, version, nativesDir);

            // 8. 构建 classpath — 遍历合并后的 libraries，跳过 natives 库
            _stateMessage = "正在构建类路径...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });
            var classpath = BuildClasspath(mcDir, version, parentId);

            // 9. 获取主类 (子版本覆盖父版本)
            var mainClass = version["mainClass"]?.GetValue<string>() ?? VanillaMainClass;

            // 10. 判断是否为 Fabric (KnotClient 是 Fabric 的入口主类)
            var isFabric = mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
                           mainClass.Contains("knot", StringComparison.OrdinalIgnoreCase);

            // 11. 获取 log4j 配置路径 (从版本 JSON logging.client.file.id 字段获取)
            var log4jPath = GetLog4jConfigPath(mcDir, version);

            // 12. 构建启动参数
            _stateMessage = "正在组装启动参数...";
            IpcRouter.PushEvent("launch.state", new { state = "preparing", message = _stateMessage });
            var versionId = version["id"]?.GetValue<string>() ?? instanceId;
            var assetsDir = Path.Combine(mcDir, "assets");
            var gameDir = mcDir;
            var assetsIndex = version["assetIndex"]?["id"]?.GetValue<string>() ?? versionId;
            var majorVersion = ExtractMajorVersion(versionId);

            var jvmArgs = BuildJvmArgs(classpath, nativesDir, log4jPath, settings, isFabric);
            var gameArgs = BuildGameArgs(version, versionId, gameDir, assetsDir, account, enableQuickPlay, majorVersion, settings, assetsIndex);

            // 预填充 options.txt — 跳过 Minecraft 首次启动的无障碍欢迎界面
            // 反编译 fmg.class 确认: onboardAccessibility:false → 跳过欢迎界面, true → 显示欢迎界面
            EnsureOptionsTxt(gameDir);

            // 13. 启动进程
            _state = LaunchState.Launching;
            _stateMessage = "正在启动游戏...";
            IpcRouter.PushEvent("launch.state", new { state = "launching", message = _stateMessage });

            // 命令行格式: javaw.exe [JVM参数] <mainClass> [游戏参数]
            // -cp 和 classpath 已在 JVM 参数末尾
            var allArgs = jvmArgs.Concat(new[] { mainClass }).Concat(gameArgs);

            // 将完整启动命令行写入调试日志，方便排查启动失败
            try
            {
                var cmdLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Java: {javaPath}\nWorkingDir: {mcDir}\nArgs:\n  {string.Join("\n  ", allArgs)}\n";
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "launch_cmd.log"), cmdLog);
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = mcDir,
            };

            // 设置环境变量 APPDATA 为 .minecraft 目录
            psi.EnvironmentVariables["APPDATA"] = mcDir;

            foreach (var arg in allArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = psi };
            process.Start();

            _processId = process.Id;
            _state = LaunchState.Running;
            _stateMessage = "游戏已启动";
            IpcRouter.PushEvent("launch.started", new { processId = process.Id });

            // 异步读取输出，捕获 stderr 用于错误诊断
            var stderrBuilder = new System.Text.StringBuilder();
            _ = Task.Run(() =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = process.StandardError.ReadLine();
                    if (line != null)
                        stderrBuilder.AppendLine(line);
                }
            });
            _ = Task.Run(() => process.StandardOutput.ReadToEnd());

            // 异步修改游戏窗口标题为"白鹤服务器" — 等 Minecraft 窗口创建后通过 Win32 API 修改
            _ = Task.Run(async () =>
            {
                try
                {
                    // 等待 Minecraft 窗口出现 (最多等 30 秒)
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(500);
                        if (process.HasExited) return;

                        // 查找 Minecraft 窗口并修改标题
                        var handle = FindMinecraftWindow(process.Id);
                        if (handle != IntPtr.Zero)
                        {
                            SetWindowText(handle, "白鹤服务器");
                            return;
                        }
                    }
                }
                catch { }
            });

            // 异步监控进程退出
            _ = Task.Run(() =>
            {
                process.WaitForExit();
                var exitCode = process.ExitCode;
                var stderr = stderrBuilder.ToString();
                _state = LaunchState.Idle;
                _stateMessage = exitCode == 0 ? "游戏已退出" : $"游戏异常退出 (code: {exitCode})";

                // 将错误输出写入日志文件，方便诊断
                if (exitCode != 0 && !string.IsNullOrEmpty(stderr))
                {
                    try
                    {
                        var logPath = Path.Combine(AppContext.BaseDirectory, "launch_error.log");
                        File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ExitCode: {exitCode}\n\nstderr:\n{stderr}\n");
                    }
                    catch { }
                }

                _processId = null;
                IpcRouter.PushEvent("launch.exited", new
                {
                    exitCode,
                    abnormal = exitCode != 0,
                    error = exitCode != 0 ? stderr : null,
                });
            });

            // closeAfterLaunch: 启动游戏后关闭启动器
            if (settings.CloseAfterLaunch)
            {
                // 延迟 2 秒退出，确保游戏进程已完全启动
                _ = Task.Delay(2000).ContinueWith(_ => Environment.Exit(0));
            }

            return new { success = true, processId = process.Id };
        }
        catch (Exception ex)
        {
            _state = LaunchState.Idle;
            _stateMessage = $"启动失败: {ex.Message}";
            IpcRouter.PushEvent("launch.state", new { state = "error", message = ex.Message });
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 启动游戏 (McAccount 版本) — 将统一账户转换为离线账户后复用现有启动逻辑
    /// </summary>
    /// <param name="instanceId">实例 ID（版本目录名）</param>
    /// <param name="account">统一账户 (离线/微软/第三方)</param>
    /// <param name="settings">启动器设置，为 null 时自动加载</param>
    public static async Task<object> Launch(string instanceId, McAccount account, LauncherSettings? settings = null)
    {
        // 转换为 OfflineAccount 传给现有逻辑
        var offlineAccount = account.ToOfflineAccount();
        return await Launch(instanceId, offlineAccount, settings);
    }

    /// <summary>
    /// 获取当前启动状态
    /// </summary>
    public static object GetStatus()
    {
        return new
        {
            state = _state.ToString().ToLowerInvariant(),
            message = _stateMessage,
            processId = _processId,
        };
    }

    // =========================================================================
    // 版本 JSON 加载与合并
    // =========================================================================

    /// <summary>
    /// 加载并合并版本 JSON — 处理 inheritsFrom 递归合并
    /// 例如 Fabric Loader 版本 fabric-loader-0.16.14-1.21.3.json 的 inheritsFrom 为 "1.21.3"，
    /// 需要读取 1.21.3.json 并合并 libraries、arguments、assetIndex 等字段
    /// </summary>
    /// <param name="mcDir">.minecraft 目录</param>
    /// <param name="versionId">版本 ID</param>
    /// <param name="depth">递归深度（防止无限循环）</param>
    /// <returns>合并后的版本 JSON 对象，找不到返回 null</returns>
    private static async Task<JsonObject?> LoadAndMergeVersion(string mcDir, string versionId, int depth = 0)
    {
        // 防止无限递归
        if (depth > 10)
            return null;

        var versionJsonPath = Path.Combine(mcDir, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(versionJsonPath))
            return null;

        var json = await File.ReadAllTextAsync(versionJsonPath);
        var node = JsonNode.Parse(json);
        if (node is not JsonObject version)
            return null;

        // 检查是否有 inheritsFrom 字段
        var inheritsFrom = version["inheritsFrom"]?.GetValue<string>();
        if (string.IsNullOrEmpty(inheritsFrom))
            return version;

        // 递归加载父版本 (父版本可能也有 inheritsFrom)
        var parent = await LoadAndMergeVersion(mcDir, inheritsFrom, depth + 1);
        if (parent == null)
        {
            // 父版本不存在，只使用子版本
            return version;
        }

        // 合并子版本和父版本
        return MergeVersion(version, parent);
    }

    /// <summary>
    /// 合并版本 JSON — 子版本覆盖父版本
    /// 合并规则:
    /// - mainClass / id / type: 子版本覆盖
    /// - libraries: 合并去重 (按 name 字段，子版本优先)
    /// - arguments.jvm / arguments.game: 合并 (子版本在前，父版本在后)
    /// - assetIndex / logging / minecraftArguments: 子版本优先
    /// - inheritsFrom: 移除 (已合并)
    /// </summary>
    private static JsonObject MergeVersion(JsonObject child, JsonObject parent)
    {
        // 从父版本深拷贝开始
        var merged = parent.DeepClone().AsObject();

        // 子版本覆盖标量字段
        CopyField(child, merged, "id");
        CopyField(child, merged, "mainClass");
        CopyField(child, merged, "type");
        CopyField(child, merged, "minecraftArguments");
        CopyField(child, merged, "assetIndex");
        CopyField(child, merged, "logging");
        CopyField(child, merged, "releaseTime");
        CopyField(child, merged, "time");

        // 合并 libraries (去重，子版本优先)
        // 按 groupId:artifactId:classifier 三段去重（不含版本号）
        // classifier 为空时只按 groupId:artifactId 去重
        // 这样 natives-windows 库不会被同名的非 native 库覆盖
        if (child["libraries"] is JsonArray childLibs)
        {
            var mergedLibs = new JsonArray();
            var libKeys = new HashSet<string>();

            // 先添加子版本的库
            foreach (var lib in childLibs)
            {
                if (lib == null) continue;
                var name = lib["name"]?.GetValue<string>();
                if (name != null)
                {
                    var key = GetLibKey(name);
                    if (libKeys.Add(key))
                    {
                        mergedLibs.Add(lib.DeepClone());
                    }
                }
            }

            // 再添加父版本的库 (跳过子版本已有的)
            if (merged["libraries"] is JsonArray parentLibs)
            {
                foreach (var lib in parentLibs)
                {
                    if (lib == null) continue;
                    var name = lib["name"]?.GetValue<string>();
                    if (name != null)
                    {
                        var key = GetLibKey(name);
                        if (libKeys.Add(key))
                        {
                            mergedLibs.Add(lib.DeepClone());
                        }
                    }
                }
            }

            merged["libraries"] = mergedLibs;
        }

        // 合并 arguments (子版本在前，父版本在后)
        if (child["arguments"] is JsonObject childArgs)
        {
            if (merged["arguments"] == null)
                merged["arguments"] = new JsonObject();

            var mergedArgs = merged["arguments"]!.AsObject();

            // 合并 jvm 参数
            MergeArgumentArray(childArgs, mergedArgs, "jvm");
            // 合并 game 参数
            MergeArgumentArray(childArgs, mergedArgs, "game");
        }

        // 移除 inheritsFrom (已合并)
        merged.Remove("inheritsFrom");

        return merged;
    }

    /// <summary>
    /// 复制字段（如果源对象中存在）
    /// </summary>
    private static void CopyField(JsonObject src, JsonObject dst, string fieldName)
    {
        if (src[fieldName] != null)
            dst[fieldName] = src[fieldName]!.DeepClone();
    }

    /// <summary>
    /// 生成库去重键 — groupId:artifactId:classifier（不含版本号）
    /// classifier 为空时只返回 groupId:artifactId
    /// 这样 org.lwjgl:lwjgl:3.3.3 和 org.lwjgl:lwjgl:3.3.3:natives-windows 不会被去重为同一个
    /// </summary>
    private static string GetLibKey(string name)
    {
        var parts = name.Split(':');
        // 格式: groupId:artifactId:version[:classifier]
        if (parts.Length >= 4)
            return $"{parts[0]}:{parts[1]}:{parts[3]}"; // groupId:artifactId:classifier
        if (parts.Length >= 2)
            return $"{parts[0]}:{parts[1]}"; // groupId:artifactId (无 classifier)
        return name;
    }

    /// <summary>
    /// 预填充 options.txt — 确保游戏跳过首次启动的无障碍欢迎界面
    /// 设置 onboardAccessibility:false 跳过无障碍引导界面 (反编译 fmg.class 确认: false=跳过, true=显示)
    /// 设置 joinedFirstServer:true 跳过"首次加入服务器"警告
    /// 设置 tutorialStep:none 跳过新手教程提示
    /// </summary>
    private static void EnsureOptionsTxt(string gameDir)
    {
        var optionsPath = Path.Combine(gameDir, "options.txt");
        var lines = new List<string>();
        var fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 读取已存在的 options.txt
        if (File.Exists(optionsPath))
        {
            lines = File.ReadAllLines(optionsPath).ToList();
            foreach (var line in lines)
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                    fieldSet.Add(line[..colonIdx]);
            }
        }

        // 确保关键字段存在且值正确
        void EnsureField(string key, string value)
        {
            if (!fieldSet.Contains(key))
            {
                lines.Add($"{key}:{value}");
                fieldSet.Add(key);
            }
            else
            {
                // 更新已有字段
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}:{value}";
                        break;
                    }
                }
            }
        }

        // onboardAccessibility: false = 跳过无障碍引导界面
        // 反编译 fmg.class 确认: true → 添加引导界面到屏幕列表, false → 跳过
        EnsureField("onboardAccessibility", "false");
        EnsureField("joinedFirstServer", "true");
        EnsureField("tutorialStep", "none");
        EnsureField("lang", "zh_cn"); // 设置游戏语言为中文

        try
        {
            File.WriteAllLines(optionsPath, lines);
        }
        catch
        {
            // 写入失败不影响启动
        }
    }

    /// <summary>
    /// 合并 arguments 中的某个参数数组 (子版本在前，父版本在后)
    /// </summary>
    private static void MergeArgumentArray(JsonObject childArgs, JsonObject mergedArgs, string key)
    {
        if (childArgs[key] is not JsonArray childArray)
            return;

        var mergedArray = new JsonArray();

        // 子版本参数在前
        foreach (var arg in childArray)
        {
            if (arg != null) mergedArray.Add(arg.DeepClone());
        }

        // 父版本参数在后
        if (mergedArgs[key] is JsonArray parentArray)
        {
            foreach (var arg in parentArray)
            {
                if (arg != null) mergedArray.Add(arg.DeepClone());
            }
        }

        mergedArgs[key] = mergedArray;
    }

    // =========================================================================
    // Java 查找
    // =========================================================================

    /// <summary>
    /// 查找 Java — 优先使用设置覆盖路径，其次捆绑 JRE (javaw.exe)，最后系统 Java
    /// </summary>
    public static async Task<string?> FindJava(LauncherSettings? settings = null)
    {
        // 1. 设置中的覆盖路径
        if (settings != null && !string.IsNullOrEmpty(settings.JavaPathOverride) && File.Exists(settings.JavaPathOverride))
            return settings.JavaPathOverride;

        // 2. 捆绑 JRE — javaw.exe (无控制台窗口，优先)
        var bundledJavaw = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "javaw.exe");
        if (File.Exists(bundledJavaw))
            return bundledJavaw;

        // 开发环境：从 bin 回溯到 installer_resources/jre
        var devJavaw = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "installer_resources", "jre", "bin", "javaw.exe");
        if (File.Exists(devJavaw))
            return devJavaw;

        // 3. 捆绑 JRE — java.exe
        var bundledJava = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "java.exe");
        if (File.Exists(bundledJava))
            return bundledJava;

        // 开发环境：java.exe
        var devJava = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "installer_resources", "jre", "bin", "java.exe");
        if (File.Exists(devJava))
            return devJava;

        // 4. 系统 javaw (通过 where 命令查找)
        var systemJavaw = await FindSystemJava("javaw");
        if (systemJavaw != null)
            return systemJavaw;

        // 5. 系统 java
        return await FindSystemJava("java");
    }

    /// <summary>
    /// 通过 where 命令查找系统 Java
    /// </summary>
    private static async Task<string?> FindSystemJava(string executable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return null;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstLine != null && File.Exists(firstLine.Trim()))
                return firstLine.Trim();
        }
        catch
        {
            // 查找失败，忽略
        }
        return null;
    }

    // =========================================================================
    // Natives 提取
    // =========================================================================

    /// <summary>
    /// 提取 natives 库 — 从 native 库 jar 中提取 .dll 文件
    /// 支持两种格式:
    /// 1. 旧格式: library 有 natives 字段，通过 classifier 查找 native jar
    /// 2. 新格式 (1.21+): native 库作为独立 library 条目，name 包含 :natives-windows，通过 rules 限制平台
    /// </summary>
    private static void ExtractNatives(string mcDir, JsonObject version, string nativesDir)
    {
        // 创建 natives 目录
        Directory.CreateDirectory(nativesDir);

        var librariesDir = Path.Combine(mcDir, "libraries");
        if (version["libraries"] is not JsonArray libs)
        {
            // 调试：记录 libraries 为空的情况
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "natives_debug.log"), $"[{DateTime.Now:HH:mm:ss}] libraries is not JsonArray or null\nversion keys: {string.Join(", ", version.Select(k => k.Key))}\n"); } catch { }
            return;
        }

        var debugLog = new System.Text.StringBuilder();
        debugLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] Total libs: {libs.Count}, nativesDir: {nativesDir}");
        int extractedCount = 0;

        foreach (var libNode in libs)
        {
            if (libNode is not JsonObject lib)
                continue;

            string? jarPath = null;

            // 方式1: 旧格式 — library 有 natives 字段
            if (lib["natives"] is JsonObject natives)
            {
                // 获取 Windows 平台的 classifier (如 "natives-windows")
                var classifier = natives["windows"]?.GetValue<string>();
                if (string.IsNullOrEmpty(classifier))
                    continue;

                jarPath = GetNativeJarPath(librariesDir, lib, classifier);
                debugLog.AppendLine($"  [old format] name={lib["name"]}, classifier={classifier}, jarPath={jarPath}, exists={jarPath != null && File.Exists(jarPath)}");
            }
            // 方式2: 新格式 — name 包含 :natives-windows，检查 rules 是否允许 Windows
            else
            {
                var name = lib["name"]?.GetValue<string>();
                if (name == null || !name.Contains("natives-windows", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 跳过非 x64 架构的 natives (arm64, x86)
                if (name.Contains("arm64", StringComparison.OrdinalIgnoreCase) || name.Contains("x86", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 检查 rules 是否允许当前平台
                if (!CheckRules(lib["rules"]))
                    continue;

                // 从 downloads.artifact.path 获取 jar 路径
                if (lib["downloads"] is JsonObject downloads
                    && downloads["artifact"] is JsonObject artifact
                    && artifact["path"] is JsonNode pathNode)
                {
                    jarPath = Path.Combine(librariesDir, pathNode.GetValue<string>());
                }
                debugLog.AppendLine($"  [new format] name={name}, jarPath={jarPath}, exists={jarPath != null && File.Exists(jarPath)}");
            }

            if (jarPath == null || !File.Exists(jarPath))
                continue;

            // 从 jar 中提取 .dll 文件
            try
            {
                using var stream = File.OpenRead(jarPath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    // 只提取 .dll 文件 (Windows 平台原生库)
                    if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destPath = Path.Combine(nativesDir, entry.Name);

                    // 增量提取：目标文件存在且大小相同则跳过
                    if (File.Exists(destPath) && new FileInfo(destPath).Length == entry.Length)
                    {
                        extractedCount++;
                        continue;
                    }

                    // 提取文件 (覆盖已存在的)
                    entry.ExtractToFile(destPath, true);
                    extractedCount++;
                    debugLog.AppendLine($"    extracted: {entry.Name} ({entry.Length} bytes)");
                }
            }
            catch (Exception ex)
            {
                debugLog.AppendLine($"    ERROR: {ex.Message}");
                Console.Error.WriteLine($"[LaunchService] 提取 native 库失败 ({jarPath}): {ex.Message}");
            }
        }

        debugLog.AppendLine($"Total extracted: {extractedCount}");
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "natives_debug.log"), debugLog.ToString()); } catch { }
    }

    /// <summary>
    /// 获取 native 库的 jar 文件路径
    /// 优先从 downloads.classifiers[classifier].path 获取，回退到 Maven 坐标解析 (带 classifier)
    /// </summary>
    private static string? GetNativeJarPath(string librariesDir, JsonObject lib, string classifier)
    {
        // 优先使用 downloads.classifiers[classifier].path
        if (lib["downloads"] is JsonObject downloads
            && downloads["classifiers"] is JsonObject classifiers
            && classifiers[classifier] is JsonObject classifierObj
            && classifierObj["path"] is JsonNode pathNode)
        {
            return Path.Combine(librariesDir, pathNode.GetValue<string>());
        }

        // 回退到 Maven 坐标解析 (带 classifier)
        var name = lib["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
            return null;

        return ResolveMavenPath(librariesDir, name, classifier);
    }

    // =========================================================================
    // Classpath 构建
    // =========================================================================

    /// <summary>
    /// 构建 classpath — 遍历合并后的 libraries，跳过 natives 库
    /// 流程:
    /// 1. 遍历合并后的 libraries
    /// 2. 跳过 natives 库 (有 natives 字段的库)
    /// 3. 根据 rules 过滤 (操作系统)
    /// 4. Maven 坐标转路径或使用 downloads.artifact.path
    /// 5. 加入客户端 jar: versions/{parentId}/{parentId}.jar
    /// 6. 用分号 ; 连接 (Windows)
    /// </summary>
    private static string BuildClasspath(string mcDir, JsonObject version, string parentId)
    {
        var libs = new List<string>();
        var librariesDir = Path.Combine(mcDir, "libraries");

        if (version["libraries"] is JsonArray libsArray)
        {
            foreach (var libNode in libsArray)
            {
                if (libNode is not JsonObject lib)
                    continue;

                // 跳过 natives 库 — 不加入 classpath (参照 PCL CE: IsNatives == true 则 continue)
                // 方式1: 旧格式 — library 有 natives 字段
                if (lib["natives"] != null)
                    continue;

                var libName = lib["name"]?.GetValue<string>() ?? "";

                // 方式2: 新格式 (1.21+) — name 包含 natives-windows
                // 这些是 native 库，只用于提取 .dll，不加入 classpath
                if (libName.Contains("natives-windows", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 检查 rules 是否匹配当前平台
                if (!CheckRules(lib["rules"]))
                    continue;

                // 获取库文件路径
                var libPath = GetLibraryPath(librariesDir, lib);
                if (libPath != null && File.Exists(libPath))
                {
                    libs.Add(libPath);
                }
            }
        }

        // 添加客户端 JAR: versions/{parentId}/{parentId}.jar
        // parentId 从 inheritsFrom 获取 (Fabric 版本的原版 jar 在父版本目录)
        var clientJar = Path.Combine(mcDir, "versions", parentId, $"{parentId}.jar");
        if (File.Exists(clientJar))
        {
            libs.Add(clientJar);
        }

        // Windows 用分号连接 classpath
        return string.Join(';', libs);
    }

    /// <summary>
    /// 获取普通库的文件路径 (不含 classifier)
    /// 优先从 downloads.artifact.path 获取，回退到 Maven 坐标解析
    /// </summary>
    private static string? GetLibraryPath(string librariesDir, JsonObject lib)
    {
        // 优先使用 downloads.artifact.path
        if (lib["downloads"] is JsonObject downloads
            && downloads["artifact"] is JsonObject artifact
            && artifact["path"] is JsonNode pathNode)
        {
            return Path.Combine(librariesDir, pathNode.GetValue<string>());
        }

        // 回退到 Maven 坐标解析
        var name = lib["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
            return null;

        return ResolveMavenPath(librariesDir, name, null);
    }

    /// <summary>
    /// 从 Maven 坐标解析库文件路径
    /// 格式: group:artifact:version[:classifier]
    ///   → group.replace(".", "/")/artifact/version/artifact-version[-classifier].jar
    /// </summary>
    private static string ResolveMavenPath(string librariesDir, string mavenName, string? classifier)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return string.Empty;

        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];

        // 文件名: artifact-version[-classifier].jar
        var fileName = string.IsNullOrEmpty(classifier)
            ? $"{artifact}-{version}.jar"
            : $"{artifact}-{version}-{classifier}.jar";

        return Path.Combine(librariesDir, groupPath, artifact, version, fileName);
    }

    // =========================================================================
    // Rules 检查
    // =========================================================================

    /// <summary>
    /// 检查 rules 是否匹配当前平台 (Windows)
    /// rules 格式: [{"action": "allow", "os": {"name": "windows"}}, ...]
    /// - 没有 rules 默认允许
    /// - 有 os 字段的 rule 只在平台匹配时生效
    /// - 没有 os 字段的 rule 对所有平台生效
    /// </summary>
    private static bool CheckRules(JsonNode? rulesNode)
    {
        if (rulesNode is not JsonArray rules)
            return true; // 没有 rules 默认允许

        var allowed = true; // 默认允许

        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule)
                continue;

            var action = rule["action"]?.GetValue<string>() ?? "allow";

            if (rule["os"] is JsonObject os)
            {
                var osName = os["name"]?.GetValue<string>() ?? "";
                var isCurrentPlatform = osName == "windows";

                if (isCurrentPlatform)
                {
                    // 当前平台匹配，应用 action
                    allowed = action == "allow";
                }
                // 当前平台不匹配，不修改 allowed
            }
            else
            {
                // 没有平台限制，直接应用 action
                allowed = action == "allow";
            }
        }

        return allowed;
    }

    // =========================================================================
    // Log4j 配置
    // =========================================================================

    /// <summary>
    /// 获取 log4j 配置文件路径 — 从版本 JSON 的 logging.client.file.id 字段获取
    /// 文件位于 .minecraft/assets/log_configs/ 目录
    /// 例如: logging.client.file.id = "client-1.21.2.xml"
    ///   → .minecraft/assets/log_configs/client-1.21.2.xml
    /// </summary>
    private static string? GetLog4jConfigPath(string mcDir, JsonObject version)
    {
        var fileId = version["logging"]?["client"]?["file"]?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(fileId))
            return null;

        var log4jPath = Path.Combine(mcDir, "assets", "log_configs", fileId);
        return File.Exists(log4jPath) ? log4jPath : null;
    }

    // =========================================================================
    // JVM 参数构建
    // =========================================================================

    /// <summary>
    /// 构建 JVM 参数 — 手动构建所有核心 JVM 参数
    /// 参考 PCL CE 的 JVM 参数列表，不解析版本 JSON 的 arguments.jvm
    /// </summary>
    private static List<string> BuildJvmArgs(string classpath, string nativesDir, string? log4jPath, LauncherSettings settings, bool isFabric)
    {
        var args = new List<string>();

        // 内存分配
        var memoryMB = settings.MemoryMB;
        var newGenMB = (int)(memoryMB * 0.15); // 新生代 = 最大堆的 15%
        args.Add($"-Xmx{memoryMB}m");
        args.Add($"-Xmn{newGenMB}m");

        // natives 相关路径 — 指定原生库提取/加载路径
        args.Add($"-Djava.library.path={nativesDir}");
        args.Add($"-Djna.tmpdir={nativesDir}");
        args.Add($"-Dorg.lwjgl.system.SharedLibraryExtractPath={nativesDir}");
        args.Add($"-Dio.netty.native.workdir={nativesDir}");

        // 启动器信息
        args.Add($"-Dminecraft.launcher.brand={LauncherBrand}");
        args.Add($"-Dminecraft.launcher.version={LauncherVersion}");

        // Log4j 防御 (CVE-2021-44228)
        args.Add("-Dlog4j2.formatMsgNoLookups=true");

        // log4j 配置文件 (从版本 JSON logging 字段获取)
        if (log4jPath != null)
        {
            args.Add($"-Dlog4j.configurationFile={log4jPath}");
        }

        // 堆转储路径
        args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");

        // Fabric 特定参数: 指定模拟的原版主类 (注意等号后有空格，Fabric Loader 特有设计)
        if (isFabric)
        {
            args.Add($"-DFabricMcEmu= {VanillaMainClass}");
        }

        // classpath — 放在 JVM 参数末尾，主类之前
        args.Add("-cp");
        args.Add(classpath);

        return args;
    }

    // =========================================================================
    // 游戏参数构建
    // =========================================================================

    /// <summary>
    /// 构建游戏参数 — 参照 PCL CE 手动构建，不解析版本 JSON 的 arguments.game
    /// PCL CE 也是手动构建所有游戏参数，避免模板变量替换问题
    /// </summary>
    private static List<string> BuildGameArgs(JsonObject version, string versionId, string gameDir, string assetsDir, OfflineAccount account, bool enableQuickPlay, int majorVersion, LauncherSettings settings, string assetsIndex)
    {
        var args = new List<string>();

        // 从版本 JSON 获取 version_type (release/snapshot/old_beta 等)
        var versionType = version["type"]?.GetValue<string>() ?? "release";

        // 手动构建游戏参数 — 参照 PCL CE McLaunchArgumentMain
        args.Add("--username");
        args.Add(account.Username);
        args.Add("--version");
        args.Add(versionId);
        args.Add("--gameDir");
        args.Add(gameDir);
        args.Add("--assetsDir");
        args.Add(assetsDir);
        args.Add("--assetIndex");
        args.Add(assetsIndex);
        args.Add("--uuid");
        args.Add(account.Uuid);
        args.Add("--accessToken");
        args.Add(account.AccessToken);
        args.Add("--userType");
        args.Add("msa"); // PCL2-CE 统一使用 "msa"，"offline" 会触发 "Unrecognized user type" 警告
        args.Add("--versionType");
        args.Add(versionType);

        // 窗口尺寸
        args.Add("--width");
        args.Add(settings.WindowWidth.ToString());
        args.Add("--height");
        args.Add(settings.WindowHeight.ToString());

        if (settings.AutoFullscreen)
        {
            args.Add("--fullscreen");
        }

        // QuickPlay 参数 (1.21+ 使用 --quickPlayMultiplayer，旧版用 --server --port)
        if (enableQuickPlay)
        {
            if (majorVersion >= 21)
            {
                args.Add("--quickPlayMultiplayer");
                args.Add($"{settings.ServerAddress}:{settings.ServerPort}");
            }
            else
            {
                args.Add("--server");
                args.Add(settings.ServerAddress);
                args.Add("--port");
                args.Add(settings.ServerPort.ToString());
            }
        }

        return args;
    }

    // =========================================================================
    // 版本号解析
    // =========================================================================

    /// <summary>
    /// 从版本 ID 提取主版本号
    /// 支持格式:
    ///   "1.21.3" → 21
    ///   "1.20" → 20
    ///   "fabric-loader-0.16.14-1.21.3" → 21 (从 Fabric 版本 ID 中提取原版版本号)
    /// </summary>
    private static int ExtractMajorVersion(string versionId)
    {
        // 使用正则匹配 Minecraft 版本号 (1.xx 或 1.xx.xx)
        var match = Regex.Match(versionId, @"1\.(\d+)(\.\d+)?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var major))
        {
            return major;
        }
        return 0;
    }

    // =========================================================================
    // 状态枚举
    // =========================================================================

    /// <summary>启动状态枚举</summary>
    private enum LaunchState
    {
        Idle,
        Preparing,
        Launching,
        Running,
    }

    // =========================================================================
    // Win32 API — 修改游戏窗口标题
    // =========================================================================

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// 查找指定进程的可见窗口句柄 — 用于定位 Minecraft 游戏窗口
    /// </summary>
    private static IntPtr FindMinecraftWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                result = hWnd;
                return false; // 找到了，停止枚举
            }
            return true; // 继续枚举
        }, IntPtr.Zero);
        return result;
    }
}
