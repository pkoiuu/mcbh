/**
 * 功能描述: 轻量 Toast 通知系统 — 全局共享状态
 * 技术实现: Svelte 5 runes，class + $state 实现响应式
 * 注意事项: 通过 toast.show() 调用，自动消失
 */

interface ToastItem {
  id: number
  message: string
  type: 'info' | 'success' | 'error'
}

class ToastState {
  items = $state<ToastItem[]>([])
  private nextId = 0

  show(message: string, type: 'info' | 'success' | 'error' = 'info'): void {
    const id = this.nextId++
    this.items = [...this.items, { id, message, type }]
    // 3 秒后自动移除
    setTimeout(() => {
      this.items = this.items.filter(i => i.id !== id)
    }, 3000)
  }

  success(message: string): void {
    this.show(message, 'success')
  }

  error(message: string): void {
    this.show(message, 'error')
  }
}

export const toast = new ToastState()
