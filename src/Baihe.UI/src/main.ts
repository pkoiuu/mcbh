/**
 * 功能描述: 前端入口文件
 * 技术实现: 挂载 Svelte 根组件到 body
 * 注意事项: 顺序为先加载全局样式，再挂载组件
 */
import './app.css'
import App from './App.svelte'
import { mount } from 'svelte'

// Svelte 5 推荐使用 mount API 替代 new App()
const app = mount(App, {
  target: document.getElementById('app')!
})

export default app
