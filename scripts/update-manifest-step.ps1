# ============================================================
# update-manifest-step.ps1 - shared CI step (release.yml + test-release.yml)
# Finds the previous release manifest on the same channel, stages the
# deploy tree, and calls generate-update-patch.ps1.
#
# Usage:
#   pwsh scripts/update-manifest-step.ps1 -Mode stable   # previous stable releases
#   pwsh scripts/update-manifest-step.ps1 -Mode test     # previous prereleases
#
# Exit code: propagates generate-update-patch.ps1 codes (0=patch, 2=manifest-only)
# Caller must normalize exit code 2 -> success step.
# NOTE: keep this file pure ASCII (PS5.1 ANSI pitfall).
# ============================================================
param(
    [Parameter(Mandatory = $true)][ValidateSet('stable', 'test')][string]$Mode,
    [string]$RepoRef = 'repos/pkoiuu/mcbh'
)

$ErrorActionPreference = 'Continue'
$tagNoV = $env:GITHUB_REF_NAME -replace '^v', ''
if (-not $tagNoV) { throw 'GITHUB_REF_NAME is empty' }
$thisTag = "v$tagNoV"

New-Item -ItemType Directory -Force -Path .temp | Out-Null

# ---- find prev manifest on same channel ----
$pm = $null
try {
    $rels = gh api "$RepoRef/releases?per_page=30" --jq '.[] | select(.draft == false)' | ConvertFrom-Json
    foreach ($r in @($rels)) {
        if ($Mode -eq 'test' -and $r.prerelease -ne $true) { continue }
        if ($Mode -eq 'stable' -and $r.prerelease -eq $true) { continue }
        if ($r.tag_name -eq $thisTag) { continue }
        $hasM = @($r.assets | Where-Object { $_.name -like 'BaiheManifest_*.json' }).Count -gt 0
        if (-not $hasM) { continue }
        Write-Host "Prev $Mode manifest found on: $($r.tag_name)"
        gh release download $r.tag_name --pattern 'BaiheManifest_*.json' --dir .temp --clobber
        $pm = Get-ChildItem .temp -Filter 'BaiheManifest_*.json' | Select-Object -First 1
        break
    }
} catch { Write-Host "WARN: search prev releases failed: $_" }

if (-not $pm) {
    Write-Host "No prev $Mode manifest -> this run produces manifest only"
}

# ---- stage tree: launcher root + .minecraft subtree ----
Remove-Item -Recurse -Force .temp\patch_stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path .temp\patch_stage | Out-Null
Copy-Item dist\launcher\* .temp\patch_stage\ -Recurse -Force
if (Test-Path dist\.minecraft) { Copy-Item dist\.minecraft .temp\patch_stage\.minecraft -Recurse -Force }

# ---- run generator ----
$genArgs = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', 'scripts/generate-update-patch.ps1',
    '-CurrentDir', '.temp/patch_stage',
    '-Version', $tagNoV,
    '-OutputDir', 'dist'
)
if ($pm) { $genArgs += @('-PrevManifestPath', $pm.FullName) }
& pwsh @genArgs
$code = $LASTEXITCODE
Write-Host "generate-update-patch exit code: $code (0=patch produced, 2=manifest-only)"
Remove-Item -Recurse -Force .temp\patch_stage -ErrorAction SilentlyContinue
exit $code