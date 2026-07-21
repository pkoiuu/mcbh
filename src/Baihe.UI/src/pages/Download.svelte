<!--
  功能描述: 下载页 — 版本搜索 + 分类 tab + 版本卡片网格
  技术实现: Svelte 5 runes，通过 IPC version.list 获取 Mojang 官方版本清单
  注意事项: 版本分类 tab 带底部蓝色指示线，卡片含已安装标记
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'

  /** Mojang 版本条目 */
  interface VersionEntry {
    id: string
    type: string
    releaseTime: string
    url: string
  }

  /** 版本清单响应 */
  interface VersionListResponse {
    latest: { release: string; snapshot: string }
    versions: VersionEntry[]
  }

  /** 已安装实例 */
  interface GameInstance {
    id: string
    version: string
    isInstalled: boolean
  }

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

  // 数据状态
  let versions = $state<VersionEntry[]>([])
  let installedIds = $state<Set<string>>(new Set())
  let isLoading = $state(true)
  let loadError = $state('')

  /** 页面加载时获取版本清单和已安装实例 */
  async function loadData(): Promise<void> {
    isLoading = true
    loadError = ''
    try {
      // 并行获取版本清单和已安装实例
      const [versionData, instances] = await Promise.all([
        ipc<VersionListResponse>('version.list'),
        ipc<GameInstance[]>('instance.list').catch(() => []),
      ])

      versions = versionData.versions
      installedIds = new Set(instances.map((i) => i.id))
    } catch (e: unknown) {
      loadError = e instanceof Error ? e.message : '获取版本清单失败'
    } finally {
      isLoading = false
    }
  }

  /** 根据分类筛选版本类型 */
  function matchCategory(version: VersionEntry, category: VersionCategory): boolean {
    switch (category) {
      case 'release':
        return version.type === 'release'
      case 'snapshot':
        return version.type === 'snapshot'
      case 'old':
        return version.type === 'old_beta' || version.type === 'old_alpha'
      // Fabric / Forge 显示全部版本（加载器安装是 Stage 3 功能）
      case 'fabric':
      case 'forge':
        return version.type === 'release'
      default:
        return true
    }
  }

  /** 格式化日期 */
  function formatDate(isoDate: string): string {
    try {
      const d = new Date(isoDate)
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    } catch {
      return isoDate
    }
  }

  /** 版本类型中文标签 */
  function typeLabel(type: string): string {
    const labels: Record<string, string> = {
      release: '正式版',
      snapshot: '快照',
      old_beta: '旧版 Beta',
      old_alpha: '旧版 Alpha',
    }
    return labels[type] ?? type
  }

  // 派生: 过滤后的版本列表
  const filteredVersions = $derived(
    versions
      .filter((v) => matchCategory(v, activeCategory))
      .filter((v) =>
        searchQuery ? v.id.toLowerCase().includes(searchQuery.toLowerCase()) : true,
      ),
  )

  // 组件挂载时加载数据
  loadData()
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
    {#if isLoading}
      <!-- 加载中骨架屏 -->
      {#each Array(8) as _, i (i)}
        <div class="flex items-center justify-between gap-3 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-5 shadow-[var(--shadow-sm)]">
          <div class="flex-1">
            <div class="h-[22px] w-24 animate-pulse rounded bg-[var(--background-200)]"></div>
            <div class="mt-2 h-[14px] w-32 animate-pulse rounded bg-[var(--background-200)]"></div>
          </div>
        </div>
      {/each}
    {:else if loadError}
      <!-- 加载失败 -->
      <div class="col-span-2 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-8 text-center">
        <p class="text-sm text-[var(--muted-foreground)]">{loadError}</p>
        <button
          type="button"
          class="mt-3 rounded-full bg-[var(--background-200)] px-4 py-1.5 text-xs font-semibold text-[var(--foreground)] hover:bg-[var(--background-300)]"
          onclick={() => loadData()}
        >
          重试
        </button>
      </div>
    {:else if filteredVersions.length === 0}
      <!-- 无搜索结果 -->
      <div class="col-span-2 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-8 text-center">
        <p class="text-sm text-[var(--muted-foreground)]">未找到匹配的版本</p>
      </div>
    {:else}
      <!-- 版本卡片列表 -->
      {#each filteredVersions as ver (ver.id)}
        <article class="relative flex items-center justify-between gap-3 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-5 shadow-[var(--shadow-sm)] transition-shadow hover:shadow-[var(--shadow-md)]">
          {#if installedIds.has(ver.id)}
            <div class="absolute right-3 top-3 flex items-center gap-1 text-[var(--success)]">
              <Icon name="check-circle" size={14} />
              <span class="text-[11px] font-medium whitespace-nowrap">已安装</span>
            </div>
          {/if}
          <div class="min-w-0 flex-1">
            <div class="truncate text-[18px] font-semibold text-[var(--foreground)]" style="font-family: var(--font-mono);">{ver.id}</div>
            <div class="mt-1.5 flex items-center gap-2">
              <span class="whitespace-nowrap text-xs text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{formatDate(ver.releaseTime)}</span>
              <span class="inline-flex items-center rounded-[6px] bg-[var(--background-200)] px-2 py-0.5 text-xs text-[var(--muted-foreground)] whitespace-nowrap">{typeLabel(ver.type)}</span>
            </div>
          </div>
          <button
            type="button"
            class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--background-200)] px-3.5 py-1.5 text-xs font-semibold text-[var(--foreground)] transition-colors hover:bg-[var(--background-300)]"
            disabled={installedIds.has(ver.id)}
          >
            <Icon name="arrow-down" size={14} />
            {installedIds.has(ver.id) ? '已安装' : '下载'}
          </button>
        </article>
      {/each}
    {/if}
  </section>
</div>
