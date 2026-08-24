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
      summary: '向管理员索要地图地址即可',
      blocks: [
        {
          kind: 'text',
          lines: [
            '游戏内无需任何指令，请向管理员索要地图网页地址（通常是服务器公网地址 + 对应端口）。',
            '网页地图可看玩家位置、建筑、地形，方便找路。',
          ],
        },
      ],
    },
  ],
}
