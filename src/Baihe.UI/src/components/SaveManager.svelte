<!--
  功能描述: 存档备份管理面板 — 存档列表 + 备份/恢复/删除（供设置页开发者选项使用）
  技术实现: 通过 saves.* IPC 与后端交互
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'
  import { toast } from '../lib/toast.svelte'

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

  let saves = $state<SaveItem[]>([])
  let backups = $state<BackupItem[]>([])
  let loading = $state(false)
  let actionLoading = $state<string | null>(null)

  /** 从备份文件名中提取存档名 */
  function extractSaveName(backupFileName: string): string {
    let name = backupFileName.replace(/\.zip$/i, '')
    name = name.replace(/[_-]backup[_-]?.*$/i, '')
    name = name.replace(/[_-]\d{4}[-_]?\d{2}[-_]?\d{2}.*$/i, '')
    return name || backupFileName.replace(/\.zip$/i, '')
  }

  /** 加载存档与备份列表 */
  async function loadData(): Promise<void> {
    loading = true
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
      loading = false
    }
  }

  /** 备份指定存档 */
  async function backupSave(saveName: string): Promise<void> {
    if (actionLoading) return
    actionLoading = saveName
    try {
      const result = await ipc<{ success: boolean; backupName: string; sizeText: string }>('saves.backup', saveName)
      if (result.success) {
        toast.success(`备份成功: ${result.backupName}`)
        await loadData()
      } else {
        toast.error('备份失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '备份失败')
    } finally {
      actionLoading = null
    }
  }

  /** 从备份恢复存档 */
  async function restoreBackup(backupFileName: string): Promise<void> {
    if (actionLoading) return
    if (!confirm(`确定从「${backupFileName}」恢复存档吗？当前同名存档会被移走。`)) return
    actionLoading = backupFileName
    try {
      const saveName = extractSaveName(backupFileName)
      const result = await ipc<{ success: boolean; saveName: string }>('saves.restore', {
        backupFileName,
        saveName,
      })
      if (result.success) {
        toast.success(`存档 "${result.saveName}" 已恢复`)
        await loadData()
      } else {
        toast.error('恢复失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '恢复失败')
    } finally {
      actionLoading = null
    }
  }

  /** 删除备份文件 */
  async function deleteBackup(fileName: string): Promise<void> {
    if (actionLoading) return
    actionLoading = fileName
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
      actionLoading = null
    }
  }

  loadData()
</script>

<div class="flex flex-col gap-6">
  {#if loading}
    <div class="flex items-center justify-center py-10">
      <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
      <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">加载中...</span>
    </div>
  {:else}
    <!-- 区域 1: 存档列表 -->
    <div>
      <div class="mb-3 flex items-center gap-2">
        <div class="flex h-7 w-7 items-center justify-center rounded-[8px] bg-[var(--accent)] text-[var(--primary)]">
          <Icon name="box" size={14} />
        </div>
        <h3 class="text-[14px] font-semibold text-[var(--foreground)]">存档列表</h3>
        <span class="text-[12px] text-[var(--muted-foreground)]">({saves.length})</span>
      </div>

      {#if saves.length === 0}
        <div class="rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] py-8 text-center">
          <p class="text-[13px] text-[var(--muted-foreground)]">暂无游戏存档</p>
          <p class="mt-1 text-[12px] text-[var(--muted-foreground)]">启动游戏后会自动创建存档</p>
        </div>
      {:else}
        <div class="flex flex-col gap-2">
          {#each saves as save (save.name)}
            <div class="flex items-center gap-3 rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] p-3">
              <div class="min-w-0 flex-1">
                <div class="truncate text-[13px] font-medium text-[var(--foreground)]">{save.name}</div>
                <div class="mt-0.5 flex items-center gap-2 text-[11px] text-[var(--muted-foreground)]">
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
                disabled={actionLoading === save.name}
                class="inline-flex h-7 shrink-0 cursor-pointer items-center gap-1.5 rounded-[0.5rem] bg-[var(--primary)] px-3 text-[11px] font-medium text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
                onclick={() => backupSave(save.name)}
              >
                {#if actionLoading === save.name}
                  <span class="inline-block h-3 w-3 animate-spin rounded-full border-2 border-[var(--primary-foreground)] border-t-transparent" aria-hidden="true"></span>
                {:else}
                  <Icon name="download" size={12} />
                {/if}
                <span>备份</span>
              </button>
            </div>
          {/each}
        </div>
      {/if}
    </div>

    <!-- 区域 2: 已有备份列表 -->
    <div>
      <div class="mb-3 flex items-center gap-2">
        <div class="flex h-7 w-7 items-center justify-center rounded-[8px] bg-[var(--accent)] text-[var(--primary)]">
          <Icon name="upload" size={14} />
        </div>
        <h3 class="text-[14px] font-semibold text-[var(--foreground)]">已有备份</h3>
        <span class="text-[12px] text-[var(--muted-foreground)]">({backups.length})</span>
      </div>

      {#if backups.length === 0}
        <div class="rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] py-8 text-center">
          <p class="text-[13px] text-[var(--muted-foreground)]">暂无备份文件</p>
          <p class="mt-1 text-[12px] text-[var(--muted-foreground)]">在上方存档列表中点击"备份"按钮创建</p>
        </div>
      {:else}
        <div class="flex flex-col gap-2">
          {#each backups as backup (backup.fileName)}
            <div class="flex items-center gap-3 rounded-[0.75rem] border border-[var(--border)] bg-[var(--background)] p-3">
              <div class="min-w-0 flex-1">
                <div class="truncate text-[13px] font-medium text-[var(--foreground)]" title={backup.fileName}>{backup.fileName}</div>
                <div class="mt-0.5 flex items-center gap-2 text-[11px] text-[var(--muted-foreground)]">
                  <span style="font-family: var(--font-mono);">{backup.sizeText}</span>
                  <span aria-hidden="true">·</span>
                  <span>{backup.createdTime || '—'}</span>
                </div>
              </div>
              <button
                type="button"
                disabled={actionLoading === backup.fileName}
                class="inline-flex h-7 shrink-0 cursor-pointer items-center gap-1.5 rounded-[0.5rem] border border-[var(--border)] bg-[var(--card)] px-3 text-[11px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
                onclick={() => restoreBackup(backup.fileName)}
              >
                {#if actionLoading === backup.fileName}
                  <span class="inline-block h-3 w-3 animate-spin rounded-full border-2 border-[var(--muted-foreground)] border-t-transparent" aria-hidden="true"></span>
                {:else}
                  <Icon name="upload" size={12} />
                {/if}
                <span>恢复</span>
              </button>
              <button
                type="button"
                aria-label="删除此备份"
                disabled={actionLoading === backup.fileName}
                class="flex h-7 w-7 shrink-0 cursor-pointer items-center justify-center rounded-[0.5rem] text-[var(--muted-foreground)] transition-[background-color,color] hover:bg-[var(--destructive)] hover:text-[var(--destructive-foreground)] disabled:cursor-not-allowed disabled:opacity-50"
                onclick={() => deleteBackup(backup.fileName)}
              >
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M3 6h18" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" /><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                </svg>
              </button>
            </div>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
</div>
