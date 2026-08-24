import type { WikiCategory } from './types'

/**
 * 常见问题与注意事项汇总 — 指南第十章（含第九章后台插件简介）
 */
export const faqCategory: WikiCategory = {
  id: 'faq',
  title: '常见问题',
  intro: '日常遇到问题先看这里；还有「后台插件」一览，帮助你知道哪些功能该找管理员。',
  pages: [
    {
      id: 'faq-qa',
      title: '常见问题速查',
      summary: '问题 → 处理办法',
      blocks: [
        {
          kind: 'table',
          caption: 'FAQ',
          headers: ['问题', '处理办法'],
          rows: [
            ['进服动不了 / 不能说话', '你还没登录，在登录 GUI 输密码（或 /login <密码>，首次 /register）'],
            ['忘记密码', '联系管理员重置'],
            ['基础指令用不了（home/spawn/tpa/msg/pay 等）', '这些本服已对普通玩家开放，若仍提示无权限，联系管理员排查'],
            ['/mail /list /baltop /ignore 等提示无权限', '属正常，这些指令本服未对普通玩家开放'],
            ['厨锅/作物是紫色方块或缺失材质', '没接受资源包，重进服并点「接受资源包」'],
            ['老客户端进服看不到新方块', '正常现象，换 1.21.8 客户端即可'],
            ['被踢/被封', '多为反作弊判定，联系管理员核实'],
            ['东西被偷 / 被破坏', '联系管理员用 CoreProtect 查记录并回滚'],
          ],
        },
      ],
    },
    {
      id: 'faq-backend',
      title: '后台插件（玩家无感）',
      summary: '看不到也用不到，遇到问题可对应找管理员',
      blocks: [
        {
          kind: 'table',
          headers: ['插件', '作用', '玩家视角'],
          rows: [
            ['AuthMe / FlexLoginUI', '登录验证（FlexLoginUI 是登录界面前端）', '已在前文说明'],
            ['LuckPerms', '权限管理', '决定你能用哪些指令'],
            ['CoreProtect', '方块/容器记录、回滚', '被熊/偷东西可找管理员查记录回滚（/co 是管理员功能）'],
            ['Vault', '经济/权限桥接', '无感'],
            ['ProtocolLib / packetevents', '发包底层库', '无感'],
            ['spark', '性能分析', '无感（管理员用 /spark）'],
            ['bStats', '匿名统计', '无感'],
            ['NeoArtisan', '农夫乐事的运行框架', '无感'],
            ['BlueMap', '网页地图', '网页访问'],
          ],
        },
      ],
    },
    {
      id: 'faq-summary',
      title: '一句话总结',
      summary: '新手快速了解日常用到的功能',
      blocks: [
        {
          kind: 'text',
          lines: [
            '普通玩家日常用到的就这几类：',
            '· 登录（AuthMe 的 GUI，本质仍是密码）',
            '· 基础指令（EssentialsX 的 home/spawn/tpa/msg/pay/bal/back/nick 等，本服已授权）',
            '· 坐下（GSit 的 /sit + 右键坐，默认开放）',
            '· 换肤（SkinsRestorer 的 /skins GUI + /skin，默认开放）',
            '外加农夫乐事的种菜做饭和网页地图看世界。',
            '',
            '遇到「看不到方块」先检查资源包和客户端版本；只有 /mail、/list、/baltop、/ignore、/motd、/rules 这几个是确实没开放给普通玩家的。',
          ],
        },
      ],
    },
  ],
}
