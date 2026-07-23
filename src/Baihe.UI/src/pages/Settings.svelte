<!--
  功能描述: 设置页 — 双栏布局（分类导航 + 内容卡片）
  技术实现: Svelte 5 runes，通过 IPC 获取 Java 检测和账户信息
  注意事项: macOS 风格 toggle 开关，账户/游戏/外观/关于四个分类
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'
  import { toast } from '../lib/toast.svelte'
  import { router } from '../lib/router.svelte'
  import { theme } from '../lib/theme.svelte'
  import defaultAvatar from '../assets/default-avatar.png'

  // 设置分类
  type SettingsCategory = 'account' | 'game' | 'appearance' | 'about' | 'developer'
  let activeCategory = $state<SettingsCategory>('account')

  const categories: { key: SettingsCategory; label: string; icon: string }[] = [
    { key: 'account', label: '账户', icon: 'user' },
    { key: 'game', label: '游戏', icon: 'box' },
    { key: 'appearance', label: '外观', icon: 'palette' },
    { key: 'about', label: '关于', icon: 'info' },
    { key: 'developer', label: '开发者', icon: 'grip' },
  ]

  /** 账户信息 */
  interface AccountInfo {
    username: string | null
    uuid: string | null
    type: string
    typeDisplay?: string
    isUserSet?: boolean
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

  // 头像 — 从 localStorage 读取 base64 图片
  let avatarData = $state<string | null>(null)
  let avatarInput: HTMLInputElement | null = null

  /** 加载头像 */
  function loadAvatar(): void {
    try {
      avatarData = localStorage.getItem('baihe_avatar')
    } catch { }
  }

  /** 选择头像图片 */
  function pickAvatar(): void {
    avatarInput?.click()
  }

  /** 处理头像选择 */
  function onAvatarChange(e: Event): void {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) return

    // 验证文件类型
    if (!file.type.startsWith('image/')) {
      toast.error('请选择图片文件')
      return
    }
    // 验证文件大小 (最大 2MB)
    if (file.size > 2 * 1024 * 1024) {
      toast.error('图片大小不能超过 2MB')
      return
    }

    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      // 裁剪为正方形缩略图 (64x64)
      const img = new Image()
      img.onload = () => {
        const canvas = document.createElement('canvas')
        canvas.width = 64
        canvas.height = 64
        const ctx = canvas.getContext('2d')
        if (!ctx) return
        // 居中裁剪
        const size = Math.min(img.width, img.height)
        const sx = (img.width - size) / 2
        const sy = (img.height - size) / 2
        ctx.drawImage(img, sx, sy, size, size, 0, 0, 64, 64)
        avatarData = canvas.toDataURL('image/png')
        try {
          localStorage.setItem('baihe_avatar', avatarData)
        } catch { }
        toast.success('头像已更新')
      }
      img.src = result
    }
    reader.readAsDataURL(file)
    // 重置 input 以便可以重复选择同一文件
    input.value = ''
  }

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
  let serverAddress = $state('play.simpfun.cn')
  let serverPort = $state(28230)
  let appVersion = $state('1.0.0')
  let settingsLoaded = $state(false)
  let settingsSaving = $state(false)

  // 更新检查
  let updateChecking = $state(false)
  let updateInfo = $state<{ hasUpdate: boolean; currentVersion: string; latestVersion: string; downloadUrl: string } | null>(null)

  /** 系统内存信息 */
  interface SystemMemory {
    totalMB: number
    totalGB: number
    recommendedMB: number
    recommendedGB: number
  }

  // 系统内存
  let systemMemory = $state<SystemMemory | null>(null)

  // 内存滑块临时值
  let memorySlider = $state(4)

  // 开发者设置状态
  let devPasswordInput = $state('')
  let devUnlocked = $state(false)
  let devPasswordError = $state('')
  let chatEnabled = $state(localStorage.getItem('baihe_chat_enabled') === 'true')

  /** 生成内存选项 — 基础选项过滤掉超过系统内存 70% 的值，确保推荐值在选项中 */
  function generateMemoryOptions(totalGB: number): number[] {
    const maxGB = Math.floor(totalGB * 0.7)
    const baseOptions = [2, 3, 4, 6, 8, 12, 16]
    let options = baseOptions.filter(g => g <= maxGB)
    // 确保至少有 2GB 和 maxGB
    if (options.length < 2) options = [2, maxGB]
    return [...new Set(options)].sort((a, b) => a - b)
  }

  /** 内存选项 — 根据系统内存动态生成 */
  let memoryOptions = $derived(systemMemory ? generateMemoryOptions(systemMemory.totalGB) : [2, 3, 4, 6, 8, 12, 16])

  /** 加载账户信息 — 处理未设置账户时返回的 null username */
  async function loadAccount(): Promise<void> {
    try {
      account = await ipc<AccountInfo>('auth.current')
    } catch {
      account = null
    }
  }

  /** 加载启动器版本号 — 从后端动态获取 */
  async function loadAppVersion(): Promise<void> {
    try {
      appVersion = await ipc<string>('app.getVersion')
    } catch {
      appVersion = '1.0.0'
    }
  }

  /** 加载系统内存信息 — 用于内存智能推荐 */
  async function loadSystemMemory(): Promise<void> {
    try {
      systemMemory = await ipc<SystemMemory>('system.memory')
      // 同步内存滑块位置
      const gb = Math.round(memoryMB / 1024)
      memorySlider = memoryOptions.indexOf(gb) >= 0 ? memoryOptions.indexOf(gb) : Math.floor(memoryOptions.length / 2)
    } catch {
      systemMemory = null
    }
  }

  /** 检查更新 — 手动触发查询 GitHub Releases */
  async function checkForUpdate(): Promise<void> {
    updateChecking = true
    try {
      updateInfo = await ipc<{ hasUpdate: boolean; currentVersion: string; latestVersion: string; downloadUrl: string }>('update.check')
    } catch {
      updateInfo = null
    } finally {
      updateChecking = false
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
      serverAddress = s.serverAddress
      serverPort = s.serverPort
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
          serverAddress,
          serverPort,
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

  /** 验证开发者密码 */
  function unlockDev(): void {
    if (devPasswordInput === '111125hj') {
      devUnlocked = true
      devPasswordError = ''
    } else {
      devPasswordError = '密码错误'
    }
  }

  /** 切换聊天功能开关 */
  function toggleChat(): void {
    chatEnabled = !chatEnabled
    localStorage.setItem('baihe_chat_enabled', String(chatEnabled))
  }

  // 组件挂载时加载数据
  loadAccount()
  loadAvatar()
  loadJavaInfo()
  loadSettings()
  loadAppVersion()
  loadSystemMemory()
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
            href="javascript:void(0)"
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
                <div class="flex items-center gap-3">
                  <input type="file" accept="image/*" class="hidden" bind:this={avatarInput} onchange={onAvatarChange} />
                  <button
                    type="button"
                    class="group relative h-12 w-12 overflow-hidden rounded-[14px] border border-[var(--border)] bg-[var(--accent)] transition-all hover:border-[var(--primary)] hover:shadow-[0_0_0_2px_var(--primary)]"
                    onclick={pickAvatar}
                    aria-label="修改头像"
                  >
                    {#if avatarData}
                      <img src={avatarData} alt="头像" class="h-full w-full object-cover" />
                    {:else}
                      <img src={defaultAvatar} alt="默认头像" class="h-full w-full object-cover" />
                    {/if}
                    <!-- 悬停遮罩 -->
                    <div class="absolute inset-0 flex items-center justify-center bg-black/50 opacity-0 transition-opacity group-hover:opacity-100">
                      <Icon name="settings" size={16} />
                    </div>
                  </button>
                  <span class="text-[12px] text-[var(--muted-foreground)]">点击修改</span>
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
                    <span class="truncate text-sm text-[var(--foreground)]">{account?.isUserSet ? account.username : '未设置'}</span>
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
                <span class="truncate text-sm text-[var(--foreground)]">{account?.typeDisplay || '离线模式'}</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">状态</span>
                <div class="flex items-center gap-2">
                  {#if account?.isUserSet}
                    <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--success);" aria-hidden="true"></span>
                    <span class="whitespace-nowrap text-sm text-[var(--foreground)]">已登录</span>
                  {:else}
                    <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--muted-foreground);" aria-hidden="true"></span>
                    <span class="whitespace-nowrap text-sm text-[var(--muted-foreground)]">未设置</span>
                  {/if}
                </div>
              </div>
            </div>
            <div class="flex justify-end pt-3">
              <button
                type="button"
                class="inline-flex h-9 items-center gap-1.5 rounded-lg bg-[var(--primary)] px-4 text-[13px] font-medium text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96]"
                onclick={() => router.navigate('login')}
              >
                切换登录方式
              </button>
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
                      class="h-1.5 flex-1 cursor-pointer appearance-none rounded-full bg-[var(--accent)] accent-[var(--primary)]"
                      aria-label="内存分配"
                    />
                    <span class="w-16 shrink-0 text-right text-sm font-medium text-[var(--foreground)]" style="font-family: var(--font-mono);">{Math.round(memoryMB / 1024)} GB</span>
                  </div>
                  <span class="text-xs text-[var(--muted-foreground)]">{systemMemory ? `检测到 ${systemMemory.totalGB}GB 内存 · 推荐 ${systemMemory.recommendedGB}GB` : '建议分配 2-4 GB'}{settingsSaving ? ' · 保存中...' : ''}</span>
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
                  style="background-color: {autoFullscreen ? 'var(--primary)' : 'var(--accent)'};"
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
                  style="background-color: {quickPlayEnabled ? 'var(--primary)' : 'var(--accent)'};"
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
                  style="background-color: {closeAfterLaunch ? 'var(--primary)' : 'var(--accent)'};"
                  onclick={() => { closeAfterLaunch = !closeAfterLaunch; saveSettings() }}
                >
                  <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({closeAfterLaunch ? '22px' : '2px'});"></span>
                </button>
              </div>
              <!-- 服务器地址 -->
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">服务器地址</span>
                <div class="flex items-center gap-2">
                  <div class="flex h-9 w-44 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="text" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" bind:value={serverAddress} onchange={saveSettings} aria-label="服务器地址" />
                  </div>
                  <span class="shrink-0 text-sm text-[var(--muted-foreground)]">:</span>
                  <div class="flex h-9 w-20 shrink-0 items-center rounded-[0.6rem] border border-[var(--input)] bg-[var(--background)] px-3 transition-colors focus-within:border-[var(--ring)] focus-within:shadow-[0_0_0_1px_var(--ring)]">
                    <input type="number" class="w-full border-0 bg-transparent text-sm text-[var(--foreground)] outline-none" style="font-family: var(--font-mono);" bind:value={serverPort} onchange={saveSettings} min="1" max="65535" aria-label="服务器端口" />
                  </div>
                </div>
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
                <div class="flex items-center gap-2">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={theme.current === 'dark'}
                    aria-label="切换主题模式"
                    class="relative h-7 w-12 shrink-0 rounded-full transition-colors duration-150"
                    style="background-color: {theme.current === 'dark' ? 'var(--primary)' : 'var(--accent)'};"
                    onclick={() => theme.toggle()}
                  >
                    <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150" style="transform: translateX({theme.current === 'dark' ? '22px' : '2px'});"></span>
                  </button>
                  <span class="text-sm text-[var(--muted-foreground)]">{theme.current === 'dark' ? '暗色' : '亮色'}</span>
                </div>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">主题色</span>
                <div class="flex items-center gap-2">
                  <span class="h-6 w-6 rounded-full border-2 border-[var(--ring)]" style="background-color: #007aff;"></span>
                </div>
              </div>
            </div>
          </section>

        {:else if activeCategory === 'developer'}
          <!-- 开发者设置卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">开发者设置</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">高级功能与实验性选项</p>
            {#if !devUnlocked}
              <div class="mt-4">
                <label class="block text-sm font-medium text-[var(--foreground)]">请输入开发者密码</label>
                <div class="mt-2 flex gap-2">
                  <input
                    type="password"
                    bind:value={devPasswordInput}
                    onkeydown={(e) => { if (e.key === 'Enter') unlockDev() }}
                    class="flex-1 rounded-lg border border-[var(--border)] bg-[var(--background)] px-3 py-2 text-sm text-[var(--foreground)] outline-none focus:border-[var(--primary)]"
                    placeholder="输入密码"
                  />
                  <button
                    type="button"
                    class="rounded-lg bg-[var(--primary)] px-4 py-2 text-sm font-semibold text-[var(--primary-foreground)] transition-[filter] hover:brightness-[0.96]"
                    onclick={unlockDev}
                  >确认</button>
                </div>
                {#if devPasswordError}
                  <p class="mt-2 text-xs text-[var(--destructive)]">{devPasswordError}</p>
                {/if}
              </div>
            {:else}
              <div class="mt-4 divide-y divide-[var(--border)]">
                <div class="flex items-center justify-between py-3">
                  <div>
                    <span class="text-sm font-medium text-[var(--foreground)]">聊天功能</span>
                    <p class="mt-0.5 text-xs text-[var(--muted-foreground)]">在工具页显示聊天入口（测试功能）</p>
                  </div>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={chatEnabled}
                    class="relative h-7 w-12 shrink-0 rounded-full transition-colors duration-150"
                    style="background-color: {chatEnabled ? 'var(--primary)' : 'var(--accent)'};"
                    onclick={toggleChat}
                  >
                    <span class="absolute top-0.5 h-6 w-6 rounded-full bg-white shadow-sm transition-transform duration-150"
                      style="transform: translateX({chatEnabled ? '22px' : '2px'});"></span>
                  </button>
                </div>
              </div>
            {/if}
          </section>

        {:else if activeCategory === 'about'}
          <!-- 关于卡片 -->
          <section class="rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
            <h2 class="text-base font-semibold text-[var(--foreground)]">关于</h2>
            <p class="mt-1 text-[13px] text-[var(--muted-foreground)]">白鹤服务器启动器</p>
            <div class="mt-4 divide-y divide-[var(--border)]">
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">版本</span>
                <div class="flex items-center gap-3">
                  <span class="text-sm text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">v{appVersion}</span>
                  <button
                    type="button"
                    class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-[opacity] hover:opacity-70 disabled:opacity-50"
                    onclick={checkForUpdate}
                    disabled={updateChecking}
                  >
                    {updateChecking ? '检查中...' : '检查更新'}
                  </button>
                </div>
              </div>
              {#if updateInfo}
                <div class="flex items-center justify-between py-3">
                  <span class="whitespace-nowrap text-sm text-[var(--foreground)]">更新状态</span>
                  <div class="flex items-center gap-3">
                    {#if updateInfo.hasUpdate}
                      <span class="text-sm text-[var(--primary)]">发现新版本 v{updateInfo.latestVersion}</span>
                      <button
                        type="button"
                        class="whitespace-nowrap text-[13px] font-medium text-[var(--primary)] transition-[opacity] hover:opacity-70"
                        onclick={() => ipc('open.url', updateInfo.downloadUrl)}
                      >
                        下载 &rarr;
                      </button>
                    {:else}
                      <span class="text-sm text-[var(--muted-foreground)]">已是最新版本</span>
                    {/if}
                  </div>
                </div>
              {/if}
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">服务器地址</span>
                <span class="text-sm text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{serverAddress}:{serverPort}</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">开源许可</span>
                <span class="text-sm text-[var(--muted-foreground)]">Apache 2.0</span>
              </div>
              <div class="flex items-center justify-between py-3">
                <span class="whitespace-nowrap text-sm text-[var(--foreground)]">开源地址</span>
                <a href="javascript:void(0)" class="text-sm text-[var(--primary)] transition-[opacity] hover:opacity-80" onclick={() => ipc('open.url', 'https://github.com/pkoiuu/mcbh')}>GitHub 仓库 →</a>
              </div>
            </div>
          </section>
        {/if}
      </div>
    </div>
  </div>
</div>
