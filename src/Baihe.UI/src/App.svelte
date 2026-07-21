<!--
  功能描述: 白鹤启动器根组件 — 窗口外壳 + 侧边栏 + 页面路由
  技术实现: Svelte 5 runes，router 单例控制页面切换
  注意事项: 页面组件懒加载，通过 router.current 响应式切换
-->
<script lang="ts">
  import WindowShell from './components/WindowShell.svelte'
  import Sidebar from './components/Sidebar.svelte'
  import { router } from './lib/router.svelte'
  import Home from './pages/Home.svelte'
  import Download from './pages/Download.svelte'
  import Settings from './pages/Settings.svelte'
  import Tools from './pages/Tools.svelte'
</script>

<WindowShell>
  <Sidebar>
    {#if router.current === 'home'}
      <Home />
    {:else if router.current === 'download'}
      <Download />
    {:else if router.current === 'settings'}
      <Settings />
    {:else if router.current === 'tools'}
      <Tools />
    {/if}
  </Sidebar>
</WindowShell>
