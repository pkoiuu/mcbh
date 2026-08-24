// 新闻服务 — 拉取仓库 news.json 的最新动态（首页「最新动态」数据源）
// 每次进入主页时拉取，失败回退内置内容；增删 news.json 无需发布新版本即可更新公告

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 新闻服务 — 提供首页最新动态
/// </summary>
public static class NewsService
{
    private static readonly HttpClient _httpClient = new();

    private const string RepoOwner = "pkoiuu";
    private const string RepoName = "mcbh";

    /// <summary>新闻数据源 — 按顺序尝试：raw 直连 → jsDelivr CDN → ghproxy 镜像，全部失败回退内置</summary>
    private static readonly string[] NewsJsonUrls =
    {
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/news.json",
        $"https://cdn.jsdelivr.net/gh/{RepoOwner}/{RepoName}@main/news.json",
        $"https://ghproxy.net/https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/news.json",
    };

    /// <summary>单个源拉取超时（毫秒）</summary>
    private const int FetchTimeoutMs = 4000;

    /// <summary>内置默认新闻（兜底）</summary>
    private static readonly List<object> BuiltinNews = new()
    {
        new { date = "08·25", title = "v1.1.10 发布：玩家指南 + 默认允许资源包 + 服务器列表选择", desc = "新增玩家指南维基（可搜索）；启动默认允许服务器资源包；主页支持服务器列表选择。" },
        new { date = "08·24", title = "v1.1.9 发布：防多开 + 光影介绍优化", desc = "单实例防多开；光影悬浮说明不再遮挡启用按钮；工具页切 Tab 卡顿优化。" },
        new { date = "08·22", title = "内置游戏升级 1.21.8", desc = "内置 Minecraft 1.21.8 + Fabric，新增光影管理。" },
        new { date = "07·23", title = "自研白鹤服务器启动器 1.0 正式版正式发布", desc = "全新 UI，超高效率，内置聊天工具。" },
    };

    static NewsService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BaiheLauncher", "1.0"));
    }

    /// <summary>
    /// 获取最新动态 — 按顺序尝试多个数据源（raw/CDN/镜像），全部失败回退内置
    /// </summary>
    public static async Task<List<object>> GetNewsAsync()
    {
        foreach (var url in NewsJsonUrls)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(FetchTimeoutMs));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                    continue; // 该源失败，尝试下一个

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("news", out var newsProp)
                    || newsProp.ValueKind != JsonValueKind.Array)
                    continue;

                var news = new List<object>();
                foreach (var item in newsProp.EnumerateArray())
                {
                    var date = item.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var desc = item.TryGetProperty("desc", out var de) ? de.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(title))
                        news.Add(new { date, title, desc });
                }
                if (news.Count > 0)
                    return news;
            }
            catch
            {
                // 单个源异常（超时/解析失败）→ 尝试下一个源
            }
        }

        return BuiltinNews;
    }
}
