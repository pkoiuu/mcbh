# Download latest CI build using China mirror acceleration
# Usage: powershell -ExecutionPolicy Bypass -File scripts\download-build.ps1
# Mirror priority: ghproxy.com > gh-proxy.com > ghfast.top > direct

param(
    [string]$OutputDir = "artifacts",
    [string]$Repo = "pkoiuu/mcbh"
)

$ErrorActionPreference = "Stop"

# China mirror list (sorted by priority)
$mirrors = @(
    "https://gh-proxy.com/",
    "https://ghfast.top/",
    "https://ghproxy.net/"
)

# Original release download URL
$releaseUrl = "https://github.com/$Repo/releases/download/latest/baihe-build.zip"

Write-Host "=== Baihe Build Download Tool ===" -ForegroundColor Cyan
Write-Host ""

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$outputFile = Join-Path $OutputDir "baihe-build.zip"

# Try each mirror
$downloaded = $false
foreach ($mirror in $mirrors) {
    $mirrorUrl = "${mirror}${releaseUrl}"
    Write-Host "[TRY] Mirror: $mirror" -ForegroundColor Yellow
    try {
        $tempFile = "$outputFile.tmp"
        Invoke-WebRequest -Uri $mirrorUrl -OutFile $tempFile -TimeoutSec 30 -UseBasicParsing
        $fileSize = (Get-Item $tempFile).Length
        if ($fileSize -gt 1MB) {
            Move-Item -Path $tempFile -Destination $outputFile -Force
            $sizeMB = [math]::Round($fileSize / 1MB, 1)
            Write-Host "[OK] Downloaded via $mirror ($sizeMB MB)" -ForegroundColor Green
            $downloaded = $true
            break
        } else {
            Write-Host "[FAIL] File too small, possibly error page" -ForegroundColor Red
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    } catch {
        Write-Host "[FAIL] $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item "$outputFile.tmp" -Force -ErrorAction SilentlyContinue
    }
}

# If all mirrors failed, try direct
if (!$downloaded) {
    Write-Host ""
    Write-Host "[TRY] Direct GitHub (may be slow)..." -ForegroundColor Yellow
    try {
        $tempFile = "$outputFile.tmp"
        Invoke-WebRequest -Uri $releaseUrl -OutFile $tempFile -TimeoutSec 60 -UseBasicParsing
        $fileSize = (Get-Item $tempFile).Length
        if ($fileSize -gt 1MB) {
            Move-Item -Path $tempFile -Destination $outputFile -Force
            $sizeMB = [math]::Round($fileSize / 1MB, 1)
            Write-Host "[OK] Direct download ($sizeMB MB)" -ForegroundColor Green
            $downloaded = $true
        }
    } catch {
        Write-Host "[FAIL] Direct also failed: $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item "$outputFile.tmp" -Force -ErrorAction SilentlyContinue
    }
}

if (!$downloaded) {
    Write-Host ""
    Write-Host "All download methods failed. Check network." -ForegroundColor Red
    exit 1
}

# Extract
Write-Host ""
Write-Host "[EXTRACT] Extracting build..." -ForegroundColor Cyan
$extractDir = Join-Path $OutputDir "baihe-build"
if (Test-Path $extractDir) {
    Remove-Item $extractDir -Recurse -Force
}
Expand-Archive -Path $outputFile -DestinationPath $extractDir -Force
Write-Host "[DONE] Extracted to $extractDir" -ForegroundColor Green

# Deploy to CI directory
$ciDir = "src\Baihe.Host\bin\CI\net10.0-windows\win-x64"
if (Test-Path $ciDir) {
    Write-Host ""
    Write-Host "[DEPLOY] Updating CI directory..." -ForegroundColor Cyan
    Stop-Process -Name Baihe -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    robocopy $extractDir $ciDir /E /IS /IT /XD "Baihe.exe.WebView2" /NJH /NJS /NP | Out-Null
    Write-Host "[DONE] Deployed to $ciDir" -ForegroundColor Green

    # Launch app
    Write-Host ""
    Write-Host "[LAUNCH] Starting Baihe..." -ForegroundColor Cyan
    Start-Process (Join-Path $ciDir "Baihe.exe") -WorkingDirectory $ciDir
    Start-Sleep -Seconds 2
    $proc = Get-Process Baihe -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "[DONE] App started (PID: $($proc.Id))" -ForegroundColor Green
    } else {
        Write-Host "[WARN] App failed to start" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "[SKIP] CI directory not found: $ciDir" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Download & Deploy Complete ===" -ForegroundColor Cyan
