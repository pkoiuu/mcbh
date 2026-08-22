# ============================================================
# 更新内置游戏版本 — 下载指定版本的 Minecraft + Fabric Loader + 模组到 installer_resources/.minecraft
# 用法: .\scripts\update-bundled-game.ps1 [-GameVersion 1.21.8]
# 产出: versions/<游戏>/ + versions/<游戏>-fabric/ + libraries/ + assets/ + mods/（模组走 Modrinth）
# 注意: 下载完成后会清理旧版版本目录与 .fabric 缓存；打包用 scripts/upload-minecraft-assets.ps1
# ============================================================

param(
    [string]$GameVersion = "1.21.8",
    [string]$McDir = (Join-Path $PSScriptRoot '..\installer_resources\.minecraft')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ManifestUrl = 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'
$FabricMetaBase = 'https://meta.fabricmc.net/v2/versions/loader'
$ModrinthApi = 'https://api.modrinth.com/v2'

function Test-FileValid([string]$path, [string]$sha1) {
    if (-not (Test-Path $path)) { return $false }
    if (-not $sha1) { return $true }
    try { return (Get-FileHash $path -Algorithm SHA1).Hash.ToLowerInvariant() -eq $sha1.ToLowerInvariant() }
    catch { return $false }
}

function Download-File([string]$url, [string]$dest, [string]$sha1) {
    $destDir = Split-Path $dest -Parent
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    if (Test-FileValid $dest $sha1) { Write-Host "  [skip] $dest"; return }
    $tmp = "$dest.tmp"
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -TimeoutSec 300 -UseBasicParsing
        if ($sha1) {
            $h = (Get-FileHash $tmp -Algorithm SHA1).Hash.ToLowerInvariant()
            if ($h -ne $sha1.ToLowerInvariant()) { throw "SHA1 mismatch for $dest : expected=$sha1 got=$h" }
        }
        Move-Item -Force $tmp $dest
        Write-Host "  [ok] $dest"
    }
    catch {
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
        throw
    }
}

function Test-Rules($rules) {
    # 无 rules 默认允许；与 LaunchService.CheckRules 语义一致（Windows 平台）
    if (-not $rules) { return $true }
    $allowed = $true
    foreach ($rule in $rules) {
        $action = if ($rule.action) { $rule.action } else { 'allow' }
        if ($rule.os) {
            if ($rule.os.name -eq 'windows') { $allowed = ($action -eq 'allow') }
        }
        else {
            $allowed = ($action -eq 'allow')
        }
    }
    return $allowed
}

