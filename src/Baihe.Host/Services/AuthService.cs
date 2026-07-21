// 认证服务 — 管理离线账户的创建、持久化和切换
// 离线账户不需要网络验证，UUID 基于用户名生成

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baihe.Host.Models;

namespace Baihe.Host.Services;

/// <summary>
/// 认证服务 — 离线账户管理
/// </summary>
public static class AuthService
{
    /// <summary>账户配置文件路径</summary>
    private static readonly string AccountPath = Path.Combine(AppContext.BaseDirectory, "account.json");

    /// <summary>当前账户</summary>
    private static OfflineAccount? _currentAccount;

    /// <summary>JSON 序列化选项</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 获取当前账户 — 不存在则自动创建默认离线账户
    /// </summary>
    public static Task<OfflineAccount> GetCurrentAccount()
    {
        if (_currentAccount != null)
            return Task.FromResult(_currentAccount);

        // 从文件加载
        if (File.Exists(AccountPath))
        {
            try
            {
                var json = File.ReadAllText(AccountPath);
                _currentAccount = JsonSerializer.Deserialize<OfflineAccount>(json, JsonOptions);
                if (_currentAccount != null)
                    return Task.FromResult(_currentAccount);
            }
            catch
            {
                // 加载失败，创建新账户
            }
        }

        // 创建默认离线账户
        _currentAccount = OfflineAccount.Create("Player");
        SaveAccount(_currentAccount);
        return Task.FromResult(_currentAccount);
    }

    /// <summary>
    /// 创建或切换离线账户
    /// </summary>
    public static Task<OfflineAccount> SetOfflineAccount(string username)
    {
        _currentAccount = OfflineAccount.Create(username);
        SaveAccount(_currentAccount);
        return Task.FromResult(_currentAccount);
    }

    /// <summary>
    /// 保存账户到文件
    /// </summary>
    private static void SaveAccount(OfflineAccount account)
    {
        try
        {
            var json = JsonSerializer.Serialize(account, JsonOptions);
            File.WriteAllText(AccountPath, json);
        }
        catch
        {
            // 保存失败不影响功能
        }
    }
}
