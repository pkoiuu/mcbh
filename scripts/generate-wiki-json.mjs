// 生成 wiki.json — 从 src/Baihe.UI/src/lib/wiki/*.ts 导出维基数据
// 用途: 启动器与网页版维基都以 wiki.json 为远程数据源；编辑 lib/wiki/*.ts 后运行本脚本重新生成
// 用法: node scripts/generate-wiki-json.mjs
import { execSync } from 'node:child_process'
import { writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const require = createRequire(import.meta.url)
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const esbuildBin = path.join(root, 'src', 'Baihe.UI', 'node_modules', '.bin', 'esbuild')
const entry = path.join(root, 'src', 'Baihe.UI', 'src', 'lib', 'wiki', 'index.ts')
const outFile = path.join(root, '.buildtemp', 'wiki-bundle.cjs')

// 用 esbuild 把 index.ts 打包为 CJS（处理 TS 与相对导入）
execSync(`"${esbuildBin}" "${entry}" --bundle --format=cjs --outfile="${outFile}"`, { stdio: 'inherit' })

// CJS 用 require 加载（ESM import 对 esbuild getter 导出的 interop 有问题）
const mod = require(outFile)
const wikiCategories = mod.wikiCategories

if (!Array.isArray(wikiCategories) || wikiCategories.length === 0) {
  console.error('ERROR: wikiCategories 为空，请检查 lib/wiki 内容')
  process.exit(1)
}

const data = {
  version: 1,
  updated: new Date().toISOString().slice(0, 10),
  note: '维基数据源（服务器玩家指南）。启动器与网页版维基均拉取本文件；直接编辑本文件即可更新维基（无需发版）。也可以编辑 src/Baihe.UI/src/lib/wiki/*.ts 后运行 scripts/generate-wiki-json.mjs 重新生成。',
  categories: wikiCategories,
}

const dest = path.join(root, 'wiki.json')
writeFileSync(dest, JSON.stringify(data, null, 2), 'utf-8')
console.log(`wiki.json 已生成: ${wikiCategories.length} 个分类 → ${dest}`)
