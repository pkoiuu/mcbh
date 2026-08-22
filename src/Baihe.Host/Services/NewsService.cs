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

    /// <summary>新闻数据源 — 仓库根目录 news.json（raw 直连，失败回退内置）</summary>
    private static readonly string NewsJsonUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/news.json";

    /// <summary>拉取超时（毫秒）</summary>
    private const int FetchTimeoutMs = 4000;

    /// <summary>内置默认新闻（兜底）</summary>
    private static readonly List<object> BuiltinNews = new()
    {
        new { date = "08·23", title = "v1.1.7 发布：输入法冲突修复 + 光影悬浮说明优化", desc = "预装 IMBlocker 输入法修复模组；光影卡片悬浮说明不再遮挡启用按钮。" },
        new { date = "08·22", title = "内置游戏升级 1.21.8", desc = "内置 Minecraft 1.21.8 + Fabric，新增光影管理。" },
        new { date = "07·23", title = "自研白鹤服务器启动器 1.0 正式版正式发布", desc = "全新 UI，超高效率，内置聊天工具。" },
    };

    static NewsService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BaiheLauncher", "1.0"));
    }

    /// <summary>
    /// 获取最新动态 — 从仓库 news.json 拉取；失败回退内置
    /// </summary>
    public static async Task<List<object>> GetNewsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(FetchTimeoutMs));
            using var request = new HttpRequestMessage(HttpMethod.Get, NewsJsonUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return BuiltinNews;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("news", out var newsProp)
                || newsProp.ValueKind != JsonValueKind.Array)
                return BuiltinNews;

            var news = new List<object>();
            foreach (var item in newsProp.EnumerateArray())
            {
                var date = item.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var desc = item.TryGetProperty("desc", out var de) ? de.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(title))
                    news.Add(new { date, title, desc });
            }
            return news.Count > 0 ? news : BuiltinNews;
        }
        catch
        {
            return BuiltinNews;
        }
    }
}
