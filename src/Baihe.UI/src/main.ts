/**
 * 功能描述: 前端入口文件
 * 技术实现: 挂载 Svelte 根组件到 body，全局错误捕获
 * 注意事项: 顺序为先加载全局样式，再挂载组件
 */
import './app.css'
import App from './App.svelte'
import { mount } from 'svelte'

// 全局错误捕获 — 在页面上显示错误，避免白屏无反馈
window.addEventListener('error', (e) => {
  console.error('[全局错误]', e.error ?? e.message)
  showError(e.error?.message ?? e.message ?? '未知错误')
})

window.addEventListener('unhandledrejection', (e) => {
  console.error('[未处理的 Promise 拒绝]', e.reason)
  showError(e.reason?.message ?? String(e.reason))
})

/** 在页面上显示错误信息 — 替换加载屏 */
function showError(message: string): void {
  const app = document.getElementById('app')
  if (!app) return
  app.innerHTML = `
    <div style="position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;background:#1a1a1c;color:#ff6b6b;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',system-ui,sans-serif;font-size:14px;padding:40px;text-align:center;">
      <div style="font-size:18px;font-weight:600;color:#fff;">白鹤服务器启动器</div>
      <div style="max-width:500px;word-break:break-word;line-height:1.6;">${message}</div>
      <div style="color:#666;font-size:12px;">请将此错误信息反馈给管理员</div>
    </div>
  `
}

// Svelte 5 推荐使用 mount API 替代 new App()
let app: unknown
try {
  app = mount(App, {
    target: document.getElementById('app')!,
  })
} catch (err) {
  showError(err instanceof Error ? err.message : String(err))
  throw err
}

export default app
