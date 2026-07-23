<!--
  功能描述: 毛玻璃侧边栏 — 用户区 + 导航项 + 版本号
  技术实现: 240px 宽，backdrop-filter 毛玻璃效果，Svelte 5 runes 响应式导航
  注意事项: 导航通过 router 单例切换页面，active 状态用 data-active 属性控制
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { router, navItems } from '../lib/router.svelte'
  import { ipc } from '../lib/ipc'
  import defaultAvatar from '../assets/default-avatar.png'
  import type { Snippet } from 'svelte'

  interface Props {
    children: Snippet
  }

  let { children }: Props = $props()

  /** 处理导航点击 */
  function handleNav(e: MouseEvent, key: string): void {
    e.preventDefault()
    router.navigate(key)
  }

  // 用户信息 — 从 IPC 获取，响应式更新
  let username = $state('未设置')
  let authMethod = $state('离线模式')
  let avatarData = $state<string | null>(null)

  /** 加载账户信息 — 参照 PCL CE McLogin，从后端获取当前登录用户 */
  async function loadAccount(): Promise<void> {
    try {
      const result = await ipc<{ username: string | null; typeDisplay?: string; isUserSet: boolean }>('auth.current')
      if (result.isUserSet && result.username) {
        username = result.username
        authMethod = result.typeDisplay || '离线模式'
      } else {
        username = '未设置'
        authMethod = '点击登录'
      }
    } catch {
      username = '未设置'
      authMethod = '点击登录'
    }
  }

  /** 加载头像 — 从 localStorage 读取，无则使用默认头像 */
  function loadAvatar(): void {
    try {
      avatarData = localStorage.getItem('baihe_avatar')
    } catch { }
  }

  // 加载账户信息和头像 — 路由变化时重新加载（登录/登出后自动刷新侧边栏）
  $effect(() => {
    const _route = router.current
    loadAccount()
    loadAvatar()
  })
</script>

<aside
  class="flex w-[240px] shrink-0 flex-col border-r border-[var(--border)] p-4"
  style="background: color-mix(in srgb, var(--sidebar) 80%, transparent); backdrop-filter: blur(30px) saturate(180%); -webkit-backdrop-filter: blur(30px) saturate(180%);"
>
  <!-- 用户区 — 点击跳转登录页 -->
  <button
    type="button"
    class="flex w-full items-center gap-3 rounded-lg p-1 transition-colors hover:bg-[var(--secondary)]"
    onclick={() => router.navigate('login')}
    aria-label="账户登录"
  >
    <div class="h-10 w-10 shrink-0 overflow-hidden rounded-[12px] border border-[var(--border)] bg-[var(--accent)]">
      {#if avatarData}
        <img src={avatarData} alt="头像" class="h-full w-full object-cover" />
      {:else}
        <img src={defaultAvatar} alt="默认头像" class="h-full w-full object-cover" />
      {/if}
    </div>
    <div class="min-w-0 flex-1">
      <div class="truncate text-sm font-semibold text-[var(--foreground)]">{username}</div>
      <div class="truncate text-xs text-[var(--muted-foreground)]">{authMethod}</div>
    </div>
  </button>

  <!-- 分隔线 -->
  <div class="mt-3 border-t border-[var(--border)]"></div>

  <!-- 导航列表 -->
  <nav class="mt-3 flex flex-col gap-1" aria-label="主导航">
    {#each navItems as item (item.key)}
      <a
      href="javascript:void(0)"
      class="nav-item group flex h-9 items-center gap-2 rounded-lg px-3 text-sm text-[var(--sidebar-foreground)] transition-all duration-200 hover:bg-[var(--secondary)] data-[active=true]:bg-[var(--sidebar-accent)] data-[active=true]:text-[var(--foreground)]"
      data-active={router.current === item.key}
      onclick={(e) => handleNav(e, item.key)}
    >
        <span class="flex items-center text-[var(--icon-muted)] transition-colors duration-200 group-data-[active=true]:text-[var(--primary)]">
          <Icon name={item.icon} size={18} />
        </span>
        <span class="truncate">{item.label}</span>
      </a>
    {/each}
  </nav>

  <!-- 底部版本号 -->
  <div class="mt-auto pt-3">
    <div class="text-xs text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">
      白鹤服务器 v1.0.0
    </div>
  </div>
</aside>

<!-- 主内容区域 -->
<div class="flex min-h-0 flex-1 flex-col overflow-hidden">
  {@render children()}
</div>
