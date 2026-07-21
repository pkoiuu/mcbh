<!--
  功能描述: 设置页 — 双栏布局（分类导航 + 内容卡片）
  技术实现: Svelte 5 runes，通过 IPC 获取 Java 检测和账户信息
  注意事项: macOS 风格 toggle 开关，账户/游戏/外观/关于四个分类
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'

  // 设置分类
  type SettingsCategory = 'account' | 'game' | 'appearance' | 'about'
  let activeCategory = $state<SettingsCategory>('account')

  const categories: { key: SettingsCategory; label: string; icon: string }[] = [
    { key: 'account', label: '账户', icon: 'user' },
    { key: 'game', label: '游戏', icon: 'box' },
    { key: 'appearance', label: '外观', icon: 'palette' },
    { key: 'about', label: '关于', icon: 'info' },
  ]

  /** 账户信息 */
  interface AccountInfo {
    username: string
    uuid: string
    type: string
  }

  /** 捆绑 Java 检测结果 */
  interface BundledJavaInfo {
    found: boolean
    path: string
    version: string
  }

  /** 系统 Java 条目 */
  interface SystemJavaEntry {
    path: string
    version: string
    is64Bit: boolean
  }

  /** 启动器设置 */
  interface LauncherSettings {
    memoryMB: number
    windowWidth: number
    windowHeight: number
    autoFullscreen: boolean
    javaPathOverride: string | null
    closeAfterLaunch: boolean
    serverAddress: string
    serverPort: number
    quickPlayEnabled: boolean
  }

  // 账户状态
  let account = $state<AccountInfo | null>(null)
  let isEditingName = $state(false)
  let editingName = $state('')
  let nameError = $state('')

  // Java 状态
  let bundledJava = $state<BundledJavaInfo | null>(null)
  let systemJava = $state<SystemJavaEntry[]>([])
  let javaLoading = $state(false)

  // 游戏设置状态 — 从后端加载
  let memoryMB = $state(4096)
  let windowWidth = $state(1280)
  let windowHeight = $state(720)
  let autoFullscreen = $state(false)
  let closeAfterLaunch = $state(false)
  let quickPlayEnabled = $state(true)
  let settingsLoaded = $state(false)
  let settingsSaving = $state(false)

  // 内存滑块临时值
  let memorySlider = $state(4)

  /** 内存选项 (GB) */
  const memoryOptions = [2, 3, 4, 6, 8, 12, 16]

  /** 加载账户信息 */
  async function loadAccount(): Promise<void> {
    try {
      account = await ipc<AccountInfo>('auth.current')
    } catch {
      // IPC 不可用时使用默认值
    }
  }

  /** 加载 Java 检测信息 */
  async function loadJavaInfo(): Promise<void> {
    javaLoading = true
    try {
      const [bundled, system] = await Promise.all([
        ipc<BundledJavaInfo>('java.bundled'),
        ipc<SystemJavaEntry[]>('java.detect').catch(() => []),
      ])
      bundledJava = bundled
      systemJava = system
    } catch {
      // IPC 不可用时使用默认值
    } finally {
      javaLoading = false
    }
  }

  /** 加载设置 */
  async function loadSettings(): Promise<void> {
    try {
      const s = await ipc<LauncherSettings>('settings.get')
      memoryMB = s.memoryMB
      windowWidth = s.windowWidth
      windowHeight = s.windowHeight
      autoFullscreen = s.autoFullscreen
      closeAfterLaunch = s.closeAfterLaunch
      quickPlayEnabled = s.quickPlayEnabled
      // 同步内存滑块位置
      const gb = Math.round(memoryMB / 1024)
      memorySlider = memoryOptions.indexOf(gb) >= 0 ? memoryOptions.indexOf(gb) : 4
    } catch {
      // IPC 不可用时使用默认值
    } finally {
      settingsLoaded = true
    }
  }

  /** 保存设置 — 防抖保存 */
  let saveTimer: ReturnType<typeof setTimeout> | null = null
  async function saveSettings(): Promise<void> {
    if (!settingsLoaded) return
    if (saveTimer) clearTimeout(saveTimer)
    saveTimer = setTimeout(async () => {
      settingsSaving = true
      try {
        await ipc('settings.set', {
          memoryMB,
          windowWidth,
          windowHeight,
          autoFullscreen,
          closeAfterLaunch,
          quickPlayEnabled,
        })
      } catch {
        // 保存失败静默处理
      } finally {
        settingsSaving = false
      }
    }, 500)
  }

  /** 内存滑块变更 */
  function onMemoryChange(): void {
    memoryMB = memoryOptions[memorySlider] * 1024
    saveSettings()
  }

  /** 窗口尺寸变更 */
  function onSizeChange(): void {
    saveSettings()
  }

  /** 保存用户名 */
  async function saveUsername(): Promise<void> {
    if (!editingName.trim()) {
      nameError = '用户名不能为空'
      return
    }
    if (editingName.length > 16) {
      nameError = '用户名最多 16 个字符'
      return
    }
    try {
      account = await ipc<AccountInfo>('auth.offline', editingName.trim())
      isEditingName = false
      nameError = ''
    } catch (e: unknown) {
      nameError = e instanceof Error ? e.message : '保存失败'
    }
  }

  /** 开始编辑用户名 */
  function startEditName(): void {
    editingName = account?.username ?? 'Player'
    isEditingName = true
    nameError = ''
  }

  // 组件挂载时加载数据
  loadAccount()
  loadJavaInfo()
  loadSettings()
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
                  {#if isEditingName}
                    <input
                      type="text"
                      class="h-8 w-32 rounded-lg border border-[var(--input)] bg-[var(--background)] px-2 text-sm text-[var(--foreground)] outline-none focus:border-[var(--ring)]"
                      bind:value={editingName}
                      maxlength={16}
                      aria-label="编辑用户名"
                    />
                    <button type="button" class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-opacity hover:opacity-70" onclick={saveUsername}>保存</button>
                    <button type="button" class="whitespace-nowrap text-[13px] font-medium text-[var(--muted-foreground)] transition-opacity hover:opacity-70" onclick={() => { isEditingName = false; nameError = '' }}>取消</button>
                  {:else}
                    <span class="truncate text-sm text-[var(--foreground)]">{account?.username ?? 'Player'}</span>
                    <button type="button" class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-opacity hover:opacity-70" onclick={startEditName}>修改</button>
                  {/if}
                </div>
              </div>
              {#if nameError}
                <div class="py-2 text-[12px] text-red-500">{nameError}</div>
              {/if}
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">UUID</span>
                <span class="truncate text-sm text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{account?.uuid ?? '—'}</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">验证方式</span>
                <span class="truncate text-sm text-[var(--foreground)]">离线模式</span>
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
              <!-- 捆绑 Java 检测 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">捆绑 Java</span>
                <div class="flex items-center gap-2">
                  {#if javaLoading}
                    <span class="text-sm text-[var(--muted-foreground)]">检测中...</span>
                  {:else if bundledJava?.found}
                    <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--success);" aria-hidden="true"></span>
                    <span class="text-sm text-[var(--foreground)]" style="font-family: var(--font-mono);">{bundledJava.version}</span>
                  {:else}
                    <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--muted-foreground);" aria-hidden="true"></span>
                    <span class="text-sm text-[var(--muted-foreground)]">未检测到</span>
                  {/if}
                </div>
              </div>
              <!-- 系统 Java 检测 -->
              <div class="flex items-start justify-between py-3">
                <span class="pt-2 whitespace-nowrap text-sm text-[var(--foreground)]">系统 Java</span>
                <div class="flex flex-col items-end gap-1">
                  {#if javaLoading}
                    <span class="text-sm text-[var(--muted-foreground)]">检测中...</span>
                  {:else if systemJava.length > 0}
                    {#each systemJava as java (java.path)}
                      <div class="flex items-center gap-2">
                        <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--success);" aria-hidden="true"></span>
                        <span class="text-sm text-[var(--foreground)]" style="font-family: var(--font-mono);">{java.version}</span>
                        <span class="text-[11px] text-[var(--muted-foreground)]">{java.is64Bit ? '64-bit' : '32-bit'}</span>
                      </div>
                    {/each}
                  {:else}
                    <span class="text-sm text-[var(--muted-foreground)]">未检测到系统 Java</span>
                  {/if}
                </div>
              </div>
              <!-- 内存分配 -->
              <div class="flex items-start justify-between py-3">
                <span class="pt-2 whitespace-nowrap text-sm text-[var(--foreground)]">内存分配</span>
                <div class="flex flex-col gap-1" style="width: 320px;">
                  <div class="flex items-center gap-3">
                    <input
                      type="range"
                      min="0"
                      max={memoryOptions.length - 1}
                      step="1"
                      bind:value={memorySlider}
                      onchange={onMemoryChange}
                      class="h-1.5 flex-1 cursor-pointer appearance-none rounded-full bg-[var(--background-300)] accent-[var(--primary)]"
                      aria-label="内存分配"
                    />
                    <span class="w-16 shrink-0 text-right text-sm font-medium text-[var(--foreground)]" style="font-family: var(--font-mono);">{Math.round(memoryMB / 1024)} GB</span>
                  </div>
                  <span class="text-xs text-[var(--muted-foreground)]">建议分配 2-4 GB{settingsSaving ? ' · 保存中...' : ''}</span>
                </div>
              </div>
              <!-- 游戏窗口尺寸 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">游戏窗口</span>
                <div class="flex items-center gap-2">
                  <div class="flex h-9 w-20 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="number" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" bind:value={windowWidth} onchange={onSizeChange} min="640" aria-label="游戏窗口宽度" />
                  </div>
                  <span class="shrink-0 text-sm text-[var(--muted-foreground)]">×</span>
                  <div class="flex h-9 w-20 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="number" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" bind:value={windowHeight} onchange={onSizeChange} min="480" aria-label="游戏窗口高度" />
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
                  onclick={() => { autoFullscreen = !autoFullscreen; saveSettings() }}
                >
                  <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({autoFullscreen ? '22px' : '2px'});"></span>
                </button>
              </div>
              <!-- QuickPlay 自动连接 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">QuickPlay 自动连接</span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={quickPlayEnabled}
                  aria-label="QuickPlay 自动连接"
                  class="relative h-7 w-12 shrink-0 rounded-full transition-colors duration-150"
                  style="background-color: {quickPlayEnabled ? 'var(--primary)' : 'var(--background-300)'};"
                  onclick={() => { quickPlayEnabled = !quickPlayEnabled; saveSettings() }}
                >
                  <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({quickPlayEnabled ? '22px' : '2px'});"></span>
                </button>
              </div>
              <!-- 启动后关闭启动器 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">启动后关闭启动器</span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={closeAfterLaunch}
                  aria-label="启动后关闭启动器"
                  class="relative h-7 w-12 shrink-0 rounded-full transition-colors duration-150"
                  style="background-color: {closeAfterLaunch ? 'var(--primary)' : 'var(--background-300)'};"
                  onclick={() => { closeAfterLaunch = !closeAfterLaunch; saveSettings() }}
                >
                  <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({closeAfterLaunch ? '22px' : '2px'});"></span>
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
