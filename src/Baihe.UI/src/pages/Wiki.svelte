<!--
  功能描述: 维基页面 — 服务器玩家指南（分级分类 + 全文搜索）
  技术实现: 内容来自 lib/wiki/*.ts（按章节拆分）；左侧分级导航（分类→页面），右侧内容区；搜索时切换为命中结果视图并高亮
  注意事项: 内容为启动器内置数据，无 IPC 依赖
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { wikiCategories, searchWiki, type WikiSearchHit } from '../lib/wiki'
  import type { WikiCategory, WikiPage, WikiBlock } from '../lib/wiki/types'

  /** 当前选中分类 */
  let activeCategoryId = $state(wikiCategories[0]?.id ?? '')
  /** 当前选中页面 */
  let activePageId = $state('')
  /** 搜索关键词 */
  let query = $state('')

  /** 当前分类（找不到回退第一个） */
  const activeCategory = $derived(
    wikiCategories.find((c) => c.id === activeCategoryId) ?? wikiCategories[0],
  )

  /** 当前页面（找不到回退第一个） */
  const activePage = $derived(
    activeCategory?.pages.find((p) => p.id === activePageId) ?? activeCategory?.pages[0],
  )

  /** 是否处于搜索模式 */
  const searching = $derived(query.trim().length > 0)

  /** 搜索结果 — 按分类分组 */
  const groupedHits = $derived.by((): { category: WikiCategory; hits: WikiSearchHit[] }[] => {
    const map = new Map<string, { category: WikiCategory; hits: WikiSearchHit[] }>()
    for (const hit of searchWiki(query)) {
      if (!map.has(hit.category.id)) {
        map.set(hit.category.id, { category: hit.category, hits: [] })
      }
      map.get(hit.category.id)!.hits.push(hit)
    }
    return [...map.values()]
  })

  /** 搜索命中总数 */
  const hitCount = $derived(searchWiki(query).length)

  /** 切换分类 — 自动选中该分类第一个页面 */
  function selectCategory(id: string): void {
    activeCategoryId = id
    const cat = wikiCategories.find((c) => c.id === id)
    activePageId = cat?.pages[0]?.id ?? ''
  }

  /** 搜索模式下表格行过滤 — 只保留含关键词的行 */
  function rowMatches(row: string[]): boolean {
    const kw = query.trim().toLowerCase()
    if (!kw) return true
    return row.some((cell) => cell.toLowerCase().includes(kw))
  }

  /** 搜索模式下块是否有效 — 表格无命中行则整块隐藏 */
  function blockVisibleInSearch(block: WikiBlock): boolean {
    if (!searching) return true
    if (block.kind === 'table') {
      return block.rows.some(rowMatches)
    }
    if (block.kind === 'text') {
      return block.lines.some((l) => l.toLowerCase().includes(query.trim().toLowerCase()))
    }
    return (
      (block.title ?? '').toLowerCase().includes(query.trim().toLowerCase()) ||
      block.lines.some((l) => l.toLowerCase().includes(query.trim().toLowerCase()))
    )
  }

  // ===== 高亮工具（内容为内置数据，安全；@html 仅用于渲染我们的结构化内容）=====

  function escapeHtml(text: string): string {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  }

  function escapeRegExp(text: string): string {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  }

  /** 对文本做关键词高亮，返回 HTML 字符串 */
  function highlight(text: string): string {
    const kw = query.trim()
    const escaped = escapeHtml(text)
    if (!kw) return escaped
    return escaped.replace(new RegExp(`(${escapeRegExp(escapeHtml(kw))})`, 'gi'), '<mark>$1</mark>')
  }
</script>

