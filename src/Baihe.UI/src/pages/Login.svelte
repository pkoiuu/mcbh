<!--
  功能描述: 登录页 — 离线模式 / 微软正版 / 第三方验证 三种登录方式
  技术实现: Svelte 5 runes，通过 IPC 调用后端认证，监听设备码与登录结果推送事件
  注意事项: 微软登录采用设备码流程，后端通过 auth.msDeviceCode / auth.msLoginResult 事件推送状态
-->
<script lang="ts">
  import Icon from '../lib/Icon.svelte'
  import { ipc, on } from '../lib/ipc'
  import { router } from '../lib/router.svelte'
  import { toast } from '../lib/toast.svelte'

  /** 登录方式 Tab */
  type LoginTab = 'offline' | 'microsoft' | 'thirdparty'
  let activeTab = $state<LoginTab>('offline')

  /** Tab 配置 */
  const tabs: { key: LoginTab; label: string }[] = [
    { key: 'offline', label: '离线模式' },
    { key: 'microsoft', label: '微软正版' },
    { key: 'thirdparty', label: '第三方验证' },
  ]

  // ===== 离线模式状态 =====
  let offlineUsername = $state('')
  let offlineLoading = $state(false)

  // ===== 微软登录状态 =====
  /** 微软登录流程状态: idle 空闲 | waiting 等待设备码/验证 | refreshing 刷新令牌中 */
  type MsStatus = 'idle' | 'waiting' | 'refreshing'
  let msStatus = $state<MsStatus>('idle')
  let msUserCode = $state('')
  let msVerificationUri = $state('')

  // ===== 第三方验证状态 =====
  /** 第三方服务器选择: littleskin 预设 | custom 自定义 */
  let thirdPartyServer = $state<'littleskin' | 'custom'>('littleskin')
  let customServerUrl = $state('')
  let thirdPartyUsername = $state('')
  let thirdPartyPassword = $state('')
  let thirdPartyLoading = $state(false)

  /** LittleSkin 预设 Yggdrasil API 地址 */
  const LITTLESKIN_URL = 'https://littleskin.cn/api/yggdrasil'

  /** 离线登录是否可提交 */
  const canOfflineLogin = $derived(offlineUsername.trim().length > 0 && !offlineLoading)

  /** 第三方登录是否可提交 */
  const canThirdPartyLogin = $derived(
    thirdPartyUsername.trim().length > 0 &&
      thirdPartyPassword.length > 0 &&
      (thirdPartyServer === 'littleskin' || customServerUrl.trim().length > 0) &&
      !thirdPartyLoading,
  )

  /** 复制文本到剪贴板 */
  async function copyToClipboard(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text)
      toast.success('已复制到剪贴板')
    } catch {
      toast.error('复制失败，请手动复制')
    }
  }

  /** 离线登录 — 直接使用用户名，无需验证 */
  async function handleOfflineLogin(): Promise<void> {
    const name = offlineUsername.trim()
    if (!name) {
      toast.error('请输入游戏用户名')
      return
    }
    if (name.length > 16) {
      toast.error('用户名最多 16 个字符')
      return
    }
    offlineLoading = true
    try {
      await ipc<{ username: string; isUserSet: boolean }>('auth.setOffline', name)
      toast.success('登录成功')
      router.navigate('home')
    } catch (e: unknown) {
      toast.error(e instanceof Error ? e.message : '登录失败')
    } finally {
      offlineLoading = false
    }
  }

  /** 开始微软登录 — 后端会通过事件推送设备码与登录结果 */
  function handleMsLogin(): void {
    msStatus = 'waiting'
    msUserCode = ''
    msVerificationUri = ''
    ipc<{ success: boolean; error?: string }>('auth.msLogin').catch((e: unknown) => {
      msStatus = 'idle'
      toast.error(e instanceof Error ? e.message : '启动微软登录失败')
    })
  }

  /** 取消微软登录 — 重置 UI 状态并通知后端（若后端支持） */
  function handleMsCancel(): void {
    msStatus = 'idle'
    msUserCode = ''
    msVerificationUri = ''
    ipc('auth.msCancel').catch(() => {
      /* 后端未实现取消时静默处理，UI 已重置 */
    })
  }

  /** 第三方验证服务器登录 */
  async function handleThirdPartyLogin(): Promise<void> {
    const serverUrl =
      thirdPartyServer === 'littleskin' ? LITTLESKIN_URL : customServerUrl.trim()
    const username = thirdPartyUsername.trim()
    const password = thirdPartyPassword

    if (thirdPartyServer === 'custom' && !serverUrl) {
      toast.error('请输入服务器地址')
      return
    }
    if (!username) {
      toast.error('请输入用户名或邮箱')
      return
    }
    if (!password) {
      toast.error('请输入密码')
      return
    }

    thirdPartyLoading = true
    try {
      const result = await ipc<{ success: boolean; error?: string; username?: string }>(
        'auth.thirdPartyLogin',
        { serverUrl, username, password },
      )
      if (result.success) {
        toast.success('登录成功')
        router.navigate('home')
      } else {
        toast.error(result.error || '登录失败')
      }
    } catch (e: unknown) {
      toast.error(e instanceof Error ? e.message : '登录失败')
    } finally {
      thirdPartyLoading = false
    }
  }

  /** 注册微软登录推送事件监听器 — 组件卸载时自动清理 */
  $effect(() => {
    // 设备码推送 — 显示用户码与验证链接
    const offDeviceCode = on('auth.msDeviceCode', (data) => {
      const evt = data as { userCode: string; verificationUri: string }
      msUserCode = evt.userCode
      msVerificationUri = evt.verificationUri
    })

    // 登录结果推送 — 成功则刷新令牌后跳转，失败则回到初始态
    const offLoginResult = on('auth.msLoginResult', (data) => {
      const evt = data as { success: boolean; error?: string; username?: string }
      if (evt.success) {
        // 进入刷新令牌状态，短暂展示后返回主页
        msStatus = 'refreshing'
        if (evt.username) {
          toast.success(`欢迎，${evt.username}`)
        }
        setTimeout(() => router.navigate('home'), 900)
      } else {
        msStatus = 'idle'
        msUserCode = ''
        msVerificationUri = ''
        toast.error(evt.error || '登录失败')
      }
    })

    return () => {
      offDeviceCode()
      offLoginResult()
    }
  })
