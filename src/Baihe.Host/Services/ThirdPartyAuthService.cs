// 第三方验证服务 — Yggdrasil / Authlib-Injector 认证流程
// 参照 PCL2-CE PCL.Core\Minecraft\IdentityModel\Yggdrasil\Client.cs
// 协议规范: https://yushijinhun.github.io/authlib-injector/zh/Yggdrasil-服务端技术规范.html
// 启动器规范: https://yushijinhun.github.io/authlib-injector/zh/启动器技术规范.html

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 第三方验证服务 — Yggdrasil (Authlib-Injector) 认证
/// </summary>
public static class ThirdPartyAuthService
{
    /// <summary>HTTP 客户端 (禁用自动重定向，手动处理 ALI 指示与 HTTP 重定向)</summary>
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>JSON 序列化选项 (camelCase，与 Yggdrasil API 一致)</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>ALI 解析最大重定向次数</summary>
    private const int MaxAliRedirects = 5;

    /// <summary>预设验证服务器</summary>
    public static readonly Dictionary<string, string> PresetServers = new()
    {
        { "LittleSkin", "https://littleskin.cn/api/yggdrasil" },
    };

    /// <summary>
    /// 第三方验证登录
    /// </summary>
    /// <param name="serverUrl">验证服务器地址 (如 https://littleskin.cn/api/yggdrasil)</param>
    /// <param name="username">用户名或邮箱</param>
    /// <param name="password">密码</param>
    /// <returns>认证成功后的 McAccount</returns>
    public static async Task<McAccount> Login(string serverUrl, string username, string password)
    {
        // 1. 解析 API Location (处理 ALI 指示)
        var apiLocation = await ResolveApiLocation(serverUrl);

        // 2. 获取服务器名称
        var serverName = await GetServerName(apiLocation);

        // 3. 发送 authenticate 请求
        var requestBody = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username,
            password,
            requestUser = true,
        };
        var responseJson = await PostJsonAsync($"{apiLocation}/authserver/authenticate", requestBody);
        CheckError(responseJson);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("accessToken").GetString()!;
        var clientToken = root.GetProperty("clientToken").GetString()!;

        // 4. 处理角色选择
        string? profileId = null;
        string? profileName = null;

