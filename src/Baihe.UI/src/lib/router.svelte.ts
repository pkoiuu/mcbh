/**
 * 功能描述: 轻量路由 — 基于 Svelte 5 runes 的页面状态管理
 * 技术实现: 使用 $state rune 维护当前页面，不引入路由库
 * 注意事项: 页面切换通过 navigate() 函数触发
 * 文件扩展名: .svelte.ts — Svelte 5 要求在 .ts 文件中使用 runes 必须用此扩展名
 */

/** 页面类型 — 可路由的页面（聊天页面通过 IPC 外部导航，不走路由，故不在此列） */
export type PageKey = 'home' | 'download' | 'settings' | 'tools' | 'login'

/** 路由状态类 — 使用 class + $state 实现响应式 */
class RouterState {
  /** 当前激活页面 */
  current = $state<PageKey>('home')

  /**
   * 导航到指定页面
   * @param page - 目标页面 key
   */
  navigate(page: PageKey): void {
    this.current = page
  }
}

/** 全局路由单例 */
export const router = new RouterState()

/** 导航项配置 */
export const navItems: { key: PageKey; label: string; icon: string }[] = [
  { key: 'home', label: '启动', icon: 'circle-play' },
  { key: 'download', label: '下载', icon: 'arrow-down' },
  { key: 'settings', label: '设置', icon: 'grip' },
  { key: 'tools', label: '工具', icon: 'box' },
]
