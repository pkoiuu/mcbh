import type { WikiCategory } from './types'

/**
 * 坐下（GSit）— 指南第三章
 * 纯服务端生效，无需客户端模组
 */
export const sitCategory: WikiCategory = {
  id: 'sit',
  title: '坐下（GSit）',
  intro: '无需任何客户端模组，纯服务端生效。以下所有坐下/姿势功能对普通玩家默认开放，直接可用。',
  pages: [
    {
      id: 'sit-actions',
      title: '坐下与姿势操作',
      summary: 'GSit 全部默认开放的操作',
      blocks: [
        {
          kind: 'table',
          headers: ['操作', '说明'],
          rows: [
            ['输入 /sit', '原地坐下'],
            ['空手右键 楼梯 / 台阶 / 地毯 / 板材 / 雪', '直接坐到方块上（地毯就是「坐垫」）'],
            ['空手右键 玩家 / NPC', '坐到别人头上'],
            ['/lay', '躺下'],
            ['/layback', '后仰躺'],
            ['/bellyflop', '趴下'],
            ['/spin', '旋转姿势'],
            ['/crawl', '爬行'],
            ['按 Shift（下蹲）', '站起'],
          ],
        },
        {
          kind: 'tip',
          title: '注意点',
          lines: [
            '只有配置为「可坐」的方块才能右键坐（默认支持楼梯、台阶、地毯、羊毛毯、雪等）。',
            '坐/躺期间部分指令被禁用（如 /skin、/nick），站起后再用即可。',
            '普通玩家无需任何授权即可使用以上全部功能；只有踢人（/sitkick）、绕过限制、重载等少数属于管理员。',
          ],
        },
      ],
    },
  ],
}
