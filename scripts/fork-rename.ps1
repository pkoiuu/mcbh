# fork-rename.ps1 — 将 PCL.Core fork 为 Baihe.Core，重命名命名空间并裁剪 WPF 耦合
# 用法: .\scripts\fork-rename.ps1
# 前提: PCL2-CE\PCL.Core 和 PCL2-CE\PCL.Core.SourceGenerators 目录存在
# 注意: 使用 .NET API 进行删除操作，绕过 PowerShell Remove-Item 限制

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$srcDir = Join-Path $root 'src'
$pclCoreSrc = Join-Path $root 'PCL2-CE\PCL.Core'
$pclSgSrc = Join-Path $root 'PCL2-CE\PCL.Core.SourceGenerators'
$baiheCoreDest = Join-Path $srcDir 'Baihe.Core'
$baiheSgDest = Join-Path $srcDir 'Baihe.Core.SourceGenerators'

# 辅助函数：安全删除目录（使用 .NET API）
function Remove-DirSafe {
    param([string]$path)
    if (Test-Path $path) {
        [System.IO.Directory]::Delete($path, $true)
        Write-Host "  删除目录: $path"
    }
}

# 辅助函数：安全删除文件（使用 .NET API）
function Remove-FileSafe {
    param([string]$path)
    if (Test-Path $path) {
        [System.IO.File]::Delete($path)
        Write-Host "  删除文件: $path"
    }
}

# 辅助函数：递归删除 bin/obj 目录
function Remove-BinObj {
    param([string]$rootPath)
    Get-ChildItem $rootPath -Recurse -Directory -Include 'bin','obj' -ErrorAction SilentlyContinue | ForEach-Object {
        [System.IO.Directory]::Delete($_.FullName, $true)
        Write-Host "  清理: $($_.FullName.Substring($rootPath.Length + 1))"
    }
}

Write-Host '[1/6] 检查源目录...'
if (-not (Test-Path $pclCoreSrc)) { throw "PCL.Core 源码不存在: $pclCoreSrc" }
if (-not (Test-Path $pclSgSrc)) { throw "PCL.Core.SourceGenerators 源码不存在: $pclSgSrc" }

# 创建 src 目录
if (-not (Test-Path $srcDir)) {
    [System.IO.Directory]::CreateDirectory($srcDir)
}

Write-Host '[2/6] 复制 PCL.Core -> Baihe.Core...'
Remove-DirSafe $baiheCoreDest
Copy-Item $pclCoreSrc $baiheCoreDest -Recurse -Force
Remove-BinObj $baiheCoreDest

Write-Host '[2/6] 复制 PCL.Core.SourceGenerators -> Baihe.Core.SourceGenerators...'
Remove-DirSafe $baiheSgDest
Copy-Item $pclSgSrc $baiheSgDest -Recurse -Force
Remove-BinObj $baiheSgDest

Write-Host '[3/6] 删除不需要的目录（UI/Link/Model/Saves/ResourceProject）...'
foreach ($dir in @('UI', 'Link', 'Model')) {
    Remove-DirSafe (Join-Path $baiheCoreDest $dir)
}
foreach ($sub in @('Saves', 'ResourceProject')) {
    Remove-DirSafe (Join-Path $baiheCoreDest "Minecraft\$sub")
}

Write-Host '[4/6] 删除 WPF 耦合文件...'
$wpfFiles = @(
    'Utils\WpfUtils.cs',
    'Utils\OS\DragHelper.cs',
    'Utils\OS\ClipboardUtils.cs',
    'Utils\Exts\UiExtension.cs',
    'Utils\Exts\LanguageSpecificStringDictionaryExtensions.cs',
    'App\Essentials\ApplicationService.cs',
    'App\Essentials\MainWindowService.cs',
    'App\Essentials\RpcService.cs',
    'App\Essentials\StartupValidation.cs',
    'App\Tools\DependencyCheckService.cs',
    'App\Localization\Lang.cs',
    'App\Localization\LocalizationService.cs',
    'App\Localization\LocalizationFontService.cs',
    'App\Localization\LocalizationFormatConverter.cs'
)
foreach ($file in $wpfFiles) {
    Remove-FileSafe (Join-Path $baiheCoreDest $file)
}

