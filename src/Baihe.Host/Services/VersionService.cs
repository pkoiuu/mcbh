// 版本清单服务 — 从 Mojang API 获取 Minecraft 版本列表
// 使用 NetworkService 预配置的 MojangPistonMeta HttpClient

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 版本清单服务 — 获取 Mojang 官方版本列表
/// </summary>
public static class VersionService
{
    /// <summary>Mojang 版本清单 API</summary>
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /// <summary>本地缓存路径</summary>
    private static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "cache", "version_manifest.json");

    /// <summary>缓存有效期（24 小时）</summary>
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);

    /// <summary>
    /// 获取版本清单 — 优先使用缓存，过期则从 Mojang API 获取
    /// </summary>
    public static async Task<object> GetVersionList(string? typeFilter = null)
    {
        var manifest = await GetManifestAsync();

        var versions = new List<object>();
        foreach (var v in manifest.Versions)
        {
            if (typeFilter != null && v.Type != typeFilter)
                continue;

            versions.Add(new
            {
                id = v.Id,
                type = v.Type,
                releaseTime = v.ReleaseTime,
                url = v.Url,
            });
        }

        return new
        {
            latest = manifest.Latest,
            versions = versions,
        };
    }

    /// <summary>
    /// 获取指定版本的 JSON URL
    /// </summary>
    public static async Task<string?> GetVersionUrlAsync(string versionId)
    {
        var manifest = await GetManifestAsync();
        return manifest.Versions.Find(v => v.Id == versionId)?.Url;
    }

    /// <summary>
    /// 获取完整版本清单 — 带缓存
    /// </summary>
    private static async Task<VersionManifest> GetManifestAsync()
    {
        // 检查本地缓存
        if (File.Exists(CachePath))
        {
            var cacheAge = DateTime.Now - File.GetLastWriteTime(CachePath);
            if (cacheAge < CacheExpiry)
            {
                var cachedJson = await File.ReadAllTextAsync(CachePath);
                var cached = JsonSerializer.Deserialize<VersionManifest>(cachedJson, JsonOptions);
                if (cached != null)
                    return cached;
            }
        }

        // 从 Mojang API 获取
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        var json = await http.GetStringAsync(ManifestUrl);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("无法解析版本清单");

        // 保存到本地缓存
        try
        {
            var cacheDir = Path.GetDirectoryName(CachePath);
            if (cacheDir != null)
                Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(CachePath, json);
        }
        catch
        {
            // 缓存写入失败不影响功能
        }

        return manifest;
    }

    /// <summary>JSON 序列化选项</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>版本清单模型</summary>
    private record VersionManifest(
        [property: JsonPropertyName("latest")] LatestInfo Latest,
        [property: JsonPropertyName("versions")] List<VersionEntry> Versions
    );

    /// <summary>最新版本信息</summary>
    private record LatestInfo(
        [property: JsonPropertyName("release")] string Release,
        [property: JsonPropertyName("snapshot")] string Snapshot
    );

    /// <summary>版本条目</summary>
    private record VersionEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("releaseTime")] string ReleaseTime
    );
}
