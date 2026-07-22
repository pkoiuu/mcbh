<!--
  功能描述: 白鹤启动器根组件 — 窗口外壳 + 侧边栏 + 页面路由 + Toast 通知
  技术实现: Svelte 5 runes，router 单例控制页面切换，toast 全局通知
  注意事项: 页面组件懒加载，通过 router.current 响应式切换
-->
<script lang="ts">
  import WindowShell from './components/WindowShell.svelte'
  import Sidebar from './components/Sidebar.svelte'
  import { router } from './lib/router.svelte'
  import { toast } from './lib/toast.svelte'
  import Home from './pages/Home.svelte'
  import Download from './pages/Download.svelte'
  import Settings from './pages/Settings.svelte'
  import Tools from './pages/Tools.svelte'
  import Login from './pages/Login.svelte'
  import Chat from './pages/Chat.svelte'
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
    {:else if router.current === 'login'}
      <Login />
    {:else if router.current === 'chat'}
      <Chat />
    {/if}
  </Sidebar>
</WindowShell>

<!-- Toast 通知容器 -->
{#if toast.items.length > 0}
  <div class="fixed bottom-6 right-6 z-50 flex flex-col gap-2">
    {#each toast.items as item (item.id)}
      <div
        class="flex items-center gap-2 rounded-lg px-4 py-3 text-sm font-medium shadow-lg backdrop-blur-md"
        style="
          background: color-mix(in srgb, var(--card) 95%, transparent);
          color: var(--foreground);
          border: 1px solid var(--border);
          animation: slide-in 0.2s ease-out;
        "
      >
        {#if item.type === 'success'}
          <span class="inline-block h-2 w-2 rounded-full" style="background: var(--success);"></span>
        {:else if item.type === 'error'}
          <span class="inline-block h-2 w-2 rounded-full" style="background: var(--destructive);"></span>
        {:else}
          <span class="inline-block h-2 w-2 rounded-full" style="background: var(--primary);"></span>
        {/if}
        {item.message}
      </div>
    {/each}
  </div>
{/if}

<style>
  @keyframes slide-in {
    from {
      opacity: 0;
      transform: translateY(8px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }
</style>
