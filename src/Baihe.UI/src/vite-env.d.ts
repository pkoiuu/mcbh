/**
 * 功能描述: Svelte 类型声明补充
 * 技术实现: 声明 .svelte 文件模块与 WebView2 类型
 * 注意事项: 确保 TypeScript 能正确识别 Svelte 组件导入
 */

/// <reference types="svelte" />
/// <reference types="vite/client" />

declare module '*.svelte' {
  import type { ComponentType } from 'svelte'
  const component: ComponentType
  export default component
}

interface Window {
  chrome?: {
    webview?: {
      postMessage: (message: string) => void
      addEventListener: (
        type: 'message',
        listener: (event: MessageEvent) => void
      ) => void
      removeEventListener: (
        type: 'message',
        listener: (event: MessageEvent) => void
      ) => void
    }
  }
}
