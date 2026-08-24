import type { WikiCategory } from './types'

/**
 * 反作弊提醒（GrimAC）— 指南第八章
 */
export const anticheatCategory: WikiCategory = {
  id: 'anticheat',
  title: '反作弊提醒（GrimAC）',
  intro: '服务器装了 GrimAC 反作弊，会自动检测作弊行为。',
  pages: [
    {
      id: 'ac-notice',
      title: '注意事项',
      summary: '别开挂，被误判找管理员',
      blocks: [
        {
          kind: 'tip',
          title: '注意点',
          lines: [
            '不要使用任何外挂、作弊客户端、自动连点器、飞行/透视/加速等，会被自动封禁或踢出。',
            '若你没有作弊却被误判（踢出/封禁），请截图报错提示，联系管理员核对解除。',
            '高延迟玩家偶尔触发误判属正常，管理员可帮你调整。',
          ],
        },
      ],
    },
  ],
}
