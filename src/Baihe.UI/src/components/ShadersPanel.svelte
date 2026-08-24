<!--
  功能描述: 光影管理面板 — 列出/启用/删除光影包，悬浮显示说明与预览
  技术实现: 通过 shaders.* IPC 与后端交互；预装光影的说明来自 lib/shaders.ts
-->
<script lang="ts">
  import { ipc } from '../lib/ipc'
  import { toast } from '../lib/toast.svelte'
  import { findShaderMeta } from '../lib/shaders'

  interface ShaderItem {
    fileName: string
    displayName: string
    size: number
    sizeText: string
    enabled: boolean
  }

  let shaders = $state<ShaderItem[]>([])
  let loading = $state(false)
  let actionLoading = $state<string | null>(null)
  /** 待二次确认删除的文件名 */
  let confirmDelete = $state<string | null>(null)
  let confirmTimer: ReturnType<typeof setTimeout> | null = null
  /** 当前悬浮的光影文件名 — 控制介绍浮层显隐（JS 状态，比 CSS group-hover 可靠） */
  let hoveredShader = $state<string | null>(null)

  /** 加载光影列表 */
  async function loadShaders(): Promise<void> {
    loading = true
    try {
      shaders = await ipc<ShaderItem[]>('shaders.list')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '加载光影列表失败')
      shaders = []
    } finally {
      loading = false
    }
  }

  /** 启用光影 */
  async function enableShader(fileName: string): Promise<void> {
    if (actionLoading) return
    actionLoading = fileName
    try {
      const result = await ipc<{ success: boolean; error?: string }>('shaders.enable', fileName)
      if (result.success) {
        toast.success('光影已启用（重启游戏后生效）')
        await loadShaders()
      } else {
        toast.error(result.error || '启用失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '启用失败')
    } finally {
      actionLoading = null
    }
  }

  /** 关闭光影 */
  async function disableShaders(): Promise<void> {
    if (actionLoading) return
    actionLoading = '__disable__'
    try {
      const result = await ipc<{ success: boolean; error?: string }>('shaders.disable')
      if (result.success) {
        toast.success('光影已关闭（重启游戏后生效）')
        await loadShaders()
      } else {
        toast.error(result.error || '关闭失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '关闭失败')
    } finally {
      actionLoading = null
    }
  }

  /** 删除光影 — 内联二次确认（先点「删除」变「确认？」，再点执行，3s 未点自动恢复） */
  function requestDelete(fileName: string): void {
    if (confirmDelete === fileName) {
      // 第二次点击 → 真正执行删除
      if (confirmTimer) clearTimeout(confirmTimer)
      confirmDelete = null
      doDelete(fileName)
    } else {
      confirmDelete = fileName
      if (confirmTimer) clearTimeout(confirmTimer)
      confirmTimer = setTimeout(() => {
        confirmDelete = null
        confirmTimer = null
      }, 3000)
    }
  }

  async function doDelete(fileName: string): Promise<void> {
    if (actionLoading) return
    actionLoading = fileName
    try {
      const result = await ipc<{ success: boolean; error?: string }>('shaders.delete', fileName)
      if (result.success) {
        shaders = shaders.filter((s) => s.fileName !== fileName)
        toast.success('光影已删除')
      } else {
        toast.error(result.error || '删除失败')
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '删除失败')
    } finally {
      actionLoading = null
    }
  }

  /** 打开光影文件夹 */
  async function openFolder(): Promise<void> {
    try {
      await ipc('shaders.openFolder')
    } catch (e) {
      toast.error(e instanceof Error ? e.message : '打开文件夹失败')
    }
  }

  loadShaders()
</script>

<div class="flex flex-col gap-4">
  <!-- 操作栏 -->
  <div class="flex items-center justify-between">
    <div class="flex items-center gap-3">
      <span class="text-[14px] text-[var(--muted-foreground)]">共 {shaders.length} 个光影包</span>
      {#if shaders.some((s) => s.enabled)}
        <span class="rounded-full px-2.5 py-0.5 text-[11px] font-medium text-[var(--success)]" style="background-color: color-mix(in srgb, var(--success) 12%, transparent);">
          光影已开启
        </span>
      {:else}
        <span class="rounded-full px-2.5 py-0.5 text-[11px] font-medium text-[var(--muted-foreground)]" style="background-color: var(--muted);">
          光影未开启
        </span>
      {/if}
    </div>
    <div class="flex items-center gap-2">
      <button
        type="button"
        class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
        onclick={disableShaders}
        disabled={actionLoading !== null || !shaders.some((s) => s.enabled)}
      >
        <span>关闭光影</span>
      </button>
      <button
        type="button"
        class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)]"
        onclick={openFolder}
      >
        <span>打开文件夹</span>
      </button>
      <button
        type="button"
        class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-[background-color] hover:bg-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
        onclick={loadShaders}
        disabled={loading}
      >
        <span>刷新</span>
      </button>
    </div>
  </div>

  <!-- 使用说明 -->
  <div class="rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 py-3 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
    光影基于 Iris 模组运行（需先启用「Iris」Mod）。选择光影包后<span class="text-[var(--foreground)]">重启游戏</span>生效；将鼠标悬停在光影卡片上可查看效果说明与适用机型。可自行下载光影 zip 放入光影文件夹。
  </div>

  <!-- 光影列表 -->
  {#if loading}
    <div class="flex items-center justify-center py-16">
      <span class="inline-block h-5 w-5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
      <span class="ml-3 text-[14px] text-[var(--muted-foreground)]">加载中...</span>
    </div>
  {:else if shaders.length === 0}
    <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
      <p class="mt-4 text-[15px] font-medium text-[var(--foreground)]">暂无光影包</p>
      <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">将光影 zip 文件放入光影文件夹后刷新</p>
    </div>
  {:else}
    <div class="grid grid-cols-1 gap-3 md:grid-cols-2">
      {#each shaders as shader (shader.fileName)}
        {@const meta = findShaderMeta(shader.fileName)}
        <div
          class="group relative overflow-hidden rounded-[1rem] border bg-[var(--card)] transition-[box-shadow,border-color] hover:shadow-[var(--shadow-sm)] {shader.enabled ? 'border-[var(--primary)]' : 'border-[var(--border)]'}"
          onmouseenter={() => (hoveredShader = shader.fileName)}
          onmouseleave={() => (hoveredShader = null)}
        >
          <!-- 预览图 -->
          <div class="relative h-36 w-full overflow-hidden">
            {#if meta}
              <img src={meta.preview} alt={meta.displayName} class="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105" loading="lazy" />
            {:else}
              <div class="flex h-full w-full items-center justify-center bg-[var(--accent)] text-[var(--muted-foreground)]">
                <span class="text-[13px]">无预览图</span>
              </div>
            {/if}
            {#if shader.enabled}
              <span class="absolute right-2 top-2 rounded-full bg-[var(--primary)] px-2.5 py-0.5 text-[11px] font-semibold text-[var(--primary-foreground)]">
                使用中
              </span>
            {/if}
          </div>

          <!-- 信息区 -->
          <div class="p-4">
            <div class="flex items-start justify-between gap-2">
              <div class="min-w-0">
                <div class="truncate text-[14px] font-semibold text-[var(--foreground)]" title={shader.displayName}>
                  {meta?.displayName ?? shader.displayName}
                </div>
                <div class="mt-0.5 flex items-center gap-2 text-[12px] text-[var(--muted-foreground)]">
                  <span style="font-family: var(--font-mono);">{shader.sizeText}</span>
                  {#if meta}
                    <span aria-hidden="true">·</span>
                    <span class="text-[var(--primary)]">{meta.tier}配</span>
                  {/if}
                </div>
              </div>
              <button
                type="button"
                aria-label={confirmDelete === shader.fileName ? '确认删除此光影' : '删除此光影'}
                disabled={actionLoading !== null}
                class="flex h-8 shrink-0 items-center justify-center rounded-[8px] px-2 text-[12px] font-medium transition-[background-color,color] disabled:cursor-not-allowed disabled:opacity-50 {confirmDelete === shader.fileName ? 'bg-[var(--destructive)] text-[var(--destructive-foreground)]' : 'text-[var(--muted-foreground)] hover:bg-[var(--destructive)] hover:text-[var(--destructive-foreground)]'}"
                onclick={() => requestDelete(shader.fileName)}
              >
                {#if actionLoading === shader.fileName}
                  <span class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-[var(--muted-foreground)] border-t-transparent" aria-hidden="true"></span>
                {:else if confirmDelete === shader.fileName}
                  <span>确认？</span>
                {:else}
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <path d="M3 6h18" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" /><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                  </svg>
                {/if}
              </button>
            </div>

            <!-- 操作按钮 -->
            <div class="mt-3 flex items-center gap-2">
              {#if shader.enabled}
                <button
                  type="button"
                  disabled={actionLoading !== null}
                  class="inline-flex h-8 flex-1 items-center justify-center gap-1.5 rounded-[0.5rem] bg-[var(--primary)] px-3 text-[12px] font-medium text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
                  onclick={disableShaders}
                >
                  关闭
                </button>
              {:else}
                <button
                  type="button"
                  disabled={actionLoading !== null}
                  class="inline-flex h-8 flex-1 items-center justify-center gap-1.5 rounded-[0.5rem] bg-[var(--primary)] px-3 text-[12px] font-medium text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96] disabled:cursor-not-allowed disabled:opacity-50"
                  onclick={() => enableShader(shader.fileName)}
                >
                  {#if actionLoading === shader.fileName}
                    <span class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-[var(--primary-foreground)] border-t-transparent" aria-hidden="true"></span>
                  {/if}
                  启用
                </button>
              {/if}
            </div>
          </div>

          <!-- 悬浮说明浮层 — 悬停时覆盖预览图区域（顶部 144px），不遮挡下方操作按钮 -->
          {#if meta && hoveredShader === shader.fileName}
            <div class="pointer-events-none absolute inset-x-0 top-0 z-10 h-36 overflow-y-auto bg-[rgba(20,20,22,0.94)] p-3 text-[12px] leading-relaxed text-[var(--foreground)] shadow-xl backdrop-blur-md">
              <div class="font-semibold">{meta.displayName}</div>
              <div class="mt-1 text-[var(--muted-foreground)]">{meta.description}</div>
              <div class="mt-2 flex items-center gap-1.5 text-[11px]">
                <span class="rounded-full bg-[var(--primary)]/15 px-2 py-0.5 font-medium text-[var(--primary)]">适合 {meta.suitable}</span>
              </div>
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
</div>
