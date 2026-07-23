/**
 * 功能描述: 主题管理 Store — 暗色/亮色切换，持久化到 localStorage
 * 技术实现: Svelte 5 runes ($state) 响应式，通过 .dark 类切换 <html> 元素，IPC 通知后端
 * 注意事项: 默认暗色主题；切换时添加 .theme-transitioning 过渡类以触发 CSS 过渡动画
 */

import { ipc } from './ipc'

/** 主题类型 */
export type Theme = 'dark' | 'light'

/** localStorage 存储键名 */
const THEME_KEY = 'baihe_theme'

/** 过渡类名移除延时 (ms) */
const TRANSITION_DURATION = 200

/** 当前主题（响应式状态），默认暗色 */
let currentTheme = $state<Theme>('dark')

/**
 * 应用主题到 DOM — 切换 <html> 元素的 .dark 类
 * @param theme - 目标主题
 */
function applyTheme(theme: Theme): void {
  const root = document.documentElement
  if (theme === 'dark') {
    root.classList.add('dark')
  } else {
    root.classList.remove('dark')
  }
}

/**
 * 初始化主题 — 从 localStorage 读取并同步到当前状态与 DOM
 * 默认（无值或读取失败时）为暗色
 */
function init(): void {
  let stored: Theme = 'dark'
  try {
    const raw = localStorage.getItem(THEME_KEY)
    if (raw === 'light' || raw === 'dark') {
      stored = raw
    }
  } catch {
    // localStorage 不可用时使用默认暗色
  }
  currentTheme = stored
  applyTheme(currentTheme)
}

/**
 * 切换主题 — 暗色/亮色互切
 * 添加过渡类、持久化到 localStorage、应用到 DOM、通过 IPC 通知后端
 */
function toggle(): void {
  // 切换当前主题
  currentTheme = currentTheme === 'dark' ? 'light' : 'dark'

  // 添加过渡类，200ms 后移除
  const root = document.documentElement
  root.classList.add('theme-transitioning')
  setTimeout(() => {
    root.classList.remove('theme-transitioning')
  }, TRANSITION_DURATION)

  // 持久化到 localStorage
  try {
    localStorage.setItem(THEME_KEY, currentTheme)
  } catch {
    // localStorage 不可用时静默处理
  }

  // 应用到 DOM
  applyTheme(currentTheme)

  // 通过 IPC 通知后端
  ipc('theme.set', { theme: currentTheme }).catch(() => {
    // IPC 不可用时静默处理
  })
}

/** 主题管理对象 */
export const theme = {
  /** 当前主题 */
  get current(): Theme {
    return currentTheme
  },
  /** 初始化主题（从 localStorage 同步） */
  init,
  /** 切换主题 */
  toggle,
}
