# 白鹤服务器 Minecraft 启动器

> 完全自主开发的 Minecraft 启动器，专为白鹤服务器定制。UI 采用 macOS 风格设计，核心启动能力基于 fork PCL.Core（Apache 2.0）重写。

## 技术栈

- **后端**: C# .NET 10 WPF + WebView2
- **前端**: Vite + Svelte 5 + Tailwind CSS 4 + Lucide Icons
- **核心库**: Baihe.Core（fork 自 PCL.Core，裁剪 WPF 耦合）
- **打包**: Inno Setup 6
- **CI/CD**: GitHub Actions

## 项目结构

```
src/
├── Baihe.Host/              # WPF 主进程宿主（WebView2 + 标题栏 + IPC）
├── Baihe.Core/              # MC 启动核心（fork 自 PCL.Core）
├── Baihe.Core.SourceGenerators/  # 源生成器（fork 自 PCL.Core.SourceGenerators）
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
# 1. 构建前端
cd src/Baihe.UI
pnpm install
pnpm build
cd ../..

# 2. 复制前端到 Host assets
Copy-Item src/Baihe.UI/build/* src/Baihe.Host/assets/ -Recurse -Force

# 3. 构建后端
dotnet build src/Baihe.Core/Baihe.Core.csproj -c Release
dotnet build src/Baihe.Host/Baihe.Host.csproj -c Release

# 4. 运行
dotnet run --project src/Baihe.Host/Baihe.Host.csproj
```

### 完整打包

```powershell
# 一键构建安装包
.\scripts\build-all.ps1
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

Apache License 2.0（继承自 PCL.Core）

Baihe.Core 基于 PCL.Core（Apache 2.0）fork 重写，保留原始版权声明。
