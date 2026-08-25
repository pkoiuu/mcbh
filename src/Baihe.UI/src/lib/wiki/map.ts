import type { WikiCategory } from './types'

/**
 * 网页地图（BlueMap）— 指南第七章
 */
export const mapCategory: WikiCategory = {
  id: 'map',
  title: '网页地图（BlueMap）',
  intro: '服务器装了 BlueMap 网页地图，可以在浏览器里实时查看世界（三维地图）。',
  pages: [
    {
      id: 'map-usage',
      title: '如何使用',
      summary: '浏览器直接打开地图地址即可',
      blocks: [
        {
          kind: 'tip',
          title: '地图地址',
          lines: [
            'http://map.hhj520.top（浏览器直接打开，实时查看世界）',
            '可看玩家位置、建筑、地形，方便找路。',
          ],
        },
        {
          kind: 'text',
          lines: [
            '游戏内无需任何指令，网页地图支持查看玩家位置、建筑、地形，方便找路。',
            '如果地址无法访问，请联系管理员获取最新地图地址。',
          ],
        },
      ],
    },
  ],
}
