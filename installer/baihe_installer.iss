; ============================================================
; 白鹤服务器启动器 — Inno Setup 安装脚本
; 安装到 %LOCALAPPDATA%\BaiheServer (无需管理员权限)
; 包含: 启动器 + JRE + WebView2 Runtime + .minecraft 游戏文件
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

; 界面
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=none
SetupIconFile=installer_resources\icon.ico
; 安装向导图片
WizardImageFile=installer_assets\wizimage.bmp
WizardSmallImageFile=installer_assets\wizsmallimage.bmp

; 输出
OutputDir=dist
OutputBaseFilename=白鹤服务器启动器_Setup_v{#MyAppVersion}
CloseApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "installer_resources\ChineseSimplified.isl"

[Files]
; 启动器主程序和所有依赖文件 (包含 wwwroot、runtimes 等)
Source: "dist\启动器\*"; DestDir: "{app}"; Excludes: "jre\*,WebView2FixedRuntime\*"; Flags: ignoreversion recursesubdirs createallsubdirs

; JRE 运行时
Source: "dist\启动器\jre\*"; DestDir: "{app}\jre"; Flags: ignoreversion recursesubdirs createallsubdirs

; WebView2 固定版本运行时
Source: "dist\启动器\WebView2FixedRuntime\*"; DestDir: "{app}\WebView2FixedRuntime"; Flags: ignoreversion recursesubdirs createallsubdirs

; .minecraft 游戏文件 — 排除日志和备份
Source: "dist\.minecraft\*"; DestDir: "{app}\.minecraft"; Excludes: "*.log,*.bak,Log\*,logs\*,crash-reports\*,.fabric\*"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; 桌面快捷方式
Name: "{commondesktop}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"

; 开始菜单快捷方式
Name: "{group}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"
Name: "{group}\卸载白鹤服务器启动器"; Filename: "{uninstallexe}"

[Run]
; 安装完成后启动
Filename: "{app}\Baihe.exe"; Description: "{cm:LaunchProgram,白鹤服务器启动器}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清理用户数据 (可选)
Type: filesandordirs; Name: "{app}\.minecraft\logs"
Type: filesandordirs; Name: "{app}\.minecraft\crash-reports"
Type: filesandordirs; Name: "{app}\.minecraft\saves"
Type: filesandordirs; Name: "{app}\.minecraft\screenshots"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
