/**
 * 功能描述: WebView2 IPC 封装 — 扩展版，支持请求-响应和推送消息
 * 技术实现: 通过 window.chrome.webview.postMessage 与 C# 后端通信
 * 注意事项: 采用 Promise + 唯一 id 机制实现请求-响应配对，支持事件推送
 */

/** IPC 响应数据结构 */
export interface IpcResponse {
  id: string
  ok: boolean
  response?: unknown
  error?: string
}

/** IPC 推送消息结构 — 后端主动推送的事件 */
export interface IpcEvent {
  type: string
  data: unknown
}

/** 事件监听器类型 */
type EventListener = (data: unknown) => void

/** 待处理的请求映射 */
const pending = new Map<
  string,
  { resolve: (value: unknown) => void; reject: (reason: Error) => void }
>()

/** 事件监听器映射 */
const eventListeners = new Map<string, Set<EventListener>>()

/**
 * 初始化 IPC 监听器
 */
function initIpcListener(): void {
  const webview = (window as any).chrome?.webview
  if (!webview) {
    console.warn('[IPC] window.chrome.webview 不可用，IPC 功能将不可用')
    return
  }

  webview.addEventListener('message', (event: MessageEvent) => {
    const data = event.data
    // 尝试解析为请求-响应
    if (data && typeof data === 'object' && 'id' in data) {
      const response = data as IpcResponse
      const handler = pending.get(response.id)
      if (handler) {
        pending.delete(response.id)
        if (response.ok) {
          handler.resolve(response.response)
        } else {
          handler.reject(new Error(response.error ?? 'IPC 调用失败'))
        }
        return
      }
    }

    // 尝试解析为推送事件
    if (data && typeof data === 'object' && 'type' in data) {
      const evt = data as IpcEvent
      const listeners = eventListeners.get(evt.type)
      if (listeners) {
        listeners.forEach((fn) => fn(evt.data))
      }
    }
  })
}

/**
 * 发起 IPC 调用
 * @param cmd - 命令名称
 * @param args - 参数
 * @returns Promise
 */
export async function ipc<T = unknown>(cmd: string, args?: unknown): Promise<T> {
  const webview = (window as any).chrome?.webview
  if (!webview) {
    throw new Error('[IPC] 当前环境不支持 WebView2 IPC')
  }

  const id = crypto.randomUUID()
  const message = { id, cmd, args }

  return new Promise<T>((resolve, reject) => {
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
      },
    })

    webview.postMessage(JSON.stringify(message))
  })
}

/** ipc 的别名，语义更清晰 */
export const call = ipc

/**
 * 监听后端推送的事件
 * @param type - 事件类型
 * @param listener - 监听器函数
 * @returns 取消监听函数
 */
export function on(type: string, listener: EventListener): () => void {
  if (!eventListeners.has(type)) {
    eventListeners.set(type, new Set())
  }
  eventListeners.get(type)!.add(listener)
  return () => {
    eventListeners.get(type)?.delete(listener)
  }
}

// 模块加载时立即初始化监听器
initIpcListener()
