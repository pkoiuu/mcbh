# 白鹤服务器 Minecraft 启动器

> 完全自主开发的 Minecraft 启动器，专为白鹤服务器定制。UI 采用 macOS 风格设计，后端为独立 WPF 宿主，无需外部核心库依赖。

## 技术栈

- **后端**: C# .NET 10 WPF + WebView2
- **前端**: Vite + Svelte 5 + Tailwind CSS 4 + Lucide Icons
- **打包**: Inno Setup 6
- **CI/CD**: GitHub Actions

## 项目结构

```
src/
├── Baihe.Host/              # WPF 主进程宿主（WebView2 + 标题栏 + IPC + 全部业务服务）
└── Baihe.UI/                # Svelte 5 前端
installer/                   # Inno Setup 安装脚本
scripts/                     # 构建脚本
.github/workflows/           # GitHub Actions CI/CD
```

## 构建指南

### 前置要求

- .NET 10 SDK
- Node.js >= 20 + pnpm
- Java 21 JDK（仅打包时需要 jlink）

### 本地构建

```powershell
# 1. 构建前端（Vite 直接输出到 ../Baihe.Host/wwwroot，无需手动复制）
cd src/Baihe.UI
pnpm install
pnpm build
cd ../..

# 2. 构建后端
dotnet build src/Baihe.Host/Baihe.Host.csproj -c Release

# 3. 运行
dotnet run --project src/Baihe.Host/Baihe.Host.csproj
```

### 完整打包

```powershell
# 打包安装包（需 7-Zip + Inno Setup 6 + Java 21 JDK 做 jlink）
# 先更新内置游戏资源 installer_resources/.minecraft（如需要），再:
#   1. 打包 .minecraft 为 7z 并上传到 v1.0-assets release:
#      .\scripts\upload-minecraft-assets.ps1
#   2. 打 tag vX.Y.Z 推送，GitHub Actions 自动构建安装包（scripts/download-build.ps1 可下载 CI 构建）
```

## CI/CD

- **编译验证**: 每次 push/PR 自动触发 `.github/workflows/ci.yml`，验证 dotnet build + pnpm build
- **Release 发布**: 打 tag `v*` 自动触发 `.github/workflows/release.yml`，构建完整安装包并发布到 GitHub Releases

### .minecraft Release Asset 设置

完整 .minecraft 游戏文件（~1.3GB）不纳入 Git 仓库，通过 GitHub Release Asset 管理：

```bash
# 首次上传 .minecraft 完整包（压缩为 7z）
7z a minecraft.7z .minecraft
gh release create v1.0-assets minecraft.7z --title "游戏资源包" --notes "预置 .minecraft 完整游戏文件"
```

CI 中通过 `gh release download v1.0-assets` 下载复用。.minecraft 更新时重新上传该 asset。

## 许可证

Apache License 2.0
