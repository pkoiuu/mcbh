/**
 * 功能描述: WebView2 IPC 封装
 * 技术实现: 通过 window.chrome.webview.postMessage 与 C# 后端通信
 * 注意事项: 采用 Promise + 唯一 id 机制实现请求-响应配对
 */

/** IPC 响应数据结构，由 C# 后端回传 */
export interface IpcResponse {
  /** 请求 id，与发送时保持一致 */
  id: string
  /** 是否成功 */
  ok: boolean
  /** 成功时的返回数据 */
  response?: unknown
  /** 失败时的错误信息 */
  error?: string
}

/** 待处理的请求映射，key 为请求 id */
const pending = new Map<string, { resolve: (value: unknown) => void; reject: (reason: Error) => void }>()

/**
 * 初始化 IPC 监听器
 * 监听 C# 后端通过 WebView2 返回的消息，根据 id 配对回调
 */
function initIpcListener(): void {
  const webview = (window as any).chrome?.webview
  if (!webview) {
    // 非 WebView2 环境（如浏览器开发调试），跳过监听
    console.warn('[IPC] window.chrome.webview 不可用，IPC 功能将不可用')
    return
  }

  webview.addEventListener('message', (event: MessageEvent) => {
    const data: IpcResponse = event.data
    const handler = pending.get(data.id)
    if (handler) {
      pending.delete(data.id)
      if (data.ok) {
        handler.resolve(data.response)
      } else {
        handler.reject(new Error(data.error ?? 'IPC 调用失败'))
      }
    }
  })
}

/**
 * 发起 IPC 调用
 * @param cmd - 命令名称，对应 C# 端注册的处理器
 * @param args - 参数，会被 JSON 序列化后发送
 * @returns Promise，成功时返回 C# 端的响应数据
 */
export async function ipc<T = unknown>(cmd: string, args?: unknown): Promise<T> {
  const webview = (window as any).chrome?.webview
  if (!webview) {
    throw new Error('[IPC] 当前环境不支持 WebView2 IPC')
  }

  const id = crypto.randomUUID()
  const message = { id, cmd, args }

  return new Promise<T>((resolve, reject) => {
    // 设置 15 秒超时，避免永久挂起
    const timer = setTimeout(() => {
      if (pending.has(id)) {
        pending.delete(id)
        reject(new Error(`[IPC] 调用超时: ${cmd}`))
      }
    }, 15000)

    pending.set(id, {
      resolve: (value: unknown) => {
        clearTimeout(timer)
        resolve(value as T)
      },
      reject: (reason: Error) => {
        clearTimeout(timer)
        reject(reason)
      }
    })

    // 发送 JSON 字符串到 C# 后端
    webview.postMessage(JSON.stringify(message))
  })
}

// 模块加载时立即初始化监听器
initIpcListener()
