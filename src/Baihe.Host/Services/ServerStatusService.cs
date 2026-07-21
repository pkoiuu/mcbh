// 服务器状态检查服务 — TCP ping 检测白鹤服务器在线状态
// 通过建立 TCP 连接测量延迟，3 秒超时

using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

/// <summary>
/// 服务器状态检查服务 — 检测白鹤服务器在线状态
/// </summary>
public static class ServerStatusService
{
    /// <summary>服务器地址</summary>
    private const string ServerAddress = "play.simpfun.cn";

    /// <summary>服务器端口</summary>
    private const int ServerPort = 28230;

    /// <summary>连接超时 (毫秒)</summary>
    private const int TimeoutMs = 3000;

    /// <summary>
    /// 检查服务器状态 — 尝试 TCP 连接并测量延迟
    /// </summary>
    public static async Task<object> CheckStatus()
    {
        try
        {
            using var client = new TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 使用 Task.WhenAny 实现超时控制
            var connectTask = client.ConnectAsync(ServerAddress, ServerPort);
            var timeoutTask = Task.Delay(TimeoutMs);

            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                sw.Stop();
                return new { online = false, latency = -1, address = ServerAddress, port = ServerPort };
            }

            await connectTask; // 确保异常被捕获
            sw.Stop();

            return new
            {
                online = true,
                latency = sw.ElapsedMilliseconds,
                address = ServerAddress,
                port = ServerPort,
            };
        }
        catch
        {
            return new { online = false, latency = -1, address = ServerAddress, port = ServerPort };
        }
    }
}
