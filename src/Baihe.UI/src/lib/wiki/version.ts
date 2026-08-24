import type { WikiCategory } from './types'

/**
 * 跨版本进服（Via 系列）— 指南第六章
 * ViaVersion / ViaBackwards / ViaRewind
 */
export const versionCategory: WikiCategory = {
  id: 'version',
  title: '跨版本进服说明',
  intro: '服务器安装了 ViaVersion / ViaBackwards / ViaRewind，支持老版本客户端进服。',
  pages: [
    {
      id: 'via-clients',
      title: '客户端版本支持',
      summary: '不同版本客户端的进服情况',
      blocks: [
        {
          kind: 'table',
          headers: ['你能用的客户端版本', '能否进服', '说明'],
          rows: [
            ['1.21.8', '原生直连', '体验最完整'],
            ['1.21.3 ~ 1.21.7', '能进', '靠 ViaBackwards 协议转换'],
            ['更老的版本（1.8~1.20）', '能进', '靠 ViaBackwards + ViaRewind'],
          ],
        },
        {
          kind: 'tip',
          title: '注意点',
          lines: [
            '强烈建议用 1.21.8 客户端，所有新内容正常显示、最稳定。',
            '用老客户端进服时，1.21.4 之后新增的方块/物品会显示成未知/缺失材质，不影响基础生存，但新内容看不全。',
          ],
        },
      ],
    },
  ],
}
