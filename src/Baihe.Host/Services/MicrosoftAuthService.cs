// 微软正版认证服务 — 设备码流程 (Device Code Flow)
// 完整 6 步流程: 设备码 → OAuth Token → Xbox Live → XSTS → Minecraft Token → Profile
// 参照 wiki.vg 文档和 PCL2-CE ModLaunch.cs 实现

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 微软正版认证服务 — 设备码流程
/// </summary>
public static class MicrosoftAuthService
{
    /// <summary>Azure AD v2.0 Client ID — 使用 Prism Launcher 公开注册的应用 ID，支持设备码流程</summary>
    private const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

    /// <summary>OAuth Scope — XboxLive.SignIn 用于 Xbox Live 认证，offline_access 是标准 OIDC scope 用于获取 refresh_token</summary>
    private const string OAuthScope = "XboxLive.SignIn offline_access";

    /// <summary>设备码端点</summary>
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";

    /// <summary>OAuth Token 端点</summary>
    private const string OAuthTokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    /// <summary>Xbox Live 认证端点</summary>
    private const string XboxAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";

    /// <summary>XSTS 认证端点</summary>
    private const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";

    /// <summary>Minecraft 登录端点</summary>
    private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";

    /// <summary>Minecraft Profile 端点</summary>
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    /// <summary>HTTP 客户端单例</summary>
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>JSON 序列化选项 — 驼峰命名 (Microsoft OAuth API)</summary>
    private static readonly JsonSerializerOptions _jsonOptionsCamel = new(JsonSerializerDefaults.Web);

    /// <summary>JSON 序列化选项 — 原始命名 (Xbox API 使用 PascalCase)</summary>
    private static readonly JsonSerializerOptions _jsonOptionsRaw = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // =========================================================================
    // 公开方法
    // =========================================================================

    /// <summary>
    /// 微软正版登录 — 设备码流程
    /// </summary>
    /// <param name="onDeviceCode">回调，当获取到设备码时调用，参数为 (userCode, verificationUri)</param>
    /// <param name="cancellationToken">取消令牌，用于取消轮询</param>
    /// <returns>认证成功后的 McAccount</returns>
    public static async Task<McAccount> LoginWithDeviceCode(
        Action<string, string> onDeviceCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: 获取设备码
            var deviceCode = await RequestDeviceCodeAsync(cancellationToken);
            onDeviceCode(deviceCode.UserCode, deviceCode.VerificationUri);

            // Step 2: 轮询获取 OAuth Token
            var oauthToken = await PollForTokenAsync(deviceCode, cancellationToken);

            // Step 3-6: 用 access_token 完成 Xbox → XSTS → Minecraft → Profile
            return await AuthenticateWithAccessTokenAsync(oauthToken.AccessToken, oauthToken.RefreshToken ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户主动取消 — 直接重新抛出
            throw;
        }
        catch (InvalidOperationException)
        {
            // 已经是有意义的错误消息，直接重新抛出
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"网络请求失败: {ex.Message}。请检查网络连接后重试。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"请求超时，请检查网络连接后重试。", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"微软登录失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 刷新 Microsoft 令牌
    /// </summary>
    /// <param name="refreshToken">之前保存的 refresh_token</param>
    /// <returns>认证成功后的 McAccount，或 null 表示刷新失败需要重新登录</returns>
    public static async Task<McAccount?> RefreshLogin(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return null;

        try
        {
            // 使用 refresh_token 获取新的 OAuth access_token
            var oauthToken = await RefreshOAuthTokenAsync(refreshToken);

            // Step 3-6: 用新 access_token 完成 Xbox → XSTS → Minecraft → Profile
            return await AuthenticateWithAccessTokenAsync(oauthToken.AccessToken, oauthToken.RefreshToken ?? refreshToken);
        }
        catch
        {
            // 刷新失败，返回 null 表示需要重新登录
            return null;
        }
    }

    /// <summary>
    /// 检查令牌是否过期
    /// </summary>
    /// <param name="account">账户信息</param>
    /// <returns>true 表示已过期或无过期时间</returns>
    public static bool IsTokenExpired(McAccount account)
    {
        if (account.ExpiresAt == null)
            return true;

        // ExpiresAt 是 Unix 毫秒时间戳，提前 60 秒判定为过期
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return nowMs >= account.ExpiresAt.Value - 60_000;
    }

    // =========================================================================
    // Step 1: 获取设备码
    // =========================================================================

    /// <summary>
    /// 请求设备码 — 向 Microsoft OAuth 端点发起 devicecode 请求
    /// </summary>
    private static async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = OAuthScope,
        };

        var content = new FormUrlEncodedContent(parameters);

        using var response = await _http.PostAsync(DeviceCodeUrl, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取设备码失败 (HTTP {response.StatusCode}): {json}");
        }

        var result = JsonSerializer.Deserialize<DeviceCodeResponse>(json, _jsonOptionsCamel)
            ?? throw new InvalidOperationException("获取设备码失败: 响应解析为空");

        return result;
    }

