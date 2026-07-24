<!--
  功能描述: 白鹤启动器根组件 — 窗口外壳 + 侧边栏 + 页面路由 + Toast 通知 + 微信名收集弹窗
  技术实现: Svelte 5 runes，router 单例控制页面切换，toast 全局通知
  注意事项: 页面组件懒加载，通过 router.current 响应式切换；首次启动检查微信名
-->
<script lang="ts">
  import { onMount } from 'svelte'
  import WindowShell from './components/WindowShell.svelte'
  import Sidebar from './components/Sidebar.svelte'
  import WeChatDialog from './components/WeChatDialog.svelte'
  import { router } from './lib/router.svelte'
  import { toast } from './lib/toast.svelte'
  import { ipc } from './lib/ipc'
  import Home from './pages/Home.svelte'
  import Download from './pages/Download.svelte'
  import Settings from './pages/Settings.svelte'
  import Tools from './pages/Tools.svelte'
  import Login from './pages/Login.svelte'
  import { theme } from './lib/theme.svelte'

  // 初始化主题（从 localStorage 同步到当前状态与 DOM）
  theme.init()

  /** 是否显示微信名收集弹窗 — 首次启动且未填写微信名时为 true */
  let showWeChatDialog = $state(false)

  // 启动时检查微信名是否已填写 — 使用 onMount 确保只执行一次
  onMount(() => {
    ipc<{ name: string | null }>('wechat.get')
      .then((res) => {
        console.log('[WeChat] check result:', res)
        if (!res?.name) {
          showWeChatDialog = true
        }
      })
      .catch((err) => {
        // IPC 失败时也弹窗 — 无法确认是否已填写，安全起见显示弹窗
        console.warn('[WeChat] IPC failed, showing dialog:', err)
        showWeChatDialog = true
      })
  })

  /** 微信名填写完成 — 关闭弹窗 */
  function handleWeChatDone(): void {
    showWeChatDialog = false
  }
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
    {/if}
  </Sidebar>
</WindowShell>

<!-- 微信名收集弹窗 — 首次启动且未填写时显示 -->
{#if showWeChatDialog}
  <WeChatDialog ondone={handleWeChatDone} />
{/if}

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
