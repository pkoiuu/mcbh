# 下载最新 CI 构建产物 — 使用国内镜像加速
# 用法: powershell -ExecutionPolicy Bypass -File scripts\download-build.ps1
# 镜像优先级: ghproxy.com > gh-proxy.com > ghfast.top > 直连

param(
    [string]$OutputDir = "artifacts",
    [string]$Repo = "pkoiuu/mcbh"
)

$ErrorActionPreference = "Stop"

# 国内镜像列表（按优先级排序）
$mirrors = @(
    "https://ghproxy.com/",
    "https://gh-proxy.com/",
    "https://ghfast.top/"
)

# 原始 release 下载 URL
$releaseUrl = "https://github.com/$Repo/releases/download/latest/baihe-build.zip"

Write-Host "=== 白鹤服务器构建下载工具 ===" -ForegroundColor Cyan
Write-Host ""

# 创建输出目录
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$outputFile = Join-Path $OutputDir "baihe-build.zip"

# 尝试每个镜像
$downloaded = $false
foreach ($mirror in $mirrors) {
    $mirrorUrl = "${mirror}${releaseUrl}"
    Write-Host "[尝试] 镜像: $mirror" -ForegroundColor Yellow
    try {
        # 下载文件，设置 10 秒连接超时
        $tempFile = "$outputFile.tmp"
        Invoke-WebRequest -Uri $mirrorUrl -OutFile $tempFile -TimeoutSec 30 -UseBasicParsing
        $fileSize = (Get-Item $tempFile).Length
        if ($fileSize -gt 1MB) {
            Move-Item -Path $tempFile -Destination $outputFile -Force
            $sizeMB = [math]::Round($fileSize / 1MB, 1)
            Write-Host "[成功] 通过 $mirror 下载完成 ($sizeMB MB)" -ForegroundColor Green
            $downloaded = $true
            break
        } else {
            Write-Host "[失败] 文件过小，可能是错误页面" -ForegroundColor Red
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    } catch {
        Write-Host "[失败] $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item "$outputFile.tmp" -Force -ErrorAction SilentlyContinue
    }
}

# 如果所有镜像都失败，尝试直连
if (!$downloaded) {
    Write-Host ""
    Write-Host "[尝试] 直连 GitHub (可能较慢)..." -ForegroundColor Yellow
    try {
        $tempFile = "$outputFile.tmp"
        Invoke-WebRequest -Uri $releaseUrl -OutFile $tempFile -TimeoutSec 60 -UseBasicParsing
        $fileSize = (Get-Item $tempFile).Length
        if ($fileSize -gt 1MB) {
            Move-Item -Path $tempFile -Destination $outputFile -Force
            $sizeMB = [math]::Round($fileSize / 1MB, 1)
            Write-Host "[成功] 直连下载完成 ($sizeMB MB)" -ForegroundColor Green
            $downloaded = $true
        }
    } catch {
        Write-Host "[失败] 直连也失败: $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item "$outputFile.tmp" -Force -ErrorAction SilentlyContinue
    }
}

if (!$downloaded) {
    Write-Host ""
    Write-Host "所有下载方式均失败，请检查网络连接" -ForegroundColor Red
    exit 1
}

# 解压
Write-Host ""
Write-Host "[解压] 正在解压构建产物..." -ForegroundColor Cyan
$extractDir = Join-Path $OutputDir "baihe-build"
if (Test-Path $extractDir) {
    Remove-Item $extractDir -Recurse -Force
}
Expand-Archive -Path $outputFile -DestinationPath $extractDir -Force
Write-Host "[完成] 解压到 $extractDir" -ForegroundColor Green

# 部署到 CI 运行目录
$ciDir = "src\Baihe.Host\bin\CI\net10.0-windows\win-x64"
$srcDir = Join-Path $extractDir "*"
if (Test-Path $ciDir) {
    Write-Host ""
    Write-Host "[部署] 正在更新 CI 运行目录..." -ForegroundColor Cyan
    # 先停止运行中的应用
    Stop-Process -Name Baihe -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    # 复制文件（排除 WebView2 用户数据）
    robocopy $extractDir $ciDir /E /IS /IT /XD "Baihe.exe.WebView2" /NJH /NJS /NP | Out-Null
    Write-Host "[完成] 部署到 $ciDir" -ForegroundColor Green

    # 启动应用
    Write-Host ""
    Write-Host "[启动] 正在启动白鹤服务器..." -ForegroundColor Cyan
    Start-Process (Join-Path $ciDir "Baihe.exe") -WorkingDirectory $ciDir
    Start-Sleep -Seconds 2
    $proc = Get-Process Baihe -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "[完成] 应用已启动 (PID: $($proc.Id))" -ForegroundColor Green
    } else {
        Write-Host "[警告] 应用启动失败" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "[跳过] CI 运行目录不存在: $ciDir" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== 下载部署完成 ===" -ForegroundColor Cyan
