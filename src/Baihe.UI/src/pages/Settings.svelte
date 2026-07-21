<!--
  功能描述: 设置页 — 双栏布局（分类导航 + 内容卡片）
  技术实现: Svelte 5 runes，设置分类切换，后续接入 IPC 持久化
  注意事项: macOS 风格 toggle 开关，账户/游戏/外观/关于四个分类
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'

  // 设置分类
  type SettingsCategory = 'account' | 'game' | 'appearance' | 'about'
  let activeCategory = $state<SettingsCategory>('account')

  const categories: { key: SettingsCategory; label: string; icon: string }[] = [
    { key: 'account', label: '账户', icon: 'user' },
    { key: 'game', label: '游戏', icon: 'box' },
    { key: 'appearance', label: '外观', icon: 'palette' },
    { key: 'about', label: '关于', icon: 'info' },
  ]

  // 游戏设置状态（后续从 IPC 获取/保存）
  let javaPath = $state('C:\\Program Files\\Java\\jdk-17')
  let memoryMB = $state(4096)
  let windowWidth = $state(1280)
  let windowHeight = $state(720)
  let autoFullscreen = $state(false)
</script>

<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background-100)] p-8">
  <!-- 标题 -->
  <div class="mb-6">
    <h1 class="text-[26px] font-semibold tracking-[-0.01em] text-[var(--foreground)]">设置</h1>
    <p class="mt-1 text-sm text-[var(--muted-foreground)]">管理你的白鹤启动器</p>
  </div>

  <div class="flex">
    <!-- 左侧: 设置分类导航 -->
    <nav class="w-[180px] shrink-0 pr-4" aria-label="设置分类">
      <div class="flex flex-col gap-1">
        {#each categories as cat (cat.key)}
          <a
            href="#"
            class="group flex h-9 items-center gap-2 rounded-lg px-3 text-sm transition-colors {activeCategory === cat.key ? 'bg-[var(--sidebar-accent)] text-[var(--foreground)]' : 'text-[var(--muted-foreground)] hover:bg-[var(--secondary)]'}"
            onclick={(e) => {
              e.preventDefault()
              activeCategory = cat.key
            }}
          >
            <span class="flex items-center {activeCategory === cat.key ? 'text-[var(--primary)]' : 'text-[var(--icon-muted)]'}">
              <Icon name={cat.icon} size={18} />
            </span>
            <span class="truncate">{cat.label}</span>
          </a>
        {/each}
      </div>
    </nav>

    <!-- 右侧: 设置内容 -->
    <div class="min-w-0 flex-1 border-l border-[var(--border)] pl-8">
      <div class="flex flex-col gap-5">
        {#if activeCategory === 'account'}
          <!-- 账户信息卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">账户信息</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">管理你的账户资料与登录状态</p>
            <div class="mt-4 divide-y divide-[var(--border)]">
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">头像</span>
                <div class="flex h-10 w-10 items-center justify-center rounded-[12px] bg-[var(--background-200)] text-[var(--icon-muted)]">
                  <Icon name="user" size={20} />
                </div>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">用户名</span>
                <div class="flex items-center gap-3">
                  <span class="truncate text-sm text-[var(--foreground)]">Player</span>
                  <button type="button" class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-opacity hover:opacity-70">修改</button>
                </div>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">验证方式</span>
                <div class="flex items-center gap-3">
                  <span class="truncate text-sm text-[var(--foreground)]">离线模式</span>
                  <button type="button" class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-opacity hover:opacity-70">切换</button>
                </div>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">状态</span>
                <div class="flex items-center gap-2">
                  <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--success);" aria-hidden="true"></span>
                  <span class="whitespace-nowrap text-sm text-[var(--foreground)]">已登录</span>
                </div>
              </div>
            </div>
          </section>

        {:else if activeCategory === 'game'}
          <!-- 游戏运行卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">游戏运行</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">配置 Java 运行环境与启动参数</p>
            <div class="mt-4 divide-y divide-[var(--border)]">
              <!-- Java 路径 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">Java 路径</span>
                <div class="flex items-center gap-2">
                  <div class="flex h-9 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]" style="width: 320px;">
                    <input type="text" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" value={javaPath} readonly aria-label="Java 路径" />
                  </div>
                  <button type="button" class="inline-flex h-8 shrink-0 items-center justify-center rounded-full bg-[var(--secondary)] px-4 text-[13px] font-medium text-[var(--secondary-foreground)] transition-colors hover:bg-[var(--muted)]">浏览</button>
                </div>
              </div>
              <!-- 内存分配 -->
              <div class="flex items-start justify-between py-3">
                <span class="pt-2 whitespace-nowrap text-sm text-[var(--foreground)]">内存分配</span>
                <div class="flex flex-col gap-1" style="width: 320px;">
                  <div class="flex h-9 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="text" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" value="{memoryMB} MB" readonly aria-label="内存分配" />
                  </div>
                  <span class="text-xs text-[var(--muted-foreground)]">建议分配 2-4 GB</span>
                </div>
              </div>
              <!-- 游戏窗口尺寸 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">游戏窗口</span>
                <div class="flex items-center gap-2">
                  <div class="flex h-9 w-20 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="text" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" value={windowWidth} readonly aria-label="游戏窗口宽度" />
                  </div>
                  <span class="shrink-0 text-sm text-[var(--muted-foreground)]">×</span>
                  <div class="flex h-9 w-20 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="text" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" value={windowHeight} readonly aria-label="游戏窗口高度" />
                  </div>
                  <span class="shrink-0 whitespace-nowrap text-sm text-[var(--muted-foreground)]">像素</span>
                </div>
              </div>
              <!-- 启动后自动全屏 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">启动后自动全屏</span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={autoFullscreen}
                  aria-label="启动后自动全屏"
                  class="relative h-7 w-12 shrink-0 rounded-full transition-colors duration-150"
                  style="background-color: {autoFullscreen ? 'var(--primary)' : 'var(--background-300)'};"
                  onclick={() => (autoFullscreen = !autoFullscreen)}
                >
                  <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({autoFullscreen ? '22px' : '2px'});"></span>
                </button>
              </div>
            </div>
          </section>

        {:else if activeCategory === 'appearance'}
          <!-- 外观设置卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">外观</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">自定义启动器外观</p>
            <div class="mt-4 divide-y divide-[var(--border)]">
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">主题模式</span>
                <span class="text-sm text-[var(--muted-foreground)]">暗色（默认）</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">主题色</span>
                <div class="flex items-center gap-2">
                  <span class="h-6 w-6 rounded-full border-2 border-[var(--ring)]" style="background-color: #007aff;"></span>
                </div>
              </div>
            </div>
          </section>

        {:else if activeCategory === 'about'}
          <!-- 关于卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">关于</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">白鹤服务器启动器</p>
            <div class="mt-4 divide-y divide-[var(--border)]">
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">版本</span>
                <span class="text-sm text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">v1.0.0</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">服务器地址</span>
                <span class="text-sm text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">play.simpfun.cn:28230</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">开源许可</span>
                <span class="text-sm text-[var(--muted-foreground)]">Apache 2.0</span>
              </div>
            </div>
          </section>
        {/if}
      </div>
    </div>
  </div>
</div>
