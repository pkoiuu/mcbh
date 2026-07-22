// 认证服务 — 统一管理离线/微软/第三方账户的创建、持久化和刷新
// 离线账户不需要网络验证，UUID 基于用户名生成 (OfflinePlayer 算法)
// 参照 PCL CE: 启动前必须先创建用户档案

using System;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 认证服务 — 统一账户管理 (离线/微软/第三方)
/// </summary>
public static class AuthService
{
    /// <summary>当前账户 (内存缓存)</summary>
    private static McAccount? _currentAccount;

    /// <summary>
    /// 获取当前账户 — 不自动创建，未设置则返回 null
    /// </summary>
    public static Task<McAccount?> GetCurrentAccount()
    {
        if (_currentAccount != null)
            return Task.FromResult(_currentAccount)!;

        _currentAccount = AccountStore.Load();
        return Task.FromResult(_currentAccount)!;
    }

    /// <summary>
    /// 检查是否已有用户配置的账户
    /// </summary>
    public static async Task<bool> HasAccount()
    {
        var account = await GetCurrentAccount();
        return account != null && account.IsUserSet;
    }

    /// <summary>
    /// 创建或切换离线账户 — 用户显式设置用户名
    /// </summary>
    public static Task<McAccount> SetOfflineAccount(string username)
    {
        _currentAccount = new McAccount
        {
            Type = AccountType.Offline,
            Username = username,
            Uuid = GenerateOfflineUuid(username),
            AccessToken = "offline-token",
            IsUserSet = true,
        };
        AccountStore.Save(_currentAccount);
        return Task.FromResult(_currentAccount);
    }

    /// <summary>保存微软或第三方登录后的账户</summary>
    public static void SaveAccount(McAccount account)
    {
        _currentAccount = account;
        AccountStore.Save(account);
    }

    /// <summary>尝试刷新微软令牌(如果过期)</summary>
    public static async Task<McAccount?> RefreshIfExpired()
    {
        var account = await GetCurrentAccount();
        if (account == null || account.Type != AccountType.Microsoft)
            return account;

        if (MicrosoftAuthService.IsTokenExpired(account) && !string.IsNullOrEmpty(account.RefreshToken))
        {
            var refreshed = await MicrosoftAuthService.RefreshLogin(account.RefreshToken);
            if (refreshed != null)
            {
                SaveAccount(refreshed);
                return refreshed;
            }
        }
        return account;
    }

    /// <summary>
    /// 生成离线玩家 UUID — 基于 "OfflinePlayer:{username}" 的 MD5 (version 3 UUID)
    /// </summary>
    private static string GenerateOfflineUuid(string username)
    {
        var input = $"OfflinePlayer:{username}";
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); // version 3
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant
        return new Guid(bytes).ToString("N");
    }
}
