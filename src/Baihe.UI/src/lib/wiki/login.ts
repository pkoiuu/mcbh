import type { WikiCategory } from './types'

/**
 * 进服与登录 — 指南第一章
 * AuthMe（账号+密码）+ FlexLoginUI（图形界面）
 */
export const loginCategory: WikiCategory = {
  id: 'login',
  title: '进服与登录',
  intro: '本服开启登录验证：底层是 AuthMe（账号+密码），前端是 FlexLoginUI（图形界面）。GUI 只是把「在聊天框打指令」换成了「点开界面输密码」，本质仍是密码登录。',
  pages: [
    {
      id: 'login-flow',
      title: '实际进服流程（GUI 为主）',
      summary: '首次进服注册、之后进服登录的界面操作',
      blocks: [
        {
          kind: 'table',
          caption: '进服场景与操作',
          headers: ['场景', '你会看到什么', '怎么做'],
          rows: [
            ['首次进服', '自动弹出「注册」界面（标题 + 密码框 + 确认密码框 + 注册按钮）', '设置密码 → 再次确认 → 点「注册」'],
            ['之后每次进服', '自动弹出「登录」界面（标题 + 密码框 + 登录按钮）', '输入密码 → 点「登录」'],
          ],
        },
        {
          kind: 'text',
          lines: [
            '界面已设为中文（「登录」「请输入密码」「密码」「登录」按钮等）。',
            '界面被关掉后，可用 /logui（重开登录页）、/regui（重开注册页）呼出。',
          ],
        },
      ],
    },
    {
      id: 'login-commands',
      title: '指令兜底（仍可用）',
      summary: 'GUI 之外的 AuthMe 备用指令',
      blocks: [
        {
          kind: 'table',
          caption: 'AuthMe 指令',
          headers: ['指令', '作用'],
          rows: [
            ['/login <密码>', '指令登录'],
            ['/register <密码> <重复密码>', '指令注册'],
            ['/changepassword <旧密码> <新密码>', '修改密码'],
            ['/logui', '重新打开登录 GUI'],
            ['/regui', '重新打开注册 GUI'],
          ],
        },
        {
          kind: 'tip',
          title: '登录注意点',
          lines: [
            '未登录前无法移动、交互、说话（只能操作登录/注册界面或相关指令）。',
            '登录有超时限制，进服后请尽快登录，超时会被踢出（重进再登录即可）。',
            '密码请牢记且不要告诉他人；强度不足时注册会被拒绝。',
            '忘记密码：只能联系管理员重置，无法自助找回。',
            '配置里预留了「验证码（captcha）」功能，但当前未启用，登录只需账号+密码。',
          ],
        },
      ],
    },
  ],
}
