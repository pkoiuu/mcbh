<!--
  功能描述: 工具箱页面 — Mod 管理、存档备份、截图管理、游戏修复四大功能 Tab
  技术实现: Svelte 5 runes ($state/$effect/$derived)，通过 IPC 与后端通信
  注意事项: 所有背景使用语义变量，暗色模式自动适配；缺失图标用内联 SVG snippet 补充
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'
  import { toast } from '../lib/toast.svelte'
  import { router } from '../lib/router.svelte'

  // ===== 类型定义 =====

  interface ModItem {
    fileName: string
    displayName: string
    size: number
    sizeText: string
    enabled: boolean
    lastModified: string
  }

  interface SaveItem {
    name: string
    lastModified: string
    folderSize: number
    sizeText: string
    hasLevelData: boolean
  }

  interface BackupItem {
    fileName: string
    sizeText: string
    createdTime: string
  }

  interface ScreenshotItem {
    fileName: string
    filePath: string
    sizeText: string
    createdTime: string
  }

  interface RepairDetail {
    type: 'success' | 'error' | 'info'
    file: string
    message: string
  }

  interface RepairResult {
    success: boolean
    hasErrors: boolean
    message: string
    details: RepairDetail[]
  }

  type TabId = 'mods' | 'saves' | 'screenshots' | 'repair' | 'chat'

  // ===== Tab 配置 =====

  const tabs: { id: TabId; name: string; icon: string }[] = [
    { id: 'mods', name: 'Mod管理', icon: 'package' },
    { id: 'saves', name: '存档备份', icon: 'box' },
    { id: 'screenshots', name: '截图管理', icon: 'grip' },
    { id: 'repair', name: '游戏修复', icon: 'info' },
    { id: 'chat', name: '聊天', icon: 'message-circle' },
  ]

  /** 聊天 Tab 是否可见 — 由设置页开发者选项控制 */
  let chatEnabled = $state(localStorage.getItem('baihe_chat_enabled') === 'true')

  // ===== 状态 =====

  let activeTab = $state<TabId>('mods')

  // Mod 管理状态
  let mods = $state<ModItem[]>([])
  let modsLoading = $state(false)
  let modActionLoading = $state<string | null>(null)

  // 存档备份状态
  let saves = $state<SaveItem[]>([])
  let backups = $state<BackupItem[]>([])
  let savesLoading = $state(false)
  let saveActionLoading = $state<string | null>(null)

  // 截图管理状态
  let screenshots = $state<ScreenshotItem[]>([])
  let screenshotsLoading = $state(false)
  let failedImages = $state<Set<string>>(new Set())

  // 游戏修复状态
  let repairResult = $state<RepairResult | null>(null)
  let repairLoading = $state(false)

  // ===== 辅助函数 =====

  /** 将 Windows 文件路径转换为 file:// URL */
  function toFileUrl(filePath: string): string {
    const normalized = filePath.replace(/\\/g, '/')
    if (normalized.startsWith('file://')) return normalized
    if (normalized.startsWith('/')) return 'file://' + normalized
    return 'file:///' + normalized
  }

  /** 从备份文件名中提取存档名 */
  function extractSaveName(backupFileName: string): string {
    let name = backupFileName.replace(/\.zip$/i, '')
    name = name.replace(/[_-]backup[_-]?.*$/i, '')
    name = name.replace(/[_-]\d{4}[-_]?\d{2}[-_]?\d{2}.*$/i, '')
    return name || backupFileName.replace(/\.zip$/i, '')
  }

  /** 截图加载失败时记录 */
  function handleImageError(fileName: string): void {
    failedImages = new Set([...failedImages, fileName])
  }

  // ===== 数据加载函数 =====

  /** 加载 Mod 列表 */
  async function loadMods(): Promise<void> {
    modsLoading = true
    try {
      mods = await ipc<ModItem[]>('mods.list')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '加载 Mod 列表失败')
      mods = []
    } finally {
      modsLoading = false
    }
  }

  /** 加载存档列表和备份列表 */
  async function loadSavesData(): Promise<void> {
    savesLoading = true
    try {
      const [savesResult, backupsResult] = await Promise.all([
        ipc<SaveItem[]>('saves.list'),
        ipc<BackupItem[]>('saves.backups'),
      ])
      saves = savesResult
      backups = backupsResult
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '加载存档数据失败')
      saves = []
      backups = []
    } finally {
      savesLoading = false
    }
  }

  /** 加载截图列表 */
  async function loadScreenshots(): Promise<void> {
    screenshotsLoading = true
    try {
      screenshots = await ipc<ScreenshotItem[]>('screenshots.list')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '加载截图列表失败')
      screenshots = []
    } finally {
      screenshotsLoading = false
    }
  }

  // ===== Mod 管理操作 =====

  /** 切换 Mod 启用/禁用状态 */
  async function toggleMod(fileName: string): Promise<void> {
    if (modActionLoading) return
    modActionLoading = fileName
    try {
      const result = await ipc<{ success: boolean; enabled: boolean }>('mods.toggle', fileName)
      if (result.success) {
        toast.success(result.enabled ? 'Mod 已启用' : 'Mod 已禁用')
        // 重新加载列表，确保 fileName 与磁盘文件同步
        await loadMods()
      } else {
        toast.error('操作失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '操作失败')
    } finally {
      modActionLoading = null
    }
  }

  /** 删除 Mod */
  async function deleteMod(fileName: string): Promise<void> {
    if (modActionLoading) return
    modActionLoading = fileName
    try {
      const result = await ipc<{ success: boolean }>('mods.delete', fileName)
      if (result.success) {
        mods = mods.filter((m) => m.fileName !== fileName)
        toast.success('Mod 已删除')
      } else {
        toast.error('删除失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '删除失败')
    } finally {
      modActionLoading = null
    }
  }

  /** 打开 mods 文件夹 */
  async function openModsFolder(): Promise<void> {
    try {
      await ipc('mods.openFolder')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '打开文件夹失败')
    }
  }

  // ===== 存档备份操作 =====

  /** 备份指定存档 */
  async function backupSave(saveName: string): Promise<void> {
    if (saveActionLoading) return
    saveActionLoading = saveName
    try {
      const result = await ipc<{ success: boolean; backupName: string; sizeText: string }>(
        'saves.backup',
        saveName,
      )
      if (result.success) {
        toast.success(`备份成功: ${result.backupName}`)
        await loadSavesData()
      } else {
        toast.error('备份失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '备份失败')
    } finally {
      saveActionLoading = null
    }
  }

  /** 从备份恢复存档 */
  async function restoreBackup(backupFileName: string): Promise<void> {
    if (saveActionLoading) return
    saveActionLoading = backupFileName
    try {
      const saveName = extractSaveName(backupFileName)
      const result = await ipc<{ success: boolean; saveName: string }>('saves.restore', {
        backupFileName,
        saveName,
      })
      if (result.success) {
        toast.success(`存档 "${result.saveName}" 已恢复`)
        await loadSavesData()
      } else {
        toast.error('恢复失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '恢复失败')
    } finally {
      saveActionLoading = null
    }
  }

  /** 删除备份文件 */
  async function deleteBackup(fileName: string): Promise<void> {
    if (saveActionLoading) return
    saveActionLoading = fileName
    try {
      const result = await ipc<{ success: boolean }>('saves.deleteBackup', fileName)
      if (result.success) {
        backups = backups.filter((b) => b.fileName !== fileName)
        toast.success('备份已删除')
      } else {
        toast.error('删除失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '删除失败')
    } finally {
      saveActionLoading = null
    }
  }

  // ===== 截图管理操作 =====

  /** 打开截图文件夹 */
  async function openScreenshotsFolder(): Promise<void> {
    try {
      await ipc('tools.openFolder', 'screenshots')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '打开文件夹失败')
    }
  }

  // ===== 游戏修复操作 =====

  /** 执行游戏文件检查 */
  async function runRepair(): Promise<void> {
    repairLoading = true
    repairResult = null
    try {
      const result = await ipc<RepairResult>('tools.repair')
      repairResult = result
      if (result.success && !result.hasErrors) {
        toast.success(result.message || '检查完成，未发现问题')
      } else if (result.hasErrors) {
        toast.error(result.message || '发现部分问题')
      } else {
        toast.show(result.message || '检查完成')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '检查失败')
    } finally {
      repairLoading = false
    }
  }

  // ===== 导航与 Tab 切换 =====

  /** 新建实例 — 跳转到下载页面 */
  function handleNewInstance(): void {
    router.navigate('download')
    toast.show('正在跳转到下载页面')
  }

  /** 导入存档 — 提示用户在存档备份页面操作 */
  function handleImportSave(): void {
    toast.show('请在存档备份页面操作')
  }

  /** 切换 Tab 并按需加载数据 */
  function switchTab(tabId: TabId): void {
    if (activeTab === tabId) return
    activeTab = tabId
    if (tabId === 'mods') loadMods()
    else if (tabId === 'saves') loadSavesData()
    else if (tabId === 'screenshots') loadScreenshots()
  }

  // ===== 初始加载 =====
  loadMods()
</script>

<!-- ===== 内联 SVG 图标片段 — 补充 Icon 组件未提供的图标 ===== -->
{#snippet RefreshIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" />
    <path d="M21 3v5h-5" />
    <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" />
    <path d="M8 16H3v5" />
  </svg>
{/snippet}

{#snippet FolderIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z" />
  </svg>
{/snippet}

{#snippet TrashIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="M3 6h18" />
    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
    <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    <line x1="10" y1="11" x2="10" y2="17" />
    <line x1="14" y1="11" x2="14" y2="17" />
  </svg>
{/snippet}

{#snippet CheckIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2.5"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="M20 6 9 17l-5-5" />
  </svg>
{/snippet}

{#snippet XIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2.5"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="M18 6 6 18" />
    <path d="m6 6 12 12" />
  </svg>
{/snippet}

{#snippet AlertIcon(size: number)}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
    <line x1="12" y1="9" x2="12" y2="13" />
    <line x1="12" y1="17" x2="12.01" y2="17" />
  </svg>
{/snippet}

<!-- ===== 主页面 ===== -->
<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background)] p-8">
  <div class="flex flex-col gap-8">

    <!-- 1. 页面标题 + 操作按钮 -->
    <section class="flex items-end justify-between">
      <div>
        <h1 class="text-[30px] font-semibold leading-tight tracking-[-0.02em] text-[var(--foreground)]">工具箱</h1>
        <p class="mt-1.5 text-[15px] text-[var(--muted-foreground)]">管理你的游戏实例、存档与模组</p>
      </div>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)]"
          onclick={handleNewInstance}
        >
          <Icon name="plus" size={16} />
          <span>新建实例</span>
        </button>
        <button
          type="button"
          class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)]"
          onclick={handleImportSave}
        >
          <Icon name="upload" size={16} />
          <span>导入存档</span>
        </button>
      </div>
    </section>

    <!-- 2. Tab 切换栏 -->
    <section>
      <div class="inline-flex gap-1 rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-1">
        {#each tabs.filter(t => t.id !== 'chat' || chatEnabled) as tab (tab.id)}
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-[0.75rem] px-4 py-2 text-[13px] font-medium transition-[background-color,color] {activeTab === tab.id ? 'bg-[var(--accent)] text-[var(--foreground)]' : 'text-[var(--muted-foreground)] hover:text-[var(--foreground)]'}"
            onclick={() => switchTab(tab.id)}
          >
            <Icon name={tab.icon} size={16} />
            <span>{tab.name}</span>
          </button>
        {/each}
      </div>
    </section>

    <!-- 3. Tab 内容区 -->
    <section>
      {#if activeTab === 'mods'}
        <!-- ===== Mod 管理 Tab ===== -->
        <div class="flex flex-col gap-4">
          <!-- 操作栏 -->
          <div class="flex items-center justify-between">
            <span class="text-[14px] text-[var(--muted-foreground)]">
              共 {mods.length} 个 Mod
            </span>
            <div class="flex items-center gap-2">
              <button
                type="button"
                class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)]"
                onclick={openModsFolder}
              >
                {@render FolderIcon(16)}
                <span>打开文件夹</span>
              </button>
              <button
                type="button"
                class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
                onclick={loadMods}
                disabled={modsLoading}
              >
                {@render RefreshIcon(16)}
                <span>刷新</span>
              </button>
            </div>
          </div>

          <!-- Mod 列表 -->
          {#if modsLoading}
            <div class="flex items-center justify-center py-16">
              <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
              <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">加载中...</span>
            </div>
          {:else if mods.length === 0}
            <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
              <div class="flex h-14 w-14 items-center justify-center rounded-[12px] bg-[var(--accent)] text-[var(--muted-foreground)]">
                <Icon name="package" size={28} />
              </div>
              <p class="mt-4 text-[15px] font-medium text-[var(--foreground)]">暂无已安装的 Mod</p>
              <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">将 Mod 文件放入 mods 文件夹后刷新</p>
            </div>
          {:else}
            <div class="flex flex-col gap-2">
              {#each mods as mod (mod.fileName)}
                <div
                  class="flex items-center gap-4 rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-4 transition-[box-shadow] hover:shadow-[var(--shadow-sm)]"
                >
                  <!-- Mod 图标 -->
                  <div
                    class="flex h-10 w-10 shrink-0 items-center justify-center rounded-[10px] {mod.enabled ? 'bg-[var(--accent)] text-[var(--primary)]' : 'text-[var(--muted-foreground)]'}"
                  >
                    <Icon name="package" size={20} />
                  </div>

                  <!-- Mod 信息 -->
                  <div class="min-w-0 flex-1">
                    <div class="truncate text-[14px] font-semibold text-[var(--foreground)]">
                      {mod.displayName || mod.fileName}
                    </div>
                    <div class="mt-0.5 flex items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
                      <span style="font-family: var(--font-mono);">{mod.sizeText}</span>
                      <span aria-hidden="true">·</span>
                      <span>{mod.lastModified || '—'}</span>
                    </div>
                  </div>

                  <!-- 状态标签 -->
                  <span
                    class="hidden shrink-0 rounded-full px-2.5 py-0.5 text-[11px] font-medium sm:inline-block {mod.enabled ? 'text-[var(--success)]' : 'text-[var(--muted-foreground)]'}"
                    style={mod.enabled ? 'background-color: color-mix(in srgb, var(--success) 12%, transparent);' : 'background-color: var(--muted);'}
                  >
                    {mod.enabled ? '已启用' : '已禁用'}
                  </span>

                  <!-- 启用/禁用开关 -->
                  <button
                    type="button"
                    role="switch"
                    aria-checked={mod.enabled}
                    aria-label={mod.enabled ? '禁用此 Mod' : '启用此 Mod'}
                    disabled={modActionLoading === mod.fileName}
                    class="relative h-[24px] w-[44px] shrink-0 cursor-pointer rounded-full transition-colors duration-200 disabled:cursor-not-allowed disabled:opacity-50 {mod.enabled ? 'bg-[var(--primary)]' : 'bg-[var(--muted)]'}"
                    onclick={() => toggleMod(mod.fileName)}
                  >
                    <span
                      class="absolute top-[2px] left-[2px] h-[20px] w-[20px] rounded-full bg-white shadow-[var(--shadow-sm)] transition-transform duration-200 {mod.enabled ? 'translate-x-[20px]' : 'translate-x-0'}"
                    ></span>
                  </button>

                  <!-- 删除按钮 -->
                  <button
                    type="button"
                    aria-label="删除此 Mod"
                    disabled={modActionLoading === mod.fileName}
                    class="flex h-8 w-8 shrink-0 cursor-pointer items-center justify-center rounded-[8px] text-[var(--muted-foreground)] transition-[background-color,color] hover:bg-[var(--destructive)] hover:text-[var(--destructive-foreground)] disabled:cursor-not-allowed disabled:opacity-50"
                    onclick={() => deleteMod(mod.fileName)}
                  >
                    {#if modActionLoading === mod.fileName}
                      <span class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-[var(--muted-foreground)] border-t-transparent" aria-hidden="true"></span>
                    {:else}
                      {@render TrashIcon(16)}
                    {/if}
                  </button>
                </div>
              {/each}
            </div>
          {/if}
        </div>

      {:else if activeTab === 'saves'}
        <!-- ===== 存档备份 Tab ===== -->
        <div class="flex flex-col gap-6">
          {#if savesLoading}
            <div class="flex items-center justify-center py-16">
              <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
              <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">加载中...</span>
            </div>
          {:else}
            <!-- 区域 1: 存档列表 -->
            <div class="rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-5">
              <div class="mb-4 flex items-center gap-2">
                <div class="flex h-8 w-8 items-center justify-center rounded-[8px] bg-[var(--accent)] text-[var(--primary)]">
                  <Icon name="box" size={16} />
                </div>
                <h2 class="text-[15px] font-semibold text-[var(--foreground)]">存档列表</h2>
                <span class="text-[13px] text-[var(--muted-foreground)]">({saves.length})</span>
              </div>

              {#if saves.length === 0}
                <div class="py-10 text-center">
                  <p class="text-[14px] text-[var(--muted-foreground)]">暂无游戏存档</p>
                  <p class="mt-1 text-[12px] text-[var(--muted-foreground)]">启动游戏后会自动创建存档</p>
                </div>
              {:else}
                <div class="flex flex-col gap-2">
                  {#each saves as save (save.name)}
                    <div class="flex items-center gap-4 rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] p-3.5">
                      <div class="min-w-0 flex-1">
                        <div class="truncate text-[14px] font-medium text-[var(--foreground)]">{save.name}</div>
                        <div class="mt-0.5 flex items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
                          <span style="font-family: var(--font-mono);">{save.sizeText}</span>
                          <span aria-hidden="true">·</span>
                          <span>{save.lastModified || '—'}</span>
                          {#if !save.hasLevelData}
                            <span aria-hidden="true">·</span>
                            <span class="text-[var(--destructive)]">无关卡数据</span>
                          {/if}
                        </div>
                      </div>
                      <button
                        type="button"
                        disabled={saveActionLoading === save.name}
                        class="inline-flex h-8 shrink-0 cursor-pointer items-center gap-1.5 rounded-[0.5rem] bg-[var(--primary)] px-3 text-[12px] font-medium text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
                        onclick={() => backupSave(save.name)}
                      >
                        {#if saveActionLoading === save.name}
                          <span class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-[var(--primary-foreground)] border-t-transparent" aria-hidden="true"></span>
                        {:else}
                          <Icon name="download" size={14} />
                        {/if}
                        <span>备份</span>
                      </button>
                    </div>
                  {/each}
                </div>
              {/if}
            </div>

            <!-- 区域 2: 已有备份列表 -->
            <div class="rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-5">
              <div class="mb-4 flex items-center gap-2">
                <div class="flex h-8 w-8 items-center justify-center rounded-[8px] bg-[var(--accent)] text-[var(--primary)]">
                  <Icon name="upload" size={16} />
                </div>
                <h2 class="text-[15px] font-semibold text-[var(--foreground)]">已有备份</h2>
                <span class="text-[13px] text-[var(--muted-foreground)]">({backups.length})</span>
              </div>

              {#if backups.length === 0}
                <div class="py-10 text-center">
                  <p class="text-[14px] text-[var(--muted-foreground)]">暂无备份文件</p>
                  <p class="mt-1 text-[12px] text-[var(--muted-foreground)]">在上方存档列表中点击"备份"按钮创建</p>
                </div>
              {:else}
                <div class="flex flex-col gap-2">
                  {#each backups as backup (backup.fileName)}
                    <div class="flex items-center gap-4 rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] p-3.5">
                      <div class="min-w-0 flex-1">
                        <div class="truncate text-[14px] font-medium text-[var(--foreground)]" title={backup.fileName}>
                          {backup.fileName}
                        </div>
                        <div class="mt-0.5 flex items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
                          <span style="font-family: var(--font-mono);">{backup.sizeText}</span>
                          <span aria-hidden="true">·</span>
                          <span>{backup.createdTime || '—'}</span>
                        </div>
                      </div>
                      <button
                        type="button"
                        disabled={saveActionLoading === backup.fileName}
                        class="inline-flex h-8 shrink-0 cursor-pointer items-center gap-1.5 rounded-[0.5rem] border border-[var(--border)] bg-[var(--card)] px-3 text-[12px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
                        onclick={() => restoreBackup(backup.fileName)}
                      >
                        {#if saveActionLoading === backup.fileName}
                          <span class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-[var(--muted-foreground)] border-t-transparent" aria-hidden="true"></span>
                        {:else}
                          <Icon name="upload" size={14} />
                        {/if}
                        <span>恢复</span>
                      </button>
                      <button
                        type="button"
                        aria-label="删除此备份"
                        disabled={saveActionLoading === backup.fileName}
                        class="flex h-8 w-8 shrink-0 cursor-pointer items-center justify-center rounded-[0.5rem] text-[var(--muted-foreground)] transition-[background-color,color] hover:bg-[var(--destructive)] hover:text-[var(--destructive-foreground)] disabled:cursor-not-allowed disabled:opacity-50"
                        onclick={() => deleteBackup(backup.fileName)}
                      >
                        {@render TrashIcon(14)}
                      </button>
                    </div>
                  {/each}
                </div>
              {/if}
            </div>
          {/if}
        </div>

      {:else if activeTab === 'screenshots'}
        <!-- ===== 截图管理 Tab ===== -->
        <div class="flex flex-col gap-4">
          <!-- 操作栏 -->
          <div class="flex items-center justify-between">
            <span class="text-[14px] text-[var(--muted-foreground)]">
              共 {screenshots.length} 张截图
            </span>
            <div class="flex items-center gap-2">
              <button
                type="button"
                class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)]"
                onclick={openScreenshotsFolder}
              >
                {@render FolderIcon(16)}
                <span>打开文件夹</span>
              </button>
              <button
                type="button"
                class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
                onclick={loadScreenshots}
                disabled={screenshotsLoading}
              >
                {@render RefreshIcon(16)}
                <span>刷新</span>
              </button>
            </div>
          </div>

          <!-- 截图网格 -->
          {#if screenshotsLoading}
            <div class="flex items-center justify-center py-16">
              <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
              <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">加载中...</span>
            </div>
          {:else if screenshots.length === 0}
            <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
              <div class="flex h-14 w-14 items-center justify-center rounded-[12px] bg-[var(--accent)] text-[var(--muted-foreground)]">
                <Icon name="grip" size={28} />
              </div>
              <p class="mt-4 text-[15px] font-medium text-[var(--foreground)]">暂无截图</p>
              <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">在游戏中按 F2 截图后会显示在这里</p>
            </div>
          {:else}
            <div class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
              {#each screenshots as shot (shot.fileName)}
                <div class="overflow-hidden rounded-[1rem] border border-[var(--border)] bg-[var(--card)] transition-[box-shadow,transform] hover:-translate-y-0.5 hover:shadow-[var(--shadow-md)]">
                  <!-- 缩略图 -->
                  <div class="flex aspect-video items-center justify-center overflow-hidden bg-[var(--accent)]">
                    {#if failedImages.has(shot.fileName)}
                      <div class="flex h-full w-full items-center justify-center text-[var(--muted-foreground)]">
                        <Icon name="grip" size={32} />
                      </div>
                    {:else}
                      <img
                        src={toFileUrl(shot.filePath)}
                        alt={shot.fileName}
                        class="h-full w-full object-cover"
                        onerror={() => handleImageError(shot.fileName)}
                      />
                    {/if}
                  </div>
                  <!-- 信息 -->
                  <div class="p-3">
                    <div class="truncate text-[13px] font-medium text-[var(--foreground)]" title={shot.fileName}>
                      {shot.fileName}
                    </div>
                    <div class="mt-0.5 flex items-center gap-2 text-[11px] text-[var(--muted-foreground)]">
                      <span style="font-family: var(--font-mono);">{shot.sizeText}</span>
                      <span aria-hidden="true">·</span>
                      <span>{shot.createdTime || '—'}</span>
                    </div>
                  </div>
                </div>
              {/each}
            </div>
          {/if}
        </div>

      {:else if activeTab === 'repair'}
        <!-- ===== 游戏修复 Tab ===== -->
        <div class="flex flex-col gap-4">
          <!-- 检查按钮 -->
          <div class="flex items-center justify-between">
            <div>
              <h2 class="text-[16px] font-semibold text-[var(--foreground)]">游戏文件完整性检查</h2>
              <p class="mt-0.5 text-[13px] text-[var(--muted-foreground)]">扫描游戏核心文件，检测缺失或损坏的内容</p>
            </div>
            <button
              type="button"
              class="inline-flex h-10 items-center gap-2 rounded-full bg-[var(--primary)] px-6 text-[14px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
              onclick={runRepair}
              disabled={repairLoading}
            >
              {#if repairLoading}
                <span class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-[var(--primary-foreground)] border-t-transparent" aria-hidden="true"></span>
                <span>检查中...</span>
              {:else}
                <Icon name="info" size={18} />
                <span>开始检查</span>
              {/if}
            </button>
          </div>

          <!-- 检查结果 -->
          {#if repairLoading}
            <div class="flex items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
              <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
              <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">正在检查游戏文件...</span>
            </div>
          {:else if repairResult}
            <!-- 总览消息 -->
            <div
              class="flex items-center gap-3 rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-4"
            >
              <div
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-[10px] text-white"
                style={repairResult.hasErrors
                  ? 'background-color: var(--destructive);'
                  : 'background-color: var(--success);'}
              >
                {#if repairResult.hasErrors}
                  {@render AlertIcon(20)}
                {:else}
                  {@render CheckIcon(20)}
                {/if}
              </div>
              <div class="min-w-0 flex-1">
                <div class="text-[14px] font-semibold text-[var(--foreground)]">
                  {repairResult.hasErrors ? '发现部分问题' : '一切正常'}
                </div>
                <div class="mt-0.5 text-[13px] text-[var(--muted-foreground)]">
                  {repairResult.message}
                </div>
              </div>
            </div>

            <!-- 详细检查项列表 -->
            {#if repairResult.details && repairResult.details.length > 0}
              <div class="rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-2">
                {#each repairResult.details as detail, i (i)}
                  <div
                    class="flex items-start gap-3 rounded-[0.75rem] p-3 {i < repairResult.details.length - 1 ? 'border-b border-[var(--border)]' : ''}"
                  >
                    <!-- 状态图标 -->
                    <div
                      class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-white"
                      style={detail.type === 'success'
                        ? 'background-color: var(--success);'
                        : detail.type === 'error'
                          ? 'background-color: var(--destructive);'
                          : 'background-color: var(--primary);'}
                    >
                      {#if detail.type === 'success'}
                        {@render CheckIcon(14)}
                      {:else if detail.type === 'error'}
                        {@render XIcon(14)}
                      {:else}
                        <Icon name="info" size={14} />
                      {/if}
                    </div>
                    <!-- 文件名 + 消息 -->
                    <div class="min-w-0 flex-1">
                      {#if detail.file}
                        <div class="truncate text-[13px] font-medium text-[var(--foreground)]" title={detail.file}>
                          {detail.file}
                        </div>
                      {/if}
                      <div class="text-[12px] {detail.file ? 'mt-0.5' : ''} text-[var(--muted-foreground)]">
                        {detail.message}
                      </div>
                    </div>
                  </div>
                {/each}
              </div>
            {/if}
          {:else}
            <!-- 初始空状态 -->
            <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
              <div class="flex h-14 w-14 items-center justify-center rounded-[12px] bg-[var(--accent)] text-[var(--muted-foreground)]">
                <Icon name="info" size={28} />
              </div>
              <p class="mt-4 text-[15px] font-medium text-[var(--foreground)]">尚未执行检查</p>
              <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">点击上方"开始检查"按钮扫描游戏文件</p>
            </div>
          {/if}
        </div>

      {:else if activeTab === 'chat'}
        <!-- ===== 聊天 Tab ===== -->
        <div class="flex flex-col items-center justify-center gap-4 py-20">
          <div class="text-center">
            <p class="text-sm text-[var(--muted-foreground)]">点击下方按钮打开聊天页面</p>
            <p class="mt-1 text-xs text-[var(--muted-foreground)]">目前还不完善，属于测试功能</p>
          </div>
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-[0.5rem] bg-[var(--primary)] px-6 py-2.5 text-sm font-semibold text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96]"
            onclick={() => {
              try {
                ipc('nav.external', 'https://chat.hhj520.top')
              } catch {
                toast.error('无法打开聊天页面')
              }
            }}
          >
            <Icon name="message-circle" size={16} />
            打开聊天
          </button>
        </div>
      {/if}
    </section>
  </div>
</div>
