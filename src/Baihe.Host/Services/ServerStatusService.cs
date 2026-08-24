// 服务器状态检查服务 — TCP ping 检测白鹤服务器在线状态
// 服务器地址从 SettingsService 动态读取

using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 服务器状态检查服务 — 检测白鹤服务器在线状态
/// </summary>
public static class ServerStatusService
{
    /// <summary>连接超时 (毫秒)</summary>
    private const int TimeoutMs = 3000;

    /// <summary>
    /// 检查服务器状态 — 尝试 TCP 连接并测量延迟
    /// 服务器地址从 SettingsService 读取，可被显式参数覆盖（服务器列表选择）
    /// </summary>
    public static async Task<object> CheckStatus(string? address = null, int? port = null)
    {
        var settings = await SettingsService.GetAsync();
        var effectiveAddress = string.IsNullOrWhiteSpace(address) ? settings.ServerAddress : address;
        var effectivePort = port is > 0 ? port.Value : settings.ServerPort;

        try
        {
            using var client = new TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var connectTask = client.ConnectAsync(effectiveAddress, effectivePort);
            var timeoutTask = Task.Delay(TimeoutMs);

            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                sw.Stop();
                return new { online = false, latency = -1, address = effectiveAddress, port = effectivePort };
            }

            await connectTask;
            sw.Stop();

            return new
            {
                online = true,
                latency = sw.ElapsedMilliseconds,
                address = effectiveAddress,
                port = effectivePort,
            };
        }
        catch
        {
            return new { online = false, latency = -1, address = effectiveAddress, port = effectivePort };
        }
    }
}
