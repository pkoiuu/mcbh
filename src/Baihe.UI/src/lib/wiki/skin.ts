import type { WikiCategory } from './types'

/**
 * 换肤（SkinsRestorer）— 指南第四章
 * 自带 GUI，普通玩家默认可用
 */
export const skinCategory: WikiCategory = {
  id: 'skin',
  title: '换肤（SkinsRestorer）',
  intro: '换肤自带图形界面（GUI），普通玩家默认可用，无需授权。',
  pages: [
    {
      id: 'skin-commands',
      title: '换肤指令',
      summary: 'GUI 与指令两种换肤方式',
      blocks: [
        {
          kind: 'table',
          headers: ['指令', '作用'],
          rows: [
            ['/skins', '打开换肤 GUI（图形菜单，点选皮肤）'],
            ['/skin <正版玩家名>', '直接换成该正版玩家的皮肤'],
            ['/skin url <图片链接>', '用网上的 PNG 皮肤'],
            ['/skin clear', '恢复默认皮肤'],
            ['/skin favourites', '查看/管理收藏的皮肤'],
            ['/skin history', '查看最近用过的皮肤'],
          ],
        },
        {
          kind: 'tip',
          title: '注意点',
          lines: [
            '/skins 打开的 GUI 里含多个菜单：皮肤选择、历史、收藏。',
            '只能获取正版账号的皮肤；随便填一个不存在的名字会提示找不到皮肤。',
            '换肤不影响账号数据，纯外观变化。',
          ],
        },
      ],
    },
  ],
}