</script>

<div class="flex h-full flex-col items-center justify-center overflow-y-auto bg-[var(--background-100)] p-8">
  <div class="w-full max-w-[460px]">
      <!-- 顶部标题区 -->
      <header class="mb-6">
        <h1 class="text-[30px] font-semibold leading-tight tracking-[-0.02em] text-[var(--foreground)]">
          登录账户
        </h1>
        <p class="mt-1.5 text-[15px] text-[var(--muted-foreground)]">选择登录方式开始游戏</p>
      </header>

      <!-- 登录卡片 -->
      <section class="rounded-[1rem] border border-[var(--border)] bg-[var(--card)] shadow-[var(--shadow-sm)]">
        <!-- Tab 切换 -->
        <div class="flex border-b border-[var(--border)]">
          {#each tabs as t (t.key)}
            <button
              type="button"
              class="relative flex-1 px-3 py-3.5 text-[14px] font-medium transition-colors {activeTab === t.key
                ? 'text-[var(--primary)]'
                : 'text-[var(--muted-foreground)] hover:text-[var(--foreground)]'}"
              onclick={() => (activeTab = t.key)}
            >
              {t.label}
              {#if activeTab === t.key}
                <span
                  class="absolute inset-x-4 bottom-[-1px] h-[2px] rounded-full bg-[var(--primary)]"
                  aria-hidden="true"
                ></span>
              {/if}
            </button>
          {/each}
        </div>

        <!-- Tab 内容区 -->
        <div class="p-6">
          <!-- 离线模式 -->
          {#if activeTab === 'offline'}
            <div class="flex flex-col gap-4">
              <div>
                <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="offline-name">
                  用户名
                </label>
                <div class="relative">
                  <span
                    class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--muted-foreground)]"
                    aria-hidden="true"
                  >
                    <Icon name="user" size={16} />
                  </span>
                  <input
                    id="offline-name"
                    type="text"
                    class="w-full border border-[var(--border)] bg-[var(--accent)] py-2.5 pl-10 pr-4 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors placeholder:text-[var(--muted-foreground)] focus:border-[var(--primary)]"
                    placeholder="输入游戏用户名"
                    bind:value={offlineUsername}
                    maxlength={16}
                    autocomplete="off"
                    aria-label="游戏用户名"
                    onkeydown={(e) => e.key === 'Enter' && handleOfflineLogin()}
                  />
                </div>
              </div>

              <button
                type="button"
                class="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[0.5rem] bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                disabled={!canOfflineLogin}
                onclick={handleOfflineLogin}
              >
                {#if offlineLoading}
                  <span
                    class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent"
                    aria-hidden="true"
                  ></span>
                {/if}
                <span>{offlineLoading ? '登录中...' : '登录'}</span>
              </button>

              <p class="flex items-start gap-1.5 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
                <span class="mt-0.5 shrink-0"><Icon name="info" size={14} /></span>
                <span>离线模式无需验证，直接使用用户名登录</span>
              </p>
            </div>

          <!-- 微软正版 -->
          {:else if activeTab === 'microsoft'}
            <div class="flex flex-col gap-4">
              {#if msStatus === 'idle'}
                <button
                  type="button"
                  class="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[0.5rem] bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2"
                  onclick={handleMsLogin}
                >
                  <Icon name="circle-play" size={18} />
                  <span>使用微软账户登录</span>
                </button>

                <p class="flex items-start gap-1.5 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
                  <span class="mt-0.5 shrink-0"><Icon name="info" size={14} /></span>
                  <span>将打开微软登录页面，输入上方代码完成验证</span>
                </p>
              {:else}
                <!-- 设备码信息区 -->
                <div class="rounded-[0.5rem] border border-[var(--border)] bg-[var(--accent)] p-5">
                  {#if msStatus === 'waiting' && msUserCode}
                    <!-- 验证链接 -->
                    <div class="text-[12px] font-medium text-[var(--muted-foreground)]">验证链接</div>
                    <a
                      href={msVerificationUri}
                      target="_blank"
                      rel="noopener noreferrer"
                      class="mt-1 block break-all text-[14px] font-medium text-[var(--primary)] transition-[opacity] hover:opacity-80"
                      style="font-family: var(--font-mono);"
                    >
                      {msVerificationUri}
                    </a>

                    <!-- 用户码 -->
                    <div class="mt-4 text-[12px] font-medium text-[var(--muted-foreground)]">用户码</div>
                    <div class="mt-1.5 flex items-center justify-between gap-3">
                      <span
                        class="select-all text-[28px] font-bold tracking-[0.15em] text-[var(--foreground)]"
                        style="font-family: var(--font-mono);"
                      >
                        {msUserCode}
                      </span>
                      <button
                        type="button"
                        class="inline-flex shrink-0 items-center gap-1.5 rounded-[0.4rem] border border-[var(--border)] bg-[var(--card)] px-3 py-1.5 text-[12px] font-medium text-[var(--foreground)] transition-colors hover:border-[var(--primary)] hover:text-[var(--primary)]"
                        onclick={() => copyToClipboard(msUserCode)}
                      >
                        复制
                      </button>
                    </div>

                    <!-- 等待验证状态 -->
                    <div class="mt-4 flex items-center gap-2 text-[13px] text-[var(--muted-foreground)]">
                      <span
                        class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent"
                        aria-hidden="true"
                      ></span>
                      <span>等待验证中...</span>
                    </div>

                    <!-- 取消按钮 -->
                    <button
                      type="button"
                      class="mt-4 inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-[0.5rem] border border-[var(--border)] bg-[var(--card)] px-4 text-[13px] font-medium text-[var(--muted-foreground)] transition-colors hover:border-[var(--destructive)] hover:text-[var(--destructive)]"
                      onclick={handleMsCancel}
                    >
                      取消
                    </button>
                  {:else if msStatus === 'waiting'}
                    <!-- 已发起请求，尚未收到设备码 -->
                    <div class="flex items-center gap-2 text-[13px] text-[var(--muted-foreground)]">
                      <span
                        class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent"
                        aria-hidden="true"
                      ></span>
                      <span>正在获取设备码...</span>
                    </div>
                  {:else if msStatus === 'refreshing'}
                    <!-- 刷新令牌中 -->
                    <div class="flex items-center gap-2 text-[14px] font-medium text-[var(--foreground)]">
                      <span
                        class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent"
                        aria-hidden="true"
                      ></span>
                      <span>正在刷新登录状态...</span>
                    </div>
                  {/if}
                </div>

                {#if msStatus === 'waiting'}
                  <p class="flex items-start gap-1.5 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
                    <span class="mt-0.5 shrink-0"><Icon name="info" size={14} /></span>
                    <span>将打开微软登录页面，输入上方代码完成验证</span>
                  </p>
                {/if}
              {/if}
            </div>

          <!-- 第三方验证 -->
          {:else}
            <div class="flex flex-col gap-4">
              <!-- 服务器选择 -->
              <div>
                <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="tp-server">
                  验证服务器
                </label>
                <div class="relative">
                  <select
                    id="tp-server"
                    class="w-full appearance-none border border-[var(--border)] bg-[var(--accent)] py-2.5 pl-4 pr-10 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors focus:border-[var(--primary)]"
                    bind:value={thirdPartyServer}
                    aria-label="验证服务器"
                  >
                    <option value="littleskin" class="bg-[var(--card)] text-[var(--foreground)]">LittleSkin</option>
                    <option value="custom" class="bg-[var(--card)] text-[var(--foreground)]">自定义</option>
                  </select>
                  <span
                    class="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[var(--muted-foreground)]"
                    aria-hidden="true"
                  >
                    <svg width="12" height="12" viewBox="0 0 12 12" fill="none" xmlns="http://www.w3.org/2000/svg">
                      <path d="M3 4.5L6 7.5L9 4.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                  </span>
                </div>
              </div>

              <!-- 自定义服务器地址（仅选择"自定义"时显示） -->
              {#if thirdPartyServer === 'custom'}
                <div>
                  <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="tp-url">
                    服务器地址
                  </label>
                  <input
                    id="tp-url"
                    type="text"
                    class="w-full border border-[var(--border)] bg-[var(--accent)] px-4 py-2.5 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors placeholder:text-[var(--muted-foreground)] focus:border-[var(--primary)]"
                    placeholder="https://example.com/api/yggdrasil"
                    bind:value={customServerUrl}
                    autocomplete="off"
                    aria-label="自定义服务器地址"
                  />
                </div>
              {/if}

              <!-- 用户名 / 邮箱 -->
              <div>
                <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="tp-user">
                  用户名 / 邮箱
                </label>
                <div class="relative">
                  <span
                    class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--muted-foreground)]"
                    aria-hidden="true"
                  >
                    <Icon name="user" size={16} />
                  </span>
                  <input
                    id="tp-user"
                    type="text"
                    class="w-full border border-[var(--border)] bg-[var(--accent)] py-2.5 pl-10 pr-4 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors placeholder:text-[var(--muted-foreground)] focus:border-[var(--primary)]"
                    placeholder="输入用户名或邮箱"
                    bind:value={thirdPartyUsername}
                    autocomplete="off"
                    aria-label="用户名或邮箱"
                    onkeydown={(e) => e.key === 'Enter' && handleThirdPartyLogin()}
                  />
                </div>
              </div>

              <!-- 密码 -->
              <div>
                <label class="mb-1.5 block text-[13px] font-medium text-[var(--foreground)]" for="tp-pass">
                  密码
                </label>
                <input
                  id="tp-pass"
                  type="password"
                  class="w-full border border-[var(--border)] bg-[var(--accent)] px-4 py-2.5 text-[14px] text-[var(--foreground)] rounded-[0.5rem] outline-none transition-colors placeholder:text-[var(--muted-foreground)] focus:border-[var(--primary)]"
                  placeholder="输入密码"
                  bind:value={thirdPartyPassword}
                  autocomplete="off"
                  aria-label="密码"
                  onkeydown={(e) => e.key === 'Enter' && handleThirdPartyLogin()}
                />
              </div>

              <button
                type="button"
                class="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[0.5rem] bg-[var(--primary)] px-6 text-[15px] font-semibold text-[var(--primary-foreground)] shadow-[var(--shadow-sm)] transition-[filter] hover:brightness-[0.96] focus-visible:outline-2 focus-visible:outline-[var(--ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                disabled={!canThirdPartyLogin}
                onclick={handleThirdPartyLogin}
              >
                {#if thirdPartyLoading}
                  <span
                    class="inline-block h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent"
                    aria-hidden="true"
                  ></span>
                {/if}
                <span>{thirdPartyLoading ? '登录中...' : '登录'}</span>
              </button>

              <p class="flex items-start gap-1.5 text-[12px] leading-relaxed text-[var(--muted-foreground)]">
                <span class="mt-0.5 shrink-0"><Icon name="info" size={14} /></span>
                <span>使用第三方验证服务器登录 (如 LittleSkin)</span>
              </p>
            </div>
          {/if}
        </div>
      </section>
    </div>
</div>