    // =========================================================================
    // Step 2: 轮询获取 OAuth Token
    // =========================================================================

    /// <summary>
    /// 轮询获取 OAuth Token — 按照 interval 间隔重复请求，直到获取到令牌或超时
    /// </summary>
    private static async Task<OAuthTokenResponse> PollForTokenAsync(
        DeviceCodeResponse deviceCode,
        CancellationToken cancellationToken)
    {
        var interval = deviceCode.Interval > 0 ? deviceCode.Interval : 5;
        var maxAttempts = deviceCode.ExpiresIn > 0
            ? deviceCode.ExpiresIn / interval + 5
            : 180; // 默认最多轮询 180 次

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = deviceCode.DeviceCode,
                ["scope"] = OAuthScope,
            };

            var content = new FormUrlEncodedContent(parameters);

            using var response = await _http.PostAsync(OAuthTokenUrl, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // 解析响应 — 可能是成功令牌，也可能是错误
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 检查是否是错误响应
            if (root.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString() ?? "";

                switch (error)
                {
                    case "authorization_pending":
                        // 用户尚未完成授权，等待 interval 秒后重试
                        await Task.Delay(interval * 1000, cancellationToken);
                        continue;

                    case "slow_down":
                        // 请求过于频繁，增加间隔
                        await Task.Delay((interval + 5) * 1000, cancellationToken);
                        continue;

                    case "expired_token":
                        throw new InvalidOperationException("设备码已过期，请重新登录");

                    case "access_denied":
                        throw new InvalidOperationException("用户拒绝了授权请求");

                    case "bad_verification_code":
                        throw new InvalidOperationException("设备码无效，请重新登录");

                    default:
                        var errorDesc = root.TryGetProperty("error_description", out var descElement)
                            ? descElement.GetString()
                            : null;
                        throw new InvalidOperationException($"OAuth 认证失败: {error}" +
                            (string.IsNullOrEmpty(errorDesc) ? "" : $" — {errorDesc}"));
                }
            }

            // 成功获取令牌
            var token = JsonSerializer.Deserialize<OAuthTokenResponse>(json, _jsonOptionsCamel)
                ?? throw new InvalidOperationException("OAuth Token 响应解析为空");

            return token;
        }

