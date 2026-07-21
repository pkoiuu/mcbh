// IPC 消息模型 — 前端与宿主层之间的通信协议
// 使用 System.Text.Json 进行序列化/反序列化

using System.Text.Json;

namespace Baihe.Host.Ipc;

/// <summary>
/// IPC 请求消息模型 — 前端发送给宿主层的消息
/// </summary>
/// <param name="Id">消息唯一标识，用于匹配请求与响应</param>
/// <param name="Cmd">命令名称，路由到对应的处理程序</param>
/// <param name="Args">命令参数，可为 null</param>
public record IpcMessage(string Id, string Cmd, JsonElement? Args);

/// <summary>
/// IPC 响应消息模型 — 宿主层返回给前端的消息
/// </summary>
/// <param name="Id">对应请求的消息唯一标识</param>
/// <param name="Ok">请求是否成功</param>
/// <param name="Response">响应数据，成功时填充</param>
/// <param name="Error">错误信息，失败时填充</param>
public record IpcResponse(string Id, bool Ok, object? Response, string? Error);
