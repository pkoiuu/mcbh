; ============================================================
; 白鹤服务器启动器 — Inno Setup 安装脚本
; 安装到 %LOCALAPPDATA%\BaiheServer (无需管理员权限)
; 包含: 启动器 + JRE + WebView2 Runtime + .minecraft 游戏文件
; 注意: 路径相对于本 .iss 文件所在目录 (installer/)
; ============================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
; 应用信息
AppName=白鹤服务器启动器
AppVersion={#MyAppVersion}
AppPublisher=白鹤服务器
AppPublisherURL=https://github.com/pkoiuu/mcbh
AppSupportURL=https://github.com/pkoiuu/mcbh
AppUpdatesURL=https://github.com/pkoiuu/mcbh/releases

; 版本信息 (用于安装包文件属性)
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=白鹤服务器
VersionInfoProductName=白鹤服务器启动器
VersionInfoProductVersion={#MyAppVersion}.0

; 安装目录 — 用户目录，无需管理员权限
DefaultDirName={localappdata}\BaiheServer
DefaultGroupName=白鹤服务器启动器
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

; 卸载
UninstallDisplayName=白鹤服务器启动器
UninstallDisplayIcon={app}\Baihe.exe
CreateUninstallRegKey=yes

; 压缩
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; 界面 — 路径相对于 .iss 文件目录
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=none
SetupIconFile=..\installer_resources\icon.ico
WizardImageFile=..\installer_assets\wizimage.bmp
WizardSmallImageFile=..\installer_assets\wizsmallimage.bmp

; 输出 — 到仓库根目录的 dist/
OutputDir=..\dist
OutputBaseFilename=白鹤服务器启动器_Setup_v{#MyAppVersion}
CloseApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "..\installer_resources\ChineseSimplified.isl"

[Files]
; 启动器主程序和所有依赖文件 (包含 wwwroot、runtimes 等，排除 WebView2 安装程序)
Source: "..\dist\launcher\*"; DestDir: "{app}"; Excludes: "jre\*,MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: ignoreversion recursesubdirs createallsubdirs

; JRE 运行时
Source: "..\dist\launcher\jre\*"; DestDir: "{app}\jre"; Flags: ignoreversion recursesubdirs createallsubdirs

; WebView2 离线安装程序
Source: "..\dist\launcher\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{app}"; Flags: ignoreversion

; .minecraft 游戏文件 — 排除日志和备份
Source: "..\dist\.minecraft\*"; DestDir: "{app}\.minecraft"; Excludes: "*.log,*.bak,Log\*,logs\*,crash-reports\*,.fabric\*"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; 桌面快捷方式
Name: "{commondesktop}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"

; 开始菜单快捷方式
Name: "{group}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"
Name: "{group}\卸载白鹤服务器启动器"; Filename: "{uninstallexe}"

[Run]
; 安装 WebView2 Runtime (如果未安装)
Filename: "{app}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; Check: not WebView2Installed(); Flags: waituntilterminated runhidden
; 安装完成后启动
Filename: "{app}\Baihe.exe"; Description: "{cm:LaunchProgram,白鹤服务器启动器}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清理用户数据 (可选)
Type: filesandordirs; Name: "{app}\.minecraft\logs"
Type: filesandordirs; Name: "{app}\.minecraft\crash-reports"
Type: filesandordirs; Name: "{app}\.minecraft\saves"
Type: filesandordirs; Name: "{app}\.minecraft\screenshots"

[Code]
// 检测 WebView2 Runtime 是否已安装
function WebView2Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    or RegKeyExists(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
