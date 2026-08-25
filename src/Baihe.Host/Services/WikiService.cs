// 维基服务 — 拉取仓库 wiki.json 的玩家指南内容（启动器维基的远程数据源）
// 数据源: 仓库根 wiki.json（由 scripts/generate-wiki-json.mjs 从 lib/wiki/*.ts 生成，也可直接手工编辑）
// 每次进入维基页拉取，失败返回 null（前端回退内置 lib/wiki 内容）；编辑 wiki.json 无需发版即可更新维基

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 维基服务 — 提供玩家指南远程内容（wiki.json）
/// </summary>
public static class WikiService
{
    private static readonly HttpClient _httpClient = new();

    private const string RepoOwner = "pkoiuu";
    private const string RepoName = "mcbh";

    /// <summary>维基数据源 — 按顺序尝试：raw 直连 → jsDelivr CDN → ghproxy 镜像</summary>
    private static readonly string[] WikiJsonUrls =
    {
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/wiki.json",
        $"https://cdn.jsdelivr.net/gh/{RepoOwner}/{RepoName}@main/wiki.json",
        $"https://ghproxy.net/https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/wiki.json",
    };

    /// <summary>单个源拉取超时（毫秒）</summary>
    private const int FetchTimeoutMs = 5000;

    static WikiService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BaiheLauncher", "1.0"));
    }

    /// <summary>
    /// 获取维基分类列表 — 多源回退；全部失败返回 null（前端回退内置）
    /// </summary>
    public static async Task<List<object>?> GetWikiAsync()
    {
        foreach (var url in WikiJsonUrls)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(FetchTimeoutMs));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("categories", out var catsProp)
                    || catsProp.ValueKind != JsonValueKind.Array
                    || catsProp.GetArrayLength() == 0)
                    continue;

                var categories = new List<object>();
                foreach (var cat in catsProp.EnumerateArray())
                {
                    if (cat.ValueKind == JsonValueKind.Object)
                        categories.Add(JsonSerializer.Deserialize<object>(cat.GetRawText())!);
                }
                if (categories.Count > 0)
                    return categories;
            }
            catch
            {
                // 单个源异常（超时/解析失败）→ 尝试下一个源
            }
        }

        return null;
    }
}
