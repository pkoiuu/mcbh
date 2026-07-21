<!--
  功能描述: 启动主页 — 欢迎区 + 当前实例卡片 + 快捷工具 + 新闻列表
  技术实现: Svelte 5 runes，通过 IPC 获取实例信息和启动游戏
  注意事项: 启动按钮调用 launch.start IPC 命令，支持 QuickPlay 直连
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc, on } from '../lib/ipc'

  /** 实例信息 */
  interface GameInstance {
    id: string
    version: string
    type: string
    loader: string
    lastPlayed: string
    modCount: number
    isInstalled: boolean
    displayName: string
  }

  /** 启动结果 */
  interface LaunchResult {
    success: boolean
    processId?: number
    error?: string
  }

  /** 服务器状态 */
  interface ServerStatus {
    online: boolean
    latency: number
    address: string
    port: number
  }

  /** 启动推送事件数据 */
  interface LaunchStateEvent {
    state: string
    message: string
  }

  interface LaunchStartedEvent {
    processId: number
  }

  interface LaunchExitedEvent {
    exitCode: number
    abnormal: boolean
  }

  // 实例状态
  let instance = $state<GameInstance | null>(null)
  let isLoading = $state(true)
  let loadError = $state('')

  // 启动状态 — 通过推送事件更新
  let isLaunching = $state(false)
  let launchError = $state('')
  let launchMessage = $state('')
  let launchSuccess = $state(false)
  let gameRunning = $state(false)

  // 服务器状态
  let serverStatus = $state<ServerStatus | null>(null)
  let serverChecking = $state(false)

  /** 快捷工具列表 */
  const tools = [
    { id: 'new-instance', icon: 'plus', title: '新建实例', desc: '配置版本与加载器' },
    { id: 'import-save', icon: 'upload', title: '导入存档', desc: '从本地或备份恢复' },
    { id: 'backup', icon: 'box', title: '备份管理', desc: '查看与恢复快照' },
    { id: 'mods', icon: 'package', title: 'Mod 管理', desc: '启用、排序与更新' },
  ]

  /** 新闻列表 */
  const news = [
    { date: '07·18', title: 'Fabric Loader 0.16.0 已发布', desc: '支持 Minecraft 1.20.4，建议尽快更新加载器以获得更好的模组兼容性。' },
    { date: '07·15', title: '白鹤服务器启动器 v1.0.0 上线', desc: '全新 macOS 风格界面、实例管理重构，启动速度提升约 40%。' },
    { date: '07·10', title: 'Java 运行时自动安装指引', desc: '新增 Adoptium Temurin 自动检测与一键安装，无需手动配置环境。' },
  ]

  /** 页面加载时获取当前实例 */
  async function loadInstance(): Promise<void> {
    isLoading = true
    loadError = ''
    try {
      instance = await ipc<GameInstance>('instance.current')
    } catch (e: unknown) {
      loadError = e instanceof Error ? e.message : '获取实例失败'
    } finally {
      isLoading = false
    }
  }

  /** 检查服务器状态 */
  async function checkServerStatus(): Promise<void> {
    serverChecking = true
    try {
      serverStatus = await ipc<ServerStatus>('server.status')
    } catch {
      serverStatus = null
    } finally {
      serverChecking = false
    }
  }

  /** 处理启动按钮点击 — 调用 IPC 启动游戏 */
  async function handleLaunch(): Promise<void> {
    if (isLaunching || gameRunning || !instance) return
    isLaunching = true
    launchError = ''
    launchMessage = '正在准备启动...'
    launchSuccess = false

    try {
      const result = await ipc<LaunchResult>('launch.start', {
        instanceId: instance.id,
        quickPlay: true,
      })
      if (!result.success) {
        launchError = result.error || '启动失败'
        isLaunching = false
      }
      // 成功时等待 launch.started 推送事件来更新状态
    } catch (e: unknown) {
      launchError = e instanceof Error ? e.message : '启动失败'
      isLaunching = false
    }
  }

  // 注册推送事件监听器 — 在组件初始化时注册
  $effect(() => {
    // 启动状态变更 (preparing/launching/error)
    const offState = on('launch.state', (data) => {
      const evt = data as LaunchStateEvent
      launchMessage = evt.message
      if (evt.state === 'error') {
        launchError = evt.message
        isLaunching = false
        gameRunning = false
      }
    })

    // 游戏进程已启动
    const offStarted = on('launch.started', (data) => {
      const evt = data as LaunchStartedEvent
      isLaunching = false
      gameRunning = true
      launchSuccess = true
      launchMessage = '游戏已启动，正在连接白鹤服务器'
      // 5 秒后隐藏成功提示
      setTimeout(() => { launchSuccess = false }, 5000)
    })

    // 游戏进程已退出
    const offExited = on('launch.exited', (data) => {
      const evt = data as LaunchExitedEvent
      gameRunning = false
      isLaunching = false
      if (evt.abnormal) {
        launchError = `游戏异常退出 (code: ${evt.exitCode})`
        launchMessage = ''
      } else {
        launchMessage = '游戏已正常退出'
        setTimeout(() => { launchMessage = '' }, 3000)
      }
    })

    // 组件卸载时清理监听器和定时器
    const statusInterval = setInterval(checkServerStatus, 60000)
    return () => {
      offState()
      offStarted()
      offExited()
      clearInterval(statusInterval)
    }
  })

  // 组件挂载时加载数据
  loadInstance()
  checkServerStatus()
