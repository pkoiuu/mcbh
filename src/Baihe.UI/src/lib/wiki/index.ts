/**
 * 维基数据汇总 — 服务器玩家指南的结构化内容
 * 按分类拆分到独立文件（可单独维护/扩展），此处统一导出
 */
import type { WikiCategory } from './types'
import { loginCategory } from './login'
import { commandsCategory } from './commands'
import { sitCategory } from './sit'
import { skinCategory } from './skin'
import { farmCategory } from './farm'
import { versionCategory } from './version'
import { mapCategory } from './map'
import { anticheatCategory } from './anticheat'
import { faqCategory } from './faq'

/** 全部分类（一级）— 顺序即侧边栏展示顺序 */
export const wikiCategories: WikiCategory[] = [
  loginCategory,
  commandsCategory,
  sitCategory,
  skinCategory,
  farmCategory,
  versionCategory,
  mapCategory,
  anticheatCategory,
  faqCategory,
]

/** 获取指定分类（找不到返回 null） */
export function findCategory(id: string): WikiCategory | undefined {
  return wikiCategories.find((c) => c.id === id)
}

/**
 * 全文搜索 — 在分类标题/简介/页面标题/表格单元格/文本/提示中匹配关键词
 * 返回匹配的 (category, page, blockIndex) 组合
 */
export interface WikiSearchHit {
  category: WikiCategory
  page: WikiPage
  /** 命中的块在 page.blocks 中的下标 */
  blockIndex: number
  /** 命中的单元格/文本行（用于高亮） */
  matchedText: string
}

import type { WikiPage } from './types'

export function searchWiki(keyword: string, cats: WikiCategory[] = wikiCategories): WikiSearchHit[] {
  const kw = keyword.trim().toLowerCase()
  if (!kw) return []

  const hits: WikiSearchHit[] = []
  for (const category of cats) {
    for (const page of category.pages) {
      for (let i = 0; i < page.blocks.length; i++) {
        const block = page.blocks[i]
        const texts: string[] = []

        if (block.kind === 'table') {
          if (block.caption) texts.push(block.caption)
          texts.push(...block.headers)
          for (const row of block.rows) texts.push(...row)
        } else if (block.kind === 'text') {
          texts.push(...block.lines)
        } else {
          if (block.title) texts.push(block.title)
          texts.push(...block.lines)
        }

        const matched = texts.find((t) => t.toLowerCase().includes(kw))
        if (matched !== undefined) {
          hits.push({ category, page, blockIndex: i, matchedText: matched })
        }
      }
    }
  }
  return hits
}
