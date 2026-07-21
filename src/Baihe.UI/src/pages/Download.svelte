<!--
  功能描述: 下载页 — 版本搜索 + 分类 tab + 版本卡片网格
  技术实现: Svelte 5 runes，静态版本数据，后续接入 IPC version.list
  注意事项: 版本分类 tab 带底部蓝色指示线，卡片含已安装标记
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'

  // 版本分类
  type VersionCategory = 'release' | 'snapshot' | 'old' | 'fabric' | 'forge'
  let activeCategory = $state<VersionCategory>('release')

  const categories: { key: VersionCategory; label: string }[] = [
    { key: 'release', label: '正式版' },
    { key: 'snapshot', label: '快照' },
    { key: 'old', label: '旧版' },
    { key: 'fabric', label: 'Fabric' },
    { key: 'forge', label: 'Forge' },
  ]

  // 搜索关键词
  let searchQuery = $state('')

  // 版本列表（后续从 IPC 获取）
  const versions = [
    { id: '1.20.4', date: '2023-12-07', type: '正式版', installed: true },
    { id: '1.20.2', date: '2023-09-21', type: '正式版', installed: false },
    { id: '1.20.1', date: '2023-06-12', type: '正式版', installed: true },
    { id: '1.20', date: '2023-06-07', type: '正式版', installed: false },
    { id: '1.19.4', date: '2023-03-14', type: '正式版', installed: false },
    { id: '1.19.3', date: '2022-12-07', type: '正式版', installed: false },
    { id: '1.19.2', date: '2022-08-05', type: '正式版', installed: false },
    { id: '1.19', date: '2022-06-07', type: '正式版', installed: false },
  ]

  // 派生: 过滤后的版本列表
  const filteredVersions = $derived(
    versions.filter((v) =>
      searchQuery ? v.id.toLowerCase().includes(searchQuery.toLowerCase()) : true,
    ),
  )
</script>

<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background-100)] p-8">
  <!-- 标题与搜索 -->
  <header class="mb-6 flex items-end justify-between gap-6">
    <div class="min-w-0 flex-1">
      <h1 class="text-[26px] font-semibold leading-tight text-[var(--foreground)]" style="text-wrap: balance;">下载游戏</h1>
      <p class="mt-1 text-sm text-[var(--muted-foreground)]">选择 Minecraft 版本开始下载</p>
    </div>
    <div class="flex h-11 w-[280px] shrink-0 items-center gap-2 rounded-[0.7rem] border border-[var(--input)] bg-[var(--background)] px-3 shadow-[var(--shadow-xs)]">
      <Icon name="search" size={16} class="shrink-0 text-[var(--muted-foreground)]" />
      <input
        type="text"
        placeholder="搜索版本，如 1.20.4"
        class="min-w-0 flex-1 border-0 bg-transparent text-sm text-[var(--foreground)] outline-none placeholder:text-[var(--muted-foreground)]"
        aria-label="搜索版本"
        bind:value={searchQuery}
      />
    </div>
  </header>

  <!-- 版本分类 tab -->
  <div class="mb-5">
    <div class="flex items-center gap-6 border-b border-[var(--border)]" role="tablist" aria-label="版本分类">
      {#each categories as cat (cat.key)}
        <button
          type="button"
          role="tab"
          aria-selected={activeCategory === cat.key}
          class="relative pb-3 pt-1 text-sm transition-colors {activeCategory === cat.key ? 'font-semibold text-[var(--foreground)]' : 'text-[var(--muted-foreground)] hover:text-[var(--foreground)]'}"
          onclick={() => (activeCategory = cat.key)}
        >
          {cat.label}
          {#if activeCategory === cat.key}
            <span class="absolute bottom-[-1px] left-0 right-0 h-[2px] bg-[var(--primary)]"></span>
          {/if}
        </button>
      {/each}
    </div>
  </div>

  <!-- 版本卡片网格 -->
  <section class="mb-6 grid grid-cols-2 gap-4" aria-label="版本列表">
    {#each filteredVersions as ver (ver.id)}
      <article class="relative flex items-center justify-between gap-3 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-5 shadow-[var(--shadow-sm)] transition-shadow hover:shadow-[var(--shadow-md)]">
        {#if ver.installed}
          <div class="absolute right-3 top-3 flex items-center gap-1 text-[var(--success)]">
            <Icon name="check-circle" size={14} />
            <span class="text-[11px] font-medium whitespace-nowrap">已安装</span>
          </div>
        {/if}
        <div class="min-w-0 flex-1">
          <div class="truncate text-[18px] font-semibold text-[var(--foreground)]" style="font-family: var(--font-mono);">{ver.id}</div>
          <div class="mt-1.5 flex items-center gap-2">
            <span class="whitespace-nowrap text-xs text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{ver.date}</span>
            <span class="inline-flex items-center rounded-[6px] bg-[var(--background-200)] px-2 py-0.5 text-xs text-[var(--muted-foreground)] whitespace-nowrap">{ver.type}</span>
          </div>
        </div>
        <button
          type="button"
          class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--background-200)] px-3.5 py-1.5 text-xs font-semibold text-[var(--foreground)] transition-colors hover:bg-[var(--background-300)]"
        >
          <Icon name="arrow-down" size={14} />
          下载
        </button>
      </article>
    {/each}
  </section>
</div>
