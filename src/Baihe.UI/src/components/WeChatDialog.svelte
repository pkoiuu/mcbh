<!--
  功能描述: 微信名收集弹窗 — 启动器首次启动时强制收集用户微信名
  技术实现: Svelte 5 runes，全屏模态遮罩，无关闭按钮，必须填写才能进入启动器
  注意事项: 弹窗不可关闭，用户必须填写微信名后点击确认才能使用启动器
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc } from '../lib/ipc'
  import { toast } from '../lib/toast.svelte'

  interface Props {
    /** 填写完成回调 */
    ondone?: () => void
  }

  let { ondone }: Props = $props()

  /** 微信名输入值 */
  let wechatName = $state('')

  /** 提交中状态 */
  let submitting = $state(false)

  /** 输入框是否获得过焦点（用于延迟显示错误提示） */
  let touched = $state(false)

  /** 是否可提交 — 非空且不在提交中 */
  const canSubmit = $derived(wechatName.trim().length > 0 && !submitting)

  /** 输入校验错误信息 */
  const errorMessage = $derived(
    touched && wechatName.trim().length === 0 ? '请输入微信名' : '',
  )

  /** 提交微信名 — 保存到本地并通知父组件 */
  async function handleSubmit(): Promise<void> {
    const name = wechatName.trim()
    if (!name) {
      touched = true
      return
    }

    submitting = true
    try {
      await ipc('wechat.set', name)
      toast.success('微信名已保存')
      ondone?.()
    } catch (e: unknown) {
      toast.error(e instanceof Error ? e.message : '保存失败，请重试')
    } finally {
      submitting = false
    }
  }
</script>

<!-- 全屏遮罩 — 阻止用户与弹窗下方内容交互 -->
<div
  class="fixed inset-0 z-[100] flex items-center justify-center"
  style="background: rgba(0, 0, 0, 0.5); backdrop-filter: blur(4px); -webkit-backdrop-filter: blur(4px);"
>
  <!-- 弹窗卡片 -->
  <div
    class="w-full max-w-[420px] rounded-[1.2rem] border border-[var(--border)] bg-[var(--card)] shadow-[var(--shadow-xl)]"
    style="animation: dialog-in 0.25s ease-out;"
  >
    <!-- 头部 — 无关闭按钮 -->
    <div class="px-7 pt-7 pb-2">
      <div class="flex items-center gap-3">
        <div
          class="flex h-10 w-10 items-center justify-center rounded-[0.6rem]"
          style="background: color-mix(in srgb, var(--primary) 12%, transparent);"
        >
          <Icon name="user" size={20} class="text-[var(--primary)]" />
        </div>
        <div>
          <h2 class="text-[20px] font-semibold tracking-[-0.01em] text-[var(--foreground)]">
            欢迎使用白鹤启动器
          </h2>
          <p class="mt-0.5 text-[13px] text-[var(--muted-foreground)]">首次使用需要填写微信名</p>
        </div>
      </div>
    </div>

    <!-- 内容区 -->
    <div class="px-7 py-5">
      <p class="mb-4 text-[14px] leading-relaxed text-[var(--muted-foreground)]">
        为了更好地服务社区成员，请填写您的微信名。此信息仅用于身份识别，不会用于其他用途。
      </p>

      <!-- 微信名输入 -->
      <div>
        <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="wechat-name">
          微信名
        </label>
        <div class="relative">
          <span
            class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--muted-foreground)]"
            aria-hidden="true"
          >
            <Icon name="user" size={16} />
          </span>
          <input
            id="wechat-name"
            type="text"
            class="w-full border border-[var(--border)] bg-[var(--accent)] py-2.5 pl-10 pr-4 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors placeholder:text-[var(--muted-foreground)] focus:border-[var(--primary)]"
            placeholder="请输入您的微信名"
            bind:value={wechatName}
            maxlength={32}
            autocomplete="off"
            aria-label="微信名"
            onfocus={() => { touched = true }}
            onkeydown={(e) => {
              if (e.key === 'Enter' && canSubmit) handleSubmit()
            }}
          />
        </div>
        {#if errorMessage}
          <p class="mt-1.5 text-[12px] text-[var(--destructive)]">{errorMessage}</p>
        {/if}
      </div>
    </div>

    <!-- 底部操作区 — 仅确认按钮，无取消/关闭 -->
    <div class="px-7 pb-7 pt-1">
      <button
        type="button"
        class="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[0.5rem] bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        disabled={!canSubmit}
        onclick={handleSubmit}
      >
        {#if submitting}
          <span
            class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent"
            aria-hidden="true"
          ></span>
        {/if}
        <span>{submitting ? '保存中...' : '确认'}</span>
      </button>
      <p class="mt-3 text-center text-[12px] text-[var(--muted-foreground)]">
        填写后不可修改，请确认填写正确
      </p>
    </div>
  </div>
</div>

<style>
  @keyframes dialog-in {
    from {
      opacity: 0;
      transform: scale(0.95) translateY(8px);
    }
    to {
      opacity: 1;
      transform: scale(1) translateY(0);
    }
  }
</style>