        if (root.TryGetProperty("selectedProfile", out var selectedProfile) &&
            selectedProfile.ValueKind == JsonValueKind.Object)
        {
            // 服务器已绑定角色
            profileId = selectedProfile.GetProperty("id").GetString();
            profileName = selectedProfile.GetProperty("name").GetString();
        }
        else if (root.TryGetProperty("availableProfiles", out var availableProfiles) &&
                 availableProfiles.ValueKind == JsonValueKind.Array &&
                 availableProfiles.GetArrayLength() > 0)
        {
            // selectedProfile 为空，从 availableProfiles 选择第一个角色
            // 多角色场景下应由 UI 层提供选择，此处取第一个
            var first = availableProfiles[0];
            profileId = first.GetProperty("id").GetString();
            profileName = first.GetProperty("name").GetString();

            // 通过 refresh 将选定角色绑定到令牌
            var refreshBody = new
            {
                accessToken,
                clientToken,
                selectedProfile = new { id = profileId, name = profileName },
                requestUser = true,
            };
            var refreshJson = await PostJsonAsync($"{apiLocation}/authserver/refresh", refreshBody);
            CheckError(refreshJson);

            using var refreshDoc = JsonDocument.Parse(refreshJson);
            var refreshRoot = refreshDoc.RootElement;
            accessToken = refreshRoot.GetProperty("accessToken").GetString()!;
            // clientToken 不变
        }
        else
        {
            throw new InvalidOperationException("该账户没有可用的游戏角色");
        }

        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(profileName))
            throw new InvalidOperationException("服务器返回的角色信息不完整");

        return new McAccount
        {
            Type = AccountType.ThirdParty,
            Username = profileName,
            Uuid = StripUuid(profileId),
            AccessToken = accessToken,
            RefreshToken = clientToken, // 复用 RefreshToken 字段存储 clientToken
            AuthServer = apiLocation,
            AuthServerName = serverName,
            Password = password,
            IsUserSet = true,
        };
    }

    /// <summary>
    /// 刷新第三方验证令牌
    /// </summary>
    /// <param name="account">需刷新的账户 (须为 ThirdParty 类型)</param>
    /// <returns>刷新后的 McAccount；若刷新失败返回 null</returns>
    public static async Task<McAccount?> Refresh(McAccount account)
    {
        if (account.Type != AccountType.ThirdParty)
            return null;

        if (string.IsNullOrEmpty(account.AuthServer) || string.IsNullOrEmpty(account.AccessToken))
            return null;

        var apiLocation = account.AuthServer;
        var clientToken = account.RefreshToken ?? string.Empty;

        var requestBody = new
        {
            accessToken = account.AccessToken,
            clientToken,
            requestUser = true,
        };

        string responseJson;
        try
        {
            responseJson = await PostJsonAsync($"{apiLocation}/authserver/refresh", requestBody);
        }
        catch
        {
            return null;
        }

        // 刷新失败 (令牌无效等)
        try
        {
            CheckError(responseJson);
        }
        catch
        {
            return null;
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        account.AccessToken = root.GetProperty("accessToken").GetString()!;
        account.RefreshToken = root.GetProperty("clientToken").GetString()!;

        // 更新角色信息 (如果返回了 selectedProfile)
        if (root.TryGetProperty("selectedProfile", out var selectedProfile) &&
            selectedProfile.ValueKind == JsonValueKind.Object)
        {
            var id = selectedProfile.GetProperty("id").GetString();
            var name = selectedProfile.GetProperty("name").GetString();
            if (!string.IsNullOrEmpty(id))
                account.Uuid = StripUuid(id);
            if (!string.IsNullOrEmpty(name))
                account.Username = name;
        }

        return account;
    }

    /// <summary>
    /// 解析验证服务器地址 — 处理 X-Authlib-Injector-Api-Location 头
    /// <para>遵循 authlib-injector 启动器技术规范:
    /// 向当前地址发送请求，若响应包含 ALI 头则跟随指示，
    /// 否则当前地址即为 API 地址。最多重定向 5 次。</para>
    /// </summary>
    /// <param name="serverUrl">用户输入的验证服务器地址</param>
    /// <returns>解析后的 API Location (无末尾斜杠)</returns>
    public static async Task<string> ResolveApiLocation(string serverUrl)
    {
        var currentUrl = CompleteProtocol(serverUrl.Trim());

        for (var i = 0; i < MaxAliRedirects; i++)
        {
            using var response = await SendHeadOrGetAsync(currentUrl);

            // 1. 检查 ALI 头 (X-Authlib-Injector-Api-Location)
            if (response.Headers.TryGetValues("X-Authlib-Injector-Api-Location", out var aliValues))
            {
                var ali = aliValues.FirstOrDefault();
                if (!string.IsNullOrEmpty(ali))
                {
                    var aliUrl = ResolveUrl(currentUrl, ali);
                    if (aliUrl.TrimEnd('/').Equals(currentUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        return currentUrl.TrimEnd('/');
                    currentUrl = aliUrl;
                    continue;
                }
            }

            // 2. 检查 HTTP 重定向 (3xx) — 手动跟随 Location 头
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 &&
                response.Headers.Location != null)
            {
                var redirectUrl = ResolveUrl(currentUrl, response.Headers.Location.ToString());
                if (redirectUrl.TrimEnd('/').Equals(currentUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    return currentUrl.TrimEnd('/');
                currentUrl = redirectUrl;
                continue;
            }

            // 3. 无 ALI 头且非重定向 — 当前地址即为 API 地址
            return currentUrl.TrimEnd('/');
        }

        throw new InvalidOperationException($"解析验证服务器地址失败: 重定向次数超过 {MaxAliRedirects} 次");
    }

    /// <summary>
    /// 获取验证服务器名称
    /// </summary>
    /// <param name="apiLocation">API Location (已解析的地址)</param>
    /// <returns>服务器名称</returns>
    public static async Task<string> GetServerName(string apiLocation)
    {
        var json = await _http.GetStringAsync(apiLocation);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("meta", out var meta) &&
            meta.TryGetProperty("serverName", out var serverName))
        {
            return serverName.GetString() ?? "未知服务器";
        }
        return "未知服务器";
    }

    /// <summary>
    /// 验证令牌是否有效
    /// </summary>
    /// <param name="apiLocation">API Location</param>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="clientToken">客户端令牌 (可选)</param>
    /// <returns>令牌有效返回 true，否则 false</returns>
    public static async Task<bool> Validate(string apiLocation, string accessToken, string? clientToken = null)
    {
        var requestBody = new
        {
            accessToken,
            clientToken,
        };
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOptions),
            Encoding.UTF8,
            "application/json");
        var response = await _http.PostAsync($"{apiLocation}/authserver/validate", content);
        // 204 No Content 表示令牌有效
        return response.StatusCode == HttpStatusCode.NoContent;
    }

    // === 私有辅助方法 ===

    /// <summary>
    /// 发送 HEAD 请求；若服务器不支持 HEAD (405) 则回退到 GET
    /// </summary>
    private static async Task<HttpResponseMessage> SendHeadOrGetAsync(string url)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
                return response;
            response.Dispose();
        }
        catch (HttpRequestException)
        {
            // HEAD 请求失败，回退到 GET
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
        return await _http.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// POST JSON 请求并返回响应文本
    /// </summary>
    private static async Task<string> PostJsonAsync(string url, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 检查 Yggdrasil 错误响应 — 若包含 error 字段则抛出异常
    /// </summary>
    private static void CheckError(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // 非 JSON 响应 (如 204 No Content)，不处理
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                var error = errorProp.GetString();
                var errorMessage = doc.RootElement.TryGetProperty("errorMessage", out var msgProp)
                    ? msgProp.GetString()
                    : error;
                throw new InvalidOperationException($"第三方验证失败: {errorMessage}");
            }
        }
    }

    /// <summary>
    /// 补全 URL 协议 — 缺少协议时补全 https:// (不回退到 HTTP，防止降级攻击)
    /// </summary>
    private static string CompleteProtocol(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url;
        }
        return url;
    }

    /// <summary>
    /// 将相对/绝对 URL 解析为绝对 URL 字符串
    /// </summary>
    private static string ResolveUrl(string baseUrl, string relativeOrAbsolute)
    {
        var baseUri = new Uri(baseUrl);
        var resolved = new Uri(baseUri, relativeOrAbsolute);
        return resolved.ToString();
    }

    /// <summary>
    /// 去掉 UUID 中的连字符 (Minecraft 格式: 32 位十六进制无连字符)
    /// </summary>
    private static string StripUuid(string uuid)
    {
        return uuid.Replace("-", "").ToLowerInvariant();
    }
}