function Resolve-MavenPath([string]$name, [string]$classifier) {
    $parts = $name.Split(':')
    if ($parts.Count -lt 3) { return $null }
    $group = $parts[0].Replace('.', '\')
    $artifact = $parts[1]
    $version = $parts[2]
    $file = if ($classifier) { "$artifact-$version-$classifier.jar" } else { "$artifact-$version.jar" }
    return "$group\$artifact\$version\$file"
}

function Download-Libraries($json, [string]$libDir) {
    $count = 0
    foreach ($lib in $json.libraries) {
        if (-not (Test-Rules $lib.rules)) { continue }
        $url = $null; $relPath = $null; $sha1 = $null
        # 1) 旧格式 natives classifier
        if ($lib.downloads.classifiers.'natives-windows') {
            $c = $lib.downloads.classifiers.'natives-windows'
            $url = $c.url; $relPath = $c.path; $sha1 = $c.sha1
        }
        # 2) 现代格式 artifact（含 :natives-windows 独立条目）
        elseif ($lib.downloads.artifact) {
            $a = $lib.downloads.artifact
            $url = $a.url; $relPath = $a.path; $sha1 = $a.sha1
        }
        # 3) Maven 格式 name+url 回退（Fabric 等）
        elseif ($lib.name -and $lib.url) {
            $relPath = Resolve-MavenPath $lib.name $null
            if ($relPath) { $url = $lib.url.TrimEnd('/') + '/' + $relPath.Replace('\', '/') }
        }
        if ($url -and $relPath) {
            Download-File $url (Join-Path $libDir $relPath) $sha1
            $count++
        }
    }
    Write-Host "  libraries processed: $count"
}

function Extract-Natives($json, [string]$nativesDir) {
    New-Item -ItemType Directory -Path $nativesDir -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($lib in $json.libraries) {
        $name = $lib.name
        if (-not $name -or $name -notmatch 'natives-windows') { continue }
        if (-not (Test-Rules $lib.rules)) { continue }
        $rel = $lib.downloads.artifact.path
        if (-not $rel) { $rel = Resolve-MavenPath $name $null }
        if (-not $rel) { continue }
        $jar = Join-Path $McDir "libraries\$rel"
        if (-not (Test-Path $jar)) { continue }
        $zip = [System.IO.Compression.ZipFile]::OpenRead($jar)
        try {
            foreach ($e in $zip.Entries) {
                if ($e.Name -like '*.dll') {
                    $dest = Join-Path $nativesDir $e.Name
                    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -ne $e.Length) {
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
                    }
                }
            }
        }
        finally { $zip.Dispose() }
    }
}

Write-Host "=== 更新内置游戏到 $GameVersion (McDir=$McDir) ==="

Write-Host "[1/6] 下载版本 JSON"
$manifest = Invoke-RestMethod $ManifestUrl -TimeoutSec 60
$entry = $manifest.versions | Where-Object { $_.id -eq $GameVersion } | Select-Object -First 1
if (-not $entry) { throw "版本 $GameVersion 不在 Mojang 清单中" }
$versionJson = Invoke-RestMethod $entry.url -TimeoutSec 60
$verDir = Join-Path $McDir "versions\$GameVersion"
New-Item -ItemType Directory -Path $verDir -Force | Out-Null
$versionJson | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $verDir "$GameVersion.json") -Encoding UTF8

Write-Host "[2/6] 下载客户端 JAR"
Download-File $versionJson.downloads.client.url (Join-Path $verDir "$GameVersion.jar") $versionJson.downloads.client.sha1

Write-Host "[3/6] 下载库文件 (libraries)"
Download-Libraries $versionJson (Join-Path $McDir 'libraries')

Write-Host "[4/6] 下载资源索引与资源文件 (assets)"
$indexId = $versionJson.assetIndex.id
Download-File $versionJson.assetIndex.url (Join-Path $McDir "assets\indexes\$indexId.json") $versionJson.assetIndex.sha1
$indexJson = Invoke-RestMethod $versionJson.assetIndex.url -TimeoutSec 60
$objectsDir = Join-Path $McDir 'assets\objects'
$assetTotal = 0; $assetNew = 0
foreach ($p in $indexJson.objects.PSObject.Properties) {
    $hash = $p.Value.hash
    $dest = Join-Path $objectsDir "$($hash.Substring(0, 2))\$hash"
    if (-not (Test-Path $dest)) {
        Download-File "https://resources.download.minecraft.net/$($hash.Substring(0, 2))/$hash" $dest $hash
        $assetNew++
    }
    $assetTotal++
}
Write-Host "  assets total: $assetTotal, downloaded: $assetNew"

Write-Host "[5/6] 安装 Fabric Loader"
$loaders = Invoke-RestMethod "$FabricMetaBase/$GameVersion" -TimeoutSec 60
$loaderEntry = $loaders | Where-Object { $_.loader.stable } | Select-Object -First 1
if (-not $loaderEntry) { $loaderEntry = $loaders | Select-Object -First 1 }
$loaderVer = $loaderEntry.loader.version
$profileJson = Invoke-RestMethod "$FabricMetaBase/$GameVersion/$loaderVer/profile/json" -TimeoutSec 60
$fabricId = "$GameVersion-fabric"
$profileJson.id = $fabricId
$fabDir = Join-Path $McDir "versions\$fabricId"
New-Item -ItemType Directory -Path $fabDir -Force | Out-Null
$profileJson | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $fabDir "$fabricId.json") -Encoding UTF8
Write-Host "  Fabric Loader: $loaderVer, profile id: $fabricId"
Download-Libraries $profileJson (Join-Path $McDir 'libraries')

