<!--
  功能描述: 通用图标组件 — 从设计稿 SVG 文件内联渲染
  技术实现: 使用 Vite import.meta.glob 加载所有 SVG，通过 name 属性查找
  注意事项: SVG 使用 stroke="currentColor"，颜色继承自父元素
-->
<script lang="ts">
  // 使用 Vite 的 import.meta.glob 以 ?raw 模式加载所有 SVG 图标
  const iconModules = import.meta.glob('./icons/*.svg', {
    query: '?raw',
    import: 'default',
    eager: true,
  })

  // 图标名称到 SVG 内容的映射
  const iconMap: Record<string, string> = {}

  // 构建 图标名 → SVG 内容 映射表
  for (const [path, content] of Object.entries(iconModules)) {
    // 从路径提取图标名: ./icons/image_1_tgt36j.svg → image_1
    const match = path.match(/image_(\d+)/)
    if (match) {
      iconMap[match[0]] = content as string
    }
  }

  // 图标语义别名映射
  const aliasMap: Record<string, string> = {
    user: 'image_0',
    'circle-play': 'image_1',
    'arrow-down': 'image_2',
    grip: 'image_3',
    box: 'image_4',
    plus: 'image_5',
    upload: 'image_6',
    package: 'image_7',
    search: 'image_8',
    'check-circle': 'image_9',
    palette: 'image_10',
    info: 'image_11',
    download: 'image_12',
  }

  interface Props {
    name: string
    size?: number
    class?: string
  }

  let { name, size = 18, class: className = '' }: Props = $props()

  // 派生: 解析图标名（支持别名或直接使用 image_N）
  const resolvedKey = $derived(aliasMap[name] ?? name)
  // 派生: 获取 SVG 内容
  const svgContent = $derived(iconMap[resolvedKey] ?? '')
</script>

{#if svgContent}
  <!-- 内联渲染 SVG，通过 currentColor 继承颜色 -->
  <span
    class="inline-flex items-center justify-center {className}"
    style="width: {size}px; height: {size}px; line-height: 0;"
    {...{ innerHTML: svgContent }}
  ></span>
{:else}
  <!-- 图标未找到时的占位 -->
  <span class="inline-block {className}" style="width: {size}px; height: {size}px;"></span>
{/if}

<style>
  :global(span[data-icon] svg) {
    width: 100%;
    height: 100%;
  }
</style>
