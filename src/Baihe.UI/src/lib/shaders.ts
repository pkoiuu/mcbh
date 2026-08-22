/**
 * 光影包元数据 — 预装光影的说明信息（描述/适用机型/预览图）
 * 与后端 ShaderService 返回的 fileName 对应；用户自装的光影无元数据时显示通用信息
 */
import complementaryReimagined from '../assets/shaders/complementary-reimagined.webp'
import bsl from '../assets/shaders/bsl-shaders.jpeg'
import sildurs from '../assets/shaders/sildurs-vibrant-shaders.webp'
import makeup from '../assets/shaders/makeup-ultra-fast-shaders.webp'

export interface ShaderMeta {
  /** 与后端 shaders.list 返回的 fileName 匹配 */
  fileName: string
  displayName: string
  /** 一句话效果描述 */
  description: string
  /** 适用机型/显卡定位 */
  suitable: string
  /** 性能等级: '低' | '中' | '高' */
  tier: '低' | '中' | '高'
  /** 预览图（前端打包） */
  preview: string
}

/** 预装光影元数据表 */
export const shaderMetas: ShaderMeta[] = [
  {
    fileName: 'ComplementaryReimagined_r5.8.1.zip',
    displayName: 'Complementary Reimagined',
    description:
      '画面通透自然的写实光影，暖色调日落氛围感强，体积光与水面倒影效果出色，适合追求画面质感的日常游玩。',
    suitable: '中高端显卡（GTX 1660 及以上 / 4GB 显存以上）',
    tier: '中',
    preview: complementaryReimagined,
  },
  {
    fileName: 'BSL_v10.1.3.zip',
    displayName: 'BSL',
    description:
      '经典全能型光影，色彩饱和、明暗层次分明，晴天与夜晚效果都很优秀，可调选项多，兼容性好。',
    suitable: '中高端显卡（GTX 1060 及以上）',
    tier: '中',
    preview: bsl,
  },
  {
    fileName: "Sildur's Vibrant Shaders v2.01 Extreme.zip",
    displayName: "Sildur's Vibrant",
    description:
      '色彩鲜艳明快的光影，Extreme 档画面华丽，光影细节丰富；同系列还有 Lite/Medium 档适合低配。',
    suitable: '中端显卡（GTX 1050 Ti ~ GTX 1660）',
    tier: '中',
    preview: sildurs,
  },
  {
    fileName: 'MakeUp-UltraFast-9.5d.zip',
    displayName: 'MakeUp Ultra Fast',
    description:
      '轻量高速光影，体积很小、帧率影响小，保留了光影核心效果（光照/阴影/水面），低配机首选。',
    suitable: '低端显卡/核显（GTX 750 / Intel UHD 630 及以上）',
    tier: '低',
    preview: makeup,
  },
]

/** 根据文件名查找元数据；未找到返回 null */
export function findShaderMeta(fileName: string): ShaderMeta | undefined {
  return shaderMetas.find((m) => m.fileName === fileName)
}
