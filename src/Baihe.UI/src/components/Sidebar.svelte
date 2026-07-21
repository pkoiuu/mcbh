<!--
  功能描述: 毛玻璃侧边栏 — 用户区 + 导航项 + 版本号
  技术实现: 240px 宽，backdrop-filter 毛玻璃效果，Svelte 5 runes 响应式导航
  注意事项: 导航通过 router 单例切换页面，active 状态用 data-active 属性控制
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { router, navItems } from '../lib/router.svelte'
  import type { Snippet } from 'svelte'

  interface Props {
    children: Snippet
  }

  let { children }: Props = $props()

  // 用户信息（后续从 IPC 获取）
  let username = $state('Player')
  let authMethod = $state('离线模式')
</script>

<aside
  class="flex w-[240px] shrink-0 flex-col border-r border-[var(--border)] p-4"
  style="background: color-mix(in srgb, var(--sidebar) 80%, transparent); backdrop-filter: blur(30px) saturate(180%); -webkit-backdrop-filter: blur(30px) saturate(180%);"
>
  <!-- 用户区 -->
  <div class="flex items-center gap-3">
    <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-[12px] bg-[var(--background-300)]">
      <Icon name="user" size={18} class="text-[var(--icon-muted)]" />
    </div>
    <div class="min-w-0 flex-1">
      <div class="truncate text-sm font-semibold text-[var(--foreground)]">{username}</div>
      <div class="truncate text-xs text-[var(--muted-foreground)]">{authMethod}</div>
    </div>
  </div>

  <!-- 分隔线 -->
  <div class="mt-3 border-t border-[var(--border)]"></div>

  <!-- 导航列表 -->
  <nav class="mt-3 flex flex-col gap-1" aria-label="主导航">
    {#each navItems as item (item.key)}
      <a
        href="#"
        class="group flex h-9 items-center gap-2 rounded-lg px-3 text-sm text-[var(--sidebar-foreground)] transition-colors hover:bg-[var(--secondary)] data-[active=true]:bg-[var(--sidebar-accent)] data-[active=true]:text-[var(--foreground)]"
        data-active={router.current === item.key}
        onclick={(e) => {
          e.preventDefault()
          router.navigate(item.key)
        }}
      >
        <span class="flex items-center text-[var(--icon-muted)] group-data-[active=true]:text-[var(--primary)]">
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
<div class="min-h-0 flex-1 overflow-hidden">
  {@render children()}
</div>
