// 服务器列表服务 — 启动器可连接的服务器列表（QuickPlay 目标选择）
// 持久化到 servers.json（exe 目录），首次启动内置「白鹤服务器」
// 主页启动时可选择目标服务器，launch.start 会把所选服务器传给 LaunchService 覆盖默认地址

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 服务器条目 — 启动器的可连接服务器
/// </summary>
public class ServerEntry
{
    /// <summary>稳定 ID（增删时使用）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名（如「白鹤服务器」）</summary>
    public string Name { get; set; } = "";

    /// <summary>服务器地址（域名或 IP）</summary>
    public string Address { get; set; } = "";

    /// <summary>服务器端口</summary>
    public int Port { get; set; } = 25565;

    /// <summary>是否内置默认条目（不可删除）</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 服务器列表服务 — 读取/新增/删除启动器服务器列表
/// </summary>
public static class ServerEntryService
{
    /// <summary>存储路径 — exe 目录 servers.json</summary>
    private static readonly string StorePath = Path.Combine(AppContext.BaseDirectory, "servers.json");

    /// <summary>JSON 序列化选项（camelCase，匹配前端）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>缓存锁 — 保护 _cached 与文件并发读写</summary>
    private static readonly object _lock = new();

    /// <summary>内存缓存</summary>
    private static List<ServerEntry>? _cached;

    /// <summary>内置默认服务器（与 settings.json 的默认 serverAddress/serverPort 一致）</summary>
    private static List<ServerEntry> CreateDefaultList() => new()
    {
        new ServerEntry
        {
            Id = "default-baihe",
            Name = "白鹤服务器",
            Address = "play.simpfun.cn",
            Port = 28230,
            IsDefault = true,
        },
    };

    /// <summary>
    /// 获取服务器列表 — 不存在存储文件时创建内置默认列表
    /// </summary>
    public static Task<List<ServerEntry>> GetServersAsync()
    {
        lock (_lock)
        {
            if (_cached != null)
                return Task.FromResult(_cached);

            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    _cached = JsonSerializer.Deserialize<List<ServerEntry>>(json, JsonOptions)
                              ?? CreateDefaultList();
                    // 容错：列表为空或没有默认条目时补一个
                    if (_cached.Count == 0)
                        _cached = CreateDefaultList();
                    if (_cached.All(s => !s.IsDefault))
                    {
                        _cached.Insert(0, CreateDefaultList()[0]);
                    }
                }
                else
                {
                    _cached = CreateDefaultList();
                    SaveLocked();
                }
            }
            catch
            {
                _cached = CreateDefaultList();
            }

            return Task.FromResult(_cached);
        }
    }

    /// <summary>
    /// 新增服务器 — 同名/同地址去重，返回新增条目（重复返回 null）
    /// </summary>
    public static Task<ServerEntry?> AddServerAsync(string name, string address, int port)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            return Task.FromResult<ServerEntry?>(null);
        if (port is < 1 or > 65535)
            return Task.FromResult<ServerEntry?>(null);

        lock (_lock)
        {
            var servers = GetServersAsync().Result;
            // 同地址同端口视为重复
            if (servers.Any(s =>
                    s.Address.Equals(address.Trim(), StringComparison.OrdinalIgnoreCase) && s.Port == port))
                return Task.FromResult<ServerEntry?>(null);

            var entry = new ServerEntry
            {
                Name = name.Trim(),
                Address = address.Trim(),
                Port = port,
            };
            servers.Add(entry);
            SaveLocked();
            return Task.FromResult<ServerEntry?>(entry);
        }
    }

    /// <summary>
    /// 删除服务器 — 内置默认条目不可删除
    /// </summary>
    public static Task<bool> RemoveServerAsync(string id)
    {
        lock (_lock)
        {
            var servers = GetServersAsync().Result;
            var target = servers.FirstOrDefault(s => s.Id == id);
            if (target == null || target.IsDefault)
                return Task.FromResult(false);

            servers.Remove(target);
            SaveLocked();
            return Task.FromResult(true);
        }
    }

    /// <summary>保存列表到文件（调用方需持有 _lock）</summary>
    private static void SaveLocked()
    {
        try
        {
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_cached, JsonOptions));
        }
        catch
        {
            // 写入失败不影响本次会话
        }
    }
}
