# ============================================================
# 白鹤服务器启动器 — .minecraft 资源包上传脚本
# 用途: 将本地 installer_resources/.minecraft 打包为 7z 并上传到 GitHub Release
# 使用方法:
#   1. 确保已安装 7-Zip 和 GitHub CLI (gh)
#   2. 确保已登录 gh (gh auth login)
#   3. 运行: .\scripts\upload-minecraft-assets.ps1
# ============================================================

param(
    [string]$Repo = "pkoiuu/mcbh",
    [string]$ReleaseTag = "v1.0-assets",
    [string]$SourcePath = ".\installer_resources\.minecraft",
    [string]$OutputFile = ".\minecraft.7z"
)

$ErrorActionPreference = "Stop"

Write-Host "=== 白鹤服务器启动器 — .minecraft 资源包上传 ===" -ForegroundColor Cyan
Write-Host ""

# 1. 检查源目录
if (-not (Test-Path $SourcePath)) {
    Write-Host "[ERROR] .minecraft 目录不存在: $SourcePath" -ForegroundColor Red
    exit 1
}

$sourceSize = (Get-ChildItem $SourcePath -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "[1/4] 源目录: $SourcePath ($([math]::Round($sourceSize, 1)) MB)" -ForegroundColor Green

# 2. 打包为 7z (排除日志、崩溃报告等)
Write-Host "[2/4] 正在打包 .minecraft 为 7z..." -ForegroundColor Yellow

# 查找 7z
$sevenZip = Get-Command "7z" -ErrorAction SilentlyContinue
if (-not $sevenZip) {
    $sevenZip = Get-Command "C:\Program Files\7-Zip\7z.exe" -ErrorAction SilentlyContinue
}
if (-not $sevenZip) {
    Write-Host "[ERROR] 未找到 7-Zip，请安装 7-Zip 或将其添加到 PATH" -ForegroundColor Red
    exit 1
}

& $sevenZip.Source a -t7z -mx=9 -mmt=on $OutputFile $SourcePath `
    -xr!*.log -xr!*.bak -xr!*.tmp `
    `-xr!logs `-xr!crash-reports `-xr!downloads `
    -xr!servers.dat_old

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] 7z 打包失败" -ForegroundColor Red
    exit 1
}

$outputSize = (Get-Item $OutputFile).Length / 1MB
Write-Host "  打包完成: $OutputFile ($([math]::Round($outputSize, 1)) MB)" -ForegroundColor Green

# 3. 检查 gh CLI
Write-Host "[3/4] 检查 GitHub CLI..." -ForegroundColor Yellow
$gh = Get-Command "gh" -ErrorAction SilentlyContinue
if (-not $gh) {
    Write-Host "[ERROR] 未找到 GitHub CLI (gh)，请安装: https://cli.github.com/" -ForegroundColor Red
    exit 1
}

# 检查登录状态
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] GitHub CLI 未登录，请运行: gh auth login" -ForegroundColor Red
    Write-Host $authStatus
    exit 1
}
Write-Host "  GitHub CLI 已登录" -ForegroundColor Green

# 4. 上传到 GitHub Release
Write-Host "[4/4] 上传到 GitHub Release ($ReleaseTag)..." -ForegroundColor Yellow

# 检查 Release 是否已存在
$existingRelease = gh release view $ReleaseTag --repo $Repo 2>$null
if ($LASTEXITCODE -eq 0) {
    # Release 已存在，更新 asset
    Write-Host "  Release $ReleaseTag 已存在，更新 asset..." -ForegroundColor Yellow
    gh release upload $ReleaseTag $OutputFile --repo $Repo --clobber
} else {
    # 创建新 Release
    Write-Host "  创建新 Release $ReleaseTag..." -ForegroundColor Yellow
    gh release create $ReleaseTag $OutputFile `
        --repo $Repo `
        --title "Minecraft 游戏资源包" `
        --notes "白鹤服务器启动器预置的 .minecraft 游戏资源包。

包含内容:
- Minecraft 1.21.3 客户端
- Fabric Loader 0.16.14
- 10 个预装模组
- 3985 个资源文件 (assets/objects)
- 76 个库文件 + 9 个原生库
- Fabric 预处理缓存 (.fabric)
- 全量模组配置文件

此资源包由 CI 自动下载用于构建安装包。"
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] 上传失败" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 上传完成 ===" -ForegroundColor Cyan
Write-Host "资源包已上传到: https://github.com/$Repo/releases/tag/$ReleaseTag" -ForegroundColor Green
Write-Host ""
Write-Host "下一步: 推送 git tag (如 v1.0.0) 即可触发自动构建安装包" -ForegroundColor Yellow

# 清理临时文件
Remove-Item $OutputFile -Force -ErrorAction SilentlyContinue
