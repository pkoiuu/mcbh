# fork-rename.ps1 — 将 PCL.Core fork 为 Baihe.Core，重命名命名空间并裁剪 WPF 耦合
# 用法: .\scripts\fork-rename.ps1
# 前提: PCL2-CE\PCL.Core 和 PCL2-CE\PCL.Core.SourceGenerators 目录存在

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$srcDir = Join-Path $root 'src'
$pclCoreSrc = Join-Path $root 'PCL2-CE\PCL.Core'
$pclSgSrc = Join-Path $root 'PCL2-CE\PCL.Core.SourceGenerators'
$baiheCoreDest = Join-Path $srcDir 'Baihe.Core'
$baiheSgDest = Join-Path $srcDir 'Baihe.Core.SourceGenerators'

Write-Host '[1/6] 检查源目录...'
if (-not (Test-Path $pclCoreSrc)) { throw "PCL.Core 源码不存在: $pclCoreSrc" }
if (-not (Test-Path $pclSgSrc)) { throw "PCL.Core.SourceGenerators 源码不存在: $pclSgSrc" }

Write-Host '[2/6] 复制 PCL.Core -> Baihe.Core...'
if (Test-Path $baiheCoreDest) { Remove-Item $baiheCoreDest -Recurse -Force }
Copy-Item $pclCoreSrc $baiheCoreDest -Recurse -Force
# 清理 bin/obj
Get-ChildItem $baiheCoreDest -Recurse -Directory -Include 'bin','obj' | Remove-Item -Recurse -Force

Write-Host '[2/6] 复制 PCL.Core.SourceGenerators -> Baihe.Core.SourceGenerators...'
if (Test-Path $baiheSgDest) { Remove-Item $baiheSgDest -Recurse -Force }
Copy-Item $pclSgSrc $baiheSgDest -Recurse -Force
Get-ChildItem $baiheSgDest -Recurse -Directory -Include 'bin','obj' | Remove-Item -Recurse -Force

Write-Host '[3/6] 删除不需要的目录（UI/Link/Saves/ResourceProject）...'
$dirsToDelete = @('UI', 'Link', 'Model')
foreach ($dir in $dirsToDelete) {
    $path = Join-Path $baiheCoreDest $dir
    if (Test-Path $path) {
        Write-Host "  删除 $dir/"
        Remove-Item $path -Recurse -Force
    }
}
# 删除 Minecraft 下的 Saves 和 ResourceProject
foreach ($sub in @('Saves', 'ResourceProject')) {
    $path = Join-Path $baiheCoreDest "Minecraft\$sub"
    if (Test-Path $path) { Remove-Item $path -Recurse -Force; Write-Host "  删除 Minecraft/$sub/" }
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
    $path = Join-Path $baiheCoreDest $file
    if (Test-Path $path) { Remove-Item $path -Force; Write-Host "  删除 $file" }
}

Write-Host '[5/6] 全局替换命名空间 PCL.Core -> Baihe.Core...'
# 获取所有 .cs, .csproj, .xaml, .json 文件
$files = Get-ChildItem $baiheCoreDest -Recurse -File -Include '*.cs','*.csproj','*.xaml','*.json','*.targets','*.props'
$files += Get-ChildItem $baiheSgDest -Recurse -File -Include '*.cs','*.csproj','*.json','*.targets','*.props'
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    if ($content -match 'PCL\.Core|PCLCE') {
        $newContent = $content -replace 'PCL\.Core', 'Baihe.Core' `
                               -replace 'PCLCE_Debug', 'Baihe_Debug' `
                               -replace 'PCLCE', 'Baihe'
        Set-Content $file.FullName -Value $newContent -Encoding UTF8 -NoNewline
        Write-Host "  替换: $($file.FullName.Substring($root.Length + 1))"
    }
}

Write-Host '[6/6] 修改 Baihe.Core.csproj 移除 WPF 依赖...'
$csproj = Join-Path $baiheCoreDest 'Baihe.Core.csproj'
if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw -Encoding UTF8
    # 移除 UseWPF
    $content = $content -replace '<UseWPF>true</UseWPF>', ''
    # 移除 Microsoft.Xaml.Behaviors.Wpf
    $content = $content -replace '<PackageReference\s+Include="Microsoft\.Xaml\.Behaviors\.Wpf"[^/]*/>', ''
    # 移除 XAML Page 和 Resource ItemGroup
    $content = $content -replace '(?s)<ItemGroup>\s*<Page\s+Include="\*\*\\\*\.xaml"[^>]*/>.*?</ItemGroup>', ''
    $content = $content -replace '(?s)<ItemGroup>\s*<Resource\s+Include="UI\\Assets\\\*"[^>]*/>.*?</ItemGroup>', ''
    Set-Content $csproj -Value $content -Encoding UTF8 -NoNewline
    Write-Host "  修改 Baihe.Core.csproj"
}

Write-Host ''
Write-Host 'fork 完成！'
Write-Host "  Baihe.Core: $baiheCoreDest"
Write-Host "  Baihe.Core.SourceGenerators: $baiheSgDest"
Write-Host ''
Write-Host '下一步需要手动处理：'
Write-Host '  1. 重写 App\Basics.cs（移除 GetResourceStream WPF 依赖）'
Write-Host '  2. 裁剪 IO\Files.cs（移除 UI 对话框）'
Write-Host '  3. 裁剪 Logging\LogService.cs（移除 System.Windows using）'
Write-Host '  4. 重写 App\IoC\LifecycleFlow.cs（移除 WPF 依赖）'
Write-Host '  5. 重写 AssemblyInfo.cs（移除 XmlnsDefinition）'
