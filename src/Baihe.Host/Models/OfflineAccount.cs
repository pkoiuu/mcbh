// 离线账户模型 — 不需要网络验证的本地账户
// UUID 基于用户名生成（OfflinePlayer: 前缀 + MD5），与 Minecraft 官方离线模式一致

using System;
using System.Security.Cryptography;
using System.Text;

namespace Baihe.Host.Models;

/// <summary>
/// 离线账户 — 用于无网络验证的本地游戏登录
/// </summary>
public class OfflineAccount
{
    /// <summary>用户名</summary>
    public string Username { get; set; } = "Player";

    /// <summary>离线 UUID（基于用户名生成）</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>访问令牌（离线模式固定值）</summary>
    public string AccessToken { get; set; } = "offline-token";

    /// <summary>账户类型</summary>
    public string Type => "offline";

    /// <summary>
    /// 根据用户名创建离线账户
    /// </summary>
    public static OfflineAccount Create(string username)
    {
        return new OfflineAccount
        {
            Username = username,
            Uuid = GenerateOfflineUuid(username),
        };
    }

    /// <summary>
    /// 生成离线 UUID — Minecraft 官方算法: OfflinePlayer:&lt;name&gt; 的 MD5
    /// </summary>
    private static string GenerateOfflineUuid(string username)
    {
        var input = $"OfflinePlayer:{username}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));

        // 将 MD5 转为 UUID 格式（版本 3 = 基于名称的 UUID）
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); // 版本 3
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // 变体

        return new Guid(bytes).ToString("N");
    }
}
