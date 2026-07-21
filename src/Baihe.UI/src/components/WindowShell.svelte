<!--
  功能描述: macOS 风格窗口外壳 — 标题栏 + 交通灯按钮
  技术实现: 固定 1180×760 布局，交通灯通过 IPC 调用后端窗口控制
  注意事项: 标题栏区域可拖拽（WPF 层处理），标题居中显示"白鹤服务器"
-->
<script lang="ts">
  import { ipc } from '../lib/ipc'
  import type { Snippet } from 'svelte'

  interface Props {
    children: Snippet
  }

  let { children }: Props = $props()

  /** 关闭窗口 */
  async function handleClose(): Promise<void> {
    try {
      await ipc('window.close')
    } catch {
      // IPC 不可用时静默失败
    }
  }

  /** 最小化窗口 */
  async function handleMinimize(): Promise<void> {
    try {
      await ipc('window.minimize')
    } catch {
      // 静默失败
    }
  }

  /** 最大化/还原窗口 */
  async function handleMaximize(): Promise<void> {
    try {
      await ipc('window.maximize')
    } catch {
      // 静默失败
    }
  }
</script>

<div class="flex h-screen w-screen items-center justify-center bg-[var(--background-200)]">
  <div
    class="flex h-[760px] w-[1180px] flex-col overflow-hidden rounded-[12px] border border-[var(--border)] bg-[var(--background)] shadow-[var(--shadow-2xl)]"
  >
    <!-- 标题栏: 44px, 交通灯 + 居中标题 -->
    <div
      class="relative flex h-11 shrink-0 items-center justify-between border-b border-[var(--border)] bg-[var(--background-100)] px-4"
    >
      <!-- 交通灯按钮 -->
      <div class="flex items-center gap-2">
        <!-- 关闭 -->
        <button
          type="button"
          class="group/close flex h-3 w-3 items-center justify-center rounded-full text-[var(--foreground)] transition-opacity hover:opacity-80"
          style="background-color: var(--traffic-close);"
          aria-label="关闭"
          onclick={handleClose}
        >
          <svg class="h-[8px] w-[8px] opacity-0 transition-opacity duration-100 group-hover/close:opacity-60" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" aria-hidden="true">
            <path d="M2 2l4 4M6 2l-4 4" />
          </svg>
        </button>
        <!-- 最小化 -->
        <button
          type="button"
          class="group/min flex h-3 w-3 items-center justify-center rounded-full text-[var(--foreground)] transition-opacity hover:opacity-80"
          style="background-color: var(--traffic-minimize);"
          aria-label="最小化"
          onclick={handleMinimize}
        >
          <svg class="h-[8px] w-[8px] opacity-0 transition-opacity duration-100 group-hover/min:opacity-60" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" aria-hidden="true">
            <path d="M2 4h4" />
          </svg>
        </button>
        <!-- 最大化 -->
        <button
          type="button"
          class="group/max flex h-3 w-3 items-center justify-center rounded-full text-[var(--foreground)] transition-opacity hover:opacity-80"
          style="background-color: var(--traffic-maximize);"
          aria-label="最大化"
          onclick={handleMaximize}
        >
          <svg class="h-[8px] w-[8px] opacity-0 transition-opacity duration-100 group-hover/max:opacity-60" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" aria-hidden="true">
            <path d="M4 2v4M2 4h4" />
          </svg>
        </button>
      </div>

      <!-- 居中标题 -->
      <span class="absolute left-1/2 -translate-x-1/2 whitespace-nowrap text-[13px] font-semibold tracking-[-0.01em] text-[var(--foreground)]">
        白鹤服务器
      </span>

      <!-- 右侧占位 -->
      <div class="w-12"></div>
    </div>

    <!-- 窗口主体 -->
    <div class="flex min-h-0 flex-1">
      {@render children()}
    </div>
  </div>
</div>
