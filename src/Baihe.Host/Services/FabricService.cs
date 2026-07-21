// Fabric 安装服务 — 从 Fabric Meta API 获取并安装 Fabric Loader
// 安装流程: 查询 Loader 版本 → 获取 Profile JSON → 下载库文件
// Profile JSON 是标准 Mojang 版本 JSON 格式，包含 Fabric Loader 和 Intermediary 映射

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Baihe.Host.Ipc;

namespace Baihe.Host.Services;

/// <summary>
/// Fabric 安装服务 — 安装 Fabric Loader 到指定游戏版本
/// </summary>
public static class FabricService
{
    /// <summary>Fabric Meta API 基地址</summary>
    private const string MetaApiBase = "https://meta.fabricmc.net/v2/versions/loader";

    /// <summary>HTTP 客户端</summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// 安装 Fabric Loader — 自动选择最新稳定版本
    /// </summary>
    /// <param name="gameVersion">Minecraft 版本号 (如 1.20.4)</param>
    public static async Task<object> Install(string gameVersion)
    {
        try
        {
            // 1. 查询可用的 Loader 版本
            IpcRouter.PushEvent("fabric.progress", new { phase = "querying", message = "正在查询 Fabric Loader 版本..." });

            var loaderVersionsUrl = $"{MetaApiBase}/{gameVersion}";
            var loaderJson = await _http.GetStringAsync(loaderVersionsUrl);
            using var loaderDoc = JsonDocument.Parse(loaderJson);

            var loaderArray = loaderDoc.RootElement;
            if (loaderArray.GetArrayLength() == 0)
            {
                return new { success = false, error = $"Fabric 不支持 Minecraft {gameVersion}" };
            }

            // 选择第一个（最新）Loader 版本
            var firstEntry = loaderArray[0];
            var loaderVersion = firstEntry.GetProperty("loader").GetProperty("version").GetString()!;
            var intermediaryVersion = firstEntry.GetProperty("intermediary").GetProperty("version").GetString()!;

            IpcRouter.PushEvent("fabric.progress", new
            {
                phase = "found",
                message = $"Fabric Loader {loaderVersion}",
                loaderVersion,
                intermediaryVersion,
            });

            // 2. 获取 Profile JSON
            IpcRouter.PushEvent("fabric.progress", new { phase = "profile", message = "正在获取 Fabric Profile..." });

            var profileUrl = $"{MetaApiBase}/{gameVersion}/{loaderVersion}/profile/json";
            var profileJson = await _http.GetStringAsync(profileUrl);

            // 修改 Profile JSON 中的 ID 为自定义版本名
            // 使用自定义版本 ID: {gameVersion}-fabric
            var fabricVersionId = $"{gameVersion}-fabric";

            // 修改 JSON 中的 id 字段（需要重新序列化）
            var modifiedJson = ModifyVersionId(profileJson, fabricVersionId);

            // 3. 下载所有文件
            IpcRouter.PushEvent("fabric.progress", new { phase = "downloading", message = "正在下载 Fabric 库文件..." });

            var result = await DownloadService.DownloadVersionFromJson(fabricVersionId, modifiedJson);

            IpcRouter.PushEvent("fabric.complete", new { success = true, versionId = fabricVersionId });

            return result;
        }
        catch (Exception ex)
        {
            IpcRouter.PushEvent("fabric.error", new { error = ex.Message });
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 获取可用的 Fabric Loader 版本列表
    /// </summary>
    public static async Task<object> GetLoaders(string gameVersion)
    {
        var url = $"{MetaApiBase}/{gameVersion}";
        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var loaders = new List<object>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var loader = entry.GetProperty("loader");
            loaders.Add(new
            {
                version = loader.GetProperty("version").GetString(),
                stable = loader.GetProperty("stable").GetBoolean(),
            });
        }

        return new { gameVersion, loaders };
    }

    /// <summary>
    /// 修改版本 JSON 中的 id 字段
    /// </summary>
    private static string ModifyVersionId(string json, string newId)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.NameEquals("id"))
            {
                writer.WriteString("id", newId);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
