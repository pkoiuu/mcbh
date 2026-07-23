// 统一账户模型 — 支持离线、微软正版、第三方验证三种登录方式
// 参照 PCL2-CE ModProfile.cs，简化为单一模型

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Baihe.Host.Models;

/// <summary>账户类型</summary>
public enum AccountType
{
    /// <summary>离线模式</summary>
    Offline,
    /// <summary>微软正版</summary>
    Microsoft,
    /// <summary>第三方验证 (Authlib-Injector / Yggdrasil)</summary>
    ThirdParty,
}

/// <summary>
/// 统一 Minecraft 账户 — 三种登录方式共用
/// </summary>
public class McAccount
{
    /// <summary>账户类型</summary>
    public AccountType Type { get; set; } = AccountType.Offline;

    /// <summary>玩家用户名</summary>
    public string Username { get; set; } = "Player";

    /// <summary>玩家 UUID</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>访问令牌 (启动游戏用)</summary>
    public string AccessToken { get; set; } = "offline-token";

    /// <summary>刷新令牌 (微软正版用，用于静默刷新)</summary>
    public string? RefreshToken { get; set; }

    /// <summary>令牌过期时间 (Unix 时间戳, 毫秒)</summary>
    public long? ExpiresAt { get; set; }

    /// <summary>第三方验证服务器地址 (仅 ThirdParty)</summary>
    public string? AuthServer { get; set; }

    /// <summary>第三方验证服务器名称 (仅 ThirdParty)</summary>
    public string? AuthServerName { get; set; }

    /// <summary>第三方验证密码 (仅 ThirdParty, 加密存储)</summary>
    public string? Password { get; set; }

    /// <summary>用户是否已显式设置</summary>
    public bool IsUserSet { get; set; } = false;

    /// <summary>转换为离线账户 (兼容旧代码)</summary>
    public OfflineAccount ToOfflineAccount()
    {
        return new OfflineAccount
        {
            Username = Username,
            Uuid = Uuid,
            AccessToken = AccessToken,
            IsUserSet = IsUserSet,
        };
    }

    /// <summary>账户类型显示名</summary>
    public string TypeDisplay => Type switch
    {
        AccountType.Microsoft => "正版",
        AccountType.ThirdParty => $"第三方{(!string.IsNullOrEmpty(AuthServerName) ? $" · {AuthServerName}" : "")}",
        _ => "离线",
    };
}

/// <summary>
/// 账户存储服务 — 持久化账户信息到 JSON 文件
/// 参照 PCL2-CE profiles.json，简化为单账户模式
/// </summary>
public static class AccountStore
{
    private static readonly string StorePath = Path.Combine(AppContext.BaseDirectory, "account.json");

    /// <summary>保存账户</summary>
    public static void Save(McAccount? account)
    {
        try
        {
            if (account == null)
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
                return;
            }

            var json = JsonSerializer.Serialize(account, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            });
            File.WriteAllText(StorePath, json);
        }
        catch { }
    }

    /// <summary>加载账户</summary>
    public static McAccount? Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return null;

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<McAccount>(json, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>检查是否有已保存的账户</summary>
    public static bool HasAccount()
    {
        return Load() != null;
    }
}
