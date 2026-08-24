import type { WikiCategory } from './types'

/**
 * 基础生存指令 — 指南第二章
 * EssentialsX，已按 LuckPerms default 组核实
 */
export const commandsCategory: WikiCategory = {
  id: 'commands',
  title: '基础生存指令',
  intro: '服务器使用 EssentialsX。下面按「普通玩家可用」与「未授权」分开展示，均已结合服务器实际权限配置（default 组）核实。',
  pages: [
    {
      id: 'cmds-allowed',
      title: '普通玩家「可用」的指令',
      summary: 'default 组已授权，直接就能用',
      blocks: [
        {
          kind: 'table',
          caption: '已授权指令（直接可用）',
          headers: ['指令', '作用'],
          rows: [
            ['/sethome [名称]', '设置家的位置（可命名，本服已开无限个家）'],
            ['/home [名称]', '传送到家'],
            ['/delhome [名称]', '删除家'],
            ['/spawn', '传送回出生点'],
            ['/tpa <玩家>', '请求传送到某玩家身边'],
            ['/tpahere <玩家>', '请求某玩家传送到你身边'],
            ['/tpaccept / /tpdeny', '接受 / 拒绝传送请求'],
            ['/tpcancel', '取消自己发出的传送请求'],
            ['/msg <玩家> <内容>', '私聊'],
            ['/r <内容>', '快速回复最近私聊你的人'],
            ['/pay <玩家> <金额>', '转钱给玩家'],
            ['/bal（或 /balance）', '查看自己的余额'],
            ['/afk', '标记自己为挂机'],
            ['/back', '返回上次传送前 / 死亡地点'],
            ['/nick <昵称>', '修改自己的昵称'],
            ['/suicide', '自杀（卡住时回出生点用）'],
            ['/seen <玩家>', '查看某玩家最后在线时间'],
            ['/warp <名称>', '传送到传送点（需管理员先建好传送点）'],
            ['/kit <名称>', '领取礼包（需管理员先配好礼包）'],
            ['/help', '查看帮助'],
          ],
        },
        {
          kind: 'tip',
          title: '说明',
          lines: [
            '上面「可用」表里的指令，普通玩家直接就能用，无需找管理员。',
            '经济（/pay、/bal）由 EssentialsX 内置经济 + Vault 桥接，已可用。',
          ],
        },
      ],
    },
    {
      id: 'cmds-denied',
      title: '普通玩家「未授权」的指令',
      summary: '提示无权限属正常现象（不是插件坏）',
      blocks: [
        {
          kind: 'table',
          caption: '未授权指令（default 组未开放）',
          headers: ['指令', '缺少的权限', '说明'],
          rows: [
            ['/mail', 'essentials.mail', '离线邮件'],
            ['/list', 'essentials.list', '在线列表'],
            ['/baltop', 'essentials.baltop', '财富榜'],
            ['/ignore', 'essentials.ignore', '屏蔽玩家消息'],
            ['/motd', 'essentials.motd', '每日提示'],
            ['/rules', 'essentials.rules', '服务器规则'],
          ],
        },
        {
          kind: 'tip',
          title: '说明',
          lines: [
            '想开放这些指令，需要管理员在 LuckPerms 给 default 组补对应权限，不是玩家自己能解决的。',
          ],
        },
      ],
    },
  ],
}
