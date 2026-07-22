<!--
  功能描述: 窗口外壳 — 仅渲染内容区域
  技术实现: 标题栏和交通灯按钮已迁移至 WPF 原生 MainWindow.xaml，前端不再负责窗口控制
  注意事项: 此组件仅作为内容容器，填满 WebView2 视口
-->
<script lang="ts">
  import type { Snippet } from 'svelte'

  interface Props {
    children: Snippet
  }

  let { children }: Props = $props()
</script>

<div class="flex h-full w-full overflow-hidden bg-[var(--background)]">
  <div class="flex min-h-0 flex-1">
    {@render children()}
  </div>
</div>
