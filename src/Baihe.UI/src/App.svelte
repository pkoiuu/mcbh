<!--
  功能描述: 白鹤启动器根组件
  技术实现: Svelte 5 runes 语法，$state/$derived 响应式
  注意事项: 顶部预留 44px 给 WebView2 透明标题栏拖拽区域
-->
<script lang="ts">
  import { ipc } from './lib/ipc'

  // 响应式状态: Ping 调用结果
  let result = $state<string>('')
  // 响应式状态: 是否正在加载
  let loading = $state<boolean>(false)
  // 响应式状态: 错误信息
  let error = $state<string>('')

  // 派生状态: 按钮是否可点击
  const disabled = $derived(loading)

  /**
   * 处理 Ping 按钮点击
   * 调用 C# 后端 ipc('ping') 并显示返回结果
   */
  async function handlePing(): Promise<void> {
    loading = true
    error = ''
    result = ''
    try {
      const response = await ipc<string>('ping')
      result = response
    } catch (err) {
      error = err instanceof Error ? err.message : String(err)
    } finally {
      loading = false
    }
  }
</script>

<!-- 顶部 44px 为标题栏拖拽区域，WebView2 中透明 -->
<div
  class="app-titlebar"
  style="-webkit-app-region: drag;"
  aria-hidden="true"
></div>

<!-- 主内容区域 -->
<main class="app-main">
  <div class="app-container">
    <!-- 品牌标题 -->
    <header class="app-header">
      <h1 class="app-title">白鹤服务器</h1>
      <p class="app-subtitle">Baihe Launcher</p>
    </header>

    <!-- Ping 测试区域 -->
    <section class="app-ping">
      <button
        class="app-button"
        {disabled}
        onclick={handlePing}
      >
        {loading ? 'Pinging...' : 'Ping'}
      </button>

      {#if result}
        <p class="app-result app-result-success">
          响应: {result}
        </p>
      {/if}

      {#if error}
        <p class="app-result app-result-error">
          错误: {error}
        </p>
      {/if}
    </section>
  </div>
</main>

<style>
  .app-titlebar {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    height: 44px;
    /* 透明，仅用于 WebView2 标题栏拖拽 */
    background: transparent;
    z-index: 100;
  }

  .app-main {
    height: 100vh;
    width: 100vw;
    display: flex;
    align-items: center;
    justify-content: center;
    /* 顶部预留 44px 标题栏高度 */
    padding-top: 44px;
    background: var(--bg-window);
    backdrop-filter: blur(20px);
    -webkit-backdrop-filter: blur(20px);
  }

  .app-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2rem;
  }

  .app-header {
    text-align: center;
  }

  .app-title {
    margin: 0;
    font-size: 2.5rem;
    font-weight: 600;
    color: var(--text-primary);
    letter-spacing: 0.05em;
  }

  .app-subtitle {
    margin: 0.5rem 0 0;
    font-size: 0.875rem;
    color: var(--text-secondary);
    letter-spacing: 0.1em;
  }

  .app-ping {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
  }

  .app-button {
    padding: 0.625rem 2rem;
    font-size: 0.875rem;
    font-weight: 500;
    color: #ffffff;
    background: var(--brand);
    border: none;
    border-radius: 8px;
    cursor: pointer;
    transition: opacity 0.2s ease, transform 0.1s ease;
  }

  .app-button:hover:not(:disabled) {
    opacity: 0.9;
  }

  .app-button:active:not(:disabled) {
    transform: scale(0.98);
  }

  .app-button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  .app-result {
    margin: 0;
    font-size: 0.875rem;
    padding: 0.5rem 1rem;
    border-radius: 6px;
    max-width: 320px;
    word-break: break-all;
  }

  .app-result-success {
    color: #34c759;
    background: rgba(52, 199, 89, 0.1);
  }

  .app-result-error {
    color: #ff453a;
    background: rgba(255, 69, 58, 0.1);
  }
</style>
