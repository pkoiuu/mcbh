# ============================================================
# generate-update-patch.ps1 - Smart incremental update (build side)
# Outputs:
#   1) BaiheManifest_v<Version>.json    full file list (rel path -> size + sha256)
#   2) BaihePatch_v<From>_to_<Version>.zip   pairwise diff zip (new/changed files)
#
# Inclusion rules (kept in sync with installer/baihe_installer.iss):
#   - launcher main entry Excludes(glob) parsed dynamically from the .iss
#     (jre deployed via its own always-overwrite entry, so jre is patchable)
#   - under .minecraft ONLY versions|libraries|assets|mods|shaderpacks are patchable
#   - hard protection (never in files/deletes): options.txt servers.dat
#     launcher_profiles.json config/** saves/** screenshots/** logs/**
#     crash-reports/** downloads/** current_instance.txt settings.json
#     account.json servers.json wechat.json *.log debug-*.txt cache/**
#     Baihe.exe.WebView2/**
#
# Exit codes: 0=patch generated  2=manifest-only (no prev manifest / versions tree changed)
#
# NOTE: keep this file pure ASCII. Windows PowerShell 5.1 reads non-BOM
#       scripts as ANSI and Chinese comments break the parser.
# ============================================================
param(
    [Parameter(Mandatory = $true)][string]$CurrentDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$PrevManifestPath = "",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = 'Stop'

# PS5.1 does not auto-load compression assemblies (pwsh7 does)
if (-not ('System.IO.Compression.ZipArchiveMode' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression
}
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Log([string]$m) { Write-Host ("[patchgen] " + $m) }

$shaProvider = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
function Get-Sha256([string]$path) {
    $fs = [System.IO.File]::OpenRead($path)
    try { return ([System.BitConverter]::ToString($shaProvider.ComputeHash($fs))).Replace('-', '').ToLowerInvariant() }
    finally { $fs.Dispose() }
}

# ---------- parse iss Excludes ----------
function Get-IssExcludes([string]$issFile) {
    $rules = @()
    foreach ($line in Get-Content $issFile -Encoding UTF8) {
        if ($line -match '^Source:\s*"\.\.\\dist\\launcher\\\*"') {
            if ($line -match 'Excludes:\s*"([^"]*)"') {
                foreach ($tok in $Matches[1].Split(',')) {
                    $t = $tok.Trim()
                    if ($t) { $rules += $t }
                }
                break
            }
        }
    }
    return $rules
}
function Convert-GlobToRegex([string]$glob) {
    # normalize separators: iss uses '\' while rel paths use '/'
    # split on '*' first, escape segments, join with single-segment wildcard
    $normGlob = $glob.Replace('\', '/')
    $parts = $normGlob.Split('*')
    $body = (($parts | ForEach-Object { [regex]::Escape($_) }) -join '[^/]*')
    return ('^' + $body + '(/.*)?$')
}

# ---------- protection / patchability rules ----------
$McPatchableRoots = @('.minecraft/versions/', '.minecraft/libraries/', '.minecraft/assets/', '.minecraft/mods/', '.minecraft/shaderpacks/')
$ProtectExact = @('options.txt', 'servers.dat', 'launcher_profiles.json', 'current_instance.txt',
    'settings.json', 'account.json', 'servers.json', 'wechat.json')
$ProtectPrefix = @('.minecraft/config/', '.minecraft/saves/', '.minecraft/screenshots/',
    '.minecraft/logs/', '.minecraft/crash-reports/', '.minecraft/downloads/',
    '.minecraft/cache/', 'cache/', 'Baihe.exe.WebView2/')
$ProtectSuffix = @('.log', '.tmp')

function Test-Protected([string]$rel) {
    $r = $rel.ToLowerInvariant()
    foreach ($e in $ProtectExact) { if ($r -eq $e) { return $true } }
    foreach ($p in $ProtectPrefix) { if ($r.StartsWith($p)) { return $true } }
    foreach ($s in $ProtectSuffix) { if ($r.EndsWith($s)) { return $true } }
    return $false
}
function Test-Patchable([string]$rel, $exRegexes) {
    if (Test-Protected $rel) { return $false }
    $r = $rel.Replace('\', '/')
    if ($r.StartsWith('.minecraft/')) {
        foreach ($root in $McPatchableRoots) {
            if ($r.StartsWith($root)) { return $true }
        }
        return $false
    }
    foreach ($rx in $exRegexes) { if ($rx.IsMatch($r)) { return $false } }
    return $true
}

# ---------- scan current tree ----------
$repoIss = Join-Path $PSScriptRoot '..\installer\baihe_installer.iss'
Write-Log ("Scanning tree: " + (Resolve-Path $CurrentDir).Path)
$exRules = @(Get-IssExcludes $repoIss)
if (-not $exRules.Count) { throw 'Failed to parse Excludes from iss' }
$exRegexes = @(); foreach ($g in $exRules) { $exRegexes += ,([regex]::new((Convert-GlobToRegex $g), 'IgnoreCase')) }
Write-Log ("iss excludes parsed: " + ($exRules -join ', '))

$current = @{}
$versionsRel = New-Object System.Collections.Generic.List[string]
$scanCount = 0
$stageAbsLen = (Resolve-Path $CurrentDir).Path.Length
Get-ChildItem -LiteralPath $CurrentDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($stageAbsLen).TrimStart('\').Replace('\', '/')
    $scanCount++
    if ((Test-Patchable $rel $exRegexes)) {
        $h = Get-Sha256 $_.FullName
        $current[$rel] = @{ size = $_.Length; sha = $h }
        if ($rel.StartsWith('.minecraft/versions/')) { $versionsRel.Add($rel) }
    }
}
Write-Log ("Total scanned: " + $scanCount + ", tracked(patchable): " + $current.Count)

# ---------- load previous manifest ----------
$prev = $null
$prevVersion = ""
if ($PrevManifestPath -and (Test-Path $PrevManifestPath)) {
    try {
        $prevRaw = Get-Content $PrevManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $prevVersion = [string]$prevRaw.version
        $prev = @{}
        foreach ($f in $prevRaw.files) { $prev[[string]$f.path] = @{ size = [long]$f.size; sha = [string]$f.sha256 } }
        Write-Log ("Prev manifest loaded: v" + $prevVersion + " files=" + $prev.Count)
    } catch {
        Write-Log "WARN: prev manifest parse failed -> no patch"
        $prev = $null
    }
} else {
    Write-Log "No previous manifest -> manifest-only run"
}

$sameVersionsTree = $false
if ($prev) {
    $prevV = New-Object System.Collections.Generic.List[string]
    foreach ($k in $prev.Keys) { if ($k.StartsWith('.minecraft/versions/')) { $prevV.Add($k) } }
    $a = ($versionsRel | Sort-Object) -join "`n"
    $b = ($prevV | Sort-Object) -join "`n"
    $sameVersionsTree = (($a -eq $b) -and ($a.Length -gt 0))
    if (-not $sameVersionsTree) {
        Write-Log ("versions tree CHANGED (or empty): prev=" + $prevV.Count + " cur=" + $versionsRel.Count + " -> patch DISABLED")
    } else {
        Write-Log ("versions tree unchanged: " + $versionsRel.Count + " entries")
    }
}
$canPatch = ($null -ne $prev) -and ($prevVersion -ne "") -and ($prevVersion -ne $Version) -and $sameVersionsTree

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# ---------- 1. current manifest ----------
$manifestPath = Join-Path $OutputDir ("BaiheManifest_v" + $Version + ".json")
$fileList = @()
foreach ($k in ($current.Keys | Sort-Object)) {
    $fileList += [ordered]@{ path = $k; size = [int64]$current[$k].size; sha256 = $current[$k].sha }
}
$manifestObj = [ordered]@{
    version     = $Version
    generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    fileCount   = $fileList.Count
    files       = $fileList
}
$json = $manifestObj | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
$mb = [math]::Round((Get-Item $manifestPath).Length / 1MB, 2)
Write-Log ("Manifest written: " + $manifestPath + " (" + $mb + " MB)")

if (-not $canPatch) {
    Write-Log "Exit 2: manifest-only"
    exit 2
}

# ---------- 2. diff patch ----------
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("baihe_patch_stage_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$changed = New-Object System.Collections.Generic.List[string]
foreach ($k in $current.Keys) {
    if (-not $prev.ContainsKey($k)) { $changed.Add($k); continue }
    if ($prev[$k].sha -ne $current[$k].sha) { $changed.Add($k) }
}
$deleted = New-Object System.Collections.Generic.List[string]
foreach ($k in $prev.Keys) {
    if (-not $current.ContainsKey($k)) {
        if (Test-Patchable $k $exRegexes) { $deleted.Add($k) } else { Write-Log ("skip delete(unpatchable): " + $k) }
    }
}

$hashMap = [ordered]@{}
foreach ($rel in $changed) {
    $dest = Join-Path $stage ($rel.Replace('/', '\'))
    $dir = Split-Path $dest -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Copy-Item (Join-Path $CurrentDir ($rel.Replace('/', '\'))) $dest -Force
    $hashMap[$rel] = $current[$rel].sha
}
$metaObj = [ordered]@{
    from        = $prevVersion
    to          = $Version
    generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    files       = @($changed | Sort-Object)
    deletes     = @($deleted | Sort-Object)
    hashes      = $hashMap
}
$metaJson = $metaObj | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $stage '_meta.json'), $metaJson, (New-Object System.Text.UTF8Encoding($false)))

$patchName = "BaihePatch_v" + $prevVersion + "_to_" + $Version + ".zip"
$patchPath = Join-Path $OutputDir $patchName

# write zip manually (entry names unified to '/' ; avoid Compress-Archive backslash entries)
if (Test-Path $patchPath) { Remove-Item $patchPath -Force }
$zipFs = [System.IO.File]::Create($patchPath)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipFs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
            $entryName = $_.FullName.Substring($stage.Length).TrimStart('\').Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            try {
                $cs = [System.IO.File]::OpenRead($_.FullName)
                try { $cs.CopyTo($es) } finally { $cs.Dispose() }
            } finally { $es.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $zipFs.Dispose() }

$sizeMB = [math]::Round((Get-Item $patchPath).Length / 1MB, 2)
Write-Log ("Patch written: " + $patchPath + "  changed=" + $changed.Count + " deleted=" + $deleted.Count + " size=" + $sizeMB + "MB")
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
exit 0