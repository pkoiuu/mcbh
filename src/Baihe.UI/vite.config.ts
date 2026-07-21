/**
 * 功能描述: Vite 构建配置
 * 技术实现: 集成 Svelte 5 + Tailwind 4 插件，输出到 Baihe.Host/assets
 * 注意事项: base 设为 '/' 以适配 WebView2 虚拟主机映射
 */
import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  // WebView2 虚拟主机映射根路径
  base: '/',
  plugins: [svelte(), tailwindcss()],
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
    sourcemap: false
  }
})
