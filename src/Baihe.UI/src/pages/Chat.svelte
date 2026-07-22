<!--
  功能描述: 聊天页面 — 全屏嵌入 chat.hhj520.top (Element/Matrix 客户端)
  技术实现: iframe 始终加载（不用 display:none），加载指示器作为叠加层
  注意事项: Element 是 JS SPA，需要完整渲染时间；iframe 不能用 display:none 否则不加载
-->
<script lang="ts">
  let loaded = $state(false)
  let showFallback = $state(false)

  /** iframe 加载完成 */
  function handleLoad(): void {
    // 延迟 500ms 隐藏加载层，确保 Element SPA 有时间开始渲染
    setTimeout(() => {
      loaded = true
    }, 500)
  }

  /** 超时检测 — 10 秒后如果仍未加载完成，显示回退选项 */
  $effect(() => {
    const timer = setTimeout(() => {
      if (!loaded) {
        showFallback = true
      }
    }, 10000)
    return () => clearTimeout(timer)
  })
</script>

<div class="relative min-h-0 flex-1 overflow-hidden">
  <!-- iframe 始终加载 — 不能用 display:none，否则 WebView2 不会加载内容 -->
  <iframe
    src="https://chat.hhj520.top"
    title="白鹤聊天"
    class="h-full w-full border-0"
    allow="clipboard-read; clipboard-write; microphone; camera; fullscreen; autoplay"
    onload={handleLoad}
  ></iframe>

  <!-- 加载叠加层 — iframe 加载完成后淡出消失 -->
  {#if !loaded}
    <div
      class="absolute inset-0 flex items-center justify-center bg-[var(--background)] transition-opacity duration-300"
      class:opacity-0={loaded}
      class:pointer-events-none={loaded}
    >
      <div class="flex flex-col items-center gap-3">
        <div
          class="h-8 w-8 animate-spin rounded-full border-2 border-[var(--border)] border-t-[var(--primary)]"
        ></div>
        <span class="text-sm text-[var(--muted-foreground)]">正在连接聊天服务器...</span>
        {#if showFallback}
          <a
            href="https://chat.hhj520.top"
            target="_blank"
            rel="noopener noreferrer"
            class="mt-2 inline-flex h-8 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-colors hover:bg-[var(--accent)]"
          >
            加载时间较长，点此在浏览器中打开
          </a>
        {/if}
      </div>
    </div>
  {/if}
</div>