</script>

<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background-100)] p-8">
  <div class="flex flex-col gap-8">
    <!-- 1. 欢迎标题区 + 服务器状态 -->
    <section class="flex items-end justify-between">
      <div>
        <div class="text-[13px] font-semibold tracking-wide text-[var(--muted-foreground)]">欢迎回来</div>
        <h1 class="mt-1.5 text-[30px] font-semibold leading-tight tracking-[-0.02em] text-[var(--foreground)]" style="text-wrap: balance;">
          开始你的冒险
        </h1>
        <p class="mt-1.5 text-[15px] text-[var(--muted-foreground)]">选择实例，启程前往方块世界</p>
      </div>
      <!-- 服务器状态指示器 -->
      <div class="flex items-center gap-2 rounded-full border border-[var(--border)] bg-[var(--card)] px-4 py-2 shadow-[var(--shadow-sm)]">
        {#if serverChecking}
          <span class="inline-block h-2 w-2 animate-pulse rounded-full bg-[var(--muted-foreground)]" aria-hidden="true"></span>
          <span class="text-[13px] text-[var(--muted-foreground)]">检测中...</span>
        {:else if serverStatus?.online}
          <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--success);" aria-hidden="true"></span>
          <span class="text-[13px] font-medium text-[var(--foreground)]">服务器在线</span>
          <span class="text-[12px] text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{serverStatus.latency}ms</span>
        {:else}
          <span class="inline-block h-2 w-2 rounded-full" style="background-color: var(--destructive);" aria-hidden="true"></span>
          <span class="text-[13px] text-[var(--muted-foreground)]">服务器离线</span>
        {/if}
      </div>
    </section>

    <!-- 2. 当前实例大卡片 -->
    <section>
      {#if isLoading}
        <!-- 加载中状态 -->
        <article class="flex items-center gap-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
          <div class="flex h-16 w-16 shrink-0 items-center justify-center rounded-[12px] bg-[var(--background-200)] text-[var(--muted-foreground)]">
            <Icon name="box" size={28} />
          </div>
          <div class="min-w-0 flex-1">
            <div class="h-[22px] w-40 animate-pulse rounded bg-[var(--background-200)]"></div>
            <div class="mt-2 h-[16px] w-56 animate-pulse rounded bg-[var(--background-200)]"></div>
          </div>
        </article>
      {:else if loadError || !instance}
        <!-- 无实例或加载失败 -->
        <article class="flex items-center gap-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
          <div class="flex h-16 w-16 shrink-0 items-center justify-center rounded-[12px] bg-[var(--background-200)] text-[var(--muted-foreground)]">
            <Icon name="box" size={28} />
          </div>
          <div class="min-w-0 flex-1">
            <h2 class="text-[18px] font-semibold text-[var(--foreground)]">暂无游戏实例</h2>
            <p class="mt-1.5 text-[13px] text-[var(--muted-foreground)]">
              {loadError || '请在下载页面安装 Minecraft 版本'}
            </p>
          </div>
        </article>
      {:else}
        <!-- 正常实例卡片 -->
        <article class="flex items-center gap-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
          <!-- 缩略图 -->
          <div class="flex h-16 w-16 shrink-0 items-center justify-center rounded-[12px] bg-[var(--background-200)] text-[var(--muted-foreground)]">
            <Icon name="box" size={28} />
          </div>
          <!-- 中间: 实例名 + meta -->
          <div class="min-w-0 flex-1">
            <h2 class="truncate text-[18px] font-semibold text-[var(--foreground)]">{instance.displayName}</h2>
            <div class="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[13px] text-[var(--muted-foreground)]">
              <span class="whitespace-nowrap" style="font-family: var(--font-mono);">{instance.version}</span>
              <span aria-hidden="true">·</span>
              <span class="whitespace-nowrap">最后游玩 {instance.lastPlayed}</span>
              <span aria-hidden="true">·</span>
              <span class="whitespace-nowrap">已安装 {instance.modCount} 个 Mod</span>
            </div>
            <!-- 启动进度消息 -->
            {#if launchMessage && !launchError}
              <div class="mt-2 flex items-center gap-1.5 text-[12px] font-medium text-[var(--primary)]">
                {#if isLaunching}
                  <span class="inline-block h-3 w-3 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" aria-hidden="true"></span>
                {/if}
                {launchMessage}
              </div>
            {/if}
            <!-- 启动错误提示 -->
            {#if launchError}
              <div class="mt-2 text-[12px] font-medium text-red-500">{launchError}</div>
            {/if}
            {#if launchSuccess}
              <div class="mt-2 text-[12px] font-medium text-green-500">游戏已启动，正在连接白鹤服务器</div>
            {/if}
          </div>
          <!-- 右侧: 启动按钮 -->
          <div class="flex shrink-0 flex-col items-end gap-1.5">
            <button
              type="button"
              class="inline-flex h-11 items-center gap-2 whitespace-nowrap rounded-full bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
              disabled={isLaunching || gameRunning || !instance.isInstalled}
              onclick={handleLaunch}
            >
              <Icon name="circle-play" size={18} />
              <span>{gameRunning ? '运行中' : isLaunching ? '启动中...' : launchSuccess ? '已启动' : '启动游戏'}</span>
            </button>
            <span class="text-[12px] text-[var(--muted-foreground)]">
              {#if !instance.isInstalled}
                版本未安装
              {:else if gameRunning}
                游戏正在运行
              {:else}
                QuickPlay 直连白鹤服务器
              {/if}
            </span>
          </div>
        </article>
      {/if}
    </section>

    <!-- 3. 快捷工具网格 -->
    <section>
      <div class="grid grid-cols-4 gap-4">
        {#each tools as tool (tool.id)}
          <a
            href="#"
            class="group flex flex-col gap-2.5 rounded-[1rem] border border-[var(--border)] bg-[var(--card)] p-5 transition-[box-shadow,transform] hover:-translate-y-0.5 hover:shadow-[var(--shadow-md)]"
            onclick={(e) => e.preventDefault()}
          >
            <Icon name={tool.icon} size={20} class="text-[var(--primary)]" />
            <div class="min-w-0">
              <div class="truncate text-[14px] font-semibold text-[var(--foreground)]">{tool.title}</div>
              <div class="mt-0.5 truncate text-[12px] text-[var(--muted-foreground)]">{tool.desc}</div>
            </div>
          </a>
        {/each}
      </div>
    </section>

    <!-- 4. 新闻公告列表 -->
    <section>
      <div class="flex items-center justify-between">
        <h2 class="text-[16px] font-semibold text-[var(--foreground)]">最新动态</h2>
        <a href="#" class="text-[13px] font-medium text-[var(--primary)] transition-[opacity] hover:opacity-80" onclick={(e) => e.preventDefault()}>查看全部</a>
      </div>
      <div class="mt-3">
        {#each news as item (item.title)}
          <div class="flex items-start gap-4 border-b border-[var(--border)] px-1 py-4 last:border-b-0">
            <span class="w-12 shrink-0 whitespace-nowrap text-[13px] text-[var(--muted-foreground)]" style="font-family: var(--font-mono);">{item.date}</span>
            <div class="min-w-0 flex-1">
              <div class="truncate text-[14px] font-semibold text-[var(--foreground)]">{item.title}</div>
              <div class="mt-1 truncate text-[13px] text-[var(--muted-foreground)]">{item.desc}</div>
            </div>
          </div>
        {/each}
      </div>
    </section>
  </div>
</div>