<div class="min-h-0 flex-1 select-text overflow-y-auto bg-[var(--background-100)] p-8">
  <!-- 标题区 + 搜索框 -->
  <div class="mb-6 flex flex-wrap items-end justify-between gap-4">
    <div>
      <h1 class="text-[26px] font-semibold tracking-[-0.01em] text-[var(--foreground)]">玩家指南</h1>
      <p class="mt-1 text-sm text-[var(--muted-foreground)]">白鹤服务器 · 普通玩家查询手册（指令 / 玩法 / 常见问题）</p>
    </div>
    <div class="flex h-10 w-96 items-center gap-2 rounded-[0.75rem] border border-[var(--border)] bg-[var(--card)] px-3 transition-colors focus-within:border-[var(--ring)]">
      <Icon name="search" size={16} class="shrink-0 text-[var(--icon-muted)]" />
      <input
        type="text"
        bind:value={query}
        placeholder="搜索指令 / 关键词，如 home、资源包…"
        class="h-full min-w-0 flex-1 border-0 bg-transparent py-0 text-sm leading-normal text-[var(--foreground)] outline-none placeholder:text-[var(--muted-foreground)]"
      />
      {#if searching}
        <button
          type="button"
          class="shrink-0 text-[12px] text-[var(--muted-foreground)] transition-colors hover:text-[var(--foreground)]"
          onclick={() => (query = '')}
        >
          清除
        </button>
      {/if}
    </div>
  </div>

  {#if searching}
    <!-- ===== 搜索结果视图 ===== -->
    <div class="flex flex-col gap-5">
      <div class="text-[13px] text-[var(--muted-foreground)]">
        共找到 <span class="font-semibold text-[var(--primary)]">{hitCount}</span> 条相关内容
      </div>
      {#if groupedHits.length === 0}
        <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-16">
          <Icon name="search" size={28} class="text-[var(--muted-foreground)]" />
          <p class="mt-3 text-[14px] font-medium text-[var(--foreground)]">没有找到相关内容</p>
          <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">换个关键词试试，如「home」「资源包」「登录」</p>
        </div>
      {:else}
        {#each groupedHits as group (group.category.id)}
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="flex items-center gap-2 text-[15px] font-semibold text-[var(--foreground)]">
              <span class="inline-block h-5 w-1 rounded-full bg-[var(--primary)]" aria-hidden="true"></span>
              {group.category.title}
              <span class="text-[12px] font-normal text-[var(--muted-foreground)]">({group.hits.length} 条)</span>
            </h2>
            <div class="mt-4 flex flex-col gap-5">
              {#each group.hits as hit (hit.page.id + '-' + hit.blockIndex)}
                <div>
                  <div class="mb-2 text-[13px] font-medium text-[var(--primary)]">{hit.page.title}</div>
                  {#each hit.page.blocks as block, i (i)}
                    {#if blockVisibleInSearch(block)}
                      {#if block.kind === 'table'}
                        <div class="overflow-x-auto rounded-[0.75rem] border border-[var(--border)]">
                          {#if block.caption}
                            <div class="border-b border-[var(--border)] px-4 py-2 text-[12px] font-medium text-[var(--muted-foreground)]">{block.caption}</div>
                          {/if}
                          <table class="w-full text-left text-[13px]">
                            <thead>
                              <tr class="border-b border-[var(--border)] bg-[var(--accent)]">
                                {#each block.headers as header (header)}
                                  <th class="px-4 py-2.5 font-semibold text-[var(--foreground)]">{@html highlight(header)}</th>
                                {/each}
                              </tr>
                            </thead>
                            <tbody>
                              {#each block.rows.filter(rowMatches) as row, ri (ri)}
                                <tr class="border-b border-[var(--border)] last:border-b-0">
                                  {#each row as cell, ci (ci)}
                                    <td class="px-4 py-2.5 align-top {ci === 0 ? 'font-medium text-[var(--foreground)]' : 'text-[var(--muted-foreground)]'}">{@html highlight(cell)}</td>
                                  {/each}
                                </tr>
                              {/each}
                            </tbody>
                          </table>
                        </div>
                      {:else if block.kind === 'text'}
                        <div class="flex flex-col gap-1.5 text-[13px] leading-relaxed text-[var(--muted-foreground)]">
                          {#each block.lines as line (line)}
                            <p class="whitespace-pre-wrap">{@html highlight(line)}</p>
                          {/each}
                        </div>
                      {:else}
                        <div class="rounded-[0.75rem] border border-[var(--primary)]/25 bg-[var(--primary)]/8 px-4 py-3">
                          {#if block.title}
                            <div class="mb-1 text-[12px] font-semibold text-[var(--primary)]">{@html highlight(block.title)}</div>
                          {/if}
                          <div class="flex flex-col gap-1 text-[13px] leading-relaxed text-[var(--foreground)]">
                            {#each block.lines as line (line)}
                              <p class="whitespace-pre-wrap">{@html highlight(line)}</p>
                            {/each}
                          </div>
                        </div>
                      {/if}
                    {/if}
                  {/each}
                </div>
              {/each}
            </div>
          </section>
        {/each}
      {/if}
    </div>
  {:else}
    <!-- ===== 正常分级浏览 ===== -->
    <div class="flex">
      <!-- 左侧: 分级导航（分类 → 页面） -->
      <nav class="w-[200px] shrink-0 pr-4" aria-label="指南分类">
        <div class="flex flex-col gap-4">
          {#each wikiCategories as cat (cat.id)}
            <div>
              <button
                type="button"
                class="flex h-8 w-full cursor-pointer items-center gap-2 rounded-lg px-2 text-[13px] font-medium transition-colors {activeCategoryId === cat.id ? 'bg-[var(--sidebar-accent)] text-[var(--foreground)]' : 'text-[var(--muted-foreground)] hover:bg-[var(--secondary)]'}"
                onclick={() => selectCategory(cat.id)}
              >
                <span class="shrink-0 {activeCategoryId === cat.id ? 'text-[var(--primary)]' : 'text-[var(--icon-muted)]'}">·</span>
                <span class="truncate">{cat.title}</span>
              </button>
              {#if activeCategoryId === cat.id}
                <div class="mt-1 flex flex-col gap-0.5 border-l border-[var(--border)] pl-3">
                  {#each cat.pages as page (page.id)}
                    <button
                      type="button"
                      class="flex h-7 cursor-pointer items-center rounded-md px-2 text-left text-[12px] transition-colors {activePageId === page.id ? 'bg-[var(--accent)] text-[var(--foreground)]' : 'text-[var(--muted-foreground)] hover:text-[var(--foreground)]'}"
                      onclick={() => (activePageId = page.id)}
                    >
                      <span class="truncate">{page.title}</span>
                    </button>
                  {/each}
                </div>
              {/if}
            </div>
          {/each}
        </div>
      </nav>

      <!-- 右侧: 内容区 -->
      <div class="min-w-0 flex-1 border-l border-[var(--border)] pl-8">
        {#if activeCategory && activePage}
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <div class="mb-1 text-[12px] font-medium text-[var(--muted-foreground)]">{activeCategory.title}</div>
            <h2 class="text-[18px] font-semibold text-[var(--foreground)]">{activePage.title}</h2>
            {#if activePage.summary}
              <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">{activePage.summary}</p>
            {/if}
            <div class="mt-5 flex flex-col gap-5">
              {#each activePage.blocks as block, i (i)}
                {#if block.kind === 'table'}
                  <div class="overflow-x-auto rounded-[0.75rem] border border-[var(--border)]">
                    {#if block.caption}
                      <div class="border-b border-[var(--border)] px-4 py-2 text-[12px] font-medium text-[var(--muted-foreground)]">{block.caption}</div>
                    {/if}
                    <table class="w-full text-left text-[13px]">
                      <thead>
                        <tr class="border-b border-[var(--border)] bg-[var(--accent)]">
                          {#each block.headers as header (header)}
                            <th class="px-4 py-2.5 font-semibold text-[var(--foreground)]">{header}</th>
                          {/each}
                        </tr>
                      </thead>
                      <tbody>
                        {#each block.rows as row, ri (ri)}
                          <tr class="border-b border-[var(--border)] last:border-b-0">
                            {#each row as cell, ci (ci)}
                              <td class="px-4 py-2.5 align-top {ci === 0 ? 'font-medium text-[var(--foreground)]' : 'text-[var(--muted-foreground)]'}">{cell}</td>
                            {/each}
                          </tr>
                        {/each}
                      </tbody>
                    </table>
                  </div>
                {:else if block.kind === 'text'}
                  <div class="flex flex-col gap-1.5 text-[13px] leading-relaxed text-[var(--muted-foreground)]">
                    {#each block.lines as line (line)}
                      <p class="whitespace-pre-wrap">{line}</p>
                    {/each}
                  </div>
                {:else}
                  <div class="rounded-[0.75rem] border border-[var(--primary)]/25 bg-[var(--primary)]/8 px-4 py-3">
                    {#if block.title}
                      <div class="mb-1 text-[12px] font-semibold text-[var(--primary)]">{block.title}</div>
                    {/if}
                    <div class="flex flex-col gap-1 text-[13px] leading-relaxed text-[var(--foreground)]">
                      {#each block.lines as line (line)}
                        <p class="whitespace-pre-wrap">{line}</p>
                      {/each}
                    </div>
                  </div>
                {/if}
              {/each}
            </div>
          </section>
        {:else}
          <div class="flex flex-col items-center justify-center rounded-[1rem] border border-[var(--border)] bg-[var(--card)] py-20 text-[var(--muted-foreground)]">
            <p class="text-[14px]">暂无内容</p>
          </div>
        {/if}
      </div>
    </div>
  {/if}
</div>

<style>
  mark {
    background-color: color-mix(in srgb, var(--primary) 30%, transparent);
    color: var(--foreground);
    border-radius: 2px;
    padding: 0 1px;
  }
</style>