Write-Host '[5/6] 全局替换命名空间 PCL.Core -> Baihe.Core...'
$files = Get-ChildItem $baiheCoreDest -Recurse -File -Include '*.cs','*.csproj','*.xaml','*.json','*.targets','*.props'
$files += Get-ChildItem $baiheSgDest -Recurse -File -Include '*.cs','*.csproj','*.json','*.targets','*.props'
$replaceCount = 0
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    if ($content -match 'PCL\.Core|PCLCE') {
        $newContent = $content -replace 'PCL\.Core', 'Baihe.Core' `
                               -replace 'PCLCE_Debug', 'Baihe_Debug' `
                               -replace 'PCLCE', 'Baihe'
        [System.IO.File]::WriteAllText($file.FullName, $newContent, [System.Text.UTF8Encoding]::new($false))
        $replaceCount++
        Write-Host "  替换: $($file.FullName.Substring($root.Length + 1))"
    }
}
Write-Host "  共替换 $replaceCount 个文件"

Write-Host '[6/6] 修改 Baihe.Core.csproj 移除 WPF 依赖...'
$csproj = Join-Path $baiheCoreDest 'Baihe.Core.csproj'
if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw -Encoding UTF8
    # 移除 UseWPF
    $content = $content -replace '<UseWPF>true</UseWPF>', ''
    # 移除 Microsoft.Xaml.Behaviors.Wpf
    $content = $content -replace '<PackageReference\s+Include="Microsoft\.Xaml\.Behaviors\.Wpf"[^/]*/>', ''
    # 移除 XAML Page ItemGroup
    $content = $content -replace '(?s)<ItemGroup>\s*<Page\s+Include="\*\*\\\*\.xaml"[^>]*/>.*?</ItemGroup>', ''
    # 移除 UI Assets Resource ItemGroup
    $content = $content -replace '(?s)<ItemGroup>\s*<Resource\s+Include="UI\\Assets\\\*"[^>]*/>.*?</ItemGroup>', ''
    [System.IO.File]::WriteAllText($csproj, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  修改 Baihe.Core.csproj"
}

# 同样处理 SourceGenerators csproj
$sgCsproj = Join-Path $baiheSgDest 'Baihe.Core.SourceGenerators.csproj'
if (-not (Test-Path $sgCsproj)) {
    # 可能还叫 PCL.Core.SourceGenerators.csproj
    $oldName = Join-Path $baiheSgDest 'PCL.Core.SourceGenerators.csproj'
    if (Test-Path $oldName) {
        # 读取内容并写入新文件名
        $content = Get-Content $oldName -Raw -Encoding UTF8
        [System.IO.File]::WriteAllText($sgCsproj, $content, [System.Text.UTF8Encoding]::new($false))
        Remove-FileSafe $oldName
        Write-Host "  重命名 csproj: PCL.Core.SourceGenerators.csproj -> Baihe.Core.SourceGenerators.csproj"
    }
}

Write-Host ''
Write-Host '=== fork 完成！ ==='
Write-Host "  Baihe.Core: $baiheCoreDest"
Write-Host "  Baihe.Core.SourceGenerators: $baiheSgDest"
Write-Host ''
Write-Host '下一步需要手动处理：'
Write-Host '  1. 重写 App\Basics.cs（移除 GetResourceStream WPF 依赖）'
Write-Host '  2. 裁剪 IO\Files.cs（移除 UI 对话框）'
Write-Host '  3. 裁剪 Logging\LogService.cs（移除 System.Windows using）'
Write-Host '  4. 重写 App\IoC\LifecycleFlow.cs（移除 WPF 依赖）'
Write-Host '  5. 重写 AssemblyInfo.cs（移除 XmlnsDefinition）'
