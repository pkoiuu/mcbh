<!--
  功能描述: 启动主页 — 欢迎区 + 当前实例卡片 + 快捷工具 + 新闻列表
  技术实现: Svelte 5 runes，Tailwind CSS 还原设计稿布局
  注意事项: 启动按钮后续接入 IPC launch.start，当前为静态 UI
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'

  // 当前实例信息（后续从 IPC 获取）
  let instanceName = $state('1.20.4 · Fabric')
  let instanceVersion = $state('1.20.4')
  let lastPlayed = $state('3 天前')
  let modCount = $state(24)
  let launchTime = $state('8s')

  // 启动状态
  let isLaunching = $state(false)

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

  /** 处理启动按钮点击 */
  function handleLaunch(): void {
    isLaunching = true
    // TODO: Stage 2 接入 ipc.call('launch.start', { instanceId, quickPlay: true })
    setTimeout(() => {
      isLaunching = false
    }, 2000)
  }
</script>

<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background-100)] p-8">
  <div class="flex flex-col gap-8">
    <!-- 1. 欢迎标题区 -->
    <section>
      <div class="text-[13px] font-semibold tracking-wide text-[var(--muted-foreground)]">欢迎回来</div>
      <h1 class="mt-1.5 text-[30px] font-semibold leading-tight tracking-[-0.02em] text-[var(--foreground)]" style="text-wrap: balance;">
        开始你的冒险
      </h1>
      <p class="mt-1.5 text-[15px] text-[var(--muted-foreground)]">选择实例，启程前往方块世界</p>
    </section>

    <!-- 2. 当前实例大卡片 -->
    <section>
      <article class="flex items-center gap-5 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)]">
        <!-- 缩略图 -->
        <div class="flex h-16 w-16 shrink-0 items-center justify-center rounded-[12px] bg-[var(--background-200)] text-[var(--muted-foreground)]">
          <Icon name="box" size={28} />
        </div>
        <!-- 中间: 实例名 + meta -->
        <div class="min-w-0 flex-1">
          <h2 class="truncate text-[18px] font-semibold text-[var(--foreground)]">{instanceName}</h2>
          <div class="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[13px] text-[var(--muted-foreground)]">
            <span class="whitespace-nowrap" style="font-family: var(--font-mono);">{instanceVersion}</span>
            <span aria-hidden="true">·</span>
            <span class="whitespace-nowrap">最后游玩 {lastPlayed}</span>
            <span aria-hidden="true">·</span>
            <span class="whitespace-nowrap">已安装 {modCount} 个 Mod</span>
          </div>
        </div>
        <!-- 右侧: 启动按钮 + 预计耗时 -->
        <div class="flex shrink-0 flex-col items-end gap-1.5">
          <button
            type="button"
            class="inline-flex h-11 items-center gap-2 whitespace-nowrap rounded-full bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2 disabled:opacity-50"
            disabled={isLaunching}
            onclick={handleLaunch}
          >
            <Icon name="circle-play" size={18} />
            <span>{isLaunching ? '启动中...' : '启动游戏'}</span>
          </button>
          <span class="text-[12px] text-[var(--muted-foreground)]">预计启动耗时 {launchTime}</span>
        </div>
      </article>
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
        {#each news as item, i (item.title)}
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
