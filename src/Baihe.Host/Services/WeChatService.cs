// 微信名存储服务 — 独立于账户系统，用户切换账户时微信名保持不变
// 存储: wechat.json（与 account.json 同目录）

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 微信名存储服务 — 管理用户微信名的持久化读写
/// </summary>
public static class WeChatService
{
    /// <summary>存储文件路径（与 account.json 同目录）</summary>
    private static readonly string StorePath = Path.Combine(AppContext.BaseDirectory, "wechat.json");

    /// <summary>JSON 序列化选项</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 获取已保存的微信名
    /// </summary>
    /// <returns>微信名；未设置时返回 null</returns>
    public static Task<string?> GetAsync()
    {
        try
        {
            if (!File.Exists(StorePath))
                return Task.FromResult<string?>(null);

            var json = File.ReadAllText(StorePath);
            var data = JsonSerializer.Deserialize<WeChatData>(json, _jsonOptions);
            return Task.FromResult(data?.Name);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// 保存微信名
    /// </summary>
    /// <param name="name">微信名</param>
    public static Task SaveAsync(string name)
    {
        try
        {
            var data = new WeChatData { Name = name };
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // 静默处理
        }
        return Task.CompletedTask;
    }

    /// <summary>微信名数据模型</summary>
    private sealed class WeChatData
    {
        /// <summary>微信名</summary>
        public string? Name { get; set; }
    }
}
