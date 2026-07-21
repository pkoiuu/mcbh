// 下载服务 — Minecraft 游戏文件下载管线
// 下载版本 JSON → 客户端 JAR → 库文件 → 资源索引 → 资源文件
// 支持进度推送 (IpcRouter.PushEvent) 和 SHA1 校验

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baihe.Host.Ipc;

namespace Baihe.Host.Services;

/// <summary>
/// 下载服务 — 编排 Minecraft 游戏文件下载流程
/// </summary>
public static class DownloadService
{
    /// <summary>HTTP 客户端（长连接复用）</summary>
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    /// <summary>并发下载限制</summary>
    private static readonly SemaphoreSlim _concurrencyLimiter = new(6);

    /// <summary>下载状态</summary>
    private static bool _isDownloading;
    private static string _error = string.Empty;

    /// <summary>
    /// 下载完整版本 — 版本 JSON + 客户端 JAR + 库文件 + 资源文件
    /// </summary>
    public static async Task<object> DownloadVersion(string versionId)
    {
        if (_isDownloading)
            return new { success = false, error = "已有下载任务在进行中" };

        _isDownloading = true;
        _error = string.Empty;

        try
        {
            var mcDir = InstanceService.GetMcDirectory();
            Directory.CreateDirectory(mcDir);

            // 1. 获取版本 JSON URL
            PushProgress("versionJson", "正在获取版本信息...", 0, 1, 0, 0);
            var versionUrl = await VersionService.GetVersionUrlAsync(versionId);
            if (string.IsNullOrEmpty(versionUrl))
            {
                return new { success = false, error = $"未找到版本: {versionId}" };
            }

            // 2. 下载版本 JSON
            var versionJson = await _http.GetStringAsync(versionUrl);
            var versionDir = Path.Combine(mcDir, "versions", versionId);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(versionJsonPath, versionJson);

            PushProgress("versionJson", "版本信息已获取", 1, 1, 0, 0);

            // 解析版本 JSON
            using var doc = JsonDocument.Parse(versionJson);
            var root = doc.RootElement;

            // 3. 下载客户端 JAR
            await DownloadClientJar(mcDir, versionId, root);

            // 4. 下载库文件
            await DownloadLibraries(mcDir, root);

            // 5. 下载资源文件
            await DownloadAssets(mcDir, root);

            PushComplete();
            return new { success = true };
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            PushError(ex.Message);
            return new { success = false, error = ex.Message };
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>
    /// 从已有的版本 JSON 下载所有文件 — 用于 Fabric 等自定义版本
    /// </summary>
    public static async Task<object> DownloadVersionFromJson(string versionId, string versionJson)
    {
        if (_isDownloading)
            return new { success = false, error = "已有下载任务在进行中" };

        _isDownloading = true;
        _error = string.Empty;

        try
        {
            var mcDir = InstanceService.GetMcDirectory();
            Directory.CreateDirectory(mcDir);

            // 保存版本 JSON
            var versionDir = Path.Combine(mcDir, "versions", versionId);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(versionJsonPath, versionJson);

            PushProgress("versionJson", "版本信息已就绪", 1, 1, 0, 0);

            using var doc = JsonDocument.Parse(versionJson);
            var root = doc.RootElement;

            // 下载客户端 JAR（如果有）
            await DownloadClientJar(mcDir, versionId, root);

            // 下载库文件
            await DownloadLibraries(mcDir, root);

            // 下载资源文件
            await DownloadAssets(mcDir, root);

            PushComplete();
            return new { success = true };
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            PushError(ex.Message);
            return new { success = false, error = ex.Message };
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>
    /// 下载客户端 JAR
    /// </summary>
    private static async Task DownloadClientJar(string mcDir, string versionId, JsonElement root)
    {
        if (!root.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("client", out var client))
            return;

        var url = client.GetProperty("url").GetString()!;
        var sha1 = client.TryGetProperty("sha1", out var sha1Prop) ? sha1Prop.GetString() : null;
        var size = client.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

        var jarPath = Path.Combine(mcDir, "versions", versionId, $"{versionId}.jar");

        PushProgress("client", $"客户端 JAR ({FormatSize(size)})", 0, 1, 0, size);
        await DownloadFileAsync(url, jarPath, sha1);
        PushProgress("client", "客户端 JAR 已完成", 1, 1, size, size);
    }

    /// <summary>
    /// 下载所有库文件 — 支持 Mojang 格式 (downloads.artifact) 和 Maven 格式 (name + url)
    /// </summary>
    private static async Task DownloadLibraries(string mcDir, JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries))
            return;

        var librariesDir = Path.Combine(mcDir, "libraries");
        var downloadList = new List<(string url, string path, string? sha1, long size)>();

        foreach (var lib in libraries.EnumerateArray())
        {
            // 检查 rules
            if (lib.TryGetProperty("rules", out var rules))
            {
                if (!CheckRules(rules))
                    continue;
            }

            string? url = null;
            string? relPath = null;
            string? sha1 = null;
            long size = 0;

            // 优先使用 downloads.artifact (Mojang 格式)
            if (lib.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("artifact", out var artifact))
            {
                url = artifact.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                relPath = artifact.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
                sha1 = artifact.TryGetProperty("sha1", out var sha1Prop) ? sha1Prop.GetString() : null;
                size = artifact.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;
            }

            // 回退到 Maven 格式 (Fabric/Forge 格式: name + url)
            if (url == null && lib.TryGetProperty("name", out var nameProp) && lib.TryGetProperty("url", out var urlProp2))
            {
                var mavenName = nameProp.GetString()!;
                var mavenUrl = urlProp2.GetString()!;
                relPath = ResolveMavenPath(mavenName);
                url = mavenUrl.TrimEnd('/') + "/" + relPath.Replace('\\', '/');
            }

            if (url != null && relPath != null)
            {
                downloadList.Add((url, Path.Combine(librariesDir, relPath), sha1, size));
            }
        }

        await DownloadFilesConcurrent("libraries", downloadList);
    }

    /// <summary>
    /// 从 Maven 坐标解析库文件相对路径
    /// 格式: group:artifact:version → group/artifact/version/artifact-version.jar
    /// </summary>
    private static string ResolveMavenPath(string mavenName)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return string.Empty;

        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];

        // 处理 classifier 格式: group:artifact:version:classifier
        var fileName = parts.Length > 3
            ? $"{artifact}-{version}-{parts[3]}.jar"
            : $"{artifact}-{version}.jar";

        return Path.Combine(groupPath, artifact, version, fileName);
    }

