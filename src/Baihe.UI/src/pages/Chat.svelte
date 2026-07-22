<!--
  功能描述: 聊天页面 — 全屏嵌入 chat.hhj520.top
  技术实现: iframe 全屏嵌入，无 padding，最大化聊天区域
  注意事项: 页面根元素不加 p-8，与其它页面不同；iframe 加载失败时显示错误提示
-->
<script lang="ts">
  let loaded = $state(false)
  let errored = $state(false)

  function handleLoad(): void {
    loaded = true
  }

  function handleError(): void {
    errored = true
    loaded = true
  }
</script>

<div class="min-h-0 flex-1 overflow-hidden">
  {#if !loaded && !errored}
    <div class="flex h-full items-center justify-center">
      <div class="flex flex-col items-center gap-3">
        <div
          class="h-8 w-8 animate-spin rounded-full border-2 border-[var(--border)] border-t-[var(--primary)]"
        ></div>
        <span class="text-sm text-[var(--muted-foreground)]">正在连接聊天服务器...</span>
      </div>
    </div>
  {/if}

  {#if errored}
    <div class="flex h-full items-center justify-center p-8">
      <div class="flex flex-col items-center gap-4 text-center">
        <div class="flex h-16 w-16 items-center justify-center rounded-full bg-[var(--secondary)]">
          <svg
            xmlns="http://www.w3.org/2000/svg"
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            class="text-[var(--muted-foreground)]"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
        </div>
        <div>
          <p class="text-base font-medium text-[var(--foreground)]">无法加载聊天页面</p>
          <p class="mt-1 text-sm text-[var(--muted-foreground)]">
            聊天服务器可能暂时不可用，或者该网站不允许被嵌入。
          </p>
        </div>
        <a
          href="https://chat.hhj520.top"
          target="_blank"
          rel="noopener noreferrer"
          class="inline-flex h-9 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--foreground)] transition-colors hover:bg-[var(--accent)]"
        >
          在浏览器中打开
        </a>
      </div>
    </div>
  {/if}

  <iframe
    src="https://chat.hhj520.top"
    title="白鹤聊天"
    class="h-full w-full border-0"
    style={loaded && !errored ? '' : 'display: none;'}
    sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox allow-downloads"
    allow="clipboard-read; clipboard-write; microphone; camera"
    onload={handleLoad}
    onerror={handleError}
  ></iframe>
</div>
