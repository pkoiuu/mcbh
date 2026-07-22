// IPC 命令路由器 — 线程安全地路由前端发来的 JSON 消息到对应的命令处理程序
// 使用 ConcurrentDictionary 保证线程安全，支持多线程并发调用

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baihe.Host.Ipc;

/// <summary>
/// IPC 命令路由器 — 负责将前端发来的 JSON 消息路由到已注册的命令处理程序
/// </summary>
public class IpcRouter
{
    /// <summary>
    /// 命令注册表 — 命令名到处理程序的映射
    /// </summary>
    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object>>> _commands = new();

    /// <summary>
    /// JSON 序列化选项 — 使用 camelCase 命名约定以匹配前端风格
    /// </summary>
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 推送消息回调 — 后端主动向前端推送事件时调用
    /// 由 MainWindow 在 WebView2 初始化后设置
    /// </summary>
    public static Action<string>? OnPushMessage { get; set; }

    /// <summary>
    /// 向前端推送事件 — 用于下载进度等主动通知
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="data">事件数据</param>
    public static void PushEvent(string type, object data)
    {
        var json = JsonSerializer.Serialize(new { type, data }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        OnPushMessage?.Invoke(json);
    }

    /// <summary>
    /// 创建 IPC 路由器实例，并注册内置命令
    /// </summary>
    public IpcRouter()
    {
        // 内置 ping 命令 — 用于前端检测宿主层是否存活
        Register("ping", _ => Task.FromResult<object>("pong"));
    }

    /// <summary>
    /// 注册命令处理程序
    /// </summary>
    /// <param name="cmd">命令名称</param>
    /// <param name="handler">处理程序 — 接收参数，返回结果</param>
    public void Register(string cmd, Func<JsonElement?, Task<object>> handler)
    {
        _commands[cmd] = handler;
    }

    /// <summary>
    /// 处理前端发来的 JSON 消息 — 反序列化、路由、执行并返回 JSON 响应
    /// </summary>
    /// <param name="json">原始 JSON 字符串</param>
    /// <returns>响应 JSON 字符串</returns>
    public async Task<string> HandleAsync(string json)
    {
        IpcResponse response;
        string messageId = "unknown";
        try
        {
            // 反序列化请求消息
            var message = JsonSerializer.Deserialize<IpcMessage>(json, _jsonOptions);
            if (message == null)
            {
                response = new IpcResponse(messageId, false, null, "无法解析消息");
            }
            else
            {
                messageId = message.Id;
                if (_commands.TryGetValue(message.Cmd, out var handler))
                {
                    // 路由到对应的处理程序
                    var result = await handler(message.Args);
                    response = new IpcResponse(messageId, true, result, null);
                }
                else
                {
                    // 未知命令
                    response = new IpcResponse(messageId, false, null, $"未知命令: {message.Cmd}");
                }
            }
        }
        catch (Exception ex)
        {
            // 捕获所有异常，避免未处理异常导致宿主崩溃
            // 使用原始消息 ID，确保前端能匹配到 pending Promise 并正确显示错误
            response = new IpcResponse(messageId, false, null, ex.Message);
        }

        return JsonSerializer.Serialize(response, _jsonOptions);
    }
}