    /// <summary>
    /// 下载资源索引和资源文件
    /// </summary>
    private static async Task DownloadAssets(string mcDir, JsonElement root)
    {
        if (!root.TryGetProperty("assetIndex", out var assetIndex))
            return;

        var indexUrl = assetIndex.GetProperty("url").GetString()!;
        var indexId = assetIndex.GetProperty("id").GetString() ?? "default";
        var indexDir = Path.Combine(mcDir, "assets", "indexes");
        var indexPath = Path.Combine(indexDir, $"{indexId}.json");

        PushProgress("assetIndex", "正在获取资源索引...", 0, 1, 0, 0);
        var indexJson = await _http.GetStringAsync(indexUrl);
        Directory.CreateDirectory(indexDir);
        await File.WriteAllTextAsync(indexPath, indexJson);
        PushProgress("assetIndex", "资源索引已获取", 1, 1, 0, 0);

        // 解析资源索引
        using var indexDoc = JsonDocument.Parse(indexJson);
        if (!indexDoc.RootElement.TryGetProperty("objects", out var objects))
            return;

        var objectsDir = Path.Combine(mcDir, "assets", "objects");
        var downloadList = new List<(string url, string path, string? sha1, long size)>();

        foreach (var prop in objects.EnumerateObject())
        {
            var obj = prop.Value;
            var hash = obj.TryGetProperty("hash", out var hashProp) ? hashProp.GetString() : null;
            var size = obj.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

            if (hash == null)
                continue;

            // 资源文件存储路径: assets/objects/<hash前两位>/<hash>
            var subDir = hash[..2];
            var filePath = Path.Combine(objectsDir, subDir, hash);
            var url = $"https://resources.download.minecraft.net/{subDir}/{hash}";

            downloadList.Add((url, filePath, hash, size));
        }

        await DownloadFilesConcurrent("assets", downloadList);
    }