Write-Host "[5.5/6] 预提取 natives"
Extract-Natives $versionJson (Join-Path $verDir 'natives')
Extract-Natives $profileJson (Join-Path $fabDir 'natives')
Copy-Item (Join-Path $verDir 'natives\*') (Join-Path $fabDir 'natives\') -Force -ErrorAction SilentlyContinue

Write-Host "[6/6] 更新模组 (Modrinth: $GameVersion + fabric)"
$slugs = @('sodium', 'sodium-extra', 'jade', 'appleskin', 'modmenu', 'xaeros-minimap', 'placeholder-api', 'fabric-api', 'cloth-config', 'bettertab')
$modsDir = Join-Path $McDir 'mods'
New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
$newModFiles = [System.Collections.Generic.List[string]]::new()
foreach ($slug in $slugs) {
    try {
        $uri = "$ModrinthApi/project/$slug/version?game_versions=" + [uri]::EscapeDataString("[""$GameVersion""]") + "&loaders=" + [uri]::EscapeDataString('["fabric"]')
        $versions = Invoke-RestMethod $uri -TimeoutSec 30
        if (-not $versions -or $versions.Count -eq 0) { Write-Host "  [WARN] $slug 无 $GameVersion fabric 版本"; continue }
        $v = $versions[0]
        $file = $v.files | Where-Object { $_.primary } | Select-Object -First 1
        if (-not $file) { $file = $v.files[0] }
        Download-File $file.url (Join-Path $modsDir $file.filename) $file.hashes.sha1
        $newModFiles.Add($file.filename)
    }
    catch { Write-Host "  [FAIL] $slug : $($_.Exception.Message)" }
}
# 清理 mods 目录中不在新模组清单内的旧 jar（含 .disabled），避免新旧模组重复加载崩溃
# 使用 .NET API + 重试删除（Remove-Item 对个别文件可能静默失败）
Get-ChildItem $modsDir -File | Where-Object { $_.Name -notin $newModFiles } | ForEach-Object {
    $full = $_.FullName
    $deleted = $false
    for ($try = 0; $try -lt 3; $try++) {
        try { [System.IO.File]::Delete($full); $deleted = $true; break }
        catch { Start-Sleep -Milliseconds 300 }
    }
    if ($deleted) { Write-Host "  [clean] removed $($_.Name)" }
    else { Write-Host "  [WARN] failed to remove $($_.Name): $($_.Exception.Message)" }
}

Write-Host "=== 清理旧版本目录与缓存 ==="
foreach ($old in @('1.21.3', 'fabric-loader-0.16.14-1.21.3')) {
    $p = Join-Path $McDir "versions\$old"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "  removed versions\$old" }
}
$fabricCache = Join-Path $McDir '.fabric'
if (Test-Path $fabricCache) { Remove-Item $fabricCache -Recurse -Force; Write-Host "  removed .fabric" }
New-Item -ItemType Directory -Path $fabricCache -Force | Out-Null  # 保持空目录，安装器 [Files] 引用不会失效

# 更新 launcher_profiles.json 指向新 fabric 版本（去掉开发期 gameDir 泄漏）
$lp = Join-Path $McDir 'launcher_profiles.json'
$lpContent = @"
{
  "profiles": {
    "$fabricId": {
      "name": "$fabricId",
      "type": "custom",
      "lastVersionId": "$fabricId"
    }
  },
  "selectedProfile": "$fabricId"
}
"@
Set-Content $lp $lpContent -Encoding UTF8
Write-Host "  updated launcher_profiles.json -> $fabricId"

Write-Host "=== 完成: 游戏 $GameVersion + Fabric $loaderVer 已就绪 ==="
