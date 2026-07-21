<!--
  功能描述: 工具页 — 工具卡片网格
  技术实现: Svelte 5 runes，静态工具列表，后续接入功能实现
  注意事项: 设计稿未提供工具页，基于启动页快捷工具网格扩展设计
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'

  /** 工具列表 */
  const tools = [
    { id: 'new-instance', icon: 'plus', title: '新建实例', desc: '配置版本与加载器，创建全新游戏实例' },
    { id: 'import-save', icon: 'upload', title: '导入存档', desc: '从本地或备份恢复存档数据' },
    { id: 'backup', icon: 'box', title: '备份管理', desc: '查看与恢复游戏快照' },
    { id: 'mods', icon: 'package', title: 'Mod 管理', desc: '启用、排序与更新已安装的 Mod' },
    { id: 'screenshots', icon: 'box', title: '截图管理', desc: '查看、导出和分享游戏截图' },
    { id: 'saves', icon: 'box', title: '存档管理', desc: '浏览、备份和迁移游戏存档' },
  ]
</script>

<div class="min-h-0 flex-1 overflow-y-auto bg-[var(--background-100)] p-8">
  <!-- 标题 -->
  <div class="mb-6">
    <h1 class="text-[26px] font-semibold leading-tight text-[var(--foreground)]" style="text-wrap: balance;">工具箱</h1>
    <p class="mt-1 text-sm text-[var(--muted-foreground)]">管理游戏实例、存档和 Mod</p>
  </div>

  <!-- 工具网格 -->
  <div class="grid grid-cols-3 gap-4">
    {#each tools as tool (tool.id)}
      <a
        href="#"
        class="group flex flex-col gap-3 rounded-[var(--radius)] border border-[var(--border)] bg-[var(--card)] p-6 shadow-[var(--shadow-sm)] transition-[box-shadow,transform] hover:-translate-y-0.5 hover:shadow-[var(--shadow-md)]"
        onclick={(e) => e.preventDefault()}
      >
        <div class="flex h-12 w-12 items-center justify-center rounded-[12px] bg-[var(--background-200)]">
          <Icon name={tool.icon} size={24} class="text-[var(--primary)]" />
        </div>
        <div>
          <div class="text-[15px] font-semibold text-[var(--foreground)]">{tool.title}</div>
          <div class="mt-1 text-[13px] text-[var(--muted-foreground)]">{tool.desc}</div>
        </div>
      </a>
    {/each}
  </div>
</div>
