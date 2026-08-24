/**
 * 维基数据结构 — 服务器玩家指南的结构化内容模型
 * 分级：Category（一级分类，对应指南章节）→ Page（二级页面）→ Block（内容单元：表格/文本/提示）
 * 内容按分类拆分到 lib/wiki/*.ts，index.ts 汇总
 */

/** 表格块 — 指令/场景等表格化内容（支持搜索） */
export interface WikiTable {
  kind: 'table'
  /** 表格标题（可选） */
  caption?: string
  headers: string[]
  rows: string[][]
}

/** 文本块 — 普通说明段落 */
export interface WikiText {
  kind: 'text'
  lines: string[]
}

/** 提示块 — 注意事项/要点（高亮样式） */
export interface WikiTip {
  kind: 'tip'
  title?: string
  lines: string[]
}

export type WikiBlock = WikiTable | WikiText | WikiTip

/** 二级页面 — 一个分类下的独立内容页 */
export interface WikiPage {
  id: string
  title: string
  /** 一句话简介（列表/搜索展示） */
  summary?: string
  blocks: WikiBlock[]
}

/** 一级分类 — 对应指南的一个大章节 */
export interface WikiCategory {
  id: string
  title: string
  /** 分类简介 */
  intro?: string
  pages: WikiPage[]
}