    /// <summary>
    /// 并发下载文件列表 — 带进度推送
    /// </summary>
    private static async Task DownloadFilesConcurrent(string phase, List<(string url, string path, string? sha1, long size)> files)
    {
        // 过滤已存在且校验通过的文件
        var toDownload = files.Where(f => !IsFileValid(f.path, f.sha1)).ToList();
        var totalSize = toDownload.Sum(f => f.size);
        var completedFiles = 0;
        var downloadedBytes = 0L;
        var totalFiles = toDownload.Count;

        if (totalFiles == 0)
        {
            PushProgress(phase, "所有文件已存在", files.Count, files.Count, 0, 0);
            return;
        }

        PushProgress(phase, $"正在下载 {totalFiles} 个文件...", 0, totalFiles, 0, totalSize);

        var lockObj = new object();

        var tasks = toDownload.Select(async file =>
        {
            await _concurrencyLimiter.WaitAsync();
            try
            {
                await DownloadFileAsync(file.url, file.path, file.sha1);

                lock (lockObj)
                {
                    completedFiles++;
                    downloadedBytes += file.size;
                    PushProgress(phase, Path.GetFileName(file.path), completedFiles, totalFiles, downloadedBytes, totalSize);
                }
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 下载单个文件 — 带目录创建和 SHA1 校验
    /// </summary>
    private static async Task DownloadFileAsync(string url, string filePath, string? expectedSha1)
    {
        // 如果文件已存在且校验通过，跳过
        if (IsFileValid(filePath, expectedSha1))
            return;

        // 创建目录
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        // 下载到临时文件，校验通过后重命名
        var tempPath = filePath + ".tmp";

        try
        {
            using var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(tempPath);
            await response.Content.CopyToAsync(fileStream);
        }
        catch
        {
            // 下载失败，清理临时文件
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        // SHA1 校验
        if (expectedSha1 != null)
        {
            var actualSha1 = await ComputeSha1Async(tempPath);
            if (!actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                throw new InvalidOperationException($"SHA1 校验失败: {Path.GetFileName(filePath)}");
            }
        }

        // 校验通过，替换文件
        if (File.Exists(filePath))
            File.Delete(filePath);
        File.Move(tempPath, filePath);
    }

    /// <summary>
    /// 检查文件是否已存在且 SHA1 校验通过
    /// </summary>
    private static bool IsFileValid(string path, string? expectedSha1)
    {
        if (!File.Exists(path))
            return false;

        // 如果没有预期 SHA1，只要文件存在就认为有效
        if (expectedSha1 == null)
            return true;

        try
        {
            var actualSha1 = ComputeSha1Async(path).GetAwaiter().GetResult();
            return actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 计算文件的 SHA1 哈希
    /// </summary>
    private static async Task<string> ComputeSha1Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA1.HashDataAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 检查 rules 是否匹配当前平台
    /// </summary>
    private static bool CheckRules(JsonElement rules)
    {
        var allowed = true;

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
    /// 格式化文件大小
    /// </summary>
    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    /// <summary>
    /// 推送下载进度事件
    /// </summary>
    private static void PushProgress(string phase, string currentFile, int completedFiles, int totalFiles, long downloadedBytes, long totalBytes)
    {
        var percent = totalFiles > 0 ? (double)completedFiles / totalFiles * 100 : 0;
        IpcRouter.PushEvent("download.progress", new
        {
            phase,
            currentFile,
            completedFiles,
            totalFiles,
            downloadedBytes,
            totalBytes,
            percent = Math.Round(percent, 1),
        });
    }

    /// <summary>
    /// 推送下载完成事件
    /// </summary>
    private static void PushComplete()
    {
        IpcRouter.PushEvent("download.complete", new { success = true });
    }

    /// <summary>
    /// 推送下载错误事件
    /// </summary>
    private static void PushError(string error)
    {
        IpcRouter.PushEvent("download.error", new { error });
    }

    /// <summary>
    /// 获取下载状态
    /// </summary>
    public static object GetStatus()
    {
        return new { isDownloading = _isDownloading, error = _error };
    }
}
