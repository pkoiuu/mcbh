<!--
  功能描述: 下载页 — 版本搜索 + 分类 tab + 版本卡片网格 + 下载进度
  技术实现: Svelte 5 runes，通过 IPC version.list 获取版本清单，download.start 触发下载
  注意事项: 监听 download.progress/complete/error 推送事件实时显示进度
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc, on } from '../lib/ipc'
  import versionIcon from '../assets/version-icon.png'

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

  /** 下载进度数据 */
  interface DownloadProgress {
    phase: string
    currentFile: string
    completedFiles: number
    totalFiles: number
    downloadedBytes: number
    totalBytes: number
    percent: number
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

  // 下载状态
  let downloadingVersion = $state('')
  let downloadProgress = $state<DownloadProgress | null>(null)
  let downloadError = $state('')
  let downloadComplete = $state(false)

  /** 页面加载时获取版本清单和已安装实例 */
  async function loadData(): Promise<void> {
    isLoading = true
    loadError = ''
    try {
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

  /** 触发版本下载 */
  async function handleDownload(versionId: string): Promise<void> {
    if (downloadingVersion) return

    downloadingVersion = versionId
    downloadProgress = null
    downloadError = ''
    downloadComplete = false

    try {
      await ipc('download.start', versionId)
    } catch (e: unknown) {
      downloadError = e instanceof Error ? e.message : '启动下载失败'
      downloadingVersion = ''
    }
  }

  /** 触发 Fabric 安装 */
  async function handleFabricInstall(gameVersion: string): Promise<void> {
    if (downloadingVersion) return

    downloadingVersion = `${gameVersion}-fabric`
    downloadProgress = null
    downloadError = ''
    downloadComplete = false

    try {
      await ipc('fabric.install', gameVersion)
    } catch (e: unknown) {
      downloadError = e instanceof Error ? e.message : '启动 Fabric 安装失败'
      downloadingVersion = ''
    }
  }

  // 注册推送事件监听器 — 在 $effect 中注册，组件卸载时清理（避免内存泄漏）
  $effect(() => {
    // 监听下载进度事件
    const offProgress = on('download.progress', (data) => {
      downloadProgress = data as DownloadProgress
    })

    const offComplete = on('download.complete', () => {
      downloadComplete = true
      downloadingVersion = ''
      // 刷新已安装列表
      loadData()
      setTimeout(() => {
        downloadProgress = null
        downloadComplete = false
      }, 3000)
    })

    const offError = on('download.error', (data) => {
      downloadError = (data as { error: string }).error
      downloadingVersion = ''
    })

    // 监听 Fabric 事件
    const offFabricProgress = on('fabric.progress', (data) => {
      const d = data as { phase: string; message: string }
      downloadProgress = {
        phase: d.phase,
        currentFile: d.message,
        completedFiles: 0,
        totalFiles: 1,
        downloadedBytes: 0,
        totalBytes: 0,
        percent: d.phase === 'downloading' ? 50 : 25,
      }
    })

    const offFabricComplete = on('fabric.complete', () => {
      downloadComplete = true
      downloadingVersion = ''
      loadData()
      setTimeout(() => {
        downloadProgress = null
        downloadComplete = false
      }, 3000)
    })

    const offFabricError = on('fabric.error', (data) => {
      downloadError = (data as { error: string }).error
      downloadingVersion = ''
    })

    // 组件卸载时清理所有监听器
    return () => {
      offProgress()
      offComplete()
      offError()
      offFabricProgress()
      offFabricComplete()
      offFabricError()
    }
  })

  /** 根据分类筛选版本类型 */
  function matchCategory(version: VersionEntry, category: VersionCategory): boolean {
    switch (category) {
      case 'release':
        return version.type === 'release'
      case 'snapshot':
        return version.type === 'snapshot'
      case 'old':
        return version.type === 'old_beta' || version.type === 'old_alpha'
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

  /** 格式化文件大小 */
  function formatSize(bytes: number): string {
    if (bytes === 0) return '—'
    return bytes < 1024 * 1024
      ? `${(bytes / 1024).toFixed(1)} KB`
      : bytes < 1024 * 1024 * 1024
        ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
        : `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
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

  /** 阶段中文标签 */
  function phaseLabel(phase: string): string {
    const labels: Record<string, string> = {
      versionJson: '版本信息',
      client: '客户端',
      libraries: '库文件',
      assetIndex: '资源索引',
      assets: '资源文件',
    }
    return labels[phase] ?? phase
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

  <!-- 下载进度条 -->
  {#if downloadingVersion || downloadProgress || downloadError || downloadComplete}
    <div class="mb-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-4 shadow-[var(--shadow-sm)]">
      {#if downloadError}
        <div class="flex items-center gap-2 text-sm text-red-500">
          <Icon name="circle-x" size={16} />
          <span>下载失败: {downloadError}</span>
        </div>
      {:else if downloadComplete}
        <div class="flex items-center gap-2 text-sm" style="color: var(--success);">
          <Icon name="check-circle" size={16} />
          <span>下载完成！实例已就绪。</span>
        </div>
      {:else if downloadProgress}
        <div class="flex flex-col gap-2">
          <div class="flex items-center justify-between text-sm">
            <span class="font-medium text-[var(--foreground)]">
              {phaseLabel(downloadProgress.phase)}: {downloadProgress.currentFile}
            </span>
            <span class="text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">
              {downloadProgress.completedFiles}/{downloadProgress.totalFiles}
              {#if downloadProgress.totalBytes > 0}
                · {formatSize(downloadProgress.downloadedBytes)}/{formatSize(downloadProgress.totalBytes)}
              {/if}
            </span>
          </div>
          <!-- 进度条 -->
          <div class="h-2 w-full overflow-hidden rounded-full bg-[var(--background-200)]">
            <div
              class="h-full rounded-full bg-[var(--primary)] transition-[width] duration-300"
              style="width: {downloadProgress.percent}%;"
            ></div>
          </div>
          <div class="text-right text-xs text-[var(--muted-foreground)]">{downloadProgress.percent}%</div>
        </div>
      {:else}
        <div class="flex items-center gap-2 text-sm text-[var(--muted-foreground)]">
          <div class="h-4 w-4 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent"></div>
          <span>正在开始下载 {downloadingVersion}...</span>
        </div>
      {/if}
    </div>
  {/if}

  <!-- Fabric/Forge 提示 -->
  {#if activeCategory === 'fabric' || activeCategory === 'forge'}
    <div class="mb-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-4 shadow-[var(--shadow-sm)]">
      <div class="flex items-center gap-3">
        <Icon name="info" size={18} class="shrink-0 text-[var(--primary)]" />
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium text-[var(--foreground)]">
            {activeCategory === 'fabric' ? 'Fabric Loader 安装' : 'Forge 安装'}
          </p>
          <p class="mt-0.5 text-xs text-[var(--muted-foreground)]">
            {activeCategory === 'fabric'
              ? '选择游戏版本后点击"安装 Fabric"按钮，将自动下载最新 Fabric Loader'
              : 'Forge 安装功能将在后续版本中提供'}
          </p>
        </div>
      </div>
    </div>
  {/if}

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
      {#each Array(8) as _, i (i)}
        <div class="flex items-center justify-between gap-3 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-5 shadow-[var(--shadow-sm)]">
          <div class="flex-1">
            <div class="h-[22px] w-24 animate-pulse rounded bg-[var(--background-200)]"></div>
            <div class="mt-2 h-[14px] w-32 animate-pulse rounded bg-[var(--background-200)]"></div>
          </div>
        </div>
      {/each}
    {:else if loadError}
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
      <div class="col-span-2 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-8 text-center">
        <p class="text-sm text-[var(--muted-foreground)]">未找到匹配的版本</p>
      </div>
    {:else}
      {#each filteredVersions as ver (ver.id)}
        <article class="relative flex items-center justify-between gap-3 rounded-[0.9rem] border border-[var(--border)] bg-[var(--card)] p-5 shadow-[var(--shadow-sm)] transition-all duration-200 hover:shadow-[var(--shadow-md)] hover:-translate-y-0.5">
          {#if installedIds.has(ver.id) || installedIds.has(`${ver.id}-fabric`)}
            <div class="absolute right-3 top-3 flex items-center gap-1 text-[var(--success)]">
              <Icon name="check-circle" size={14} />
              <span class="whitespace-nowrap text-[11px] font-medium">已安装</span>
            </div>
          {/if}
          <!-- 版本图标 -->
          <img src={versionIcon} alt="" class="h-10 w-10 shrink-0 rounded-[8px] object-cover" />
          <div class="min-w-0 flex-1">
            <div class="truncate text-[18px] font-semibold text-[var(--foreground)]" style="font-family: var(--font-mono);">{ver.id}</div>
            <div class="mt-1.5 flex items-center gap-2">
              <span class="whitespace-nowrap text-xs text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{formatDate(ver.releaseTime)}</span>
              <span class="inline-flex items-center rounded-[6px] bg-[var(--background-200)] px-2 py-0.5 text-xs whitespace-nowrap text-[var(--muted-foreground)]">{typeLabel(ver.type)}</span>
            </div>
          </div>
          {#if activeCategory === 'fabric'}
            <!-- Fabric 安装按钮 -->
            <button
              type="button"
              class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--primary)] px-3.5 py-1.5 text-xs font-semibold text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
              disabled={!!downloadingVersion || installedIds.has(`${ver.id}-fabric`)}
              onclick={() => handleFabricInstall(ver.id)}
            >
              <Icon name="arrow-down" size={14} />
              {installedIds.has(`${ver.id}-fabric`) ? '已安装' : '安装 Fabric'}
            </button>
          {:else}
            <!-- 普通下载按钮 -->
            <button
              type="button"
              class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--background-200)] px-3.5 py-1.5 text-xs font-semibold text-[var(--foreground)] transition-colors hover:bg-[var(--background-300)] disabled:cursor-not-allowed disabled:opacity-50"
              disabled={!!downloadingVersion || installedIds.has(ver.id)}
              onclick={() => handleDownload(ver.id)}
            >
              <Icon name="arrow-down" size={14} />
              {installedIds.has(ver.id) ? '已安装' : '下载'}
            </button>
          {/if}
        </article>
      {/each}
    {/if}
  </section>
</div>
