/**
 * 功能描述: Vite 构建配置
 * 技术实现: 集成 Svelte 5 + Tailwind 4 插件，输出到 Baihe.Host/wwwroot
 * 注意事项: base 设为 '/' 以适配 WebView2 虚拟主机映射
 */
import { defineConfig, type PluginOption } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

/**
 * WebView2 兼容插件 — 移除 crossorigin，将 type="module" 替换为 defer
 * WebView2 虚拟主机映射 (SetVirtualHostNameToFolderMapping) 对 ES module 支持不完整
 * 1. 移除 crossorigin 属性 — 虚拟主机映射的 CORS 处理可能不完整
 * 2. 将 type="module" 替换为 defer — Vite 构建产物已完全打包，无 ESM 语法
 *    使用 defer 确保脚本在 DOM 解析完成后执行，避免 getElementById('app') 返回 null
 */
function removeCrossOrigin(): PluginOption {
  return {
    name: 'remove-crossorigin',
    enforce: 'post',
    transformIndexHtml(html: string): string {
      return html
        .replace(/<script([^>]*?)\scrossorigin([^>]*?)>/g, '<script$1$2>')
        .replace(/<link([^>]*?)\scrossorigin([^>]*?)>/g, '<link$1$2>')
        .replace(/<script([^>]*?)\stype="module"([^>]*?)>/g, '<script$1 defer$2>')
    },
  }
}

export default defineConfig({
  // WebView2 虚拟主机映射根路径
  base: '/',
  plugins: [svelte(), tailwindcss(), removeCrossOrigin()],
  resolve: {
    alias: {
      // 提供 @ 路径别名，指向 src 目录
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    strictPort: true
  },
  build: {
    // 输出到 Host 的 wwwroot 目录，由 WebView2 虚拟主机映射加载
    // 注意: 不使用 assets 目录，因为 Windows 不区分大小写，会与 Assets/icon.ico 冲突
    outDir: '../Baihe.Host/wwwroot',
    emptyOutDir: true,
    target: 'esnext',
    // 关闭 sourcemap，减少生产环境体积
    sourcemap: false,
    // 关闭模块预加载 — WebView2 虚拟主机映射不需要
    modulePreload: false,
  }
})