        throw new InvalidOperationException("设备码轮询超时，请重新登录");
    }

    // =========================================================================
    // Step 3: Xbox Live 认证
    // =========================================================================

    /// <summary>
    /// Xbox Live 认证 — 用 OAuth access_token 换取 Xbox Live Token (XBL Token)
    /// </summary>
    /// <param name="oauthAccessToken">OAuth access_token</param>
    /// <returns>(XBL Token, UHS)</returns>
    private static async Task<(string Token, string Uhs)> AuthenticateXboxLiveAsync(string oauthAccessToken)
    {
        var requestBody = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={oauthAccessToken}",
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOptionsRaw);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, XboxAuthUrl)
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Xbox Live 认证失败 (HTTP {response.StatusCode}): {responseJson}");
        }

        var result = JsonSerializer.Deserialize<XboxAuthResponse>(responseJson, _jsonOptionsRaw)
            ?? throw new InvalidOperationException("Xbox Live 认证失败: 响应解析为空");

        var token = result.Token
            ?? throw new InvalidOperationException("Xbox Live 认证失败: 未返回 Token");

        var uhs = result.DisplayClaims?.Xui?[0].Uhs
            ?? throw new InvalidOperationException("Xbox Live 认证失败: 未返回 UHS");

        return (token, uhs);
    }

    // =========================================================================
    // Step 4: XSTS 认证
    // =========================================================================

    /// <summary>
    /// XSTS 认证 — 用 XBL Token 换取 XSTS Token，用于 Minecraft 服务验证
    /// </summary>
    /// <param name="xblToken">Xbox Live Token</param>
    /// <returns>(XSTS Token, UHS)</returns>
    private static async Task<(string Token, string Uhs)> AuthenticateXstsAsync(string xblToken)
    {
        var requestBody = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken },
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOptionsRaw);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, XstsAuthUrl)
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // XSTS 错误通常返回 401，body 中包含 Xerr 字段
            var errorMessage = ParseXstsError(response.StatusCode, responseJson);
            throw new InvalidOperationException(errorMessage);
        }

        var result = JsonSerializer.Deserialize<XboxAuthResponse>(responseJson, _jsonOptionsRaw)
            ?? throw new InvalidOperationException("XSTS 认证失败: 响应解析为空");

        var token = result.Token
            ?? throw new InvalidOperationException("XSTS 认证失败: 未返回 Token");

        var uhs = result.DisplayClaims?.Xui?[0].Uhs
            ?? throw new InvalidOperationException("XSTS 认证失败: 未返回 UHS");

        return (token, uhs);
    }

    /// <summary>
    /// 解析 XSTS 错误 — 根据 Xerr 代码返回有意义的错误消息
    /// </summary>
    private static string ParseXstsError(System.Net.HttpStatusCode statusCode, string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("Xerr", out var xerrElement))
            {
                var xerr = xerrElement.GetInt64();
                return xerr switch
                {
                    2148916233 => "该微软账号没有 Xbox 档案，请先在 Xbox.com 创建档案",
                    2148916235 => "该账号所在地区不受支持，无法使用 Xbox Live 服务",
                    2148916238 => "该账号是未成年账号，需要成人账号陪同才能使用 Xbox Live",
                    2148916236 => "该账号需要完成 Xbox Live 验证才能继续",
                    2148916234 => "该账号所在的国家/地区不支持 Xbox Live 服务",
                    _ => $"XSTS 认证失败 (Xerr: {xerr})",
                };
            }

            var message = root.TryGetProperty("Message", out var msgElement)
                ? msgElement.GetString()
                : null;

            return $"XSTS 认证失败 (HTTP {statusCode})" +
                (string.IsNullOrEmpty(message) ? "" : $": {message}");
        }
        catch
        {
            return $"XSTS 认证失败 (HTTP {statusCode}): {responseJson}";
        }
    }

    // =========================================================================
    // Step 5: Minecraft Token
    // =========================================================================

    /// <summary>
    /// 获取 Minecraft Token — 用 XSTS Token 和 UHS 换取 Minecraft 访问令牌
    /// </summary>
    /// <param name="uhs">User Hash</param>
    /// <param name="xstsToken">XSTS Token</param>
    /// <returns>(Minecraft Access Token, 过期秒数)</returns>
    private static async Task<(string AccessToken, int ExpiresIn)> GetMinecraftTokenAsync(string uhs, string xstsToken)
    {
        var requestBody = new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}",
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOptionsCamel);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, McLoginUrl)
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取 Minecraft Token 失败 (HTTP {response.StatusCode}): {responseJson}");
        }

        var result = JsonSerializer.Deserialize<MinecraftTokenResponse>(responseJson, _jsonOptionsCamel)
            ?? throw new InvalidOperationException("获取 Minecraft Token 失败: 响应解析为空");

        var accessToken = result.AccessToken
            ?? throw new InvalidOperationException("获取 Minecraft Token 失败: 未返回 access_token");

        return (accessToken, result.ExpiresIn);
    }

    // =========================================================================
    // Step 6: 获取 Profile
    // =========================================================================

    /// <summary>
    /// 获取 Minecraft Profile — 用 Minecraft Token 获取玩家 UUID 和用户名
    /// </summary>
    /// <param name="mcAccessToken">Minecraft 访问令牌</param>
    /// <returns>(UUID, 用户名)</returns>
    private static async Task<(string Uuid, string Username)> GetMinecraftProfileAsync(string mcAccessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccessToken);

        using var response = await _http.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("该账号没有 Minecraft 档案，请确认已购买 Minecraft");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取 Minecraft Profile 失败 (HTTP {response.StatusCode}): {responseJson}");
        }

        var result = JsonSerializer.Deserialize<MinecraftProfileResponse>(responseJson, _jsonOptionsCamel)
            ?? throw new InvalidOperationException("获取 Minecraft Profile 失败: 响应解析为空");

        var uuid = result.Id
            ?? throw new InvalidOperationException("获取 Minecraft Profile 失败: 未返回 UUID");

        var username = result.Name
            ?? throw new InvalidOperationException("获取 Minecraft Profile 失败: 未返回用户名");

        return (uuid, username);
    }

    // =========================================================================
    // 内部编排: 用 OAuth access_token 完成 Step 3-6
    // =========================================================================

    /// <summary>
    /// 用 OAuth access_token 完成剩余认证步骤 (Step 3-6)
    /// </summary>
    /// <param name="oauthAccessToken">OAuth access_token</param>
    /// <param name="refreshToken">OAuth refresh_token (用于后续刷新)</param>
    /// <returns>认证完成后的 McAccount</returns>
    private static async Task<McAccount> AuthenticateWithAccessTokenAsync(string oauthAccessToken, string refreshToken)
    {
        // Step 3: Xbox Live 认证
        var (xblToken, xblUhs) = await AuthenticateXboxLiveAsync(oauthAccessToken);

        // Step 4: XSTS 认证
        var (xstsToken, xstsUhs) = await AuthenticateXstsAsync(xblToken);

        // Step 5: Minecraft Token
        var (mcToken, mcExpiresIn) = await GetMinecraftTokenAsync(xstsUhs, xstsToken);

        // Step 6: 获取 Profile
        var (uuid, username) = await GetMinecraftProfileAsync(mcToken);

        // 计算 ExpiresAt — Unix 毫秒时间戳
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(mcExpiresIn).ToUnixTimeMilliseconds();

        return new McAccount
        {
            Type = AccountType.Microsoft,
            Username = username,
            Uuid = uuid,
            AccessToken = mcToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            IsUserSet = true,
        };
    }

    // =========================================================================
    // OAuth Token 刷新
    // =========================================================================

    /// <summary>
    /// 使用 refresh_token 获取新的 OAuth access_token
    /// 使用 v2.0 端点，与设备码流程的 Client ID 和 scope 匹配
    /// </summary>
    private static async Task<OAuthTokenResponse> RefreshOAuthTokenAsync(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = OAuthScope,
        };

        var content = new FormUrlEncodedContent(parameters);

        using var response = await _http.PostAsync(OAuthTokenUrl, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OAuth 令牌刷新失败 (HTTP {response.StatusCode}): {json}");
        }

        var result = JsonSerializer.Deserialize<OAuthTokenResponse>(json, _jsonOptionsCamel)
            ?? throw new InvalidOperationException("OAuth 令牌刷新失败: 响应解析为空");

        if (string.IsNullOrEmpty(result.AccessToken))
        {
            throw new InvalidOperationException("OAuth 令牌刷新失败: 未返回 access_token");
        }

        return result;
    }

    // =========================================================================
    // 响应模型
    // =========================================================================

    /// <summary>设备码响应 (Step 1)</summary>
    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    /// <summary>OAuth Token 响应 (Step 2)</summary>
    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>Xbox 认证响应 (Step 3 & 4) — PascalCase 命名</summary>
    private sealed class XboxAuthResponse
    {
        [JsonPropertyName("Token")]
        public string? Token { get; set; }

        [JsonPropertyName("DisplayClaims")]
        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    /// <summary>Xbox DisplayClaims</summary>
    private sealed class XboxDisplayClaims
    {
        [JsonPropertyName("xui")]
        public List<XboxXui>? Xui { get; set; }
    }

    /// <summary>Xbox XUI (User Info)</summary>
    private sealed class XboxXui
    {
        [JsonPropertyName("uhs")]
        public string Uhs { get; set; } = string.Empty;
    }

    /// <summary>Minecraft Token 响应 (Step 5)</summary>
    private sealed class MinecraftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    /// <summary>Minecraft Profile 响应 (Step 6)</summary>
    private sealed class MinecraftProfileResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
